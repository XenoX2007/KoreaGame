using System;
using UnityEngine;
using UnityEngine.UIElements;

// Người Gỗ / Mugunghwa, 2 or 4 players. Hold to run toward the caller in the middle of the
// courtyard, let go before they look. The Vietnamese and Korean callers take turns chanting.
public class NguoiGoGame : PanelMiniGame
{
    const float CountdownSeconds = 3f;
    const float CallerRadius = 150f;  // panel units around the center where runners reach the caller
    const float StartInset = 190f;    // panel units from the seat button to the start of each lane
    const float RunnerSize = 88f;     // matches .runner in NguoiGoHud.uss

    public static readonly string[] Facts =
    {
        "Bạn có biết? Ở Hàn Quốc, người hô trò này đọc \"Hoa mugunghwa đã nở\". Mugunghwa là quốc hoa của Hàn Quốc.",
        "알고 있나요? 베트남에서는 이 놀이를 \"하나, 둘, 셋, 나무 사람!\"이라는 뜻의 \"Một hai ba người gỗ\"라고 외칩니다.",
    };

    [SerializeField] NguoiGoSettings settings = new NguoiGoSettings();

    NguoiGoModel _model;
    VisualElement _caller;
    VisualElement _callerRing;
    VisualElement _lanes;
    VisualElement _runnerLayer;
    VisualElement[] _runners = Array.Empty<VisualElement>();
    Vector2[] _laneStarts = Array.Empty<Vector2>();
    Vector2[] _laneEnds = Array.Empty<Vector2>();

    protected override string[] FunFacts => Facts;

    protected override void Bind(VisualElement root)
    {
        _caller = root.Q("caller");
        _callerRing = root.Q("callerRing");
        _lanes = root.Q("lanes");
        _runnerLayer = root.Q("runnerLayer");
        _lanes.generateVisualContent += DrawLanes;
        _runnerLayer.RegisterCallback<GeometryChangedEvent>(_ => LayoutLanes());
    }

    public override void Setup(int playerCount)
    {
        base.Setup(playerCount);

        _model = new NguoiGoModel(playerCount, settings, Now + CountdownSeconds, Environment.TickCount);
        _model.Caught += OnCaught;
        BuildRunners();
        LayoutLanes();

        // A finger may already be down when the round restarts.
        var router = PlayerInputRouter.Instance;
        for (int i = 0; i < playerCount; i++) _model.SetHeld(i, router.IsPressed(i));
    }

    public override void OnPlayerInput(int playerId, bool pressed)
    {
        if (_model != null) _model.SetHeld(playerId, pressed);
    }

    public override int[] GetRanking() => RanksFromScores(_model.PlayerScores());

    protected override void Update()
    {
        base.Update();
        if (_model == null) return;
        double now = Now;

        if (!IsRunning && !RoundOver && now >= _model.StartTime) Begin();

        if (IsRunning)
        {
            _model.Tick(now, Time.unscaledDeltaTime);
            if (_model.IsOver) EndRound(ResultTitle());
        }

        UpdateView(now);
    }

    string ResultTitle()
    {
        var winners = _model.Winners();
        if (winners.Count == 0) return "Hòa!";

        var names = new string[winners.Count];
        for (int i = 0; i < names.Length; i++) names[i] = $"P{winners[i] + 1}";
        return $"{string.Join(" + ", names)} thắng!";
    }

    void OnCaught(int playerId) => Seats[playerId].ShowFeedback("Bị bắt!", "feedback--miss", Now);

    void UpdateView(double now)
    {
        double left = _model.EndTime - now;
        Timer.text = Mathf.CeilToInt((float)Math.Max(0.0, Math.Min(left, settings.roundSeconds))).ToString();
        Timer.EnableInClassList("timer--final", left <= 10.0);
        CenterMessage.text = RoundOver ? "" : CountdownText(_model.StartTime - now, "Chạy!");

        var phase = IsRunning || RoundOver ? _model.Phase : CallerPhase.Chanting;
        bool looking = phase == CallerPhase.Turning || phase == CallerPhase.Watching;
        _caller.EnableInClassList("is-korean", _model.Version == ChantVersion.Korean);
        _caller.EnableInClassList("is-watching", looking);
        _callerRing.EnableInClassList("is-watching", phase == CallerPhase.Watching);

        // The tell is deliberately small: just the shoulders twitching, no color change.
        float twitch = phase == CallerPhase.Tell ? 3f : phase == CallerPhase.Fake ? 7f : 0f;
        _caller.style.rotate = new Rotate(new Angle(Mathf.Sin((float)now * 42f) * twitch));

        string chant = !IsRunning ? "" : phase == CallerPhase.Watching ? "Đứng im!" : _model.SungText(now);
        foreach (var seat in Seats)
        {
            seat.Chant.text = chant;
            seat.Chant.style.opacity = 1f;
        }

        for (int i = 0; i < _runners.Length; i++)
        {
            bool moving = IsRunning && _model.IsMoving(i);
            Seats[i].Ring.style.display = moving ? DisplayStyle.Flex : DisplayStyle.None;

            var position = Vector2.Lerp(_laneStarts[i], _laneEnds[i], _model.PositionOf(i));
            float bob = moving ? Mathf.Abs(Mathf.Sin((float)now * 14f)) * -10f : 0f;
            _runners[i].style.translate = new Translate(position.x - RunnerSize / 2f, position.y - RunnerSize / 2f + bob);
        }
    }

    void BuildRunners()
    {
        _runnerLayer.Clear();
        _runners = new VisualElement[PlayerCount];
        for (int id = 0; id < PlayerCount; id++)
        {
            var runner = new VisualElement { pickingMode = PickingMode.Ignore };
            runner.AddToClassList("runner");
            runner.AddToClassList($"player--p{id + 1}");
            runner.Add(new Label($"P{id + 1}") { pickingMode = PickingMode.Ignore });
            _runnerLayer.Add(runner);
            _runners[id] = runner;
        }
    }

    // Lanes run in a straight line from each seat to the caller.
    void LayoutLanes()
    {
        var size = new Vector2(_runnerLayer.resolvedStyle.width, _runnerLayer.resolvedStyle.height);
        if (float.IsNaN(size.x) || size.x < 1f || PlayerCount == 0) return;

        var seats = SeatLayout.Get(PlayerCount);
        var center = size / 2f;
        _laneStarts = new Vector2[seats.Length];
        _laneEnds = new Vector2[seats.Length];

        foreach (var seat in seats)
        {
            var from = new Vector2(seat.Anchor.x * size.x, (1f - seat.Anchor.y) * size.y);
            var direction = (center - from).normalized;
            _laneStarts[seat.PlayerId] = from + direction * StartInset;
            _laneEnds[seat.PlayerId] = center - direction * CallerRadius;
        }

        _lanes.MarkDirtyRepaint();
    }

    void DrawLanes(MeshGenerationContext context)
    {
        var painter = context.painter2D;
        painter.lineWidth = 8f;
        painter.lineCap = LineCap.Round;
        painter.strokeColor = new Color32(0xB9, 0x8F, 0x58, 0xFF);
        painter.SetDashPattern(2f, 26f);

        for (int i = 0; i < _laneStarts.Length; i++)
        {
            painter.BeginPath();
            painter.MoveTo(_laneStarts[i]);
            painter.LineTo(_laneEnds[i]);
            painter.Stroke();
        }
    }
}
