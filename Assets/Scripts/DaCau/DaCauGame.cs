using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

// Đá Cầu / Jegichagi, 2 to 4 players, last one with lives wins. Kick the shuttle as it reaches
// your seat: the ring closing on your button shows when. Feather and jegi take turns by rally.
public class DaCauGame : PanelMiniGame
{
    const float CountdownSeconds = 3f;
    const float StationInset = 150f;  // panel units from the seat button to where the shuttle lands
    const float ShuttleSize = 120f;   // matches .shuttle in DaCauHud.uss
    const float ShadowSize = 70f;     // matches .shuttle-shadow in DaCauHud.uss
    const float WindBend = 110f;      // panel units the wind pushes the flight sideways
    const float FeatherHeight = 190f;
    const float JegiHeight = 110f;

    public static readonly string[] Facts =
    {
        "Bạn có biết? Cầu đá Việt Nam thường làm từ lông gà, còn jegi của Hàn Quốc làm từ đồng xu bọc giấy hoặc vải.",
        "알고 있나요? 베트남의 제기는 주로 닭 깃털로 만들고, 한국의 제기는 엽전을 종이나 천으로 싸서 만듭니다.",
    };

    [SerializeField] DaCauSettings settings = new DaCauSettings();

    DaCauModel _model;
    double _startTime;
    VisualElement _shuttle;
    VisualElement _shadow;
    VisualElement _stageLayer;
    Label _wind;
    VisualElement[][] _lifeDots = Array.Empty<VisualElement[]>();
    Vector2[] _stations = Array.Empty<Vector2>();

    protected override string[] FunFacts => Facts;

    protected override void Bind(VisualElement root)
    {
        _shuttle = root.Q("shuttle");
        _shadow = root.Q("shuttleShadow");
        _stageLayer = root.Q("stageLayer");
        _wind = root.Q<Label>("windLabel");
        _stageLayer.RegisterCallback<GeometryChangedEvent>(_ => LayoutStations());
    }

    public override void Setup(int playerCount)
    {
        base.Setup(playerCount);

        _startTime = Now + CountdownSeconds;
        _model = new DaCauModel(playerCount, settings, _startTime, Environment.TickCount);
        _model.Missed += OnMissed;
        BuildLives();
        LayoutStations();
    }

    public override void OnPlayerInput(int playerId, bool pressed)
    {
        if (!pressed || RoundOver || _model == null) return;

        switch (_model.Tap(playerId, Now))
        {
            case KickResult.Perfect: Seats[playerId].ShowFeedback("Perfect!", "feedback--perfect", Now); break;
            case KickResult.Good: Seats[playerId].ShowFeedback("Good", "feedback--good", Now); break;
            case KickResult.TooEarly: Seats[playerId].ShowFeedback("Sớm quá!", "feedback--miss", Now); break;
        }
    }

    public override int[] GetRanking() => RanksFromScores(_model.PlayerScores());

    protected override void Update()
    {
        base.Update();
        if (_model == null) return;
        double now = Now;

        if (!IsRunning && !RoundOver && now >= _startTime) Begin();

        if (IsRunning)
        {
            _model.Tick(now);
            if (_model.IsOver) EndRound($"P{_model.Winner + 1} thắng!");
        }

        UpdateView(now);
    }

    void OnMissed(int playerId)
    {
        if (_model.LivesOf(playerId) == 0) Seats[playerId].Root.AddToClassList("is-out");
        Seats[playerId].ShowFeedback(_model.LivesOf(playerId) == 0 ? "Hết mạng!" : "Trượt!", "feedback--miss", Now);
        UpdateLives();
    }

