using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

// Bắn Bi / Guseul-chigi, 1v1 or 2v2, top-down team marbles. Roll dice for the order, then on your
// turn your team's hand slides along the bottom of the square: press to stop it, hold to pull back,
// release to shoot straight up. Red (left) is Vietnam, blue (right) is Korea.
// The hands are the arm slices of VN.png / KR.png; the zone and marbles are plain shapes.
public class BanBiGame : PanelMiniGame
{
    const float CountdownSeconds = 3f;
    const float ZoneHalf = 300f;      // panel units, half of .zone's 600 x 600 in BanBiHud.uss
    const float ZoneCenterY = 0.42f;  // fraction of the stage height, matches .zone's top
    const float HandTipGap = 10f;     // panel units from the held marble's center to the fingertips
    const float PullBack = 60f;       // panel units the hand pulls down at full power
    const float FlickUp = 20f;
    const float MessageSeconds = 1.3f;
    const float FlickSeconds = 0.15f;
    const float OutFadeSeconds = 0.4f;
    const int AimDotCount = 14;

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
    VisualElement _marbleLayer;
    VisualElement _aimDots;
    VisualElement _hand;
    VisualElement _heldMarble;
    Label _handName;
    Label _hint;
    Label[] _teamCounts;
    readonly List<VisualElement> _marbles = new List<VisualElement>();
    readonly List<VisualElement> _shadows = new List<VisualElement>();
    readonly List<double> _outAt = new List<double>();
    readonly VisualElement[] _dots = new VisualElement[AimDotCount];
    Label[] _dice = Array.Empty<Label>();
    Label[] _orderBadges = Array.Empty<Label>();
    readonly List<BanBiEvent> _events = new List<BanBiEvent>();
    string _message;
    double _messageAt = double.NegativeInfinity;
    double _flickAt = double.NegativeInfinity;

    float MarbleSize => 2f * settings.marbleRadius * ZoneHalf; // matches .marble in BanBiHud.uss

    protected override string[] FunFacts => Facts;

    protected override bool HasWinner => _model != null && _model.WinnerTeam >= 0;

    protected override void Bind(VisualElement root)
    {
        _stageLayer = root.Q("stageLayer");
        _marbleLayer = root.Q("marbleLayer");
        _aimDots = root.Q("aimDots");
        _hand = root.Q("hand");
        _heldMarble = root.Q("heldMarble");
        _handName = root.Q<Label>("handName");
        _hint = root.Q<Label>("hint");
        _teamCounts = new[] { root.Q<Label>("countVN"), root.Q<Label>("countKR") };

        _aimDots.Clear();
        for (int i = 0; i < AimDotCount; i++)
        {
            _dots[i] = Piece("aim-dot");
            _aimDots.Add(_dots[i]);
        }
    }

