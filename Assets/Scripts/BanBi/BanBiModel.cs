using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public enum BanBiPhase
{
    Idle,     // before Start
    DiceRoll,
    Aiming,
    Charging,
    Rolling,
    Finished,
}

public enum BanBiEventKind
{
    DiceStopped,  // Player, Value = face
    DiceReroll,   // Player
    TurnStarted,  // Player
    TurnSkipped,  // Player: the turn timer ran out
    Shot,         // Player, Other = the new marble's index, Value = power
    Hit,          // Player, Other = marble indices, Value = impulse
    MarbleOut,    // Player = marble index, Other = shooter
    Missed,       // Player: the shot marble left the circle without touching anything
    BonusShot,    // Player
    Finished,     // Player = winning team, -1 for a draw
}

public readonly struct BanBiEvent
{
    public readonly BanBiEventKind Kind;
    public readonly int Player;
    public readonly int Other;
    public readonly float Value;

    public BanBiEvent(BanBiEventKind kind, int player, int other = -1, float value = 0f)
    {
        Kind = kind;
        Player = player;
        Other = other;
        Value = value;
    }
}

[Serializable]
public class BanBiSettings
{
    [Header("Marbles (zone units: the circle has radius 1)")]
    public int startMarblesPerTeam = 6;
    [Tooltip("Start marbles go inside a circle of this radius.")]
    public float startArea = 0.65f;
    public float marbleRadius = 0.085f;

    [Header("Dice (seconds)")]
    public float diceFaceSeconds = 0.08f;
    public float diceAutoStopSeconds = 3f;
    [Tooltip("How long the final faces stay up before the first turn.")]
    public float diceResultSeconds = 1.2f;

    [Header("Turn")]
    [Tooltip("Time to press before the turn is skipped.")]
    public float turnSeconds = 10f;
    [Tooltip("The hand slides within +-this below the circle.")]
    public float slideRange = 0.75f;
    public float slideHz = 0.5f;
    [Tooltip("Where the marble leaves the hand, just below the circle.")]
    public float launchY = -1.1f;
    [Tooltip("Power goes 0 -> 1 in this time, then back down.")]
    public float chargeSeconds = 1.2f;
    [Tooltip("A hold this long releases on its own.")]
    public float maxHoldSeconds = 4f;

    [Header("Physics (zone units, seconds)")]
    [Tooltip("Stopping distance is v^2 / (2 friction): 1.0 just reaches inside, 2.8 overshoots the top.")]
    public float minShotSpeed = 1f;
    public float maxShotSpeed = 2.8f;
    [Tooltip("Rolling friction, as a constant deceleration.")]
    public float friction = 1.1f;
    public float stopSpeed = 0.02f;
    public float restitution = 0.9f;
    public float physicsStep = 1f / 240f;
    public float maxRollSeconds = 6f;

    [Header("Scoring")]
    [Tooltip("Chained bonus shots allowed in one turn.")]
    public int maxBonusShots = 2;
    [Tooltip("Regular shots per team; skipped turns count, bonus shots don't.")]
    public int shotsPerTeam = 8;
}

// One marble in the circle, in zone units (center at the origin, y toward the top of the screen).
public class MarbleState
{
    public readonly int Team;  // 0 red / Vietnam, 1 blue / Korea
    public int ShotBy = -1;    // the player who shot it, or -1 for a start marble
    public Vector2 Position;
    public Vector2 Velocity;
    public bool InPlay = true;
    public bool Moved;         // moved during the current shot, so the out rule applies to it

    public MarbleState(int team, Vector2 position)
    {
        Team = team;
        Position = position;
    }
}

