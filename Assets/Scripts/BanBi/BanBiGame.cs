using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

// Bắn Bi / Guseul-chigi, 1v1 or 2v2, top-down team marbles in a big circle. Roll dice for the
// order on your own seat, then on your turn your team's hand slides along under the circle: press
// the shared shoot button to stop it, hold to pull back, release to shoot straight up.
// Vietnam (left) plays blue, Korea (right) red, as the marble art (VN (Blue).png, KR (Red).png) shows.
// The hands are the arm slices of VN.png / KR.png and the circle is a plain shape. Everything on
// the field is sized from the stage height.
public class BanBiGame : PanelMiniGame
{
    const float CountdownSeconds = 3f;
    const float ZoneRadiusOfHeight = 0.435f; // the circle's radius, as a fraction of the stage height
    const float ZoneCenterOfHeight = 0.45f;
    const float SeatGap = 36f;               // panel units between the circle's edge and a seat disc
    const float SeatHalf = 85f;              // half of .seat's 170
    const float ShootButtonOfHeight = 0.139f;
    const float ShootButtonGap = 130f;       // at 1080 tall, from the right end of the slide
    const float ShootNameOverhang = 22f;     // the name chip hangs below the button
    const float HandTipGap = 14f;            // panel units from the held marble's center to the fingertips
    const float PullBack = 84f;              // panel units the hand pulls down at full power
    const float FlickUp = 28f;
    const float MessageSeconds = 1.3f;
    const float FlickSeconds = 0.15f;
    const float OutFadeSeconds = 0.4f;
    const int AimDotCount = 14;
    const int StartRingDots = 32;
    const int FinalSeconds = 3;              // the countdown turns red
    // Side column, matching .team-count, .turn-row and .hint in BanBiHud.uss.
    const float CountHeight = 70f;
    const float TurnRowHeight = 100f;
    const float HintHeight = 64f;
    const float ColumnGap = 12f;
    const float SideColumnHeight = CountHeight + TurnRowHeight + HintHeight + 2f * ColumnGap;

    // The router id of the shared shoot button.
    public const int ShootId = SeatLayout.MaxPlayers;
    static readonly string[] ShootColors = { "shoot-button--p1", "shoot-button--p2", "shoot-button--p3", "shoot-button--p4" };

    public static readonly string[] Facts =
    {
        "Bạn có biết? Trẻ em Việt Nam và Hàn Quốc đều búng bi bằng ngón cái, ở sân trường hay đầu ngõ.",
        "알고 있나요? 베트남과 한국의 아이들 모두 학교 운동장이나 골목에서 엄지손가락으로 구슬을 튕겼어요.",
        "Did you know? Kids in both Vietnam and Korea flick marbles with their thumb, in schoolyards and alleys.",
    };

    [SerializeField] BanBiSettings settings = new BanBiSettings();

    BanBiModel _model;
    double _startTime;
    VisualElement _stageLayer;
    VisualElement _seatLayer;
    VisualElement _zone;
    VisualElement _startRing;
    VisualElement _marbleLayer;
    VisualElement _aimDots;
    VisualElement _hand;
    VisualElement _heldMarble;
    VisualElement _shootButton;
    Label _shootName;
    Label _hint;
    VisualElement _turnRow;
    Label _turnLabel;
    Label[] _teamCounts;
    readonly List<VisualElement> _marbles = new List<VisualElement>();
    readonly List<VisualElement> _shadows = new List<VisualElement>();
    readonly List<double> _outAt = new List<double>();
    readonly VisualElement[] _dots = new VisualElement[AimDotCount];
    readonly VisualElement[] _ringDots = new VisualElement[StartRingDots];
    readonly HashSet<int> _hadATurn = new HashSet<int>();
    Label[] _dice = Array.Empty<Label>();
    Label[] _orderBadges = Array.Empty<Label>();
    readonly List<BanBiEvent> _events = new List<BanBiEvent>();
    string _message;
    double _messageAt = double.NegativeInfinity;
    double _flickAt = double.NegativeInfinity;
    bool _shootHeld;
    bool _shootZoneRegistered;

    // Field geometry in panel units, updated every frame from the stage size.
    Vector2 _zoneCenter;
    float _zoneRadius;
    float MarbleSize => 2f * settings.marbleRadius * _zoneRadius;

    protected override string[] FunFacts => Facts;

    protected override bool HasWinner => _model != null && _model.WinnerTeam >= 0;

