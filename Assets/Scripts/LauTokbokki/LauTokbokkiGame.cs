using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

// The art from Assets/UI Assets, filled in by ProjectSetup when the scene is generated.
[Serializable]
public class LauTokbokkiArt
{
    public Sprite table;
    public Sprite flags;
    public Sprite potEmpty;
    public Sprite potFull;
    public Sprite vnPlayer;
    public Sprite krPlayer;
    [Tooltip("Indexed by Ingredient: Tteok, Eomuk, Kimchi, Enoki, Greens, Egg.")]
    public Sprite[] ingredients = new Sprite[6];

    public IEnumerable<Sprite> All => new[] { table, flags, potEmpty, potFull, vnPlayer, krPlayer }.Concat(ingredients ?? Array.Empty<Sprite>());
}

// Lẩu Tokbokki / Jeukseok tteokbokki, 2 or 4 players at once, each for themselves. Walk with your
// joystick, grab what your recipe needs off the spinning table and bring it to your pot; tap the
// joystick at a rival's pot to steal, and run into people to knock their ingredient loose.
public class LauTokbokkiGame : PanelMiniGame
{
    const float CountdownSeconds = 3f;
    const float FinalSeconds = 10f;
    const float ReferenceUnit = 120f;      // panel px per arena unit at 1080 tall
    const float PotSize = 1.6f;            // the pot art, wider than its solid circle
    const float PotItemSize = 0.36f;
    const float PotItemRing = 0.3f;
    const int PotItemSlots = 8;
    const float FlagsOfTable = 0.48f;      // of the table radius
    const float PlayerSize = 0.95f;
    const float RingSize = 0.86f;
    const float CarriedSize = 0.55f;
    const float FloorItemSize = 0.6f;
    const float CardWidth = 1f;            // matches .recipe-card at ReferenceUnit
    const float CardHeight = 176f / ReferenceUnit;
    const float FeedbackSeconds = 0.9f;
    const float PopSeconds = 0.3f;
    const float TapFlashSeconds = 0.15f;
    const float TapMaxTilt = 0.3f;
    const float KnobTravel = 50f;
    const float FloorFadeSeconds = 2f;

    public static readonly string[][] Facts =
    {
        new[]
        {
            "Bạn có biết? Tokbokki \"tự nấu\" là bánh gạo, chả cá, trứng và mì nấu ngay trên bàn trong nồi nước dùng cay.",
            "알고 있나요? 즉석떡볶이는 떡, 어묵, 계란, 라면을 테이블에서 바로 끓여 먹어요.",
            "Did you know? Jeukseok tteokbokki is rice cake, fish cake, egg and ramyeon cooked right at your table.",
        },
        new[]
        {
            "Bạn có biết? Người Việt có lẩu, người Hàn có jeongol: đều là món ăn chung một nồi.",
            "알고 있나요? 베트남에는 러우, 한국에는 전골: 한 냄비를 함께 나눠 먹어요.",
            "Did you know? Vietnam has lẩu, Korea has jeongol: both are shared from one pot.",
        },
        new[]
        {
            "Bạn có biết? Quán tokbokki tự chọn rất được giới trẻ Việt Nam yêu thích.",
            "알고 있나요? 베트남 젊은이들은 골라 먹는 떡볶이 뷔페를 아주 좋아해요.",
            "Did you know? Pick-your-own tteokbokki buffets are a favorite with young people in Vietnam.",
        },
    };

    static readonly Color TileLine = new Color32(0xE2, 0xD1, 0xAE, 0xFF);

    [SerializeField] LauTokbokkiSettings settings = new LauTokbokkiSettings();
    [SerializeField] LauTokbokkiArt art = new LauTokbokkiArt();

    LauTokbokkiModel _model;
    double _startTime;
    string[] _facts = Facts[0];
    float _unit = ReferenceUnit;
    Vector2 _origin;

    VisualElement _hud;
    VisualElement _stageLayer;
    VisualElement _floorDrawing;
    VisualElement _cardLayer;
    VisualElement _potLayer;
    VisualElement _floorItemLayer;
    VisualElement _tableLayer;
    VisualElement _playerLayer;
    VisualElement _feedbackLayer;
    Label _hint;

    VisualElement _table;
    VisualElement _flags;
    VisualElement[] _plates = Array.Empty<VisualElement>();
    readonly List<VisualElement> _floorItems = new List<VisualElement>();
    PlayerView[] _views = Array.Empty<PlayerView>();

