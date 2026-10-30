using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Kéo Co (tug of war), 1v1 or 2v2. Tap on the chant beat to pull; teammates hitting the
// same beat together get the "Đồng lòng" bonus. Placeholder art is generated in code.
public class KeoCoGame : MiniGameBase
{
    const float CountdownSeconds = 3f;
    const float TrackHalfWidth = 560f; // canvas units from center to each win line
    const float ResultInputDelay = 1.5f;

    static readonly string[] LeftChant = { "Hò", "dô", "ta!" };
    static readonly string[] RightChant = { "Yeong", "cha!" };
    static readonly Rect ToggleZone = new Rect(0.42f, 0f, 0.16f, 0.12f);

    static readonly Color FieldColor = new Color32(0xE8, 0xD8, 0xB0, 0xFF);
    static readonly Color LineColor = new Color32(0x8A, 0x6A, 0x45, 0xFF);
    static readonly Color RopeColor = new Color32(0x9C, 0x6B, 0x3A, 0xFF);
    static readonly Color RibbonColor = new Color32(0xD6, 0x33, 0x6C, 0xFF);
    static readonly Color InkColor = new Color32(0x3B, 0x2A, 0x1A, 0xFF);
    static readonly Color GoldColor = new Color32(0xFF, 0xB8, 0x1C, 0xFF);

    [SerializeField] KeoCoSettings settings = new KeoCoSettings();

    SeatCanvas _canvas;
    KeoCoModel _model;
    readonly List<GameObject> _built = new List<GameObject>();
    RectTransform _rope;
    Text _timer;
    Text _center;
    GameObject _toggle;
    SeatView[] _seats;
    int _lastBeat = -1;
    bool _finished;
    double _finishedAt;

    class SeatView
    {
        public RectTransform Button;
        public Image Ring;
        public Text Feedback;
        public Text Chant;
        public double FeedbackAt = double.NegativeInfinity;
    }

    // Frame clock for now; when recorded chants arrive, schedule them with AudioSource.PlayScheduled on the beat times.
    static double Now => Time.unscaledTimeAsDouble;

    protected override void Start()
    {
        _canvas = SeatCanvas.Create("KeoCoCanvas");
        UiBuilder.EnsureEventSystem();
        base.Start();
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        var router = PlayerInputRouter.Instance;
        if (router != null) router.IgnoredZones.Remove(ToggleZone);
    }

    public override void Setup(int playerCount)
    {
        base.Setup(playerCount);
        foreach (var go in _built) Destroy(go);
        _built.Clear();

        _model = new KeoCoModel(playerCount, settings, Now + CountdownSeconds);
        _lastBeat = -1;
        _finished = false;

        BuildField();
        BuildSeats();
        BuildPlayerCountToggle();
    }

    public override void OnPlayerInput(int playerId, bool pressed)
    {
        if (!pressed) return;

        if (_finished)
        {
            if (Now - _finishedAt >= ResultInputDelay) PlayAgain();
            return;
        }

        var result = _model.Tap(playerId, Now);
        if (result.Judgement != TapJudgement.Ignored) ShowFeedback(result);
    }

