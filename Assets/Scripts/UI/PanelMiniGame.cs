using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

// A mini game drawn with UI Toolkit: owns the panel lifecycle, seats, results and the
// 1v1/2v2 switch used when a scene is played directly. The HUD must contain seatLayer,
// timer, centerMessage, togglePlayers and instances of ResultsOverlay.uxml and PauseOverlay.uxml.
// Online, the host runs the rules: joining phones send presses with the time they happened, and a
// round starts once every phone has loaded the scene (each phone shows only its own seat).
[RequireComponent(typeof(PanelRenderer))]
public abstract class PanelMiniGame : MiniGameBase
{
    const string FinishedClass = "is-finished";
    const string PressMessage = "press"; // client -> host: press time, pressed
    const string RoundMessage = "round"; // host -> clients: when the countdown starts
    const string SetupMessage = "setup"; // host -> clients: "play again"
    const double OnlineStartDelay = 0.5; // lets the round message reach every phone before the countdown
    const double MaxInputLag = 0.5;      // older presses count as this old, so a stalled phone can't rewrite the past

    [SerializeField] VisualTreeAsset seatTemplate;

    VisualElement _hud;
    VisualElement _seatLayer;
    ResultsOverlay _results;
    PauseOverlay _pause;
    Button _togglePlayers;
    bool _started;
    bool _awaitingPlayers;
    Rect _pauseZone;
    readonly PausableClock _clock = new PausableClock();
    readonly List<string> _listening = new List<string>();

    public override bool IsPaused => _clock.IsPaused;

    protected Label Timer { get; private set; }
    protected Label CenterMessage { get; private set; }

    // Indexed by player id. Online only this phone's player has a seat, so others are null.
    protected PlayerSeatView[] Seats { get; private set; }
    protected bool RoundOver { get; private set; }

    protected static bool IsOnline => GameManager.Instance != null && GameManager.Instance.Mode == SessionMode.Online;
    protected static bool IsOnlineHost => IsOnline && OnlineSession.IsHost;
    protected static bool IsOnlineClient => IsOnline && !OnlineSession.IsHost;

    // Frame clock: gameplay time, unaffected by Time.timeScale, stopped while paused.
    // Online it is the shared network clock, so every phone sees the same beats.
    protected double Now => IsOnline ? OnlineSession.Now : _clock.At(Time.unscaledTimeAsDouble);

    // Online: the host has picked when the countdown starts, on every phone at once.
    protected virtual void OnRoundScheduled(double countdownStart) { }

    // Online, on the host: a joining phone's press, with the network time it happened.
    protected virtual void OnRemotePlayerInput(int playerId, bool pressed, double time) => OnPlayerInput(playerId, pressed);

    // Online: where a game registers its own messages (they are removed with the scene).
    protected virtual void ListenOnline() { }

    protected void Listen(string name, CustomMessagingManager.HandleNamedMessageDelegate handler)
    {
        OnlineSession.Instance.Listen(name, handler);
        _listening.Add(name);
    }

    protected abstract string[] FunFacts { get; }

    // False for a draw, which gets no victory fanfare.
    protected virtual bool HasWinner => true;

    // Fingers may have gone down or lifted during the pause.
    protected virtual void OnResumed() { }

    // Finds this game's own elements; called each time the panel loads, before Setup.
    protected abstract void Bind(VisualElement root);

    void Awake() => GetComponent<PanelRenderer>().RegisterUIReloadCallback(OnUiLoaded);

    // Starts from OnUiLoaded, because the panel may not be ready yet.
    protected override void Start() => UiBuilder.EnsureEventSystem();

    protected override void OnDestroy()
    {
        base.OnDestroy();
        GetComponent<PanelRenderer>().UnregisterUIReloadCallback(OnUiLoaded);
        var router = PlayerInputRouter.Existing;
        if (router != null) router.IgnoredZones.Remove(_pauseZone);
        if (OnlineSession.Instance != null)
            foreach (var name in _listening) OnlineSession.Instance.Unlisten(name);
    }

    void OnUiLoaded(PanelRenderer panel, VisualElement root)
    {
        _hud = root.Q(className: "app");
        _seatLayer = root.Q("seatLayer");
        Timer = root.Q<Label>("timer");
        CenterMessage = root.Q<Label>("centerMessage");
        _togglePlayers = root.Q<Button>("togglePlayers");
        _results = new ResultsOverlay(root);
        _results.PlayAgain += PlayAgain;
        _togglePlayers.clicked += TogglePlayerCount;
        _pause = new PauseOverlay(root);
        _pause.PauseRequested += Pause;
        _pause.ResumeRequested += Resume;
        _pause.PauseButton.RegisterCallback<GeometryChangedEvent>(_ => UpdatePauseZone());

        SeatPanels.FitSafeArea(_seatLayer);
        Bind(root);
        Localization.Bind(root);

        if (_started)
        {
            Setup(PlayerCount);
        }
        else
        {
            _started = true;
            // Before Setup, which tells the host this phone is ready to hear the round start.
            if (IsOnline) ListenForRounds();
            base.Start();
        }
    }