    void UpdateView(double now)
    {
        Timer.text = _model.Rally.ToString();
        CenterMessage.text = RoundOver ? "" : CountdownText(_startTime - now, "Đá!");

        _wind.text = _model.Wind > 0 ? "Gió xuôi!" : _model.Wind < 0 ? "Gió ngược!" : "";
        _shuttle.EnableInClassList("is-jegi", _model.Kind == ShuttleKind.Jegi);

        for (int i = 0; i < Seats.Length; i++)
        {
            bool closing = IsRunning && _model.IsFlying && _model.To == i && now >= _model.ZoneStart;
            var ring = Seats[i].Ring;
            ring.style.display = closing ? DisplayStyle.Flex : DisplayStyle.None;
            if (!closing) continue;

            // The ring closes onto the button exactly as the shuttle arrives: kick when they meet.
            float closed = Mathf.Clamp01((float)((now - _model.ZoneStart) / (_model.Arrival - _model.ZoneStart)));
            float scale = Mathf.Lerp(1.8f, 1f, closed);
            ring.style.scale = new Scale(new Vector2(scale, scale));
            ring.style.opacity = Mathf.Lerp(0.3f, 1f, closed);
        }

        if (_stations.Length == 0) return;
        PlaceShuttle(now);
    }

    void PlaceShuttle(double now)
    {
        if (!_model.IsFlying)
        {
            // Dropped: it rests on the ground where the last player missed it.
            Draw(_stations[_model.To], 0f, 100f);
            return;
        }

        float t = Mathf.Clamp01((float)((now - _model.FlightStart) / (_model.Arrival - _model.FlightStart)));
        bool feather = _model.Kind == ShuttleKind.Feather;
        float travel = feather ? 1f - Mathf.Pow(1f - t, 1.4f) : t; // feathers float, then drop late

        var from = _stations[_model.From];
        var to = _stations[_model.To];
        var side = Vector2.Perpendicular((to - from).normalized);
        float arc = Mathf.Sin(travel * Mathf.PI);

        var ground = Vector2.Lerp(from, to, travel) + side * (arc * WindBend * _model.Wind);
        Draw(ground, arc * (feather ? FeatherHeight : JegiHeight), feather ? 0f : t * 720f);
    }

    void Draw(Vector2 ground, float height, float spin)
    {
        // Passes along the top edge fly lower, so the shuttle stays on screen.
        height = Mathf.Min(height, Mathf.Max(ground.y - ShuttleSize, 0f));

        _shadow.style.translate = new Translate(ground.x - ShadowSize / 2f, ground.y - ShadowSize / 2f);
        float shadowScale = 1f - height / 400f;
        _shadow.style.scale = new Scale(new Vector2(shadowScale, shadowScale));

        _shuttle.style.translate = new Translate(ground.x - ShuttleSize / 2f, ground.y - ShuttleSize / 2f - height);
        float scale = 0.8f + height / 300f;
        _shuttle.style.scale = new Scale(new Vector2(scale, scale));
        _shuttle.style.rotate = new Rotate(new Angle(spin));
    }

    void BuildLives()
    {
        _lifeDots = new VisualElement[PlayerCount][];
        for (int id = 0; id < PlayerCount; id++)
        {
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.AddToClassList("lives");
            _lifeDots[id] = new VisualElement[settings.startingLives];
            for (int life = 0; life < settings.startingLives; life++)
            {
                var dot = new VisualElement { pickingMode = PickingMode.Ignore };
                dot.AddToClassList("life");
                row.Add(dot);
                _lifeDots[id][life] = dot;
            }
            Seats[id].Root.Add(row);
        }
    }

    void UpdateLives()
    {
        for (int id = 0; id < _lifeDots.Length; id++)
            for (int life = 0; life < _lifeDots[id].Length; life++)
                _lifeDots[id][life].EnableInClassList("is-lost", life >= _model.LivesOf(id));
    }

    // Each player's landing spot sits just in front of their seat, toward the middle.
    void LayoutStations()
    {
        var size = new Vector2(_stageLayer.resolvedStyle.width, _stageLayer.resolvedStyle.height);
        if (float.IsNaN(size.x) || size.x < 1f || PlayerCount == 0) return;

        var seats = SeatLayout.Get(PlayerCount);
        var center = size / 2f;
        _stations = seats
            .OrderBy(seat => seat.PlayerId)
            .Select(seat =>
            {
                var position = new Vector2(seat.Anchor.x * size.x, (1f - seat.Anchor.y) * size.y);
                return position + (center - position).normalized * StationInset;
            })
            .ToArray();
    }
}
