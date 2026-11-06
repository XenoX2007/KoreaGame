using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

// Kéo Co (tug of war), 1v1 or 2v2. Tap on the chant beat to pull; teammates hitting the
// same beat together get the "Đồng lòng" bonus. The HUD is UI Toolkit (Assets/UI/KeoCo).
// Online the host's model is the only one that pulls; joining phones keep a copy only for the beats,
// draw the rope where the host says, and show their judgements as the host sends them.
public class KeoCoGame : PanelMiniGame
{
    const float CountdownSeconds = 3f;
    const float TrackHalfWidth = 560f; // panel units from center to each win line; matches .win-line in KeoCoHud.uss
    const float RopeSmoothing = 18f;   // per second, how fast a joining phone's rope catches up with the host's
    const string RopeMessage = "keoco.rope"; // host -> clients: rope position
    const string TapMessage = "keoco.tap";   // host -> clients: a judged tap
    const string EndMessage = "keoco.end";   // host -> clients: the winning team, -1 for a draw

    public static readonly string[] Facts =
    {
        "Bạn có biết? Năm 2015, UNESCO ghi danh kéo co là di sản chung của Việt Nam, Hàn Quốc, Campuchia và Philippines.",
        "알고 있나요? 2015년 유네스코는 줄다리기를 베트남, 한국, 캄보디아, 필리핀의 공동 유산으로 등재했습니다.",
        "Did you know? In 2015, UNESCO inscribed tug of war as a shared heritage of Vietnam, Korea, Cambodia and the Philippines.",
    };

    static readonly string[] LeftChant = { "Hò", "dô", "ta!" };
    static readonly string[] RightChant = { "Yeong", "cha!" };

    [SerializeField] KeoCoSettings settings = new KeoCoSettings();

    KeoCoModel _model;
    VisualElement _rope;
    readonly List<VisualElement> _pullers = new List<VisualElement>();
    int _lastBeat = -1;
    float _remoteRope;
    float _shownRope;
    int _remoteWinner = -1;

    protected override string[] FunFacts => Facts;
    protected override bool HasWinner => WinningTeam >= 0;

    int WinningTeam => IsOnlineClient ? _remoteWinner : _model.WinningTeam;
    float RopePosition => IsOnlineClient ? _shownRope : _model.RopePosition;

    protected override void Bind(VisualElement root) => _rope = root.Q("rope");

    public override void Setup(int playerCount)
    {
        base.Setup(playerCount);

        _lastBeat = -1;
        _remoteRope = _shownRope = 0f;
        _remoteWinner = -1;
        // Online the round waits for the host to say when the countdown starts.
        _model = IsOnline ? null : new KeoCoModel(playerCount, settings, Now + CountdownSeconds);
        BuildPullers();
    }

    protected override void OnRoundScheduled(double countdownStart, int seed) =>
        _model = new KeoCoModel(PlayerCount, settings, countdownStart + CountdownSeconds);

    protected override void ListenOnline()
    {
        if (IsOnlineHost) return;

        Listen(RopeMessage, (sender, reader) => reader.ReadValueSafe(out _remoteRope));
        Listen(TapMessage, (sender, reader) =>
        {
            reader.ReadValueSafe(out int player);
            reader.ReadValueSafe(out int judgement);
            reader.ReadValueSafe(out bool synced);
            ShowFeedback(new TapResult(player, (TapJudgement)judgement, synced, 0f));
        });
        Listen(EndMessage, (sender, reader) =>
        {
            reader.ReadValueSafe(out _remoteWinner);
            _shownRope = _remoteRope;
            EndRound(ResultTitle);
        });
    }

    public override void OnPlayerInput(int playerId, bool pressed)
    {
        if (pressed) Pull(playerId, Now);
    }

    protected override void OnRemotePlayerInput(int playerId, bool pressed, double time)
    {
        if (pressed) Pull(playerId, time);
    }

    void Pull(int playerId, double time)
    {
        if (RoundOver || _model == null) return;

        var result = _model.Tap(playerId, time);
        if (result.Judgement == TapJudgement.Ignored) return;
        ShowFeedback(result);

        if (IsOnlineHost)
        {
            OnlineSession.Instance.Broadcast(TapMessage, (ref FastBufferWriter writer) =>
            {
                writer.WriteValueSafe(result.PlayerId);
                writer.WriteValueSafe((int)result.Judgement);
                writer.WriteValueSafe(result.Synced);
            });
        }
    }

    public override int[] GetRanking() => RanksFromScores(_model.PlayerScores());