// Equal-mass marbles rolling with constant friction. No walls: "out" is judged once everything stops.
public static class BanBiPhysics
{
    public static void Step(IReadOnlyList<MarbleState> marbles, BanBiSettings settings, float dt, Action<int, int, float> hit = null)
    {
        foreach (var marble in marbles)
        {
            if (!marble.InPlay) continue;
            float speed = marble.Velocity.magnitude;
            if (speed == 0f) continue;

            marble.Moved = true;
            marble.Position += marble.Velocity * dt;
            float slower = speed - settings.friction * dt;
            marble.Velocity = slower < settings.stopSpeed ? Vector2.zero : marble.Velocity * (slower / speed);
        }

        float touching = settings.marbleRadius * 2f;
        for (int i = 0; i < marbles.Count; i++)
        {
            var a = marbles[i];
            if (!a.InPlay) continue;
            for (int j = i + 1; j < marbles.Count; j++)
            {
                var b = marbles[j];
                if (!b.InPlay) continue;

                var offset = b.Position - a.Position;
                float distance = offset.magnitude;
                if (distance >= touching) continue;

                var normal = distance > 0f ? offset / distance : Vector2.right;
                var separate = normal * ((touching - distance) / 2f);
                a.Position -= separate;
                b.Position += separate;

                float approach = Vector2.Dot(b.Velocity - a.Velocity, normal);
                if (approach >= 0f) continue;

                float impulse = -(1f + settings.restitution) * approach / 2f;
                a.Velocity -= normal * impulse;
                b.Velocity += normal * impulse;
                a.Moved = b.Moved = true;
                hit?.Invoke(i, j, impulse);
            }
        }
    }
}

// Rules of Bắn Bi / Guseul-chigi, independent of rendering and input. Top-down team marbles:
// dice decide the order, then on each turn the team's hand slides along a straight line below the
// circle (press = stop, hold = pull back, release = shoot straight up). Shot marbles that stay
// in the circle join their team; knocking a rival marble out earns a bonus shot. A team left with
// no marbles loses; otherwise after shotsPerTeam shots each, more marbles in the circle wins.
// Every time is an explicit argument, so the model is fully deterministic.
public class BanBiModel
{
    readonly BanBiSettings _settings;
    readonly int _playerCount;
    readonly int _seed;
    readonly List<MarbleState> _marbles = new List<MarbleState>();
    readonly int[] _shotsTaken = new int[2];
    readonly List<int> _turnOrder = new List<int>();

    // Dice
    readonly bool[] _spinning;
    readonly double[] _spinStart;
    readonly int[] _rollCount;
    readonly List<int>[] _rolls;
    double _lastDieStop;
    double _diceDoneAt = double.NaN;

    double _phaseStart;
    float _lockedX;
    float _shotPower;
    double _turnLeftAtCharge;
    int _bonusShots;
    int _shotMarble = -1;
    bool _shotTouched;
    double _simTime;
    double _rollStart;

    public event Action<BanBiEvent> Event;

    public BanBiPhase Phase { get; private set; }
    public int ActivePlayer { get; private set; } = -1;
    public int ActiveTeam => ActivePlayer >= 0 ? SeatLayout.TeamOf(ActivePlayer) : -1;
    public IReadOnlyList<int> TurnOrder => _turnOrder;
    public IReadOnlyList<MarbleState> Marbles => _marbles;
    public int WinnerTeam { get; private set; } = -1;
    public double PhaseStart => _phaseStart;
    public int PlayerCount => _playerCount;

    public BanBiModel(BanBiSettings settings, int playerCount, int seed)
    {
        _settings = settings;
        _playerCount = playerCount;
        _seed = seed;
        _spinning = new bool[playerCount];
        _spinStart = new double[playerCount];
        _rollCount = new int[playerCount];
        _rolls = new List<int>[playerCount];
        for (int id = 0; id < playerCount; id++) _rolls[id] = new List<int>();
        PlaceStartMarbles(new System.Random(seed));
    }

    public static bool IsOut(Vector2 position) => position.sqrMagnitude > 1f;

    public bool IsDieSpinning(int player) => _spinning[player];
    public int OrderOf(int player) => _turnOrder.IndexOf(player); // -1 before the dice decide
    public int ShotsTaken(int team) => _shotsTaken[team];

    // Marbles of this team in the circle.
    public int TeamCount(int team) => _marbles.Count(marble => marble.InPlay && marble.Team == team);

    // The active team's regular shot, from 1 to shotsPerTeam.
    public int Round => Math.Min(_shotsTaken[Math.Max(ActiveTeam, 0)] + 1, _settings.shotsPerTeam);

    public int DiceFace(int player, double time)
    {
        if (!_spinning[player]) return _rolls[player].Count > 0 ? _rolls[player][_rolls[player].Count - 1] : 1;
        long face = (long)Math.Floor(Math.Max(0.0, time - _spinStart[player]) / _settings.diceFaceSeconds);
        return FaceAt(player, _rollCount[player], face);
    }

