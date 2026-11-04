using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

// Tách Kẹo / Ppopgi, 2 or 4 players at once, each for themselves. Everyone steers a needle around the
// shape stamped in their own honeycomb candy with their own joystick; a cracked candy is replaced and
// the clock keeps running. Fastest clean cut wins, then whoever carved the most.
public class TachKeoGame : PanelMiniGame
{
    const float CountdownSeconds = 3f;
    const float FinalSeconds = 5f;
    const float FourCandyOfHeight = 0.213f;  // 230 px at 1080 tall
    const float TwoCandyOfHeight = 0.315f;   // 340 px
    const float FourShift = 80f;             // toward the screen's vertical midline, at 1080 tall
    const float TwoShift = 120f;
    const float TinScale = 1.12f;
    const float JoystickClearance = 85f + 14f; // the seat ring's radius (the knob stays inside it) plus a gap
    const float TinGap = 10f;
    const float KnobTravel = 50f;
    const float BreakSeconds = 0.6f;
    const float SlideInSeconds = 0.35f;
    const float LiftSeconds = 0.6f;
    const int BubbleCount = 26;
    const int ShardCount = 5;

    // One fact per round, each in Vietnamese, Korean and English.
    public static readonly string[][] Facts =
    {
        new[]
        {
            "Bạn có biết? Kẹo đường làm từ đường thắng chảy với một nhúm bột nở, ép dẹt rồi in hình.",
            "알고 있나요? 뽑기는 녹인 설탕에 소다를 조금 넣어 납작하게 누르고 모양을 찍어요.",
            "Did you know? Ppopgi is melted sugar with a pinch of baking soda, pressed flat and stamped with a shape.",
        },
        new[]
        {
            "Bạn có biết? Ở Hàn Quốc xưa, tách được hình mà không vỡ thì người bán tặng thêm một cái kẹo.",
            "알고 있나요? 옛날 한국에서는 모양을 깨지 않고 떼어내면 하나를 더 받았어요.",
            "Did you know? In old Korea, cutting the shape out cleanly won you another candy for free.",
        },
        new[]
        {
            "Bạn có biết? \"Dalgona\" bắt nguồn từ chữ 달다, nghĩa là \"ngọt\".",
            "알고 있나요? '달고나'는 '달다'에서 온 말이에요.",
            "Did you know? \"Dalgona\" comes from 달다, \"sweet\".",
        },
    };

    static readonly string[] ShapeNames = { "Cái ô", "Hình vuông", "Hình bầu dục", "Ngôi sao" };

    static readonly Color CandyColor = new Color32(0xE9, 0xA9, 0x3B, 0xFF);
    static readonly Color CandyEdge = new Color32(0xC9, 0x83, 0x21, 0xFF);
    static readonly Color BubbleLight = new Color(1f, 0.9f, 0.62f, 0.45f);
    static readonly Color BubbleDark = new Color(0.66f, 0.42f, 0.1f, 0.25f);
    static readonly Color StampColor = new Color32(0x7A, 0x4B, 0x12, 0xFF);
    static readonly Color CarvedColor = new Color32(0x5A, 0x32, 0x08, 0xFF);
    static readonly Color CrackColor = new Color32(0x3B, 0x2A, 0x1A, 0xFF);
    static readonly Color NeedleColor = new Color32(0x6E, 0x6E, 0x6E, 0xFF);
    static readonly Color PinHead = new Color32(0xD6, 0x33, 0x6C, 0xFF);
    static readonly Color Gold = new Color32(0xF2, 0xB2, 0x33, 0xFF);
    static readonly Color RingTrack = new Color(0f, 0f, 0f, 0.12f);
    static readonly Color Shadow = new Color(0.23f, 0.16f, 0.1f, 0.3f);

    [SerializeField] TachKeoSettings settings = new TachKeoSettings();

    TachKeoModel _model;
    double _startTime;
    string[] _facts = Facts[0];
    VisualElement _hud;
    VisualElement _stageLayer;
    VisualElement _seatLayer;
    VisualElement _tinLayer;
    CandyView[] _candies = Array.Empty<CandyView>();
    VisualElement[] _knobs = Array.Empty<VisualElement>();
    Label[] _statuses = Array.Empty<Label>();

