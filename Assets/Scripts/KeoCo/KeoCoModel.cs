using System;
using System.Collections.Generic;
using UnityEngine;

public enum TapJudgement
{
    Ignored, // before the round starts or after it ends
    Miss,    // off the beat, a second tap on a beat already scored, or tapping while grip is lost
    Good,
    Perfect,
}

public readonly struct TapResult
{
    public readonly int PlayerId;
    public readonly TapJudgement Judgement;
    public readonly bool Synced; // "Đồng lòng": teammates hit the same beat together
    public readonly float Impulse;

    public TapResult(int playerId, TapJudgement judgement, bool synced, float impulse)
    {
        PlayerId = playerId;
        Judgement = judgement;
        Synced = synced;
        Impulse = impulse;
    }
}

[Serializable]
public class KeoCoSettings
{
    public float roundSeconds = 30f;

    [Header("Chant tempo")]
    public float startBpm = 96f;
    public float endBpm = 126f;
    [Tooltip("The tempo ramps from start to end BPM over the last N seconds.")]
    public float rampSeconds = 10f;

    [Header("Timing windows (seconds from the beat)")]
    public float perfectWindow = 0.07f;
    public float goodWindow = 0.14f;

    [Header("Pull per tap")]
    public float perfectImpulse = 1f;
    public float goodImpulse = 0.6f;
    public float missImpulse = 0.1f;
    [Tooltip("After an off-beat tap the player loses grip: taps score nothing until they stop for this long.")]
    public float slipSeconds = 0.25f;
    [Tooltip("Extra impulse when both teammates hit the same beat within syncWindow.")]
    public float syncBonus = 0.5f;
    public float syncWindow = 0.1f;

    [Header("Rope")]
    [Tooltip("How far one unit of impulse moves the ribbon. The ribbon wins at ±1.")]
    public float pullPerImpulse = 0.06f;
    [Tooltip("How fast a pull settles. Higher = snappier, lower = more glide.")]
    public float damping = 5f;
    [Tooltip("At timeout, a ribbon closer than this to the center is a draw.")]
    public float drawMargin = 0.01f;
}

// Rules of Kéo Co, independent of rendering and input so they can be tested with explicit times.
// Team 0 (left) pulls the ribbon toward -1, team 1 (right) toward +1.
public class KeoCoModel
{
    readonly KeoCoSettings _settings;
    readonly int _playerCount;
    readonly List<double> _beats = new List<double>();
    readonly int[] _lastScoredBeat;
    readonly double[] _lastScoredTime;
    readonly double[] _gripLostUntil;
    float _velocity;

    public double StartTime { get; }
    public double EndTime => StartTime + _settings.roundSeconds;
    public float RopePosition { get; private set; }
    public bool IsOver { get; private set; }
    public int WinningTeam { get; private set; } = -1; // -1 = draw or not over

    public KeoCoModel(int playerCount, KeoCoSettings settings, double startTime)
    {
        _settings = settings;
        _playerCount = playerCount;
        StartTime = startTime;
        _lastScoredBeat = new int[playerCount];
        _lastScoredTime = new double[playerCount];
        _gripLostUntil = new double[playerCount];
        for (int i = 0; i < playerCount; i++) _lastScoredBeat[i] = -1;

        // Beat 0 lands on the start; one extra second of beats so "next beat" always exists.
        for (double t = startTime; t <= EndTime + 1.0; t += 60.0 / BpmAt(t - startTime))
            _beats.Add(t);
    }

    public float BpmAt(double elapsed)
    {
        double rampStart = _settings.roundSeconds - _settings.rampSeconds;
        if (elapsed <= rampStart) return _settings.startBpm;
        return Mathf.Lerp(_settings.startBpm, _settings.endBpm, (float)((elapsed - rampStart) / _settings.rampSeconds));
    }

    public double BeatTime(int index) => _beats[Mathf.Clamp(index, 0, _beats.Count - 1)];