    public override int[] GetRanking() => RanksFromScores(_model.PlayerScores());

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
            }
        }

        UpdateView(now);
    }

    void PlayAgain()
    {
        if (GameManager.Instance != null) GameManager.Instance.Continue();
        else Setup(PlayerCount);
    }

    void UpdateView(double now)
    {
        _rope.anchoredPosition = new Vector2(_model.RopePosition * TrackHalfWidth, 0f);

        double left = _model.EndTime - now;
        _timer.text = Mathf.CeilToInt((float)System.Math.Min(left, settings.roundSeconds)).ToString();
        _timer.color = left <= settings.rampSeconds ? RibbonColor : InkColor;

        _center.text = CenterMessage(now);
        _toggle.SetActive(GameManager.Instance == null && !IsRunning);
        var router = PlayerInputRouter.Instance;
        router.IgnoredZones.Remove(ToggleZone);
        if (_toggle.activeSelf) router.IgnoredZones.Add(ToggleZone);

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
            seat.Ring.enabled = showBeat;
            seat.Ring.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.7f, 1f, phase);
            var ringColor = seat.Ring.color;
            ringColor.a = Mathf.Lerp(0.15f, 0.9f, phase);
            seat.Ring.color = ringColor;

            seat.Button.localScale = Vector3.one * (1f + 0.12f * Mathf.Exp(-sinceBeat * 12f));

            float fade = 1f - (float)(now - seat.FeedbackAt) / 0.6f;
            var feedbackColor = seat.Feedback.color;
            feedbackColor.a = Mathf.Clamp01(fade);
            seat.Feedback.color = feedbackColor;

            var chantColor = seat.Chant.color;
            chantColor.a = showBeat ? Mathf.Clamp01(1f - sinceBeat * 1.5f) : 0f;
            seat.Chant.color = chantColor;
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
        if (_finished)
        {
            string result = _model.WinningTeam < 0 ? "Hòa!" : $"{TeamName(_model.WinningTeam)} thắng!";
            return now - _finishedAt >= ResultInputDelay ? $"{result}\n<size=40>Chạm để chơi lại</size>" : result;
        }

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
        var seat = _seats[result.PlayerId];
        seat.FeedbackAt = Now;

        if (result.Synced)
        {
            seat.Feedback.text = "Đồng lòng!";
            seat.Feedback.color = GoldColor;
            // Show it to the teammate too: they earned it together.
            int mate = SeatLayout.TeammateOf(result.PlayerId, PlayerCount);
            _seats[mate].Feedback.text = "Đồng lòng!";
            _seats[mate].Feedback.color = GoldColor;
            _seats[mate].FeedbackAt = Now;
            return;
        }

        switch (result.Judgement)
        {
            case TapJudgement.Perfect:
                seat.Feedback.text = "Perfect!";
                seat.Feedback.color = GoldColor;
                break;
            case TapJudgement.Good:
                seat.Feedback.text = "Good";
                seat.Feedback.color = InkColor;
                break;
            default:
                seat.Feedback.text = "Miss";
                seat.Feedback.color = LineColor;
                break;
        }
    }

    void BuildField()
    {
        var field = UiBuilder.Rect(_canvas.transform, "KeoCo Field");
        field.SetAsFirstSibling();
        _built.Add(field.gameObject);
        UiBuilder.Image(field, FieldColor);

        var center = new Vector2(0.5f, 0.5f);
        UiBuilder.Image(UiBuilder.Rect(field, "Center Line", center, new Vector2(6f, 420f)), new Color(LineColor.r, LineColor.g, LineColor.b, 0.4f));
        UiBuilder.Image(UiBuilder.Rect(field, "Left Win Line", center, new Vector2(10f, 520f), new Vector2(-TrackHalfWidth, 0f)), LineColor);
        UiBuilder.Image(UiBuilder.Rect(field, "Right Win Line", center, new Vector2(10f, 520f), new Vector2(TrackHalfWidth, 0f)), LineColor);

        _rope = UiBuilder.Rect(field, "Rope", center, new Vector2(1100f, 18f));
        UiBuilder.Image(_rope, RopeColor);
        UiBuilder.Image(UiBuilder.Rect(_rope, "Ribbon", center, new Vector2(30f, 90f)), RibbonColor);

        // Pullers hold the rope on their team's side, nearest the ribbon first.
        for (int id = 0; id < PlayerCount; id++)
        {
            int team = SeatLayout.TeamOf(id);
            int rank = id / 2; // 0 = front, 1 = back (4 players)
            float x = (team == 0 ? -1f : 1f) * (240f + rank * 130f);
            var puller = UiBuilder.Rect(_rope, $"Puller P{id + 1}", center, new Vector2(100f, 100f), new Vector2(x, 0f));
            UiBuilder.Image(puller, SeatLayout.PlayerColors[id], UiBuilder.Circle);
            UiBuilder.Text(UiBuilder.Rect(puller, "Label"), $"P{id + 1}", 36, InkColor);
        }

        _timer = UiBuilder.Text(UiBuilder.Rect(field, "Timer", new Vector2(0.5f, 0.88f), new Vector2(200f, 100f)), "", 72, InkColor);
        _center = UiBuilder.Text(UiBuilder.Rect(field, "Center Message", new Vector2(0.5f, 0.7f), new Vector2(900f, 200f)), "", 96, InkColor);
        _center.supportRichText = true;
    }

    void BuildSeats()
    {
        var seats = SeatLayout.Get(PlayerCount);
        _seats = new SeatView[seats.Length];

        foreach (var seat in seats)
        {
            var color = SeatLayout.PlayerColors[seat.PlayerId];
            var root = _canvas.CreateSeatRoot(seat, new Vector2(170f, 170f));
            _built.Add(root.gameObject);

            var view = new SeatView();
            view.Ring = UiBuilder.Image(UiBuilder.Rect(root, "Beat Ring"), color, UiBuilder.Ring);
            view.Button = UiBuilder.Rect(root, "Button");
            UiBuilder.Image(view.Button, color, UiBuilder.Circle);
            UiBuilder.Text(UiBuilder.Rect(view.Button, "Label"), $"P{seat.PlayerId + 1}", 48, InkColor);
            // Local +y points away from the player, toward the middle of the table. In 1v1 the rope runs
            // along that axis, so the text moves to the player's right, beside the button.
            var textOffset = PlayerCount == 2 ? new Vector2(230f, -150f) : Vector2.zero;
            view.Feedback = UiBuilder.Text(UiBuilder.Rect(root, "Feedback", new Vector2(0.5f, 0.5f), new Vector2(400f, 80f), textOffset + new Vector2(0f, 190f)), "", 48, InkColor);
            view.Chant = UiBuilder.Text(UiBuilder.Rect(root, "Chant", new Vector2(0.5f, 0.5f), new Vector2(400f, 80f), textOffset + new Vector2(0f, 260f)), "", 56, InkColor);
            view.Feedback.fontStyle = view.Chant.fontStyle = FontStyle.Bold;
            _seats[seat.PlayerId] = view;
        }
    }

    // Only when the scene is played directly (no GameManager): switch 1v1 / 2v2 between rounds.
    void BuildPlayerCountToggle()
    {
        var rect = _canvas.CreateScreenRegion("Toggle 1v1/2v2", ToggleZone);
        rect.SetAsLastSibling();
        _built.Add(rect.gameObject);
        _toggle = rect.gameObject;

        var image = UiBuilder.Image(rect, new Color(1f, 1f, 1f, 0.85f));
        image.raycastTarget = true;
        rect.gameObject.AddComponent<Button>().onClick.AddListener(() =>
        {
            int next = PlayerCount == 2 ? 4 : 2;
            PlayerInputRouter.Instance.Configure(next);
            Setup(next);
        });
        UiBuilder.Text(UiBuilder.Rect(rect, "Label"), PlayerCount == 2 ? "1v1 → 2v2" : "2v2 → 1v1", 32, InkColor);
    }
}