    // Where the hand is along the bottom edge: sliding while aiming, then locked.
    public float HandX(double time)
    {
        if (Phase != BanBiPhase.Aiming) return _lockedX;
        return _settings.slideRange * Mathf.Sin(2f * Mathf.PI * _settings.slideHz * (float)(time - _phaseStart));
    }

    // 0..1: a triangle wave that peaks at chargeSeconds, so holding too long loses power.
    public float Power(double time)
    {
        if (Phase != BanBiPhase.Charging) return _shotPower;
        double cycle = (time - _phaseStart) / _settings.chargeSeconds % 2.0;
        return (float)(cycle <= 1.0 ? cycle : 2.0 - cycle);
    }

    public float ShotSpeed(float power) => Mathf.Lerp(_settings.minShotSpeed, _settings.maxShotSpeed, power);

    // How far a shot with this power rolls if it hits nothing.
    public float StoppingDistance(float power) => ShotSpeed(power) * ShotSpeed(power) / (2f * _settings.friction);

    // The turn timer only runs while aiming; a hold freezes it.
    public double TurnTimeLeft(double time)
    {
        switch (Phase)
        {
            case BanBiPhase.Aiming: return Math.Max(0.0, _phaseStart + _settings.turnSeconds - time);
            case BanBiPhase.Charging: return _turnLeftAtCharge;
            default: return 0.0;
        }
    }

    public void Start(double time)
    {
        Phase = BanBiPhase.DiceRoll;
        _phaseStart = time;
        for (int id = 0; id < _playerCount; id++) Spin(id, time);
    }

    public void Press(int player, bool pressed, double time)
    {
        Advance(time);
        switch (Phase)
        {
            case BanBiPhase.DiceRoll:
                if (pressed && _spinning[player])
                {
                    StopDie(player, time);
                    if (_spinning.All(spinning => !spinning)) ResolveDice(time);
                }
                break;

            case BanBiPhase.Aiming:
                if (pressed && player == ActivePlayer)
                {
                    _lockedX = HandX(time);
                    _turnLeftAtCharge = TurnTimeLeft(time);
                    Phase = BanBiPhase.Charging;
                    _phaseStart = time;
                }
                break;

            case BanBiPhase.Charging:
                if (!pressed && player == ActivePlayer) Launch(time);
                break;
        }
    }

    // Runs phase timers and physics up to this time. Every transition happens at its own exact
    // time, not the frame's, so results don't depend on the frame rate.
    public void Advance(double time)
    {
        while (true)
        {
            switch (Phase)
            {
                case BanBiPhase.DiceRoll:
                    if (double.IsNaN(_diceDoneAt))
                    {
                        if (!AutoStopDice(time)) return;
                        ResolveDice(_lastDieStop);
                        continue;
                    }
                    double firstTurn = _diceDoneAt + _settings.diceResultSeconds;
                    if (time < firstTurn) return;
                    StartTurn(_turnOrder[0], firstTurn);
                    continue;

                case BanBiPhase.Aiming:
                    double deadline = _phaseStart + _settings.turnSeconds;
                    if (time < deadline) return;
                    Emit(BanBiEventKind.TurnSkipped, ActivePlayer);
                    EndTurn(deadline);
                    continue;

                case BanBiPhase.Charging:
                    double autoRelease = _phaseStart + _settings.maxHoldSeconds;
                    if (time < autoRelease) return;
                    Launch(autoRelease);
                    continue;

                case BanBiPhase.Rolling:
                    if (!Roll(time)) return;
                    continue;

                default:
                    return;
            }
        }
    }

    // Winning team 1, losing team 0, draw all 0: teammates always share a rank.
    public float[] PlayerScores()
    {
        var scores = new float[_playerCount];
        for (int id = 0; id < _playerCount; id++)
            scores[id] = WinnerTeam >= 0 && SeatLayout.TeamOf(id) == WinnerTeam ? 1f : 0f;
        return scores;
    }

    // ---- Dice ----

    void Spin(int player, double time)
    {
        _spinning[player] = true;
        _spinStart[player] = time;
        _rollCount[player]++;
    }

    void StopDie(int player, double time)
    {
        int face = DiceFace(player, time);
        _spinning[player] = false;
        _rolls[player].Add(face);
        _lastDieStop = Math.Max(_lastDieStop, time);
        Emit(BanBiEventKind.DiceStopped, player, value: face);
    }

