using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

// Đá Cầu / Jegichagi, 2 to 4 players, last one with lives wins. Kick the shuttle as it reaches
// your seat: the ring closing on your button shows when. Feather and jegi take turns by rally.
public class DaCauGame : PanelMiniGame
{
    const float CountdownSeconds = 3f;
    const float StationInset = 200f;  // panel units from the seat button to where the shuttle lands
    const float ShuttleSize = 120f;   // matches .shuttle in DaCauHud.uss
    const float ShadowSize = 70f;     // matches .shuttle-shadow in DaCauHud.uss
    const float WindBend = 110f;      // panel units the wind pushes the flight sideways
    const float FeatherHeight = 190f;
    const float JegiHeight = 110f;
    const float KickSeconds = 0.4f;
    const float KickHopHeight = 26f;
    const float KickTiltDegrees = 24f;

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
    VisualElement _playerLayer;
    Label _wind;
    VisualElement[][] _lifeDots = Array.Empty<VisualElement[]>();
    CharacterView[] _players = Array.Empty<CharacterView>();
    double[] _kickedAt = Array.Empty<double>();
    Vector2[] _stations = Array.Empty<Vector2>();

    protected override string[] FunFacts => Facts;

    protected override void Bind(VisualElement root)
    {
        _shuttle = root.Q("shuttle");
        _shadow = root.Q("shuttleShadow");
        _stageLayer = root.Q("stageLayer");
        _playerLayer = root.Q("playerLayer");
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
        BuildPlayers();
        LayoutStations();
    }

    public override void OnPlayerInput(int playerId, bool pressed)
    {
        if (!pressed || RoundOver || _model == null) return;

        switch (_model.Tap(playerId, Now))
        {
            case KickResult.Perfect:
                Seats[playerId].ShowFeedback("Perfect!", "feedback--perfect", Now);
                _kickedAt[playerId] = Now;
                break;
            case KickResult.Good:
                Seats[playerId].ShowFeedback("Good", "feedback--good", Now);
                _kickedAt[playerId] = Now;
                break;
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
            if (_model.IsOver) EndRound(() => Bilingual.Won($"P{_model.Winner + 1}"));
        }

        UpdateView(now);
    }

    void OnMissed(int playerId)
    {
        if (_model.LivesOf(playerId) == 0)
        {
            Seats[playerId].Root.AddToClassList("is-out");
            _players[playerId].Root.AddToClassList("is-out");
        }
        Seats[playerId].ShowFeedback(_model.LivesOf(playerId) == 0 ? "Hết mạng!" : "Trượt!", "feedback--miss", Now);
        UpdateLives();
    }

    void UpdateView(double now)
    {
        Timer.text = _model.Rally.ToString();
        CenterMessage.text = RoundOver ? "" : CountdownText(_startTime - now, "Đá!");

        _wind.text = _model.Wind > 0 ? Bilingual.Text("Gió xuôi!") : _model.Wind < 0 ? Bilingual.Text("Gió ngược!") : "";
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
        PlacePlayers(now);
        PlaceShuttle(now);
    }

    void PlacePlayers(double now)
    {
        var stage = new Vector2(_stageLayer.resolvedStyle.width, _stageLayer.resolvedStyle.height);
        for (int i = 0; i < _players.Length; i++)
        {
            float kick = Mathf.Clamp01((float)((now - _kickedAt[i]) / KickSeconds));
            float hop = Mathf.Sin(kick * Mathf.PI) * KickHopHeight;
            float scale = CharacterView.DepthScale(_stations[i].y / stage.y);
            _players[i].Stand(_stations[i] + new Vector2(0f, -hop), scale);

            // A kick swings back, then through toward the middle, settling as it ends.
            float swing = -Mathf.Sin(kick * Mathf.PI * 2f) * (1f - kick) * KickTiltDegrees;
            _players[i].Tilt(swing * Mathf.Sign(stage.x / 2f - _stations[i].x));
        }
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

    // Far players go in first, so nearer ones are drawn over them.
    void BuildPlayers()
    {
        _playerLayer.Clear();
        _players = new CharacterView[PlayerCount];
        _kickedAt = Enumerable.Repeat(double.NegativeInfinity, PlayerCount).ToArray();
        foreach (var seat in SeatLayout.Get(PlayerCount).OrderByDescending(seat => seat.Anchor.y))
        {
            var player = new CharacterView(seat.PlayerId);
            _playerLayer.Add(player.Root);
            _players[seat.PlayerId] = player;
        }
    }

    // Each player's landing spot is on the ground on their side of the courtyard. Across
    // the far and near sides they look at each other; alone on each side, across the middle.
    void LayoutStations()
    {
        var size = new Vector2(_stageLayer.resolvedStyle.width, _stageLayer.resolvedStyle.height);
        if (float.IsNaN(size.x) || size.x < 1f || PlayerCount == 0) return;

        var seats = SeatLayout.Get(PlayerCount);
        _stations = new Vector2[seats.Length];
        foreach (var seat in seats)
        {
            float side = Mathf.Sign(seat.Anchor.x - 0.5f);
            _stations[seat.PlayerId] = new Vector2(
                seat.Anchor.x * size.x - side * StationInset,
                CharacterView.GroundOf(seat) * size.y);

            float across = seat.Anchor.y - 0.5f;
            _players[seat.PlayerId].Face(across == 0f ? new Vector2(-side, 0f) : new Vector2(0f, across));
        }
    }
}
