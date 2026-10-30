using System;
using System.Collections.Generic;
using UnityEngine;

public enum CallerPhase
{
    Chanting, // eyes covered, runners are safe
    Tell,     // shoulders twitch: the warning a sharp player can read
    Fake,     // pretends to look, then keeps chanting; nobody can be caught
    Turning,  // hands dropping: the last moment to let go
    Watching, // anyone still holding goes back to the start
}

public enum ChantVersion
{
    Vietnamese,
    Korean,
}

[Serializable]
public class NguoiGoSettings
{
    [Tooltip("Seconds of holding needed to run from the start to the caller.")]
    public float runSeconds = 7f;
    [Tooltip("Safety limit: when time runs out, whoever is closest wins.")]
    public float roundSeconds = 75f;

    [Header("Chant styles")]
    public float normalChantSeconds = 2.4f;
    public float fastChantSeconds = 1.1f;
    [Range(0f, 1f)] public float fastChance = 0.25f;
    [Tooltip("Chance the last syllable is drawn out, by a random hold between the two values below.")]
    [Range(0f, 1f)] public float stretchChance = 0.3f;
    public float stretchMinSeconds = 0.8f;
    public float stretchMaxSeconds = 1.8f;

    [Header("Turning around")]
    public float tellSeconds = 0.25f;
    [Tooltip("Reaction grace after the tell, before holding gets a player caught.")]
    public float turningSeconds = 0.2f;
    public float watchMinSeconds = 1.2f;
    public float watchMaxSeconds = 2.2f;

    [Header("Fake turns")]
    [Range(0f, 1f)] public float fakeChance = 0.3f;
    public float fakeSeconds = 0.35f;
}

// Rules of Người Gỗ / Mugunghwa, independent of rendering and input. The caller's timeline for
// each chant is generated up front from a seed, so tests can replay and inspect it exactly.
public class NguoiGoModel
{
    public const float CaughtPosition = 0f;

    static readonly string[] VietnameseChant = { "Một ", "hai ", "ba ", "người ", "gỗ!" };
    static readonly string[] KoreanChant = { "무", "궁", "화 ", "꽃", "이 ", "피", "었", "습", "니", "다!" };

    readonly struct Segment
    {
        public readonly CallerPhase Phase;
        public readonly double End;

        public Segment(CallerPhase phase, double end)
        {
            Phase = phase;
            End = end;
        }
    }

    readonly NguoiGoSettings _settings;
    readonly int _playerCount;
    readonly System.Random _random;
    readonly float[] _positions;
    readonly bool[] _held;
    readonly bool[] _caughtThisWatch;
    readonly List<Segment> _segments = new List<Segment>();
    readonly List<double> _syllableTimes = new List<double>();
    int _segment;

    public event Action<int> Caught;

    public double StartTime { get; }
    public double EndTime => StartTime + _settings.roundSeconds;
    public CallerPhase Phase { get; private set; } = CallerPhase.Chanting;
    public ChantVersion Version { get; private set; } = ChantVersion.Korean; // the first chant flips this to Vietnamese
    public bool IsOver { get; private set; }

    public NguoiGoModel(int playerCount, NguoiGoSettings settings, double startTime, int seed)
    {
        _settings = settings;
        _playerCount = playerCount;
        _random = new System.Random(seed);
        _positions = new float[playerCount];
        _held = new bool[playerCount];
        _caughtThisWatch = new bool[playerCount];
        StartTime = startTime;
        StartChant(startTime);
    }

    public float PositionOf(int playerId) => _positions[playerId];

    public bool IsMoving(int playerId) => _held[playerId] && Phase != CallerPhase.Watching && !IsOver;

    public void SetHeld(int playerId, bool held) => _held[playerId] = held;

    // The chant so far, for showing it syllable by syllable.
    public string SungText(double time)
    {
        var syllables = Version == ChantVersion.Vietnamese ? VietnameseChant : KoreanChant;
        var text = "";
        for (int i = 0; i < syllables.Length && time >= _syllableTimes[i]; i++)
            text += syllables[i];
        return text;
    }

    public void Tick(double time, float deltaTime)
    {
        if (IsOver) return;

        AdvanceCaller(time);

        for (int i = 0; i < _playerCount; i++)
        {
            if (!_held[i]) continue;

            if (Phase == CallerPhase.Watching)
            {
                if (_caughtThisWatch[i]) continue;
                _caughtThisWatch[i] = true;
                _positions[i] = CaughtPosition;
                Caught?.Invoke(i);
            }
            else
            {
                _positions[i] = Mathf.Min(_positions[i] + deltaTime / _settings.runSeconds, 1f);
            }
        }

        if (Array.IndexOf(_positions, 1f) >= 0 || time >= EndTime) IsOver = true;
    }

    // Reaching the caller beats everyone; otherwise the closest player ranks highest.
    public float[] PlayerScores() => (float[])_positions.Clone();

    // Ids of the players sharing the lead when the round ended; empty if nobody left the start.
    public List<int> Winners()
    {
        var winners = new List<int>();
        float best = Mathf.Max(_positions);
        if (best <= 0f) return winners;

        for (int i = 0; i < _playerCount; i++)
            if (_positions[i] >= best) winners.Add(i);
        return winners;
    }

    void AdvanceCaller(double time)
    {
        while (time >= _segments[_segment].End)
        {
            _segment++;
            if (_segment == _segments.Count) StartChant(_segments[_segment - 1].End);
        }

        var phase = _segments[_segment].Phase;
        if (phase == CallerPhase.Watching && Phase != CallerPhase.Watching)
            Array.Clear(_caughtThisWatch, 0, _playerCount);
        Phase = phase;
    }

    // One full cycle: chant (maybe with a fake turn in the middle), tell, turn, watch.
    void StartChant(double start)
    {
        Version = Version == ChantVersion.Vietnamese ? ChantVersion.Korean : ChantVersion.Vietnamese;
        int syllables = Version == ChantVersion.Vietnamese ? VietnameseChant.Length : KoreanChant.Length;

        _segments.Clear();
        _syllableTimes.Clear();
        _segment = 0;

        bool fast = _random.NextDouble() < _settings.fastChance;
        bool fake = !fast && _random.NextDouble() < _settings.fakeChance;
        double step = (fast ? _settings.fastChantSeconds : _settings.normalChantSeconds) / syllables;

        double time = start;
        for (int i = 0; i < syllables; i++)
        {
            if (fake && i == syllables / 2)
            {
                _segments.Add(new Segment(CallerPhase.Chanting, time));
                time += _settings.tellSeconds;
                _segments.Add(new Segment(CallerPhase.Tell, time));
                time += _settings.fakeSeconds;
                _segments.Add(new Segment(CallerPhase.Fake, time));
            }
            _syllableTimes.Add(time);
            time += step;
        }

        if (!fast && _random.NextDouble() < _settings.stretchChance)
            time += Lerp(_settings.stretchMinSeconds, _settings.stretchMaxSeconds);

        _segments.Add(new Segment(CallerPhase.Chanting, time));
        time += _settings.tellSeconds;
        _segments.Add(new Segment(CallerPhase.Tell, time));
        time += _settings.turningSeconds;
        _segments.Add(new Segment(CallerPhase.Turning, time));
        time += Lerp(_settings.watchMinSeconds, _settings.watchMaxSeconds);
        _segments.Add(new Segment(CallerPhase.Watching, time));
    }

    double Lerp(float min, float max) => min + (max - min) * _random.NextDouble();
}