    // Stops every die whose time is up. True once none is spinning.
    bool AutoStopDice(double time)
    {
        for (int id = 0; id < _playerCount; id++)
        {
            double stopAt = _spinStart[id] + _settings.diceAutoStopSeconds;
            if (_spinning[id] && time >= stopAt) StopDie(id, stopAt);
        }
        return _spinning.All(spinning => !spinning);
    }

    // Highest roller's team goes first, then teams alternate; within a team the higher roll goes first.
    // Only the players tied for the top across teams, or tied with their teammate, roll again.
    void ResolveDice(double time)
    {
        var players = Enumerable.Range(0, _playerCount).ToList();
        int top = players.Aggregate((a, b) => CompareRolls(a, b) >= 0 ? a : b);
        var reroll = new HashSet<int>();

        var tiedTop = players.Where(id => CompareRolls(id, top) == 0).ToList();
        if (tiedTop.Select(SeatLayout.TeamOf).Distinct().Count() > 1) reroll.UnionWith(tiedTop);

        foreach (int id in players)
        {
            int mate = SeatLayout.TeammateOf(id, _playerCount);
            if (mate >= 0 && CompareRolls(id, mate) == 0) reroll.Add(id);
        }

        if (reroll.Count > 0)
        {
            foreach (int id in reroll)
            {
                Spin(id, time);
                Emit(BanBiEventKind.DiceReroll, id);
            }
            return;
        }

        int firstTeam = SeatLayout.TeamOf(top);
        var teams = new[] { firstTeam, 1 - firstTeam }
            .Select(team => players.Where(id => SeatLayout.TeamOf(id) == team).OrderByDescending(id => id, Comparer<int>.Create(CompareRolls)).ToList())
            .ToArray();
        _turnOrder.Clear();
        for (int i = 0; i < teams[0].Count || i < teams[1].Count; i++)
            foreach (var team in teams)
                if (i < team.Count) _turnOrder.Add(team[i]);

        _diceDoneAt = time;
    }

    // First rolls, then each reroll breaks the tie.
    int CompareRolls(int a, int b)
    {
        var first = _rolls[a];
        var second = _rolls[b];
        for (int i = 0; i < first.Count && i < second.Count; i++)
            if (first[i] != second[i]) return first[i].CompareTo(second[i]);
        return first.Count.CompareTo(second.Count);
    }

    int FaceAt(int player, int roll, long index)
    {
        unchecked
        {
            uint hash = (uint)_seed * 0x9E3779B1u ^ (uint)(player + 1) * 0x85EBCA77u ^ (uint)roll * 0xC2B2AE3Du ^ (uint)index * 0x27D4EB2Fu;
            hash ^= hash >> 15;
            hash *= 0x2C1B3C6Du;
            hash ^= hash >> 12;
            hash *= 0x297A2D39u;
            hash ^= hash >> 15;
            return (int)(hash % 6u) + 1;
        }
    }

    // ---- Turns ----

    void StartTurn(int player, double time)
    {
        ActivePlayer = player;
        _bonusShots = 0;
        StartAiming(time);
        Emit(BanBiEventKind.TurnStarted, player);
    }

    void StartAiming(double time)
    {
        Phase = BanBiPhase.Aiming;
        _phaseStart = time;
    }

    // A new marble leaves the hand, straight up.
    void Launch(double time)
    {
        _shotPower = Power(time);
        foreach (var marble in _marbles) marble.Moved = false;

        var shot = new MarbleState(ActiveTeam, new Vector2(_lockedX, _settings.launchY))
        {
            ShotBy = ActivePlayer,
            Velocity = new Vector2(0f, ShotSpeed(_shotPower)),
        };
        _marbles.Add(shot);
        _shotMarble = _marbles.Count - 1;
        _shotTouched = false;

        Phase = BanBiPhase.Rolling;
        _phaseStart = time;
        _simTime = time;
        _rollStart = time;
        Emit(BanBiEventKind.Shot, ActivePlayer, _shotMarble, _shotPower);
    }

