using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

// Người Gỗ / Mugunghwa, 2 or 4 players. Hold to run toward the caller in the middle of the
// courtyard, let go before they look. The chant alternates between Vietnamese and Korean.
// Online every phone plays the caller's timeline from the round's seed on the shared clock, so they all
// see the same turn. The host runs the runners JudgeDelay behind, applying every press at the moment it
// happened, so a release that reaches it late is still judged against what that player saw.
public class NguoiGoGame : PanelMiniGame
{
    const float CountdownSeconds = 3f;
    const float CallerReach = 170f;   // panel units beside the caller where runners reach them
    const float CallerGround = 0.72f; // fraction of the stage height; matches .caller in NguoiGoHud.uss
    const float StartInset = 190f;    // panel units from the seat button to the start of each lane
    const double JudgeDelay = 0.3;    // online, how far the host's runners trail the clock
    const string RunMessage = "nguoigo.run";       // host -> clients: each runner's position and whether they move
    const string CaughtMessage = "nguoigo.caught"; // host -> clients: a runner was caught
    const string EndMessage = "nguoigo.end";       // host -> clients: final positions

    readonly struct Hold
    {
        public readonly double Time;
        public readonly int Player;
        public readonly bool Held;

        public Hold(double time, int player, bool held)
        {
            Time = time;
            Player = player;
            Held = held;
        }
    }

    public static readonly string[] Facts =
    {
        "Bạn có biết? Ở Hàn Quốc, người hô trò này đọc \"Hoa mugunghwa đã nở\". Mugunghwa là quốc hoa của Hàn Quốc.",
        "알고 있나요? 베트남에서는 이 놀이를 \"하나, 둘, 셋, 나무 사람!\"이라는 뜻의 \"Một hai ba người gỗ\"라고 외칩니다.",
        "Did you know? In Korea the caller chants \"The mugunghwa has bloomed\", and in Vietnam \"One, two, three, wooden person!\"",
    };

    [SerializeField] NguoiGoSettings settings = new NguoiGoSettings();

    NguoiGoModel _model;       // the runners
    NguoiGoModel _callerClock; // the same seed with nobody running: the caller as everyone sees it now
    double _simTime;           // how far _model has run
    readonly List<Hold> _holds = new List<Hold>();
    VisualElement _caller;
    VisualElement _lanes;
    VisualElement _runnerLayer;
    CharacterView[] _runners = Array.Empty<CharacterView>();
    Vector2[] _laneStarts = Array.Empty<Vector2>();
    Vector2[] _laneEnds = Array.Empty<Vector2>();

    protected override string[] FunFacts => Facts;
    protected override bool HasWinner => _model.Winners().Count > 0;

    protected override void Bind(VisualElement root)
    {
        _caller = root.Q("caller");
        _lanes = root.Q("lanes");
        _runnerLayer = root.Q("runnerLayer");
        _lanes.generateVisualContent += DrawLanes;
        _runnerLayer.RegisterCallback<GeometryChangedEvent>(_ => LayoutLanes());
    }

    public override void Setup(int playerCount)
    {
        base.Setup(playerCount);

        // Online the round waits for the host's start time and seed.
        _model = null;
        _callerClock = null;
        _runnerLayer.Clear();
        _runners = Array.Empty<CharacterView>();
        _laneStarts = _laneEnds = Array.Empty<Vector2>();
        if (!IsOnline) StartRound(Now + CountdownSeconds, Environment.TickCount);
    }

    protected override void OnRoundScheduled(double countdownStart, int seed) => StartRound(countdownStart + CountdownSeconds, seed);

    void StartRound(double startTime, int seed)
    {
        _model = new NguoiGoModel(PlayerCount, settings, startTime, seed);
        _callerClock = new NguoiGoModel(PlayerCount, settings, startTime, seed);
        _simTime = startTime;
        _holds.Clear();
        if (!IsOnlineClient) _model.Caught += OnCaught;
        BuildRunners();
        LayoutLanes();

        // A finger may already be down when the round restarts.
        SyncHeld();
    }