    public override void Setup(int playerCount)
    {
        base.Setup(playerCount);

        _startTime = Now + CountdownSeconds;
        _model = new BanBiModel(settings, playerCount, Environment.TickCount);
        _model.Event += _events.Add;
        _events.Clear();
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

    public override void OnPlayerInput(int playerId, bool pressed)
    {
        if (!IsRunning || RoundOver || _model == null) return;
        _model.Press(playerId, pressed, Now);
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

        if (!IsRunning && !RoundOver) CenterMessage.text = CountdownText(_startTime - now, "Đổ xúc xắc!");
        else if (phase == BanBiPhase.DiceRoll) CenterMessage.text = Localization.Text("Đổ xúc xắc!");
        else CenterMessage.text = !RoundOver && now - _messageAt < MessageSeconds ? _message : "";

        Timer.text = aiming
            ? Mathf.CeilToInt((float)_model.TurnTimeLeft(now)).ToString()
            : string.Format(Localization.Text("Lượt {0}/{1}"), _model.Round, settings.shotsPerTeam);
        Timer.EnableInClassList("timer--turn", !aiming);
        _teamCounts[0].text = $"VN ● {_model.TeamCount(0)}";
        _teamCounts[1].text = $"KR ● {_model.TeamCount(1)}";
        _hint.style.display = aiming && !RoundOver ? DisplayStyle.Flex : DisplayStyle.None;

        for (int id = 0; id < Seats.Length; id++)
        {
            if (Seats[id] == null) continue;
            bool waiting = IsRunning && phase != BanBiPhase.DiceRoll && id != _model.ActivePlayer;
            Seats[id].Root.EnableInClassList("is-waiting", waiting);

            bool rolling = phase == BanBiPhase.DiceRoll || phase == BanBiPhase.Idle;
            _dice[id].style.display = rolling ? DisplayStyle.Flex : DisplayStyle.None;
            _dice[id].text = _model.DiceFace(id, now).ToString();
            _dice[id].EnableInClassList("is-stopped", !_model.IsDieSpinning(id));

            int order = _model.OrderOf(id);
            _orderBadges[id].style.display = !rolling && order >= 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _orderBadges[id].text = (order + 1).ToString();
        }

        var stage = new Vector2(_stageLayer.resolvedStyle.width, _stageLayer.resolvedStyle.height);
        if (float.IsNaN(stage.x) || stage.x < 1f) return;
        PlaceMarbles(stage, now);
        PlaceHand(stage, now, aiming);
        PlaceAimLine(stage, now, aiming);
    }

    // Zone units (the square spans -1..1, y up) to panel units.
    static Vector2 ToStage(Vector2 zone, Vector2 stage) =>
        new Vector2(stage.x / 2f + zone.x * ZoneHalf, stage.y * ZoneCenterY - zone.y * ZoneHalf);

    void PlaceMarbles(Vector2 stage, double now)
    {
        float size = MarbleSize;
        for (int i = 0; i < _marbles.Count; i++)
        {
            var state = _model.Marbles[i];
            float fade = state.InPlay ? 1f : 1f - Mathf.Clamp01((float)((now - _outAt[i]) / OutFadeSeconds));
            var display = fade > 0f ? DisplayStyle.Flex : DisplayStyle.None;
            _marbles[i].style.display = display;
            _shadows[i].style.display = display;
            if (fade <= 0f) continue;

            var at = ToStage(state.Position, stage);
            var scale = new Scale(Vector2.one * Mathf.Lerp(0.6f, 1f, fade));
            _shadows[i].style.translate = new Translate(at.x - size / 2f + 4f, at.y - size / 2f + 5f);
            _shadows[i].style.opacity = fade;
            _shadows[i].style.scale = scale;
            _marbles[i].style.translate = new Translate(at.x - size / 2f, at.y - size / 2f);
            _marbles[i].style.opacity = fade;
            _marbles[i].style.scale = scale;
        }
    }

    // The active team's hand sits under the square: it slides while aiming, pulls back while
    // charging, and flicks up just after the release, when the shot marble takes over.
    void PlaceHand(Vector2 stage, double now, bool aiming)
    {
        int team = _model.ActiveTeam;
        _hand.style.display = team >= 0 && !RoundOver ? DisplayStyle.Flex : DisplayStyle.None;
        _heldMarble.style.display = team >= 0 && aiming ? DisplayStyle.Flex : DisplayStyle.None;
        if (team < 0) return;

        float power = _model.Phase == BanBiPhase.Charging ? _model.Power(now) : 0f;
        float flick = now - _flickAt < FlickSeconds ? FlickUp : 0f;
        var marble = ToStage(new Vector2(_model.HandX(now), settings.launchY), stage) + new Vector2(0f, power * PullBack - flick);

        _hand.style.translate = new Translate(marble.x, marble.y + HandTipGap);
        _hand.EnableInClassList("hand--vn", team == 0);
        _hand.EnableInClassList("hand--kr", team == 1);
        _handName.text = $"P{_model.ActivePlayer + 1}";

        float size = MarbleSize;
        _heldMarble.style.translate = new Translate(marble.x - size / 2f, marble.y - size / 2f);
        _heldMarble.EnableInClassList("marble--red", team == 0);
        _heldMarble.EnableInClassList("marble--blue", team == 1);
    }

    // Dots straight up from the held marble: to the top edge while aiming; while charging, as far
    // as the shot would roll on an empty square, fading past the top edge where it would overshoot.
    void PlaceAimLine(Vector2 stage, double now, bool aiming)
    {
        _aimDots.style.display = aiming ? DisplayStyle.Flex : DisplayStyle.None;
        if (!aiming) return;

        bool charging = _model.Phase == BanBiPhase.Charging;
        float reach = charging ? _model.StoppingDistance(_model.Power(now)) : 1f - settings.launchY;
        float x = _model.HandX(now);
        for (int i = 0; i < AimDotCount; i++)
        {
            float y = settings.launchY + reach * (i + 1) / AimDotCount;
            var at = ToStage(new Vector2(x, y), stage);
            _dots[i].style.translate = new Translate(at.x, at.y);
            _dots[i].EnableInClassList("is-over", y > 1f);
        }
    }

    // Shot marbles join the list, so elements are added as the model adds marbles.
    void SyncMarbles()
    {
        for (int i = _marbles.Count; i < _model.Marbles.Count; i++)
        {
            var shadow = Piece("marble-shadow");
            var marble = Piece("marble", _model.Marbles[i].Team == 0 ? "marble--red" : "marble--blue");
            marble.Add(Piece("marble-rim"));
            marble.Add(Piece("marble-shine"));
            _shadows.Add(shadow);
            _marbles.Add(marble);
            _outAt.Add(double.PositiveInfinity);
            // Shadows stay below every marble.
            _marbleLayer.Insert(_shadows.Count - 1, shadow);
            _marbleLayer.Add(marble);
        }
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