    // Index of the last beat at or before time, or -1 before the first beat.
    public int BeatIndexAt(double time)
    {
        int index = _beats.BinarySearch(time);
        return index >= 0 ? index : ~index - 1;
    }

    public TapResult Tap(int playerId, double time)
    {
        if (IsOver || time < StartTime - _settings.goodWindow)
            return new TapResult(playerId, TapJudgement.Ignored, false, 0f);

        // Mashing keeps the grip lost, so it can't out-pull a player tapping on the beat.
        if (time < _gripLostUntil[playerId])
        {
            _gripLostUntil[playerId] = time + _settings.slipSeconds;
            return new TapResult(playerId, TapJudgement.Miss, false, 0f);
        }

        int beat = NearestBeat(time);
        var judgement = Judge(Math.Abs(time - _beats[beat]));
        if (judgement != TapJudgement.Miss && _lastScoredBeat[playerId] == beat)
            judgement = TapJudgement.Miss; // only one tap per beat can score

        float impulse = ImpulseFor(judgement);
        bool synced = false;

        if (judgement == TapJudgement.Miss)
        {
            _gripLostUntil[playerId] = time + _settings.slipSeconds;
        }
        else
        {
            _lastScoredBeat[playerId] = beat;
            _lastScoredTime[playerId] = time;

            int mate = SeatLayout.TeammateOf(playerId, _playerCount);
            if (mate >= 0 && _lastScoredBeat[mate] == beat && Math.Abs(time - _lastScoredTime[mate]) <= _settings.syncWindow)
            {
                synced = true;
                impulse += _settings.syncBonus;
            }
        }

        // Velocity is scaled so the total distance travelled by one tap equals impulse * pullPerImpulse.
        float direction = SeatLayout.TeamOf(playerId) == 0 ? -1f : 1f;
        _velocity += direction * impulse * _settings.pullPerImpulse * _settings.damping;
        return new TapResult(playerId, judgement, synced, impulse);
    }

    public void Tick(double time, float deltaTime)
    {
        if (IsOver) return;

        // Exact integration of exponentially damped velocity, so the pull distance doesn't depend on frame rate.
        float decay = Mathf.Exp(-_settings.damping * deltaTime);
        RopePosition += _velocity * (1f - decay) / _settings.damping;
        _velocity *= decay;

        if (Mathf.Abs(RopePosition) >= 1f)
        {
            RopePosition = Mathf.Sign(RopePosition);
            End(RopePosition < 0f ? 0 : 1);
        }
        else if (time >= EndTime)
        {
            End(Mathf.Abs(RopePosition) < _settings.drawMargin ? -1 : RopePosition < 0f ? 0 : 1);
        }
    }

    // Per-player score for ranking: teammates share their team's result.
    public float[] PlayerScores()
    {
        var scores = new float[_playerCount];
        for (int i = 0; i < _playerCount; i++)
        {
            int team = SeatLayout.TeamOf(i);
            scores[i] = IsOver
                ? (WinningTeam == team ? 1f : 0f)
                : (team == 0 ? -RopePosition : RopePosition);
        }
        return scores;
    }

    void End(int winningTeam)
    {
        IsOver = true;
        WinningTeam = winningTeam;
        _velocity = 0f;
    }

    int NearestBeat(double time)
    {
        int before = Mathf.Max(BeatIndexAt(time), 0);
        int after = Mathf.Min(before + 1, _beats.Count - 1);
        return time - _beats[before] <= _beats[after] - time ? before : after;
    }

    TapJudgement Judge(double offset)
    {
        if (offset <= _settings.perfectWindow) return TapJudgement.Perfect;
        if (offset <= _settings.goodWindow) return TapJudgement.Good;
        return TapJudgement.Miss;
    }

    float ImpulseFor(TapJudgement judgement)
    {
        switch (judgement)
        {
            case TapJudgement.Perfect: return _settings.perfectImpulse;
            case TapJudgement.Good: return _settings.goodImpulse;
            case TapJudgement.Miss: return _settings.missImpulse;
            default: return 0f;
        }
    }
}