    // Taps: a short press on the joystick that never really tilted it.
    double[] _pressedAt = Array.Empty<double>();
    float[] _pressTilt = Array.Empty<float>();
    double[] _tappedAt = Array.Empty<double>();

    sealed class PlayerView
    {
        public VisualElement Body;
        public VisualElement Ring;
        public VisualElement Sprite;
        public VisualElement Carried;
        public VisualElement[] Stars;
        public VisualElement Pot;
        public VisualElement PotArt;
        public VisualElement PerfectRing;
        public VisualElement[] PotItems;
        public VisualElement Card;
        public VisualElement[] RecipeSlots;
        public Label Count;
        public Label Feedback;
        public double FeedbackAt = double.NegativeInfinity;
        public VisualElement Knob;
    }

    public LauTokbokkiArt Art => art;

    protected override string[] FunFacts => _facts;

    protected override bool HasWinner => _model != null && _model.HasWinner;

    protected override void Bind(VisualElement root)
    {
        _hud = root.Q("lauTokbokkiHud");
        _stageLayer = root.Q("stageLayer");
        _floorDrawing = root.Q("floorDrawing");
        _cardLayer = root.Q("cardLayer");
        _potLayer = root.Q("potLayer");
        _floorItemLayer = root.Q("floorItemLayer");
        _tableLayer = root.Q("tableLayer");
        _playerLayer = root.Q("playerLayer");
        _feedbackLayer = root.Q("feedbackLayer");
        _hint = root.Q<Label>("hint");
        _floorDrawing.generateVisualContent += DrawFloor;
        _stageLayer.RegisterCallback<GeometryChangedEvent>(_ => _floorDrawing.MarkDirtyRepaint());
    }

    public override void Setup(int playerCount)
    {
        base.Setup(playerCount);

        int seed = Environment.TickCount;
        _facts = Facts[(seed & int.MaxValue) % Facts.Length];
        _startTime = Now + CountdownSeconds;
        _model = new LauTokbokkiModel(settings, playerCount, seed);
        _model.Event += OnModelEvent;
        _pressedAt = Enumerable.Repeat(double.NaN, playerCount).ToArray();
        _pressTilt = new float[playerCount];
        _tappedAt = Enumerable.Repeat(double.NegativeInfinity, playerCount).ToArray();
        BuildTable();
        BuildPlayers();
        _floorItemLayer.Clear();
        _floorItems.Clear();
        _floorDrawing.MarkDirtyRepaint();
    }

    public override void OnPlayerInput(int playerId, bool pressed)
    {
        if (_model == null || playerId >= PlayerCount) return;
        double now = Now;
        if (pressed)
        {
            _pressedAt[playerId] = now;
            _pressTilt[playerId] = PlayerInputRouter.Instance.Stick(playerId).magnitude;
            return;
        }

        bool tap = !double.IsNaN(_pressedAt[playerId]) && now - _pressedAt[playerId] < settings.tapSeconds && _pressTilt[playerId] <= TapMaxTilt;
        _pressedAt[playerId] = double.NaN;
        if (!tap || !IsRunning) return;
        _tappedAt[playerId] = now;
        _model.Action(playerId, now);
    }

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

        var router = PlayerInputRouter.Instance;
        for (int id = 0; id < PlayerCount; id++)
            if (!double.IsNaN(_pressedAt[id])) _pressTilt[id] = Mathf.Max(_pressTilt[id], router.Stick(id).magnitude);

        if (IsRunning)
        {
            for (int id = 0; id < PlayerCount; id++) _model.SetStick(id, router.Stick(id), now);
            _model.Advance(now);
            if (_model.IsOver) EndRound(Title);
        }