    // One player's tin, drawn in candy space: center (Size / 2), +y away from the player.
    sealed class CandyView
    {
        public VisualElement Root;
        public VisualElement Drawing;
        public Label Caption;
        public Label ShapeName;
        public float CandyRadius;
        public Vector2[] Bubbles;     // candy units, z = radius
        public float[] BubbleSizes;
        public float[] CrackAngles;
        public float[] ShardAngles;
        public double RestartedAt = double.NegativeInfinity;
        public double Now;
    }

    protected override string[] FunFacts => _facts;

    protected override bool HasWinner => _model != null && _model.HasWinner;

    protected override void Bind(VisualElement root)
    {
        _hud = root.Q("tachKeoHud");
        _stageLayer = root.Q("stageLayer");
        _seatLayer = root.Q("seatLayer");
        _tinLayer = root.Q("tinLayer");
    }

    public override void Setup(int playerCount)
    {
        base.Setup(playerCount);

        int seed = Environment.TickCount;
        _facts = Facts[(seed & int.MaxValue) % Facts.Length];
        _startTime = Now + CountdownSeconds;
        _model = new TachKeoModel(settings, playerCount, seed);
        _model.Event += OnModelEvent;
        _hud.EnableInClassList("tach-keo--four", playerCount == 4);
        BuildCandies();
        BuildJoysticks();
    }

    // Presses mean nothing here: the needle follows the stick.
    public override void OnPlayerInput(int playerId, bool pressed) { }

    public override int[] GetRanking() => _model.Ranking();

    protected override void Update()
    {
        base.Update();
        if (_model == null) return;
        double now = Now;

        if (!IsRunning && !RoundOver && now >= _startTime)
        {
            Begin();
            _model.Start(_startTime);
        }

        if (IsRunning)
        {
            var router = PlayerInputRouter.Instance;
            for (int id = 0; id < PlayerCount; id++) _model.SetStick(id, ToCandy(router.Stick(id), id), now);
            _model.Advance(now);
            if (_model.IsOver) EndRound(Title);
        }

        UpdateView(now);
    }

    string Title()
    {
        if (!_model.HasWinner) return Localization.Text("Hòa!");
        var ranks = _model.Ranking();
        return Localization.Won(string.Join(" + ", Enumerable.Range(0, ranks.Length).Where(id => ranks[id] == 1).Select(id => $"P{id + 1}")));
    }

    void OnModelEvent(TachKeoEvent e)
    {
        if (e.Player < 0 || e.Player >= Seats.Length || Seats[e.Player] == null) return;
        switch (e.Kind)
        {
            case TachKeoEventKind.Broke: Seats[e.Player].ShowFeedback("Vỡ rồi!", "feedback--miss", Now); break;
            case TachKeoEventKind.Finished: Seats[e.Player].ShowFeedback("Xong!", "feedback--perfect", Now); break;
            case TachKeoEventKind.Restarted: _candies[e.Player].RestartedAt = Now; break;
        }
    }

    // The stick in screen space, turned so "away from the player" is +y on their candy.
    static Vector2 ToCandy(Vector2 stick, int playerId, int playerCount) =>
        Rotate(stick, -SeatLayout.Get(playerCount)[playerId].Rotation);

    Vector2 ToCandy(Vector2 stick, int playerId) => ToCandy(stick, playerId, PlayerCount);

    static Vector2 Rotate(Vector2 v, float degrees)
    {
        float radians = degrees * Mathf.Deg2Rad;
        float cos = Mathf.Cos(radians);
        float sin = Mathf.Sin(radians);
        return new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
    }

