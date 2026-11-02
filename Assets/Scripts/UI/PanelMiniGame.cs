using UnityEngine;
using UnityEngine.UIElements;

// A mini game drawn with UI Toolkit: owns the panel lifecycle, seats, results and the
// 1v1/2v2 switch used when a scene is played directly. The HUD must contain seatLayer,
// timer, centerMessage, togglePlayers and instances of ResultsOverlay.uxml and PauseOverlay.uxml.
[RequireComponent(typeof(PanelRenderer))]
public abstract class PanelMiniGame : MiniGameBase
{
    const string FinishedClass = "is-finished";

    [SerializeField] VisualTreeAsset seatTemplate;

    VisualElement _hud;
    VisualElement _seatLayer;
    ResultsOverlay _results;
    PauseOverlay _pause;
    Button _togglePlayers;
    bool _started;
    Rect _pauseZone;
    readonly PausableClock _clock = new PausableClock();

    public override bool IsPaused => _clock.IsPaused;

    protected Label Timer { get; private set; }
    protected Label CenterMessage { get; private set; }
    protected PlayerSeatView[] Seats { get; private set; }
    protected bool RoundOver { get; private set; }

    // Frame clock: gameplay time, unaffected by Time.timeScale, stopped while paused.
    protected double Now => _clock.At(Time.unscaledTimeAsDouble);

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
    }

    void OnUiLoaded(PanelRenderer panel, VisualElement root)
    {
        _hud = root.Q(className: "app");
        _seatLayer = root.Q("seatLayer");
        Timer = root.Q<Label>("timer");
        CenterMessage = root.Q<Label>("centerMessage");
        _togglePlayers = root.Q<Button>("togglePlayers");
        _results = new ResultsOverlay(root);
        _results.PlayAgain += () => Setup(PlayerCount);
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
            base.Start();
        }
    }

    public override void Setup(int playerCount)
    {
        base.Setup(playerCount);
        RoundOver = false;
        GameAudio.StopVictory();
        _hud.RemoveFromClassList(FinishedClass);
        _results.Hide();
        _pause.ButtonVisible = true;

        _seatLayer.Clear();
        var seats = SeatLayout.Get(playerCount);
        Seats = new PlayerSeatView[seats.Length];
        foreach (var seat in seats)
        {
            var view = new PlayerSeatView(seatTemplate, seat, playerCount == 2);
            _seatLayer.Add(view.Root);
            Seats[seat.PlayerId] = view;
        }
    }

    protected virtual void Update()
    {
        if (Seats == null) return;

        _results.Update();
        _togglePlayers.text = Localization.Text(PlayerCount == 2 ? "Đổi sang 2 đấu 2" : "Đổi sang 1 đấu 1");
        _togglePlayers.style.display = GameManager.Instance == null && !IsRunning ? DisplayStyle.Flex : DisplayStyle.None;
        foreach (var seat in Seats) seat.UpdateFeedback(Now);
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
