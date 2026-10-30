using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// Kéo Co (tug of war), 1v1 or 2v2. Tap on the chant beat to pull; teammates hitting the
// same beat together get the "Đồng lòng" bonus. The HUD is UI Toolkit (Assets/UI/KeoCo).
public class KeoCoGame : PanelMiniGame
{
    const float CountdownSeconds = 3f;
    const float TrackHalfWidth = 560f; // panel units from center to each win line; matches .win-line in KeoCoHud.uss

    public static readonly string[] Facts =
    {
        "Bạn có biết? Năm 2015, UNESCO ghi danh kéo co là di sản chung của Việt Nam, Hàn Quốc, Campuchia và Philippines.",
        "알고 있나요? 2015년 유네스코는 줄다리기를 베트남, 한국, 캄보디아, 필리핀의 공동 유산으로 등재했습니다.",
    };

    static readonly string[] LeftChant = { "Hò", "dô", "ta!" };
    static readonly string[] RightChant = { "Yeong", "cha!" };

    [SerializeField] KeoCoSettings settings = new KeoCoSettings();

    KeoCoModel _model;
    VisualElement _rope;
    readonly List<VisualElement> _pullers = new List<VisualElement>();
    int _lastBeat = -1;

    protected override string[] FunFacts => Facts;

    protected override void Bind(VisualElement root) => _rope = root.Q("rope");

    public override void Setup(int playerCount)
    {
        base.Setup(playerCount);

        _model = new KeoCoModel(playerCount, settings, Now + CountdownSeconds);
        _lastBeat = -1;
        BuildPullers();
    }

    public override void OnPlayerInput(int playerId, bool pressed)
    {
        if (!pressed || RoundOver || _model == null) return;

        var result = _model.Tap(playerId, Now);
        if (result.Judgement != TapJudgement.Ignored) ShowFeedback(result);
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
            if (_model.IsOver)
                EndRound(_model.WinningTeam < 0 ? "Hòa!" : $"{TeamName(_model.WinningTeam)} thắng!");
        }

        UpdateView(now);
    }

    void UpdateView(double now)
    {
        _rope.style.translate = new Translate(_model.RopePosition * TrackHalfWidth, 0f);

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
            seat.ShowFeedback("Đồng lòng!", "feedback--sync", Now);
            Seats[SeatLayout.TeammateOf(result.PlayerId, PlayerCount)].ShowFeedback("Đồng lòng!", "feedback--sync", Now);
            return;
        }

        switch (result.Judgement)
        {
            case TapJudgement.Perfect: seat.ShowFeedback("Perfect!", "feedback--perfect", Now); break;
            case TapJudgement.Good: seat.ShowFeedback("Good", "feedback--good", Now); break;
            default: seat.ShowFeedback("Miss", "feedback--miss", Now); break;
        }
    }

    // Pullers hold the rope on their team's side, nearest the ribbon first.
    void BuildPullers()
    {
        foreach (var puller in _pullers) puller.RemoveFromHierarchy();
        _pullers.Clear();

        for (int id = 0; id < PlayerCount; id++)
        {
            int team = SeatLayout.TeamOf(id);
            int place = id / 2; // 0 = front, 1 = back (4 players)
            float x = (team == 0 ? -1f : 1f) * (240f + place * 130f);

            var puller = new VisualElement { pickingMode = PickingMode.Ignore };
            puller.AddToClassList("puller");
            puller.AddToClassList($"player--p{id + 1}");
            puller.style.translate = new Translate(x, 0f);
            puller.Add(new Label($"P{id + 1}") { pickingMode = PickingMode.Ignore });
            _rope.Add(puller);
            _pullers.Add(puller);
        }
    }
}