        UpdateView(now);
    }

    string Title() => _model.HasWinner ? Localization.Won($"P{_model.WinnerPlayer + 1}") : Localization.Text("Hòa!");

    void OnModelEvent(LtEvent e)
    {
        switch (e.Kind)
        {
            case LtEventKind.Filled: ShowFeedback(e.Player, "+1", "feedback--perfect"); break;
            case LtEventKind.Spoiled: ShowFeedback(e.Player, "Hỏng!", "feedback--miss"); break;
            case LtEventKind.Stolen: ShowFeedback(e.Other, "Bị trộm!", "feedback--miss"); break;
        }
    }

    void ShowFeedback(int player, string text, string className)
    {
        if (player < 0 || player >= _views.Length) return;
        var label = _views[player].Feedback;
        label.text = Localization.Text(text);
        label.EnableInClassList("feedback--miss", className == "feedback--miss");
        label.EnableInClassList("feedback--perfect", className == "feedback--perfect");
        _views[player].FeedbackAt = Now;
    }

    static VisualElement Make(VisualElement parent, params string[] classes)
    {
        var element = new VisualElement { pickingMode = PickingMode.Ignore };
        foreach (var name in classes) element.AddToClassList(name);
        parent.Add(element);
        return element;
    }

    static void Show(VisualElement element, Sprite sprite) =>
        element.style.backgroundImage = sprite != null ? new StyleBackground(sprite) : new StyleBackground(StyleKeyword.None);

    Sprite IngredientArt(Ingredient item) => art.ingredients != null && (int)item < art.ingredients.Length ? art.ingredients[(int)item] : null;

    void BuildTable()
    {
        _tableLayer.Clear();
        _table = Make(_tableLayer, "piece", "table");
        Show(_table, art.table);
        _plates = new VisualElement[settings.plateSlots];
        for (int i = 0; i < _plates.Length; i++) _plates[i] = Make(_tableLayer, "piece", "plate");
        _flags = Make(_tableLayer, "piece", "flags");
        Show(_flags, art.flags);
    }

    void BuildPlayers()
    {
        _cardLayer.Clear();
        _potLayer.Clear();
        _playerLayer.Clear();
        _feedbackLayer.Clear();
        _views = new PlayerView[PlayerCount];
        for (int id = 0; id < PlayerCount; id++)
        {
            string player = $"player--p{id + 1}";
            var view = new PlayerView();

            view.Pot = Make(_potLayer, "piece", "pot");
            view.PerfectRing = Make(view.Pot, "perfect-ring");
            view.PotArt = Make(view.Pot, "piece");
            view.PotItems = new VisualElement[PotItemSlots];
            for (int i = 0; i < PotItemSlots; i++)
            {
                view.PotItems[i] = Make(view.Pot, "piece", "pot-item");
                Make(view.PotItems[i], "spoiled-bar", "spoiled-bar--a");
                Make(view.PotItems[i], "spoiled-bar", "spoiled-bar--b");
            }

            view.Card = Make(_cardLayer, "recipe-card", player);
            var recipe = _model.RecipeOf(id);
            view.RecipeSlots = new VisualElement[recipe.Count];
            for (int i = 0; i < recipe.Count; i++)
            {
                view.RecipeSlots[i] = Make(view.Card, "recipe-slot");
                Show(view.RecipeSlots[i], IngredientArt(recipe[i]));
                Make(view.RecipeSlots[i], "recipe-check");
            }
            view.Count = new Label { pickingMode = PickingMode.Ignore };
            view.Count.AddToClassList("recipe-count");
            view.Card.Add(view.Count);

            view.Body = Make(_playerLayer, "piece", player);
            view.Ring = Make(view.Body, "player-ring");
            view.Sprite = Make(view.Body, "player-sprite");
            Show(view.Sprite, SeatLayout.TeamOf(id) == 0 ? art.vnPlayer : art.krPlayer);
            view.Carried = Make(view.Body, "carried");
            view.Stars = new[] { Make(view.Body, "stun-star"), Make(view.Body, "stun-star") };

            view.Feedback = new Label { pickingMode = PickingMode.Ignore };
            view.Feedback.AddToClassList("pot-feedback");
            _feedbackLayer.Add(view.Feedback);

            if (Seats[id] != null)
            {
                Seats[id].Root.AddToClassList("seat--joystick");
                view.Knob = Make(Seats[id].Root, "joystick-knob");
            }
            _views[id] = view;
        }
    }

    // Arena units (y up) to panel px (y down).
    Vector2 ToPanel(Vector2 arena) => _origin + new Vector2(arena.x, LauTokbokkiLayout.Height - arena.y) * _unit;

    void Place(VisualElement element, Vector2 arena, float widthUnits, float heightUnits)
    {
        float width = widthUnits * _unit;
        float height = heightUnits * _unit;
        var at = ToPanel(arena);
        element.style.width = width;
        element.style.height = height;
        element.style.translate = new Translate(at.x - width / 2f, at.y - height / 2f);
    }

    static void Round(VisualElement element, float diameter, float border)
    {
        var style = element.style;
        style.width = style.height = diameter;
        style.borderTopLeftRadius = style.borderTopRightRadius = style.borderBottomLeftRadius = style.borderBottomRightRadius = diameter / 2f;
        style.borderTopWidth = style.borderBottomWidth = style.borderLeftWidth = style.borderRightWidth = border;
    }

    void UpdateView(double now)
    {
        bool counting = !IsRunning && !RoundOver;
        double untilStart = _startTime - now;
        double timeLeft = _model.TimeLeft(RoundOver ? _model.SimulatedTime : now);
        int seconds = Mathf.CeilToInt((float)(counting && untilStart > 0 ? untilStart : timeLeft));
        Timer.text = counting ? seconds.ToString() : $"{seconds / 60}:{seconds % 60:00}";
        Timer.EnableInClassList("timer--final", IsRunning && timeLeft <= FinalSeconds);
        CenterMessage.text = counting ? Localization.Text("Nấu lẩu thôi!")
            : RoundOver && _model.HasWinner && _model.IsPerfect(_model.WinnerPlayer) ? Localization.Text("Lẩu hoàn hảo!") : "";
        _hint.EnableInClassList("is-hidden", !counting);

        var stage = new Vector2(_stageLayer.resolvedStyle.width, _stageLayer.resolvedStyle.height);
        if (float.IsNaN(stage.x) || stage.y < 1f) return;
        float unit = stage.y / LauTokbokkiLayout.Height;
        var origin = new Vector2((stage.x - LauTokbokkiLayout.Width * unit) / 2f, 0f);
        if (unit != _unit || origin != _origin) _floorDrawing.MarkDirtyRepaint();
        _unit = unit;
        _origin = origin;

        PlaceTable(now);
        PlaceFloorItems(now);
        for (int id = 0; id < PlayerCount; id++) PlacePlayer(id, now);
    }

    void PlaceTable(double now)
    {
        var layout = _model.Layout;
        float diameter = 2f * layout.TableRadius;
        var sprite = art.table;
        float aspect = sprite != null ? sprite.rect.height / sprite.rect.width : 1f;
        Place(_table, layout.TableCenter, diameter, diameter * aspect);
        _table.style.rotate = new Rotate(new Angle(_model.TableAngle * Mathf.Rad2Deg));

        float flags = FlagsOfTable * layout.TableRadius;
        Place(_flags, layout.TableCenter, flags, flags);

        for (int slot = 0; slot < _plates.Length; slot++)
        {
            var plate = _model.Plates[slot];
            var element = _plates[slot];
            element.EnableInClassList("is-empty", !plate.Item.HasValue);
            if (!plate.Item.HasValue) continue;
            Show(element, IngredientArt(plate.Item.Value));
            Place(element, _model.PlatePosition(slot), layout.PlateSize, layout.PlateSize);
            // A new plate pops in with a small overshoot (ease-out-back).
            float t = Mathf.Clamp01((float)(now - plate.FilledAt) / PopSeconds) - 1f;
            float scale = 1f + 2.70158f * t * t * t + 1.70158f * t * t;
            element.style.scale = new Scale(new Vector2(scale, scale));
        }
    }

    void PlaceFloorItems(double now)
    {
        var items = _model.FloorItems;
        while (_floorItems.Count < items.Count) _floorItems.Add(Make(_floorItemLayer, "piece", "floor-item"));
        for (int i = 0; i < _floorItems.Count; i++)
        {
            var element = _floorItems[i];
            element.EnableInClassList("is-hidden", i >= items.Count);
            if (i >= items.Count) continue;
            Show(element, IngredientArt(items[i].Item));
            Place(element, items[i].Position, FloorItemSize, FloorItemSize);
            // Blinks in its last seconds on the floor.
            double left = settings.floorSeconds - (_model.SimulatedTime - items[i].DroppedAt);
            element.EnableInClassList("is-fading", left < FloorFadeSeconds && (int)(left * 4) % 2 == 0);
        }
    }

    void PlacePlayer(int id, double now)
    {
        var view = _views[id];
        var layout = _model.Layout;
        var seat = SeatLayout.Get(PlayerCount)[id];

        // Pot, its contents, and the perfect state.
        var potCenter = layout.Pots[id];
        Place(view.Pot, potCenter, PotSize, PotSize);
        float potPx = PotSize * _unit;
        view.PotArt.style.width = view.PotArt.style.height = potPx;
        bool perfect = _model.IsPerfect(id);
        Show(view.PotArt, perfect ? art.potFull : art.potEmpty);
        view.Pot.EnableInClassList("is-perfect", perfect);
        Round(view.PerfectRing, potPx * 1.08f, 0.08f * _unit);
        view.PerfectRing.style.translate = new Translate(-potPx * 0.04f, -potPx * 0.04f);

        var pot = _model.PotOf(id);
        int ring = Mathf.Max(pot.Count, 5);
        for (int i = 0; i < view.PotItems.Length; i++)
        {
            var item = view.PotItems[i];
            bool shown = i < pot.Count && !perfect;
            item.EnableInClassList("is-hidden", !shown);
            if (!shown) continue;
            Show(item, IngredientArt(pot[i]));
            item.EnableInClassList("is-spoiled", _model.IsSpoiled(id, i));
            float angle = Mathf.PI / 2f - i * 2f * Mathf.PI / ring;
            var local = new Vector2(Mathf.Cos(angle), -Mathf.Sin(angle)) * (PotItemRing * _unit) + new Vector2(potPx, potPx) / 2f;
            float size = PotItemSize * _unit;
            item.style.width = item.style.height = size;
            item.style.translate = new Translate(local.x - size / 2f, local.y - size / 2f);
        }

        // Recipe card beside the pot, turned toward its player.
        var pen = layout.Pens[id];
        var cardCenter = PlayerCount == 4
            ? new Vector2(pen.center.x < layout.TableCenter.x ? pen.xMin + 0.55f : pen.xMax - 0.55f, pen.center.y)
            : new Vector2(pen.center.x, potCenter.y + LauTokbokkiLayout.PotRadius + 0.1f + CardWidth / 2f);
        var cardAt = ToPanel(cardCenter);
        float cardScale = _unit / ReferenceUnit;
        view.Card.style.translate = new Translate(cardAt.x - ReferenceUnit * CardWidth / 2f, cardAt.y - ReferenceUnit * CardHeight / 2f);
        view.Card.style.scale = new Scale(new Vector2(cardScale, cardScale));
        view.Card.style.rotate = new Rotate(new Angle(-seat.Rotation));
        UpdateRecipe(id, view);

        // Feedback near the pot, toward the table, facing its owner.
        var feedbackAt = ToPanel(potCenter + layout.OpenSide[id] * 0.95f);
        view.Feedback.style.translate = new Translate(feedbackAt.x, feedbackAt.y);
        view.Feedback.style.rotate = new Rotate(new Angle(-seat.Rotation));
        view.Feedback.style.scale = new Scale(new Vector2(cardScale, cardScale));
        view.Feedback.style.opacity = Mathf.Clamp01(1f - (float)(now - view.FeedbackAt) / FeedbackSeconds);

        // The player, facing where they walk, with what they carry floating above.
        var at = _model.PositionOf(id);
        float body = PlayerSize * _unit;
        Place(view.Body, at, PlayerSize, PlayerSize);
        float ringPx = RingSize * _unit;
        Round(view.Ring, ringPx, 0.06f * _unit);
        view.Ring.style.translate = new Translate((body - ringPx) / 2f, (body - ringPx) / 2f);
        view.Sprite.style.width = view.Sprite.style.height = body;
        view.Sprite.style.rotate = new Rotate(new Angle(90f - _model.FacingOf(id) * Mathf.Rad2Deg)); // the art faces up

        var carried = _model.CarriedBy(id);
        view.Carried.style.display = carried.HasValue ? DisplayStyle.Flex : DisplayStyle.None;
        if (carried.HasValue)
        {
            float size = CarriedSize * _unit;
            Show(view.Carried, IngredientArt(carried.Value));
            view.Carried.style.width = view.Carried.style.height = size;
            view.Carried.style.translate = new Translate((body - size) / 2f, -size * 0.45f);
        }

        bool stunned = _model.IsStunned(id);
        view.Body.EnableInClassList("is-stunned", stunned);
        for (int i = 0; i < view.Stars.Length; i++)
        {
            float angle = (float)now * 9f + i * Mathf.PI;
            view.Stars[i].style.translate = new Translate(body / 2f + Mathf.Cos(angle) * body * 0.35f, body * 0.1f + Mathf.Sin(angle) * body * 0.12f);
        }

        if (view.Knob != null)
        {
            var stick = IsRunning || !RoundOver ? PlayerInputRouter.Instance.Stick(id) : Vector2.zero;
            float radians = -seat.Rotation * Mathf.Deg2Rad;
            var local = new Vector2(stick.x * Mathf.Cos(radians) - stick.y * Mathf.Sin(radians), stick.x * Mathf.Sin(radians) + stick.y * Mathf.Cos(radians));
            view.Knob.style.translate = new Translate(local.x * KnobTravel, -local.y * KnobTravel);
            view.Knob.EnableInClassList("is-tapped", now - _tappedAt[id] < TapFlashSeconds);
        }
    }

    // Each recipe slot is ticked once a matching correct ingredient is in the pot.
    void UpdateRecipe(int id, PlayerView view)
    {
        var recipe = _model.RecipeOf(id);
        var pot = _model.PotOf(id);
        var inPot = new int[Enum.GetValues(typeof(Ingredient)).Length];
        for (int i = 0; i < pot.Count; i++)
            if (!_model.IsSpoiled(id, i)) inPot[(int)pot[i]]++;
        for (int i = 0; i < recipe.Count; i++)
        {
            bool filled = inPot[(int)recipe[i]] > 0;
            if (filled) inPot[(int)recipe[i]]--;
            view.RecipeSlots[i].EnableInClassList("is-filled", filled);
        }
        view.Count.text = $"{_model.CorrectCount(id)}/{recipe.Count}";
    }

    // Tile lines over the whole floor, and each pen: a tint and a fence on three sides.
    void DrawFloor(MeshGenerationContext context)
    {
        if (_model == null || _unit < 1f) return;
        var painter = context.painter2D;
        var size = _floorDrawing.contentRect.size;

        painter.strokeColor = TileLine;
        painter.lineWidth = 2f;
        painter.BeginPath();
        for (float x = _origin.x % _unit; x < size.x; x += _unit)
        {
            painter.MoveTo(new Vector2(x, 0f));
            painter.LineTo(new Vector2(x, size.y));
        }
        for (float y = 0f; y < size.y; y += _unit)
        {
            painter.MoveTo(new Vector2(0f, y));
            painter.LineTo(new Vector2(size.x, y));
        }
        painter.Stroke();

        var layout = _model.Layout;
        for (int id = 0; id < _model.PlayerCount; id++)
        {
            var color = SeatLayout.PlayerColors[id];
            var pen = layout.Pens[id];
            var topLeft = ToPanel(new Vector2(pen.xMin, pen.yMax));
            var bottomRight = ToPanel(new Vector2(pen.xMax, pen.yMin));
            painter.fillColor = new Color(color.r, color.g, color.b, 0.15f);
            painter.BeginPath();
            painter.MoveTo(topLeft);
            painter.LineTo(new Vector2(bottomRight.x, topLeft.y));
            painter.LineTo(bottomRight);
            painter.LineTo(new Vector2(topLeft.x, bottomRight.y));
            painter.ClosePath();
            painter.Fill();

            // The fence: every side except the one facing the table.
            var corners = new[]
            {
                new Vector2(pen.xMin, pen.yMax), new Vector2(pen.xMax, pen.yMax),
                new Vector2(pen.xMax, pen.yMin), new Vector2(pen.xMin, pen.yMin),
            };
            var open = layout.OpenSide[id];
            painter.strokeColor = color;
            painter.lineWidth = 0.1f * _unit;
            painter.lineCap = LineCap.Round;
            painter.lineJoin = LineJoin.Round;
            painter.BeginPath();
            bool drawing = false;
            for (int side = 0; side < 4; side++)
            {
                var from = corners[side];
                var to = corners[(side + 1) % 4];
                var middle = (from + to) / 2f - pen.center;
                bool isOpen = Vector2.Dot(middle.normalized, open) > 0.9f;
                if (isOpen)
                {
                    drawing = false;
                    continue;
                }
                if (!drawing) painter.MoveTo(ToPanel(from));
                painter.LineTo(ToPanel(to));
                drawing = true;
            }
            painter.Stroke();
        }
    }
}
