using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

// Kéo Co (tug of war), 1v1 or 2v2. Tap on the chant beat to pull; teammates hitting the
// same beat together get the "Đồng lòng" bonus. The HUD is UI Toolkit (Assets/UI/KeoCo).
[RequireComponent(typeof(PanelRenderer))]
public class KeoCoGame : MiniGameBase
{
    const float CountdownSeconds = 3f;
    const float TrackHalfWidth = 560f; // panel units from center to each win line; matches .win-line in KeoCoHud.uss
    const string FinishedClass = "is-finished";
    const float FunFactSeconds = 2f;

    // Vietnamese and Korean take turns on the results screen.
    static readonly string[] FunFacts =
    {
        "Bạn có biết? Năm 2015, UNESCO ghi danh kéo co là di sản chung của Việt Nam, Hàn Quốc, Campuchia và Philippines.",
        "알고 있나요? 2015년 유네스코는 줄다리기를 베트남, 한국, 캄보디아, 필리핀의 공동 유산으로 등재했습니다.",
    };

    static readonly string[] LeftChant = { "Hò", "dô", "ta!" };
    static readonly string[] RightChant = { "Yeong", "cha!" };
    static readonly string[] FeedbackClasses = { "feedback--perfect", "feedback--good", "feedback--miss", "feedback--sync" };

    [SerializeField] KeoCoSettings settings = new KeoCoSettings();
    [SerializeField] VisualTreeAsset seatTemplate;

    KeoCoModel _model;
    VisualElement _root;
    VisualElement _rope;
    VisualElement _seatLayer;
    VisualElement _results;
    Label _timer;
    Label _center;
    Label _resultTitle;
    Label _resultFact;
    Button _togglePlayers;
    Button _continue;
    Button _playAgain;
    Button _backToMenu;
    readonly List<VisualElement> _pullers = new List<VisualElement>();
    SeatView[] _seats;
    int _lastBeat = -1;
    bool _finished;
    double _finishedAt;

    class SeatView
    {
        public VisualElement Ring;
        public VisualElement Disc;
        public Label Feedback;
        public Label Chant;
        public double FeedbackAt = double.NegativeInfinity;
    }

    // Frame clock for now; when recorded chants arrive, schedule them with AudioSource.PlayScheduled on the beat times.
    static double Now => Time.unscaledTimeAsDouble;

    static bool HasSession => GameManager.Instance != null;

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
        _root = root.Q("keoCoHud");
        _rope = root.Q("rope");
        _seatLayer = root.Q("seatLayer");
        _results = root.Q("results");
        _timer = root.Q<Label>("timer");
        _center = root.Q<Label>("centerMessage");
        _resultTitle = root.Q<Label>("resultTitle");
        _togglePlayers = root.Q<Button>("togglePlayers");
        _continue = root.Q<Button>("continueButton");
        _playAgain = root.Q<Button>("playAgainButton");
        _backToMenu = root.Q<Button>("backToMenuButton");

        _resultFact = root.Q<Label>("resultFact");
        _togglePlayers.clicked += TogglePlayerCount;
        _continue.clicked += () => GameManager.Instance.Continue();
        _playAgain.clicked += () => Setup(PlayerCount);
        _backToMenu.clicked += BackToMenu;

        SeatPanels.FitSafeArea(_seatLayer);