    void BuildCandies()
    {
        _tinLayer.Clear();
        _candies = new CandyView[PlayerCount];
        var random = new System.Random(PlayerCount);
        for (int id = 0; id < PlayerCount; id++)
        {
            var view = new CandyView
            {
                Root = new VisualElement { pickingMode = PickingMode.Ignore },
                Drawing = new VisualElement { pickingMode = PickingMode.Ignore },
                Caption = new Label { pickingMode = PickingMode.Ignore },
                ShapeName = new Label { pickingMode = PickingMode.Ignore },
                Bubbles = new Vector2[BubbleCount],
                BubbleSizes = new float[BubbleCount],
                CrackAngles = Enumerable.Range(0, 4).Select(i => i * 90f + (float)random.NextDouble() * 50f).ToArray(),
                ShardAngles = Enumerable.Range(0, ShardCount).Select(i => (i + (float)random.NextDouble() * 0.4f) * 360f / ShardCount).ToArray(),
            };
            for (int i = 0; i < BubbleCount; i++)
            {
                float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                float distance = Mathf.Sqrt((float)random.NextDouble()) * 0.88f;
                view.Bubbles[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
                view.BubbleSizes[i] = 0.015f + (float)random.NextDouble() * 0.03f;
            }

            int player = id;
            view.Root.AddToClassList("tin");
            view.Drawing.AddToClassList("candy-drawing");
            view.Drawing.generateVisualContent += context => DrawCandy(context, player, view);
            view.Caption.AddToClassList("candy-caption");
            view.ShapeName.AddToClassList("shape-name");
            view.Root.Add(view.Drawing);
            view.Root.Add(view.Caption);
            view.Root.Add(view.ShapeName);
            _tinLayer.Add(view.Root);
            _candies[id] = view;
        }
    }

    void BuildJoysticks()
    {
        _knobs = new VisualElement[PlayerCount];
        _statuses = new Label[PlayerCount];
        for (int id = 0; id < PlayerCount; id++)
        {
            if (Seats[id] == null) continue;
            _knobs[id] = new VisualElement { pickingMode = PickingMode.Ignore };
            _knobs[id].AddToClassList("joystick-knob");
            _statuses[id] = new Label { pickingMode = PickingMode.Ignore };
            _statuses[id].AddToClassList("joystick-status");
            Seats[id].Root.Add(_knobs[id]);
            Seats[id].Root.Add(_statuses[id]);
        }
    }

    void UpdateView(double now)
    {
        bool counting = !IsRunning && !RoundOver;
        double untilStart = _startTime - now;
        // The countdown shows in the pill too, since nobody reads the middle of the table the right way up.
        double timeLeft = _model.TimeLeft(RoundOver ? _model.SimulatedTime : now);
        Timer.text = Mathf.CeilToInt((float)(counting && untilStart > 0 ? untilStart : timeLeft)).ToString();
        Timer.EnableInClassList("timer--final", IsRunning && timeLeft <= FinalSeconds);
        CenterMessage.text = "";

        var stage = new Vector2(_stageLayer.resolvedStyle.width, _stageLayer.resolvedStyle.height);
        if (float.IsNaN(stage.x) || stage.y < 1f) return;
        var safe = _seatLayer.layout;
        var router = PlayerInputRouter.Instance;

        for (int id = 0; id < PlayerCount; id++)
        {
            var view = _candies[id];
            view.Now = now;
            PlaceCandy(id, view, stage, safe);
            view.Caption.text = Localization.Text(counting ? "Chuẩn bị tách kẹo!" : "Kéo cần để cắt theo đường");
            view.ShapeName.text = Localization.Text(ShapeNames[(int)_model.ShapeOf(id)]);
            // The how-to stays until the player starts carving.
            view.Caption.EnableInClassList("is-hidden", !counting && (_model.ProgressOf(id) > 0f || _model.StateOf(id) != CutState.Cutting || RoundOver));
            view.ShapeName.EnableInClassList("is-hidden", !counting);
            view.Drawing.MarkDirtyRepaint();

            if (_knobs[id] == null) continue;
            var stick = IsRunning || counting ? ToCandy(router.Stick(id), id) : Vector2.zero;
            _knobs[id].style.translate = new Translate(stick.x * KnobTravel, -stick.y * KnobTravel);
            UpdateStatus(id);
        }
    }

    void UpdateStatus(int id)
    {
        var label = _statuses[id];
        string name = $"P{id + 1}";
        var state = _model.StateOf(id);
        label.EnableInClassList("is-done", state == CutState.Done);
        label.EnableInClassList("is-broken", state == CutState.Broken);
        switch (state)
        {
            case CutState.Done: label.text = $"{name} · {_model.FinishTimeOf(id):0.0} s"; break;
            case CutState.Broken: label.text = $"{name} · {Localization.Text("Vỡ rồi!")}"; break;
            case CutState.Cutting: label.text = $"{name} · {Mathf.FloorToInt(_model.ProgressOf(id) * 100f)}%"; break;
            default: label.text = name; break;
        }
    }

    // Sizes come from the stage height, then the tin is kept inside its player's part of the table,
    // clear of their joystick (shrinking if there isn't room, e.g. 2 players on a 16:9 screen).
    void PlaceCandy(int id, CandyView view, Vector2 stage, Rect safe)
    {
        var seat = SeatLayout.Get(PlayerCount)[id];
        bool four = PlayerCount == 4;
        float unit = stage.y / 1080f;
        var cell = new Rect(seat.Zone.x * stage.x, (1f - seat.Zone.yMax) * stage.y, seat.Zone.width * stage.x, seat.Zone.height * stage.y);
        float towardMiddle = Mathf.Sign(stage.x / 2f - cell.center.x);
        var desired = cell.center + new Vector2(towardMiddle * (four ? FourShift : TwoShift) * unit, 0f);
        var joystick = new Vector2(safe.x + seat.Anchor.x * safe.width, safe.y + (1f - seat.Anchor.y) * safe.height);

        float tinRadius = (four ? FourCandyOfHeight : TwoCandyOfHeight) * stage.y * TinScale;
        var center = FitTin(cell, desired, ref tinRadius, joystick, JoystickClearance, TinGap * unit);

        view.CandyRadius = tinRadius / TinScale;
        var root = view.Root.style;
        root.width = root.height = 2f * tinRadius;
        root.borderTopLeftRadius = root.borderTopRightRadius = root.borderBottomLeftRadius = root.borderBottomRightRadius = tinRadius;
        root.translate = new Translate(center.x - tinRadius, center.y - tinRadius);
        root.rotate = new Rotate(new Angle(-seat.Rotation)); // seat rotation is counter-clockwise, USS is clockwise

        float r = view.CandyRadius;
        PlaceLabel(view.ShapeName, tinRadius, new Vector2(0f, -0.66f), r * 1.3f, r * 0.24f, r * 0.15f);
        PlaceLabel(view.Caption, tinRadius, new Vector2(0f, 0.7f), r * 1.4f, r * 0.26f, r * 0.1f);
    }

    static void PlaceLabel(Label label, float tinRadius, Vector2 at, float width, float height, float fontSize)
    {
        label.style.width = width;
        label.style.height = height;
        label.style.fontSize = fontSize;
        label.style.translate = new Translate(tinRadius + at.x * tinRadius / TinScale - width / 2f, tinRadius - at.y * tinRadius / TinScale - height / 2f);
    }

    public static Vector2 FitTin(Rect cell, Vector2 desired, ref float tinRadius, Vector2 joystick, float clearance, float gap)
    {
        for (int attempt = 0; attempt < 40; attempt++, tinRadius *= 0.97f)
        {
            float edge = tinRadius + gap;
            if (cell.width < 2f * edge || cell.height < 2f * edge) continue;
            var center = new Vector2(
                Mathf.Clamp(desired.x, cell.xMin + edge, cell.xMax - edge),
                Mathf.Clamp(desired.y, cell.yMin + edge, cell.yMax - edge));

            // Too close to the joystick: slide sideways, away from it.
            float need = tinRadius + clearance;
            var away = center - joystick;
            if (away.magnitude < need)
            {
                float side = Mathf.Sign(desired.x - joystick.x);
                center.x = joystick.x + side * Mathf.Sqrt(Mathf.Max(0f, need * need - away.y * away.y));
                if (center.x < cell.xMin + edge - 0.01f || center.x > cell.xMax - edge + 0.01f) continue;
            }
            return center;
        }
        return desired;
    }

    void DrawCandy(MeshGenerationContext context, int id, CandyView view)
    {
        // Before the first layout the candy has no size yet (and a zero dash pattern is an error).
        if (_model == null || id >= _model.PlayerCount || view.CandyRadius < 1f) return;
        var painter = context.painter2D;
        var size = view.Drawing.contentRect.size;
        var middle = size / 2f;
        float r = view.CandyRadius;
        double now = view.Now;
        var state = _model.StateOf(id);
        float crack = _model.CrackOf(id);

        DrawProgressRing(painter, middle, r, state == CutState.Done ? 1f : _model.ProgressOf(id), state == CutState.Done ? Gold : SeatLayout.PlayerColors[id]);

        if (state == CutState.Broken)
        {
            DrawShards(painter, view, middle, r, (float)(now - _model.BrokeAtOf(id)));
            return;
        }

        // A fresh candy slides in from the player's side; a badly cracked one trembles.
        float slide = Mathf.Clamp01((float)(now - view.RestartedAt) / SlideInSeconds);
        var offset = new Vector2(0f, (1f - slide) * (1f - slide) * 1.3f * r);
        if (crack >= 0.8f && state == CutState.Cutting)
            offset += new Vector2(Mathf.Sin((float)now * 70f), Mathf.Cos((float)now * 90f)) * (0.012f * r);
        var center = middle + offset;
        Vector2 At(Vector2 candy, float scale = 1f) => center + new Vector2(candy.x, -candy.y) * (r * scale);

        // The candy and its bubbles.
        painter.fillColor = CandyColor;
        painter.BeginPath();
        painter.Arc(center, r, 0f, 360f);
        painter.Fill();
        painter.strokeColor = CandyEdge;
        painter.lineWidth = Mathf.Max(2f, 0.03f * r);
        painter.Stroke();
        for (int i = 0; i < view.Bubbles.Length; i++)
        {
            painter.fillColor = i % 3 == 0 ? BubbleDark : BubbleLight;
            painter.BeginPath();
            painter.Arc(At(view.Bubbles[i]), view.BubbleSizes[i] * r, 0f, 360f);
            painter.Fill();
        }

        var outline = _model.OutlineOf(id);
        int count = outline.Count;

        if (state == CutState.Done)
        {
            // The shape lifts out with a little bounce.
            float since = (float)(now - (_model.StartTime + _model.FinishTimeOf(id)));
            float lift = 1.08f + 0.08f * Mathf.Exp(-6f * since) * Mathf.Sin(Mathf.Min(since, LiftSeconds * 3f) * 18f);
            var shadow = new Vector2(0.03f, -0.045f);
            FillShape(painter, outline, p => At(p + shadow, lift), Shadow);
            FillShape(painter, outline, p => At(p, lift), CandyEdge);
            painter.strokeColor = CarvedColor;
            painter.lineWidth = Mathf.Max(3f, 0.025f * r);
            painter.lineJoin = LineJoin.Round;
            painter.Stroke();
            return;
        }

        // The stamped line, dashed, then the carved parts as a dark groove.
        painter.lineWidth = Mathf.Max(2f, 0.013f * r);
        painter.strokeColor = StampColor;
        painter.lineCap = LineCap.Butt;
        painter.SetDashPattern(0.035f * r, 0.025f * r);
        painter.BeginPath();
        painter.MoveTo(At(outline[0]));
        for (int i = 1; i <= count; i++) painter.LineTo(At(outline[i % count]));
        painter.Stroke();
        painter.dashPattern = ReadOnlySpan<float>.Empty;

        painter.lineWidth = Mathf.Max(4f, 0.032f * r);
        painter.strokeColor = CarvedColor;
        painter.lineCap = LineCap.Round;
        painter.lineJoin = LineJoin.Round;
        for (int i = 0; i < count; i++)
        {
            if (!_model.IsCarved(id, i) || _model.IsCarved(id, (i + count - 1) % count) && i != 0) continue;
            // The start of a carved run: follow it to its end.
            painter.BeginPath();
            painter.MoveTo(At(outline[i]));
            int j = i;
            for (int steps = 0; steps < count && _model.IsCarved(id, j % count); steps++, j++) painter.LineTo(At(outline[(j + 1) % count]));
            painter.Stroke();
            if (j - i >= count) break;
        }

        // Cracks spread from the needle as the meter fills.
        var needle = _model.NeedleOf(id);
        int cracks = crack >= 0.8f ? 4 : crack >= 0.5f ? 2 : 0;
        float reach = crack >= 0.8f ? 0.3f : 0.18f;
        painter.strokeColor = CrackColor;
        painter.lineWidth = Mathf.Max(2f, 0.011f * r);
        for (int i = 0; i < cracks; i++)
        {
            float angle = view.CrackAngles[i] * Mathf.Deg2Rad;
            var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            var bend = new Vector2(-direction.y, direction.x);
            painter.BeginPath();
            painter.MoveTo(At(needle));
            painter.LineTo(At(needle + direction * reach * 0.4f + bend * 0.04f));
            painter.LineTo(At(needle + direction * reach * 0.7f - bend * 0.03f));
            painter.LineTo(At(needle + direction * reach));
            painter.Stroke();
        }

        // The needle: tip on the line, the pin leaning away from the player.
        var tip = At(needle);
        var head = tip + new Vector2(0.55f, -0.84f) * (0.26f * r);
        painter.strokeColor = NeedleColor;
        painter.lineWidth = Mathf.Max(3f, 0.02f * r);
        painter.lineCap = LineCap.Round;
        painter.BeginPath();
        painter.MoveTo(tip);
        painter.LineTo(head);
        painter.Stroke();
        painter.fillColor = PinHead;
        painter.BeginPath();
        painter.Arc(head, Mathf.Max(5f, 0.035f * r), 0f, 360f);
        painter.Fill();
    }

    static void DrawProgressRing(Painter2D painter, Vector2 middle, float r, float progress, Color color)
    {
        float radius = r * (1f + TinScale) / 2f;
        painter.lineWidth = Mathf.Max(4f, 0.04f * r);
        painter.lineCap = LineCap.Butt;
        painter.strokeColor = RingTrack;
        painter.BeginPath();
        painter.Arc(middle, radius, 0f, 360f);
        painter.Stroke();
        if (progress <= 0f) return;

        painter.strokeColor = color;
        painter.BeginPath();
        painter.Arc(middle, radius, -90f, -90f + 360f * Mathf.Min(progress, 0.9999f));
        painter.Stroke();
    }

    static void FillShape(Painter2D painter, System.Collections.Generic.IReadOnlyList<Vector2> outline, Func<Vector2, Vector2> at, Color color)
    {
        painter.fillColor = color;
        painter.BeginPath();
        painter.MoveTo(at(outline[0]));
        for (int i = 1; i < outline.Count; i++) painter.LineTo(at(outline[i]));
        painter.ClosePath();
        painter.Fill();
    }

    // The broken candy falls apart into pieces that slide away and fade.
    static void DrawShards(Painter2D painter, CandyView view, Vector2 middle, float r, float since)
    {
        float t = Mathf.Clamp01(since / BreakSeconds);
        if (t >= 1f) return;
        float fade = 1f - t;
        for (int i = 0; i < view.ShardAngles.Length; i++)
        {
            float from = view.ShardAngles[i];
            float to = view.ShardAngles[(i + 1) % view.ShardAngles.Length] + (i == view.ShardAngles.Length - 1 ? 360f : 0f);
            float middleAngle = (from + to) / 2f * Mathf.Deg2Rad;
            var center = middle + new Vector2(Mathf.Cos(middleAngle), Mathf.Sin(middleAngle)) * (t * 0.15f * r);

            painter.fillColor = new Color(CandyColor.r, CandyColor.g, CandyColor.b, fade);
            painter.strokeColor = new Color(CandyEdge.r, CandyEdge.g, CandyEdge.b, fade);
            painter.lineWidth = Mathf.Max(2f, 0.02f * r);
            painter.lineJoin = LineJoin.Round;
            painter.BeginPath();
            painter.MoveTo(center + new Vector2(Mathf.Cos(middleAngle), Mathf.Sin(middleAngle)) * (0.08f * r));
            painter.Arc(center, r * 0.9f, from, to);
            painter.ClosePath();
            painter.Fill();
            painter.Stroke();
        }
    }
}