    protected override void Bind(VisualElement root)
    {
        _stageLayer = root.Q("stageLayer");
        _seatLayer = root.Q("seatLayer");
        _zone = root.Q("zone");
        _startRing = root.Q("startRing");
        _marbleLayer = root.Q("marbleLayer");
        _aimDots = root.Q("aimDots");
        _hand = root.Q("hand");
        _heldMarble = root.Q("heldMarble");
        _shootButton = root.Q("shootButton");
        _shootName = root.Q<Label>("shootName");
        _hint = root.Q<Label>("hint");
        _turnRow = root.Q("turnRow");
        _turnLabel = root.Q<Label>("turnLabel");
        _teamCounts = new[] { root.Q<Label>("countVN"), root.Q<Label>("countKR") };

        _aimDots.Clear();
        for (int i = 0; i < AimDotCount; i++)
        {
            _dots[i] = Piece("aim-dot");
            _aimDots.Add(_dots[i]);
        }
        _startRing.Clear();
        for (int i = 0; i < StartRingDots; i++)
        {
            _ringDots[i] = Piece("start-dot");
            _startRing.Add(_ringDots[i]);
        }
    }

    public override void Setup(int playerCount)
    {
        base.Setup(playerCount);

        _startTime = Now + CountdownSeconds;
        _model = new BanBiModel(settings, playerCount, Environment.TickCount);
        _model.Event += _events.Add;
        _events.Clear();
        _hadATurn.Clear();
        _message = null;
        _messageAt = double.NegativeInfinity;
        _flickAt = double.NegativeInfinity;
        _marbleLayer.Clear();
        _marbles.Clear();
        _shadows.Clear();
        _outAt.Clear();
        SyncMarbles();
        BuildDice();
    }

    // Seats stop their own die; during turns only the shared button shoots, for whoever's turn it is.
    public override void OnPlayerInput(int playerId, bool pressed)
    {
        if (playerId == ShootId) _shootHeld = pressed;
        if (!IsRunning || RoundOver || _model == null) return;

        double now = Now;
        _model.Advance(now);
        if (_model.Phase == BanBiPhase.DiceRoll)
        {
            if (playerId < PlayerCount) _model.Press(playerId, pressed, now);
        }
        else if (playerId == ShootId && _model.ActivePlayer >= 0)
        {
            _model.Press(_model.ActivePlayer, pressed, now);
        }
    }

    public override int[] GetRanking() => RanksFromScores(_model.PlayerScores());

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

        if (IsRunning) _model.Advance(now);
        HandleEvents(now);
        if (IsRunning && _model.Phase == BanBiPhase.Finished) EndRound(Title);