    // Fixed substeps up to this time. True once everything has stopped and the shot is resolved.
    bool Roll(double time)
    {
        double step = _settings.physicsStep;
        while (_simTime + step <= time)
        {
            BanBiPhysics.Step(_marbles, _settings, _settings.physicsStep, OnHit);
            _simTime += step;

            bool stopped = _marbles.All(marble => !marble.InPlay || marble.Velocity == Vector2.zero);
            if (stopped || _simTime - _rollStart >= _settings.maxRollSeconds)
            {
                ResolveShot(_simTime);
                return true;
            }
        }
        return false;
    }

    void OnHit(int a, int b, float impulse)
    {
        if (a == _shotMarble || b == _shotMarble) _shotTouched = true;
        Emit(BanBiEventKind.Hit, a, b, impulse);
    }

    // Marbles that moved and stopped outside the circle are gone, whoever they belong to.
    void ResolveShot(double time)
    {
        int shooter = ActivePlayer;
        int rivalsOut = 0;

        for (int i = 0; i < _marbles.Count; i++)
        {
            var marble = _marbles[i];
            marble.Velocity = Vector2.zero;
            if (!marble.InPlay || !marble.Moved || !IsOut(marble.Position)) continue;

            marble.InPlay = false;
            if (marble.Team != ActiveTeam) rivalsOut++;
            Emit(BanBiEventKind.MarbleOut, i, shooter);
            if (i == _shotMarble && !_shotTouched) Emit(BanBiEventKind.Missed, shooter);
        }
        foreach (var marble in _marbles) marble.Moved = false;

        int red = TeamCount(0);
        int blue = TeamCount(1);
        if (red == 0 || blue == 0)
        {
            Finish(red > 0 ? 0 : blue > 0 ? 1 : -1);
            return;
        }

        if (rivalsOut > 0 && _bonusShots < _settings.maxBonusShots)
        {
            _bonusShots++;
            StartAiming(time);
            Emit(BanBiEventKind.BonusShot, shooter);
            return;
        }

        EndTurn(time);
    }

    // A skipped turn counts as a shot; bonus shots never do.
    void EndTurn(double time)
    {
        _shotsTaken[ActiveTeam]++;
        if (_shotsTaken[0] >= _settings.shotsPerTeam && _shotsTaken[1] >= _settings.shotsPerTeam)
        {
            FinishOnCount();
            return;
        }

        int index = (_turnOrder.IndexOf(ActivePlayer) + 1) % _turnOrder.Count;
        StartTurn(_turnOrder[index], time);
    }

    // More marbles in the circle wins; on equal counts, the team with the marble closest to the center.
    void FinishOnCount()
    {
        int red = TeamCount(0);
        int blue = TeamCount(1);
        if (red != blue)
        {
            Finish(red > blue ? 0 : 1);
            return;
        }

        float left = ClosestToCenter(0);
        float right = ClosestToCenter(1);
        Finish(left < right ? 0 : right < left ? 1 : -1);
    }

    float ClosestToCenter(int team)
    {
        float closest = float.MaxValue;
        foreach (var marble in _marbles)
            if (marble.InPlay && marble.Team == team)
                closest = Math.Min(closest, marble.Position.magnitude);
        return closest;
    }

    void Finish(int winnerTeam)
    {
        WinnerTeam = winnerTeam;
        Phase = BanBiPhase.Finished;
        ActivePlayer = -1;
        Emit(BanBiEventKind.Finished, winnerTeam);
    }

    // Teams alternate while placing, so they start mixed; no two marbles start close together.
    void PlaceStartMarbles(System.Random random)
    {
        float spacing = _settings.marbleRadius * 2.5f;
        for (int n = 0; n < _settings.startMarblesPerTeam * 2; n++)
        {
            var marble = new MarbleState(n % 2, Vector2.zero);
            for (int attempt = 0; attempt < 500; attempt++)
            {
                var point = new Vector2((float)(random.NextDouble() * 2.0 - 1.0), (float)(random.NextDouble() * 2.0 - 1.0));
                if (point.sqrMagnitude > 1f) continue; // outside the start circle
                marble.Position = point * _settings.startArea;
                if (!_marbles.Any(other => (other.Position - marble.Position).magnitude < spacing)) break;
            }
            _marbles.Add(marble);
        }
    }

    void Emit(BanBiEventKind kind, int player, int other = -1, float value = 0f) =>
        Event?.Invoke(new BanBiEvent(kind, player, other, value));
}