    protected override void ListenOnline()
    {
        if (IsOnlineHost) return;

        Listen(RunMessage, (sender, reader) =>
        {
            if (_model == null) return;
            for (int i = 0; i < PlayerCount; i++)
            {
                reader.ReadValueSafe(out float position);
                reader.ReadValueSafe(out bool moving);
                _model.SetPosition(i, position);
                _model.SetHeld(i, moving);
            }
        });
        Listen(CaughtMessage, (sender, reader) =>
        {
            reader.ReadValueSafe(out int player);
            OnCaught(player);
        });
        Listen(EndMessage, (sender, reader) =>
        {
            if (_model == null || RoundOver) return;
            for (int i = 0; i < PlayerCount; i++)
            {
                reader.ReadValueSafe(out float position);
                _model.SetPosition(i, position);
                _model.SetHeld(i, false);
            }
            if (!IsRunning) Begin();
            EndRound(ResultTitle);
        });
    }

    protected override void OnResumed() => SyncHeld();

    void SyncHeld()
    {
        if (_model == null || IsOnlineClient) return;
        var router = PlayerInputRouter.Instance;
        for (int i = 0; i < PlayerCount; i++)
            if (!IsOnline || i == LocalPlayer) _holds.Add(new Hold(Now, i, router.IsPressed(i)));
    }

    public override void OnPlayerInput(int playerId, bool pressed)
    {
        if (_model != null && playerId < PlayerCount) _holds.Add(new Hold(Now, playerId, pressed));
    }

    protected override void OnRemotePlayerInput(int playerId, bool pressed, double time)
    {
        if (_model != null && playerId < PlayerCount) _holds.Add(new Hold(time, playerId, pressed));
    }

    public override int[] GetRanking() => RanksFromScores(_model.PlayerScores());

    protected override void Update()
    {
        base.Update();
        if (_model == null) return;
        double now = Now;

        if (!IsRunning && !RoundOver && now >= _model.StartTime) Begin();
        if (IsRunning) _callerClock.Tick(now, 0f);

        if (!IsOnlineClient && !RoundOver)
        {
            RunTo(IsOnline ? now - JudgeDelay : now);
            if (IsOnlineHost && StateDue()) SendRunners();
            if (_model.IsOver)
            {
                if (IsOnlineHost) SendEnd();
                EndRound(ResultTitle);
            }
        }

        UpdateView(now);
    }

    // Runs the runners up to this time, applying each press at its own moment, in the order they happened.
    void RunTo(double time)
    {
        _holds.Sort((a, b) => a.Time.CompareTo(b.Time));
        int used = 0;
        for (; used < _holds.Count && _holds[used].Time <= time && !_model.IsOver; used++)
        {
            TickTo(_holds[used].Time);
            _model.SetHeld(_holds[used].Player, _holds[used].Held);
        }
        _holds.RemoveRange(0, used);
        TickTo(time);
    }

    void TickTo(double time)
    {
        if (time <= _simTime || time <= _model.StartTime) return;
        _model.Tick(time, (float)(time - _simTime));
        _simTime = time;
    }

    void SendRunners()
    {
        var model = _model;
        int count = PlayerCount;
        OnlineSession.Instance.Broadcast(RunMessage, (ref FastBufferWriter writer) =>
        {
            for (int i = 0; i < count; i++)
            {
                writer.WriteValueSafe(model.PositionOf(i));
                writer.WriteValueSafe(model.IsMoving(i));
            }
        }, NetworkDelivery.UnreliableSequenced);
    }

    void SendEnd()
    {
        var model = _model;
        int count = PlayerCount;
        OnlineSession.Instance.Broadcast(EndMessage, (ref FastBufferWriter writer) =>
        {
            for (int i = 0; i < count; i++) writer.WriteValueSafe(model.PositionOf(i));
        });
    }

    string ResultTitle()
    {
        var winners = _model.Winners();
        if (winners.Count == 0) return Localization.Text("Hòa!");

        var names = new string[winners.Count];
        for (int i = 0; i < names.Length; i++) names[i] = $"P{winners[i] + 1}";
        return Localization.Won(string.Join(" + ", names));
    }