    protected override void Update()
    {
        base.Update();
        if (_model == null) return;
        double now = Now;

        if (!IsRunning && !RoundOver && now >= _model.StartTime) Begin();

        if (IsOnlineClient)
        {
            _shownRope = Mathf.Lerp(_shownRope, _remoteRope, 1f - Mathf.Exp(-RopeSmoothing * Time.unscaledDeltaTime));
        }
        else if (IsRunning)
        {
            _model.Tick(now, Time.unscaledDeltaTime);
            if (IsOnlineHost) SendRope();
            if (_model.IsOver)
            {
                if (IsOnlineHost)
                {
                    int winner = _model.WinningTeam;
                    OnlineSession.Instance.Broadcast(EndMessage, (ref FastBufferWriter writer) => writer.WriteValueSafe(winner));
                }
                EndRound(ResultTitle);
            }
        }

        UpdateView(now);
    }

    // Every frame and unreliable: a lost position is replaced by the next one.
    void SendRope()
    {
        float rope = _model.RopePosition;
        OnlineSession.Instance.Broadcast(RopeMessage, (ref FastBufferWriter writer) => writer.WriteValueSafe(rope), NetworkDelivery.UnreliableSequenced);
    }

    string ResultTitle() => WinningTeam < 0 ? Localization.Text("Hòa!") : Localization.Won(TeamName(WinningTeam));

    void UpdateView(double now)
    {
        _rope.style.translate = new Translate(RopePosition * TrackHalfWidth, 0f);

        double left = _model.EndTime - now;
        Timer.text = Mathf.CeilToInt((float)System.Math.Max(0.0, System.Math.Min(left, settings.roundSeconds))).ToString();
        Timer.EnableInClassList("timer--final", left <= settings.rampSeconds);

        CenterMessage.text = RoundOver ? "" : CountdownText(_model.StartTime - now, "Kéo!");

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

        foreach (var seat in Seats)
        {
            if (seat == null) continue;

            // The ring closes onto the button exactly on the beat: tap when they meet.
            float ringScale = Mathf.Lerp(1.7f, 1f, phase);
            seat.Ring.style.display = showBeat ? DisplayStyle.Flex : DisplayStyle.None;
            seat.Ring.style.scale = new Scale(new Vector2(ringScale, ringScale));
            seat.Ring.style.opacity = Mathf.Lerp(0.15f, 0.9f, phase);

            float discScale = 1f + 0.12f * Mathf.Exp(-sinceBeat * 12f);
            seat.Disc.style.scale = new Scale(new Vector2(discScale, discScale));

            seat.Chant.style.opacity = showBeat ? Mathf.Clamp01(1f - sinceBeat * 1.5f) : 0f;
        }
    }

    void OnBeat(int beat)
    {
        for (int i = 0; i < Seats.Length; i++)
        {
            if (Seats[i] == null) continue;
            var chant = SeatLayout.TeamOf(i) == 0 ? LeftChant : RightChant;
            Seats[i].Chant.text = chant[beat % chant.Length];
        }
    }

    string TeamName(int team)
    {
        if (PlayerCount == 2) return $"P{team + 1}";
        return $"P{team + 1} + P{team + 3}";
    }

    void ShowFeedback(TapResult result)
    {
        var seat = Seats[result.PlayerId];
        if (result.Synced)
        {
            seat?.ShowFeedback("Đồng lòng!", "feedback--sync", Now);
            Seats[SeatLayout.TeammateOf(result.PlayerId, PlayerCount)]?.ShowFeedback("Đồng lòng!", "feedback--sync", Now);
            return;
        }

        switch (result.Judgement)
        {
            case TapJudgement.Perfect: seat?.ShowFeedback("Perfect!", "feedback--perfect", Now); break;
            case TapJudgement.Good: seat?.ShowFeedback("Good", "feedback--good", Now); break;
            default: seat?.ShowFeedback("Miss", "feedback--miss", Now); break;
        }
    }

    // Pullers hold the rope on their team's side, nearest the ribbon first, leaning back.
    void BuildPullers()
    {
        foreach (var puller in _pullers) puller.RemoveFromHierarchy();
        _pullers.Clear();

        for (int id = 0; id < PlayerCount; id++)
        {
            int team = SeatLayout.TeamOf(id);
            int place = id / 2; // 0 = front, 1 = back (4 players)
            float side = team == 0 ? -1f : 1f;

            var puller = new CharacterView(id);
            puller.Root.AddToClassList("puller");
            puller.Face(new Vector2(-side, 0f));
            puller.AddArm();
            puller.Root.style.translate = new Translate(side * (190f + place * 170f), 0f);
            puller.Root.style.rotate = new Rotate(new Angle(side * 6f));
            _rope.Add(puller.Root);
            _pullers.Add(puller.Root);
        }
    }
}
