using System;
using UnityEngine;

public enum KickResult
{
    Ignored,  // not this player's turn, or the shuttle hasn't reached their zone yet
    TooEarly, // kicked at air inside the zone: costs a life, so mashing doesn't pay
    Good,
    Perfect,
}

public enum ShuttleKind
{
    Feather, // Vietnamese cầu lông gà: floats high
    Jegi,    // Korean paper or cloth jegi: flatter and quicker
}

[Serializable]
public class DaCauSettings
{
    public int startingLives = 3;

    [Header("Flight (seconds)")]
    public float serveSeconds = 1.6f;
    public float baseFlightSeconds = 1.25f;
    public float minFlightSeconds = 0.55f;
    [Tooltip("Each kick in a rally multiplies the flight time by this.")]
    public float rallySpeedup = 0.965f;
    [Tooltip("A Perfect kick flies this much faster, so it is harder for the next player.")]
    public float perfectScale = 0.8f;
    public float jegiScale = 0.9f;

    [Header("Timing (seconds from arrival)")]
    public float perfectWindow = 0.07f;
    public float goodWindow = 0.15f;
    [Tooltip("Tapping counts from this long before arrival. Earlier taps are ignored.")]
    public float zoneSeconds = 0.55f;
    public float dropPauseSeconds = 1f;

    [Header("Wind")]
    [Range(0f, 1f)] public float windChance = 0.2f;
    [Tooltip("Wind can only start once a rally reaches this many kicks.")]
    public int windStartRally = 3;
    [Tooltip("A tailwind shortens the flight by this fraction; a headwind lengthens it.")]
    public float windScale = 0.15f;
}

// Rules of Đá Cầu / Jegichagi, independent of rendering and input.
// Each kick flies to a random other player still in the game, so nobody can stop watching.
// Whoever fails to return it loses a life.
public class DaCauModel
{
    readonly DaCauSettings _settings;
    readonly System.Random _random;
    readonly int[] _lives;
    readonly int[] _knockedOutAt;
    int _knockedOut;
    int _server;
    double _serveAt;

    public event Action<int> Missed;

    public bool IsFlying { get; private set; }
    public int From { get; private set; }
    public int To { get; private set; }
    public double FlightStart { get; private set; }
    public double Arrival { get; private set; }
    public ShuttleKind Kind { get; private set; } = ShuttleKind.Feather;
    public int Wind { get; private set; } // +1 tailwind, -1 headwind, 0 calm
    public int Rally { get; private set; }
    public bool IsOver { get; private set; }
    public int Winner { get; private set; } = -1;

    public double ZoneStart => Arrival - Math.Min(_settings.zoneSeconds, (Arrival - FlightStart) * 0.6);

    public DaCauModel(int playerCount, DaCauSettings settings, double startTime, int seed)
    {
        _settings = settings;
        _random = new System.Random(seed);
        _lives = new int[playerCount];
        _knockedOutAt = new int[playerCount];
        for (int i = 0; i < playerCount; i++)
        {
            _lives[i] = settings.startingLives;
            _knockedOutAt[i] = -1;
        }
        Serve(0, startTime);
    }

    public int LivesOf(int playerId) => _lives[playerId];

    public KickResult Tap(int playerId, double time)
    {
        if (IsOver || !IsFlying || playerId != To || time < ZoneStart) return KickResult.Ignored;

        double offset = time - Arrival;
        if (offset < -_settings.goodWindow)
        {
            Miss(playerId, time);
            return KickResult.TooEarly;
        }
        if (offset > _settings.goodWindow) return KickResult.Ignored; // Tick drops it this frame

        bool perfect = Math.Abs(offset) <= _settings.perfectWindow;
        Rally++;
        StartFlight(playerId, time, NextFlightSeconds(perfect));
        return perfect ? KickResult.Perfect : KickResult.Good;
    }

    public void Tick(double time)
    {
        if (IsOver) return;

        if (IsFlying)
        {
            if (time > Arrival + _settings.goodWindow) Miss(To, time);
        }
        else if (time >= _serveAt)
        {
            Serve(_server, time);
        }
    }

    // Last player standing ranks first; the others by how long they lasted.
    public float[] PlayerScores()
    {
        var scores = new float[_lives.Length];
        for (int i = 0; i < scores.Length; i++)
            scores[i] = _lives[i] > 0 ? _lives.Length + _lives[i] : _knockedOutAt[i];
        return scores;
    }

    void Serve(int from, double time)
    {
        Wind = 0;
        StartFlight(from, time, _settings.serveSeconds * KindScale);
    }

    void StartFlight(int from, double time, double seconds)
    {
        IsFlying = true;
        From = from;
        To = RandomOtherPlayer(from);
        FlightStart = time;
        Arrival = time + seconds;
    }

    void Miss(int playerId, double time)
    {
        IsFlying = false;
        Rally = 0;
        Wind = 0;
        Kind = Kind == ShuttleKind.Feather ? ShuttleKind.Jegi : ShuttleKind.Feather;

        _lives[playerId]--;
        if (_lives[playerId] == 0) _knockedOutAt[playerId] = _knockedOut++;
        Missed?.Invoke(playerId);

        if (_knockedOut == _lives.Length - 1)
        {
            IsOver = true;
            Winner = Array.FindIndex(_lives, lives => lives > 0);
            return;
        }

        _server = _lives[playerId] > 0 ? playerId : RandomOtherPlayer(playerId);
        _serveAt = time + _settings.dropPauseSeconds;
    }

    double NextFlightSeconds(bool perfect)
    {
        if (Rally >= _settings.windStartRally && _random.NextDouble() < _settings.windChance)
            Wind = _random.Next(2) == 0 ? 1 : -1;

        double seconds = _settings.baseFlightSeconds * Math.Pow(_settings.rallySpeedup, Rally) * KindScale;
        if (perfect) seconds *= _settings.perfectScale;
        seconds *= 1.0 - Wind * _settings.windScale;
        return Math.Max(seconds, _settings.minFlightSeconds);
    }

    float KindScale => Kind == ShuttleKind.Jegi ? _settings.jegiScale : 1f;

    // Any player still in the game except this one; with two left, that's always the opponent.
    int RandomOtherPlayer(int playerId)
    {
        int others = 0;
        for (int i = 0; i < _lives.Length; i++)
            if (i != playerId && _lives[i] > 0) others++;

        int pick = _random.Next(others);
        for (int i = 0; i < _lives.Length; i++)
            if (i != playerId && _lives[i] > 0 && pick-- == 0) return i;

        return playerId;
    }
}
