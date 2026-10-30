using UnityEngine;
using UnityEngine.UIElements;

// A mini game drawn with UI Toolkit: owns the panel lifecycle, seats, results and the
// 1v1/2v2 switch used when a scene is played directly. The HUD must contain seatLayer,
// timer, centerMessage, togglePlayers and an instance of ResultsOverlay.uxml.
[RequireComponent(typeof(PanelRenderer))]
public abstract class PanelMiniGame : MiniGameBase
{
    const string FinishedClass = "is-finished";

    [SerializeField] VisualTreeAsset seatTemplate;

    VisualElement _hud;
    VisualElement _seatLayer;
    ResultsOverlay _results;
    Button _togglePlayers;
    bool _started;

    protected Label Timer { get; private set; }
    protected Label CenterMessage { get; private set; }
    protected PlayerSeatView[] Seats { get; private set; }
    protected bool RoundOver { get; private set; }

    // Frame clock: gameplay time, unaffected by Time.timeScale.
    protected static double Now => Time.unscaledTimeAsDouble;

    protected abstract string[] FunFacts { get; }

    // Finds this game's own elements; called each time the panel loads, before Setup.
    protected abstract void Bind(VisualElement root);

    void Awake() => GetComponent<PanelRenderer>().RegisterUIReloadCallback(OnUiLoaded);

    // Starts from OnUiLoaded, because the panel may not be ready yet.
    protected override void Start() => UiBuilder.EnsureEventSystem();

    protected override void OnDestroy()
    {
        base.OnDestroy();
        GetComponent<PanelRenderer>().UnregisterUIReloadCallback(OnUiLoaded);
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

        SeatPanels.FitSafeArea(_seatLayer);
        Bind(root);

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
        _hud.RemoveFromClassList(FinishedClass);
        _results.Hide();
        _togglePlayers.text = playerCount == 2 ? "Đổi sang 2 đấu 2" : "Đổi sang 1 đấu 1";

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

        _results.Update(Now);
        _togglePlayers.style.display = GameManager.Instance == null && !IsRunning ? DisplayStyle.Flex : DisplayStyle.None;
        foreach (var seat in Seats) seat.UpdateFeedback(Now);
    }

    // Ends the round and shows the results card.
    protected void EndRound(string title)
    {
        RoundOver = true;
        Finish();
        _hud.AddToClassList(FinishedClass);
        _results.Show(title, FunFacts, Now);
    }

    protected static string CountdownText(double untilStart, string go)
    {
        if (untilStart > 0) return Mathf.CeilToInt((float)untilStart).ToString();
        return untilStart > -0.8 ? go : "";
    }

    void TogglePlayerCount()
    {
        int next = PlayerCount == 2 ? 4 : 2;
        PlayerInputRouter.Instance.Configure(next);
        Setup(next);
    }
}