    void ListenForRounds()
    {
        if (IsOnlineHost)
        {
            Listen(PressMessage, (sender, reader) =>
            {
                reader.ReadValueSafe(out double time);
                reader.ReadValueSafe(out bool pressed);
                int player = OnlineSession.Instance.PlayerOf(sender);
                double now = Now;
                if (player >= 0) OnRemotePlayerInput(player, pressed, System.Math.Max(System.Math.Min(time, now), now - MaxInputLag));
            });
        }
        else
        {
            Listen(RoundMessage, (sender, reader) =>
            {
                reader.ReadValueSafe(out double countdownStart);
                OnRoundScheduled(countdownStart);
            });
            Listen(SetupMessage, (sender, reader) => Setup(PlayerCount));
        }
        ListenOnline();
    }

    protected override void HandlePlayerInput(int playerId, bool pressed)
    {
        if (!IsOnlineClient)
        {
            base.HandlePlayerInput(playerId, pressed);
            return;
        }
        double time = Now;
        OnlineSession.Instance.SendToHost(PressMessage, (ref FastBufferWriter writer) =>
        {
            writer.WriteValueSafe(time);
            writer.WriteValueSafe(pressed);
        });
    }

    void PlayAgain()
    {
        if (IsOnlineHost)
        {
            OnlineSession.Instance.ClearReady();
            OnlineSession.Instance.Broadcast(SetupMessage);
        }
        Setup(PlayerCount);
    }

    public override void Setup(int playerCount)
    {
        base.Setup(playerCount);
        RoundOver = false;
        GameAudio.StopVictory();
        _hud.RemoveFromClassList(FinishedClass);
        _results.Hide();
        // One player shouldn't be able to freeze everyone else's game.
        _pause.ButtonVisible = !IsOnline;

        _seatLayer.Clear();
        Seats = new PlayerSeatView[playerCount];
        var seats = IsOnline ? new[] { OnlineSeat(GameManager.Instance.LocalPlayerId) } : SeatLayout.Get(playerCount);
        foreach (var seat in seats)
        {
            var view = new PlayerSeatView(seatTemplate, seat, playerCount == 2 && !IsOnline);
            _seatLayer.Add(view.Root);
            Seats[seat.PlayerId] = view;
        }

        if (IsOnlineHost) _awaitingPlayers = true;
        else if (IsOnlineClient) OnlineSession.Instance.SendReady();
    }

    // Online the phone is held by one player, so their seat sits upright at the bottom of their team's side.
    static Seat OnlineSeat(int playerId) =>
        new Seat(playerId, new Rect(0f, 0f, 1f, 1f), new Vector2(SeatLayout.TeamOf(playerId) == 0 ? 0.12f : 0.88f, 0.15f), 0f);

    protected virtual void Update()
    {
        if (Seats == null) return;

        // The host starts the countdown once every phone has loaded and is listening.
        if (_awaitingPlayers && OnlineSession.Instance.AllClientsReady)
        {
            _awaitingPlayers = false;
            double countdownStart = Now + OnlineStartDelay;
            OnlineSession.Instance.Broadcast(RoundMessage, (ref FastBufferWriter writer) => writer.WriteValueSafe(countdownStart));
            OnRoundScheduled(countdownStart);
        }

        _results.Update();
        _togglePlayers.text = Localization.Text(PlayerCount == 2 ? "Đổi sang 2 đấu 2" : "Đổi sang 1 đấu 1");
        _togglePlayers.style.display = GameManager.Instance == null && !IsRunning ? DisplayStyle.Flex : DisplayStyle.None;
        foreach (var seat in Seats) seat?.UpdateFeedback(Now);
    }

    // Ends the round and shows the results card.
    protected void EndRound(System.Func<string> title)
    {
        RoundOver = true;
        Finish();
        _hud.AddToClassList(FinishedClass);
        _results.Show(title, FunFacts);
        _pause.ButtonVisible = false;
        if (HasWinner) GameAudio.PlayVictory();
    }

    // Disabling this component stops every Update, here and in the subclass, so nothing ticks.
    void Pause()
    {
        _clock.Pause(Time.unscaledTimeAsDouble);
        enabled = false;
        _pause.Show();
    }

    void Resume()
    {
        _clock.Resume(Time.unscaledTimeAsDouble);
        enabled = true;
        _pause.Hide();
        OnResumed();
    }

    // The seat zones cover the whole screen, so without this a tap on the pause button is also a player tap.
    void UpdatePauseZone()
    {
        var router = PlayerInputRouter.Instance;
        router.IgnoredZones.Remove(_pauseZone);

        var button = _pause.PauseButton.worldBound;
        var panel = _pause.PauseButton.panel.visualTree.layout;
        _pauseZone = new Rect(
            button.x / panel.width,
            1f - button.yMax / panel.height,
            button.width / panel.width,
            button.height / panel.height);
        router.IgnoredZones.Add(_pauseZone);
    }

    protected static string CountdownText(double untilStart, string go)
    {
        if (untilStart > 0) return Mathf.CeilToInt((float)untilStart).ToString();
        return untilStart > -0.8 ? Localization.Text(go) : "";
    }

    void TogglePlayerCount()
    {
        int next = PlayerCount == 2 ? 4 : 2;
        PlayerInputRouter.Instance.Configure(next);
        Setup(next);
    }
}