        UpdateView(now);
    }

    // Pausing disables this component; the zone comes back with the next frame after resuming.
    void OnDisable() => ClearShootZone();

    protected override void OnDestroy()
    {
        ClearShootZone();
        base.OnDestroy();
    }

    string Title()
    {
        if (_model.WinnerTeam < 0) return Localization.Text("Hòa!");
        var names = Enumerable.Range(0, PlayerCount).Where(id => SeatLayout.TeamOf(id) == _model.WinnerTeam).Select(id => $"P{id + 1}");
        return Localization.Won(string.Join(" + ", names));
    }

    // Events also arrive from presses, so they are shown once per frame, in order.
    void HandleEvents(double now)
    {
        SyncMarbles();
        foreach (var e in _events)
        {
            switch (e.Kind)
            {
                case BanBiEventKind.TurnStarted:
                    Say(string.Format(Localization.Text("Lượt của P{0}"), e.Player + 1), now);
                    break;
                case BanBiEventKind.BonusShot:
                    Say(Localization.Text("Thêm lượt!"), now);
                    break;
                case BanBiEventKind.Missed:
                    Say(Localization.Text("Hụt!"), now);
                    break;
                case BanBiEventKind.Shot:
                    _flickAt = now;
                    _hadATurn.Add(e.Player);
                    break;
                case BanBiEventKind.TurnSkipped:
                    _hadATurn.Add(e.Player);
                    break;
                case BanBiEventKind.MarbleOut:
                    _outAt[e.Player] = now;
                    break;
            }
        }
        _events.Clear();
    }

    void Say(string text, double now)
    {
        _message = text;
        _messageAt = now;
    }

    void UpdateView(double now)
    {
        var phase = _model.Phase;
        bool aiming = phase == BanBiPhase.Aiming || phase == BanBiPhase.Charging;
        bool turns = IsRunning && !RoundOver && (aiming || phase == BanBiPhase.Rolling);
        bool rolling = phase == BanBiPhase.DiceRoll || phase == BanBiPhase.Idle;

        if (!IsRunning && !RoundOver) CenterMessage.text = CountdownText(_startTime - now, "Đổ xúc xắc!");
        else if (phase == BanBiPhase.DiceRoll) CenterMessage.text = Localization.Text("Đổ xúc xắc!");
        else CenterMessage.text = !RoundOver && now - _messageAt < MessageSeconds ? _message : "";

        // The turn count shows through the whole turn; the countdown only while it runs.
        _turnRow.style.display = _model.ActiveTeam >= 0 && !RoundOver ? DisplayStyle.Flex : DisplayStyle.None;
        _turnLabel.text = string.Format(Localization.Text("Lượt {0}/{1}"), _model.Round, settings.shotsPerTeam);
        int secondsLeft = Mathf.CeilToInt((float)_model.TurnTimeLeft(now));
        Timer.text = secondsLeft.ToString();
        Timer.style.display = aiming ? DisplayStyle.Flex : DisplayStyle.None;
        Timer.EnableInClassList("timer--final", secondsLeft <= FinalSeconds);
        _teamCounts[0].text = $"VN ● {_model.TeamCount(0)}";
        _teamCounts[1].text = $"KR ● {_model.TeamCount(1)}";
        bool firstTurn = aiming && !RoundOver && !_hadATurn.Contains(_model.ActivePlayer);
        _hint.style.display = firstTurn ? DisplayStyle.Flex : DisplayStyle.None;

        for (int id = 0; id < Seats.Length; id++)
        {
            if (Seats[id] == null) continue;
            bool waiting = IsRunning && phase != BanBiPhase.DiceRoll && id != _model.ActivePlayer;
            Seats[id].Root.EnableInClassList("is-waiting", waiting);

            _dice[id].style.display = rolling ? DisplayStyle.Flex : DisplayStyle.None;
            _dice[id].text = _model.DiceFace(id, now).ToString();
            _dice[id].EnableInClassList("is-stopped", !_model.IsDieSpinning(id));

            int order = _model.OrderOf(id);
            _orderBadges[id].style.display = !rolling && order >= 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _orderBadges[id].text = (order + 1).ToString();
        }

        var stage = new Vector2(_stageLayer.resolvedStyle.width, _stageLayer.resolvedStyle.height);
        if (float.IsNaN(stage.x) || stage.x < 1f) return;
        _zoneRadius = stage.y * ZoneRadiusOfHeight;
        _zoneCenter = new Vector2(stage.x / 2f, stage.y * ZoneCenterOfHeight);
        var safe = _seatLayer.layout;
        if (float.IsNaN(safe.width) || safe.width < 1f) safe = new Rect(Vector2.zero, stage);

        PlaceZone(rolling);
        PlaceSeats(safe);
        PlaceSideColumns(stage, safe);
        PlaceMarbles(now);
        PlaceHand(now, aiming);
        PlaceAimLine(now, aiming);
        PlaceShootButton(stage, safe, turns, phase == BanBiPhase.Rolling);
    }

    // Zone units (the circle has radius 1, y up) to panel units.
    Vector2 ToStage(Vector2 zone) => _zoneCenter + new Vector2(zone.x, -zone.y) * _zoneRadius;

    void PlaceZone(bool rolling)
    {
        _zone.style.left = _zoneCenter.x - _zoneRadius;
        _zone.style.top = _zoneCenter.y - _zoneRadius;
        _zone.style.width = 2f * _zoneRadius;
        _zone.style.height = 2f * _zoneRadius;
        SetRadius(_zone, _zoneRadius);

        _startRing.style.display = rolling && !RoundOver ? DisplayStyle.Flex : DisplayStyle.None;
        for (int i = 0; i < StartRingDots; i++)
        {
            float angle = 2f * Mathf.PI * i / StartRingDots;
            var at = ToStage(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * settings.startArea);
            _ringDots[i].style.translate = new Translate(at.x, at.y);
        }
    }

    // In this game the seat discs sit just outside the circle, on the line from its center to
    // where SeatLayout puts them. Only the visuals move: the touch zones stay full-screen.
    void PlaceSeats(Rect safe)
    {
        if (IsOnline) return;
        foreach (var seat in SeatLayout.Get(PlayerCount))
        {
            var view = Seats[seat.PlayerId];
            if (view == null) continue;
            var normal = new Vector2(safe.x + seat.Anchor.x * safe.width, safe.y + (1f - seat.Anchor.y) * safe.height);
            var target = _zoneCenter + (normal - _zoneCenter).normalized * (_zoneRadius + SeatGap + SeatHalf);
            target.x = Mathf.Clamp(target.x, safe.xMin + SeatHalf, safe.xMax - SeatHalf);
            target.y = Mathf.Clamp(target.y, safe.yMin + SeatHalf, safe.yMax - SeatHalf);
            view.Root.style.translate = new Translate(target.x - normal.x, target.y - normal.y);
        }
    }

    // Counters sit in the strips beside the circle: high up with 2 players (above the seats); with 4,
    // the counter, turn row and hint are centered between the corner seats. The turn row (turn
    // count + countdown) and the first-turn hint follow the active team.
    void PlaceSideColumns(Vector2 stage, Rect safe)
    {
        float y = PlayerCount == 4
            ? stage.y / 2f - SideColumnHeight / 2f + CountHeight / 2f
            : 0.18f * stage.y;
        float left = (safe.xMin + _zoneCenter.x - _zoneRadius) / 2f;
        float right = (safe.xMax + _zoneCenter.x + _zoneRadius) / 2f;
        _teamCounts[0].style.translate = new Translate(left, y);
        _teamCounts[1].style.translate = new Translate(right, y);

        float x = _model.ActiveTeam == 1 ? right : left;
        float rowTop = y + CountHeight / 2f + ColumnGap;
        _turnRow.style.left = x;
        _turnRow.style.top = rowTop;
        _hint.style.translate = new Translate(x, rowTop + TurnRowHeight + ColumnGap);
    }

    void PlaceMarbles(double now)
    {
        float size = MarbleSize;
        var shadowOffset = new Vector2(0.1f, 0.12f) * size;
        for (int i = 0; i < _marbles.Count; i++)
        {
            var state = _model.Marbles[i];
            float fade = state.InPlay ? 1f : 1f - Mathf.Clamp01((float)((now - _outAt[i]) / OutFadeSeconds));
            var display = fade > 0f ? DisplayStyle.Flex : DisplayStyle.None;
            _marbles[i].style.display = display;
            _shadows[i].style.display = display;
            if (fade <= 0f) continue;

            var at = ToStage(state.Position);
            var scale = new Scale(Vector2.one * Mathf.Lerp(0.6f, 1f, fade));
            SizeMarble(_shadows[i], size);
            _shadows[i].style.translate = new Translate(at.x - size / 2f + shadowOffset.x, at.y - size / 2f + shadowOffset.y);
            _shadows[i].style.opacity = fade;
            _shadows[i].style.scale = scale;
            SizeMarble(_marbles[i], size);
            _marbles[i].style.translate = new Translate(at.x - size / 2f, at.y - size / 2f);
            _marbles[i].style.opacity = fade;
            _marbles[i].style.scale = scale;
        }
    }

    // The active team's hand sits under the circle: it slides while aiming, pulls back while
    // charging, and flicks up just after the release, when the shot marble takes over.
    void PlaceHand(double now, bool aiming)
    {
        int team = _model.ActiveTeam;
        _hand.style.display = team >= 0 && !RoundOver ? DisplayStyle.Flex : DisplayStyle.None;
        _heldMarble.style.display = team >= 0 && aiming ? DisplayStyle.Flex : DisplayStyle.None;
        if (team < 0) return;

        float power = _model.Phase == BanBiPhase.Charging ? _model.Power(now) : 0f;
        float flick = now - _flickAt < FlickSeconds ? FlickUp : 0f;
        var marble = ToStage(new Vector2(_model.HandX(now), settings.launchY)) + new Vector2(0f, power * PullBack - flick);

        _hand.style.translate = new Translate(marble.x, marble.y + HandTipGap);
        _hand.EnableInClassList("hand--vn", team == 0);
        _hand.EnableInClassList("hand--kr", team == 1);

        float size = MarbleSize;
        SizeMarble(_heldMarble, size);
        _heldMarble.style.translate = new Translate(marble.x - size / 2f, marble.y - size / 2f);
        _heldMarble.EnableInClassList("marble--vn", team == 0);
        _heldMarble.EnableInClassList("marble--kr", team == 1);
    }

    // Dots straight up from the held marble: to the far edge while aiming; while charging, as far
    // as the shot would roll on an empty circle, fading past the far edge where it would leave.
    void PlaceAimLine(double now, bool aiming)
    {
        _aimDots.style.display = aiming ? DisplayStyle.Flex : DisplayStyle.None;
        if (!aiming) return;

        float x = _model.HandX(now);
        bool charging = _model.Phase == BanBiPhase.Charging;
        float farEdge = Mathf.Sqrt(Mathf.Max(0f, 1f - x * x));
        float reach = charging ? _model.StoppingDistance(_model.Power(now)) : farEdge - settings.launchY;
        for (int i = 0; i < AimDotCount; i++)
        {
            var point = new Vector2(x, settings.launchY + reach * (i + 1) / AimDotCount);
            var at = ToStage(point);
            _dots[i].style.translate = new Translate(at.x, at.y);
            _dots[i].EnableInClassList("is-over", point.y > 0f && BanBiModel.IsOut(point));
        }
    }

    // Just right of the hand's slide, at the bottom. It shows only during turns, greyed while
    // marbles roll, and its touch zone exists only while it shows.
    void PlaceShootButton(Vector2 stage, Rect safe, bool turns, bool waiting)
    {
        _shootButton.style.display = turns ? DisplayStyle.Flex : DisplayStyle.None;
        if (!turns)
        {
            ClearShootZone();
            return;
        }

        float size = stage.y * ShootButtonOfHeight;
        var center = ToStage(new Vector2(settings.slideRange, settings.launchY)) + new Vector2(ShootButtonGap * stage.y / 1080f, 0f);
        center.x = Mathf.Min(center.x, safe.xMax - size / 2f);
        center.y = Mathf.Min(center.y, safe.yMax - size / 2f - ShootNameOverhang);
        _shootButton.style.width = size;
        _shootButton.style.height = size;
        SetRadius(_shootButton, size / 2f);
        _shootButton.style.translate = new Translate(center.x - size / 2f, center.y - size / 2f);

        // P1 is the base look; the others have their own seat color.
        for (int id = 1; id < ShootColors.Length; id++)
            _shootButton.EnableInClassList(ShootColors[id], _model.ActivePlayer == id);
        _shootButton.EnableInClassList("is-pressed", _shootHeld);
        _shootButton.EnableInClassList("is-disabled", waiting);
        _shootName.text = $"P{_model.ActivePlayer + 1}";

        // The router works in normalized screen space, y up.
        var root = _shootButton.panel.visualTree.layout;
        var bounds = new Rect(center.x - size / 2f, center.y - size / 2f, size, size);
        PlayerInputRouter.Instance.RegisterExtraZone(ShootId, new Rect(
            bounds.x / root.width,
            1f - bounds.yMax / root.height,
            bounds.width / root.width,
            bounds.height / root.height));
        _shootZoneRegistered = true;
    }

    void ClearShootZone()
    {
        if (!_shootZoneRegistered) return;
        _shootZoneRegistered = false;
        var router = PlayerInputRouter.Existing;
        if (router != null) router.ClearExtraZones();
    }

    // Shot marbles join the list, so elements are added as the model adds marbles.
    void SyncMarbles()
    {
        for (int i = _marbles.Count; i < _model.Marbles.Count; i++)
        {
            var shadow = Piece("marble-shadow");
            var marble = Piece("marble", _model.Marbles[i].Team == 0 ? "marble--vn" : "marble--kr");
            _shadows.Add(shadow);
            _marbles.Add(marble);
            _outAt.Add(double.PositiveInfinity);
            // Shadows stay below every marble.
            _marbleLayer.Insert(_shadows.Count - 1, shadow);
            _marbleLayer.Add(marble);
        }
    }

    // A marble, shadow or held marble at this size; its rim and shine are round too.
    static void SizeMarble(VisualElement marble, float size)
    {
        marble.style.width = size;
        marble.style.height = size;
        SetRadius(marble, size / 2f);
        foreach (var child in marble.Children()) SetRadius(child, size / 2f);
    }

    static void SetRadius(VisualElement element, float radius)
    {
        element.style.borderTopLeftRadius = radius;
        element.style.borderTopRightRadius = radius;
        element.style.borderBottomLeftRadius = radius;
        element.style.borderBottomRightRadius = radius;
    }

    void BuildDice()
    {
        _dice = new Label[PlayerCount];
        _orderBadges = new Label[PlayerCount];
        for (int id = 0; id < PlayerCount; id++)
        {
            _dice[id] = new Label { pickingMode = PickingMode.Ignore };
            _dice[id].AddToClassList("die");
            _orderBadges[id] = new Label { pickingMode = PickingMode.Ignore };
            _orderBadges[id].AddToClassList("order-badge");
            Seats[id]?.Root.Add(_dice[id]);
            Seats[id]?.Root.Add(_orderBadges[id]);
        }
    }

    static VisualElement Piece(params string[] classes)
    {
        var piece = new VisualElement { pickingMode = PickingMode.Ignore };
        foreach (var name in classes) piece.AddToClassList(name);
        return piece;
    }
}