    void OnCaught(int playerId)
    {
        Seats[playerId]?.ShowFeedback("Bị bắt!", "feedback--miss", Now);
        if (IsOnlineHost) OnlineSession.Instance.Broadcast(CaughtMessage, (ref FastBufferWriter writer) => writer.WriteValueSafe(playerId));
    }

    void UpdateView(double now)
    {
        double left = _model.EndTime - now;
        Timer.text = Mathf.CeilToInt((float)Math.Max(0.0, Math.Min(left, settings.roundSeconds))).ToString();
        Timer.EnableInClassList("timer--final", left <= 10.0);
        CenterMessage.text = RoundOver ? "" : CountdownText(_model.StartTime - now, "Chạy!");

        var phase = IsRunning || RoundOver ? _callerClock.Phase : CallerPhase.Chanting;
        bool looking = phase == CallerPhase.Turning || phase == CallerPhase.Watching;
        _caller.EnableInClassList("is-watching", looking);

        // The tell is deliberately small: just the shoulders twitching, no color change.
        float twitch = phase == CallerPhase.Tell ? 3f : phase == CallerPhase.Fake ? 7f : 0f;
        _caller.style.rotate = new Rotate(new Angle(Mathf.Sin((float)now * 42f) * twitch));

        string chant = !IsRunning ? "" : phase == CallerPhase.Watching ? Localization.Text("Đứng im!") : _callerClock.SungText(now);
        foreach (var seat in Seats)
        {
            if (seat == null) continue;
            seat.Chant.text = chant;
            seat.Chant.style.opacity = 1f;
        }

        float stageHeight = _runnerLayer.resolvedStyle.height;
        for (int i = 0; i < _runners.Length && i < _laneStarts.Length; i++)
        {
            bool moving = IsRunning && _model.IsMoving(i);
            if (Seats[i] != null) Seats[i].Ring.style.display = moving ? DisplayStyle.Flex : DisplayStyle.None;

            var feet = Vector2.Lerp(_laneStarts[i], _laneEnds[i], _model.PositionOf(i));
            float scale = CharacterView.DepthScale(feet.y / stageHeight);
            float bob = moving ? Mathf.Abs(Mathf.Sin((float)now * 14f)) * -10f : 0f;
            _runners[i].Stand(feet + new Vector2(0f, bob), scale);
        }
    }

    // Far runners go in first, so nearer ones are drawn over them.
    void BuildRunners()
    {
        _runnerLayer.Clear();
        _runners = new CharacterView[PlayerCount];
        foreach (var seat in SeatLayout.Get(PlayerCount).OrderByDescending(seat => seat.Anchor.y))
        {
            var runner = new CharacterView(seat.PlayerId);
            _runnerLayer.Add(runner.Root);
            _runners[seat.PlayerId] = runner;
        }
    }

    // Lanes cross the ground from each player's side of the courtyard to the caller.
    void LayoutLanes()
    {
        var size = new Vector2(_runnerLayer.resolvedStyle.width, _runnerLayer.resolvedStyle.height);
        if (float.IsNaN(size.x) || size.x < 1f || PlayerCount == 0 || _runners.Length != PlayerCount) return;

        var seats = SeatLayout.Get(PlayerCount);
        _laneStarts = new Vector2[seats.Length];
        _laneEnds = new Vector2[seats.Length];

        foreach (var seat in seats)
        {
            float side = Mathf.Sign(seat.Anchor.x - 0.5f);
            float ground = CharacterView.GroundOf(seat);
            var start = new Vector2(seat.Anchor.x * size.x - side * StartInset, ground * size.y);
            var end = new Vector2(size.x / 2f + side * CallerReach, Mathf.Lerp(ground, CallerGround, 0.7f) * size.y);

            _laneStarts[seat.PlayerId] = start;
            _laneEnds[seat.PlayerId] = end;
            _runners[seat.PlayerId].Face(end - start);
        }

        _lanes.MarkDirtyRepaint();
    }

    void DrawLanes(MeshGenerationContext context)
    {
        var painter = context.painter2D;
        painter.lineWidth = 8f;
        painter.lineCap = LineCap.Round;
        painter.strokeColor = new Color32(0xE3, 0xCF, 0xA6, 0xFF);
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