        if (_model == null) base.Start();
        else Setup(PlayerCount);
    }

    public override void Setup(int playerCount)
    {
        base.Setup(playerCount);

        _model = new KeoCoModel(playerCount, settings, Now + CountdownSeconds);
        _lastBeat = -1;
        _finished = false;
        _root.RemoveFromClassList(FinishedClass);
        _results.style.display = DisplayStyle.None;
        _results.RemoveFromClassList("is-in");

        BuildPullers();
        BuildSeats();
        _togglePlayers.text = playerCount == 2 ? "Đổi sang 2 đấu 2" : "Đổi sang 1 đấu 1";
    }

    public override void OnPlayerInput(int playerId, bool pressed)
    {
        if (!pressed || _finished) return;

        var result = _model.Tap(playerId, Now);
        if (result.Judgement != TapJudgement.Ignored) ShowFeedback(result);
    }

    public override int[] GetRanking() => RanksFromScores(_model.PlayerScores());

    public static string FunFactAt(double elapsed) => FunFacts[(int)(elapsed / FunFactSeconds) % FunFacts.Length];

    void Update()
    {
        if (_model == null) return;
        double now = Now;

        if (!IsRunning && !_finished && now >= _model.StartTime) Begin();

        if (IsRunning)
        {
            _model.Tick(now, Time.unscaledDeltaTime);
            if (_model.IsOver)
            {
                _finished = true;
                _finishedAt = now;
                Finish();
                ShowResults();
            }
        }

        UpdateView(now);
    }

    void TogglePlayerCount()
    {
        int next = PlayerCount == 2 ? 4 : 2;
        PlayerInputRouter.Instance.Configure(next);
        Setup(next);
    }

    void BackToMenu()
    {
        if (HasSession) GameManager.Instance.ReturnToMenu();
        else SceneManager.LoadScene("Menu");
    }

    void ShowResults()
    {
        _resultTitle.text = _model.WinningTeam < 0 ? "Hòa!" : $"{TeamName(_model.WinningTeam)} thắng!";

        bool tournament = HasSession && GameManager.Instance.Mode == SessionMode.Tournament;
        _continue.style.display = tournament ? DisplayStyle.Flex : DisplayStyle.None;
        _playAgain.style.display = tournament ? DisplayStyle.None : DisplayStyle.Flex;
        _backToMenu.style.display = tournament ? DisplayStyle.None : DisplayStyle.Flex;

        _root.AddToClassList(FinishedClass);
        _results.style.display = DisplayStyle.Flex;
        SeatPanels.Reveal(_results);
    }

    void UpdateView(double now)
    {
        _rope.style.translate = new Translate(_model.RopePosition * TrackHalfWidth, 0f);

        double left = _model.EndTime - now;
        _timer.text = Mathf.CeilToInt((float)System.Math.Max(0.0, System.Math.Min(left, settings.roundSeconds))).ToString();
        _timer.EnableInClassList("timer--final", left <= settings.rampSeconds);
        if (_finished) _resultFact.text = FunFactAt(now - _finishedAt);

        _center.text = CenterMessage(now);
        _togglePlayers.style.display = !HasSession && !IsRunning ? DisplayStyle.Flex : DisplayStyle.None;

        int beat = _model.BeatIndexAt(now);
        bool showBeat = IsRunning && beat >= 0;
        if (showBeat && beat != _lastBeat)
        {
            _lastBeat = beat;
            OnBeat(beat);
        }

        float phase = 0f, sinceBeat = 10f;
        if (showBeat)
        {
            double previous = _model.BeatTime(beat), next = _model.BeatTime(beat + 1);
            phase = (float)((now - previous) / (next - previous));
            sinceBeat = (float)(now - previous);
        }

        foreach (var seat in _seats)
        {
            // The ring closes onto the button exactly on the beat: tap when they meet.
            float ringScale = Mathf.Lerp(1.7f, 1f, phase);
            seat.Ring.style.display = showBeat ? DisplayStyle.Flex : DisplayStyle.None;
            seat.Ring.style.scale = new Scale(new Vector2(ringScale, ringScale));
            seat.Ring.style.opacity = Mathf.Lerp(0.15f, 0.9f, phase);

            float discScale = 1f + 0.12f * Mathf.Exp(-sinceBeat * 12f);
            seat.Disc.style.scale = new Scale(new Vector2(discScale, discScale));

            seat.Feedback.style.opacity = Mathf.Clamp01(1f - (float)(now - seat.FeedbackAt) / 0.6f);
            seat.Chant.style.opacity = showBeat ? Mathf.Clamp01(1f - sinceBeat * 1.5f) : 0f;
        }
    }

    void OnBeat(int beat)
    {
        for (int i = 0; i < _seats.Length; i++)
        {
            var chant = SeatLayout.TeamOf(i) == 0 ? LeftChant : RightChant;
            _seats[i].Chant.text = chant[beat % chant.Length];
        }
    }

    string CenterMessage(double now)
    {
        if (_finished) return "";

        double untilStart = _model.StartTime - now;
        if (untilStart > 0) return Mathf.CeilToInt((float)untilStart).ToString();
        return untilStart > -0.8 ? "Kéo!" : "";
    }

    string TeamName(int team)
    {
        if (PlayerCount == 2) return $"P{team + 1}";
        return $"P{team + 1} + P{team + 3}";
    }

    void ShowFeedback(TapResult result)
    {
        if (result.Synced)
        {
            SetFeedback(result.PlayerId, "Đồng lòng!", "feedback--sync");
            SetFeedback(SeatLayout.TeammateOf(result.PlayerId, PlayerCount), "Đồng lòng!", "feedback--sync");
            return;
        }

        switch (result.Judgement)
        {
            case TapJudgement.Perfect: SetFeedback(result.PlayerId, "Perfect!", "feedback--perfect"); break;
            case TapJudgement.Good: SetFeedback(result.PlayerId, "Good", "feedback--good"); break;
            default: SetFeedback(result.PlayerId, "Miss", "feedback--miss"); break;
        }
    }

    void SetFeedback(int playerId, string text, string className)
    {
        var seat = _seats[playerId];
        seat.FeedbackAt = Now;
        seat.Feedback.text = text;
        foreach (var feedbackClass in FeedbackClasses)
            seat.Feedback.EnableInClassList(feedbackClass, feedbackClass == className);
    }

    // Pullers hold the rope on their team's side, nearest the ribbon first.
    void BuildPullers()
    {
        foreach (var puller in _pullers) puller.RemoveFromHierarchy();
        _pullers.Clear();

        for (int id = 0; id < PlayerCount; id++)
        {
            int team = SeatLayout.TeamOf(id);
            int place = id / 2; // 0 = front, 1 = back (4 players)
            float x = (team == 0 ? -1f : 1f) * (240f + place * 130f);

            var puller = new VisualElement { pickingMode = PickingMode.Ignore };
            puller.AddToClassList("puller");
            puller.AddToClassList($"player--p{id + 1}");
            puller.style.translate = new Translate(x, 0f);
            puller.Add(new Label($"P{id + 1}") { pickingMode = PickingMode.Ignore });
            _rope.Add(puller);
            _pullers.Add(puller);
        }
    }

    void BuildSeats()
    {
        _seatLayer.Clear();
        var seats = SeatLayout.Get(PlayerCount);
        _seats = new SeatView[seats.Length];

        foreach (var seat in seats)
        {
            var element = seatTemplate.Instantiate();
            element.pickingMode = PickingMode.Ignore;
            element.AddToClassList("seat");
            element.AddToClassList($"player--p{seat.PlayerId + 1}");
            element.EnableInClassList("seat--duel", PlayerCount == 2);
            SeatPanels.Place(element, seat);
            element.Q<Label>("seatLabel").text = $"P{seat.PlayerId + 1}";
            _seatLayer.Add(element);

            _seats[seat.PlayerId] = new SeatView
            {
                Ring = element.Q("seatRing"),
                Disc = element.Q("seatDisc"),
                Feedback = element.Q<Label>("seatFeedback"),
                Chant = element.Q<Label>("seatChant"),
            };
        }
    }
}
