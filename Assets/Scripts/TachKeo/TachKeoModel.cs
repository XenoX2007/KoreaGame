using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[Serializable]
public class TachKeoSettings
{
    [Tooltip("Round length in seconds; whoever hasn't finished by then ranks by progress.")]
    public float roundSeconds = 20f;
    [Tooltip("Every outline is scaled to this length, in candy units (candy radius = 1).")]
    public float outlinePerimeter = TachKeoShapes.DefaultPerimeter;
    [Tooltip("Carve resolution: how many pieces the outline is cut into.")]
    public int segments = 160;
    [Tooltip("Needle speed at full stick, in candy units per second.")]
    public float needleSpeed = 1.13f;
    [Tooltip("How close to the line counts as on it.")]
    public float carveTolerance = 0.06f;
    [Tooltip("Within this distance the line gently pulls the needle onto it.")]
    public float magnetRadius = 0.045f;
    public float magnetStrength = 0.4f;
    [Tooltip("Stick tilt above this stresses the candy, even on the line.")]
    [Range(0.1f, 0.95f)] public float pushThreshold = 0.7f;
    [Tooltip("Crack per second just off the line, more the further off.")]
    public float offLineRate = 1.2f;
    [Tooltip("Crack per second at full tilt.")]
    public float pushRate = 0.6f;
    [Tooltip("Crack recovery per second while cutting carefully on the line.")]
    public float healRate = 0.15f;
    [Tooltip("Seconds between breaking a candy and getting a fresh one.")]
    public float restartDelay = 1f;
    [Tooltip("Fixed simulation substep, so results don't depend on the frame rate.")]
    public float step = 1f / 240f;
}

public enum CutState
{
    Waiting,
    Cutting,
    Broken,
    Done,
}

public enum TachKeoEventKind
{
    Cracking,
    Broke,
    Restarted,
    Finished,
    TimeUp,
}

public readonly struct TachKeoEvent
{
    public readonly TachKeoEventKind Kind;
    public readonly int Player;  // -1 for TimeUp
    public readonly float Level; // Cracking: the crack level crossed (0.5 or 0.8)

    public TachKeoEvent(TachKeoEventKind kind, int player, float level = 0f)
    {
        Kind = kind;
        Player = player;
        Level = level;
    }
}

// Tách Kẹo / Ppopgi rules, everyone at once and each for themselves. Each player steers a needle
// around their candy's stamped outline with a stick. Straying off the line or pushing too hard fills a
// crack meter; a full meter breaks the candy and the player starts again on a fresh one while the round
// clock runs on. Carve the whole outline to finish. Candy units: radius 1, +y away from the player.
public sealed class TachKeoModel
{
    const float NeedleLimit = 0.95f;
    const float ActiveStick = 0.05f;
    static readonly float[] CrackLevels = { 0.5f, 0.8f };

    readonly TachKeoSettings _settings;
    readonly double _step;
    readonly Player[] _players;
    double _startTime = double.NaN;
    long _steps;

    public event Action<TachKeoEvent> Event;

    public int PlayerCount => _players.Length;
    public int SegmentCount => _settings.segments;
    public bool IsOver { get; private set; }
    public double StartTime => _startTime;

    // Simulated time, which trails the last Advance by less than a substep.
    public double SimulatedTime => double.IsNaN(_startTime) ? double.NaN : _startTime + _steps * _step;

    public double TimeLeft(double time) =>
        double.IsNaN(_startTime) ? _settings.roundSeconds : Math.Max(0.0, _startTime + _settings.roundSeconds - time);

    sealed class Player
    {
        public TachKeoShape Shape;
        public Vector2[] Outline;
        public bool[] Carved;
        public int CarvedCount;
        public CutState State;
        public Vector2 Needle;
        public Vector2 Stick;
        public float Crack;
        public double BrokeAt;
        public double FinishTime = double.NaN;
        public int Breaks;
        public bool Simulated = true;
    }

    public TachKeoModel(TachKeoSettings settings, int playerCount, int seed)
    {
        _settings = settings;
        _step = settings.step;

        // Shuffled with the round's seed, so nobody shares a shape.
        var shapes = (TachKeoShape[])TachKeoShapes.All.Clone();
        var random = new System.Random(seed);
        for (int i = shapes.Length - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (shapes[i], shapes[j]) = (shapes[j], shapes[i]);
        }

        _players = new Player[playerCount];
        for (int id = 0; id < playerCount; id++)
        {
            var outline = TachKeoShapes.Outline(shapes[id], settings.segments, settings.outlinePerimeter);
            _players[id] = new Player { Shape = shapes[id], Outline = outline, Carved = new bool[outline.Length] };
            FreshCandy(_players[id]);
            _players[id].State = CutState.Waiting;
        }
    }

    public TachKeoShape ShapeOf(int player) => _players[player].Shape;
    public IReadOnlyList<Vector2> OutlineOf(int player) => _players[player].Outline;
    public CutState StateOf(int player) => _players[player].State;
    public Vector2 NeedleOf(int player) => _players[player].Needle;
    public float CrackOf(int player) => _players[player].Crack;
    public bool IsCarved(int player, int segment) => _players[player].Carved[segment];
    public double FinishTimeOf(int player) => _players[player].FinishTime;
    public int BreaksOf(int player) => _players[player].Breaks;

    // A broken candy counts as nothing carved.
    public float ProgressOf(int player)
    {
        var p = _players[player];
        return p.State == CutState.Broken ? 0f : p.CarvedCount / (float)p.Carved.Length;
    }

    // When the current candy broke, for the break animation.
    public double BrokeAtOf(int player) => _players[player].BrokeAt;

    // False only when nobody finished and nobody carved anything.
    public bool HasWinner
    {
        get
        {
            for (int i = 0; i < _players.Length; i++)
                if (_players[i].State == CutState.Done || ProgressOf(i) > 0f) return true;
            return false;
        }
    }

    // Online each phone cuts only its own player's candy; the others arrive with ReadPlayer and are
    // left alone here, so they don't break or restart twice.
    public void SetSimulated(int player, bool simulated) => _players[player].Simulated = simulated;

    public void WritePlayer(int player, BinaryWriter writer)
    {
        var p = _players[player];
        writer.Write((byte)p.State);
        writer.Write(p.Needle);
        writer.Write(p.Stick);
        writer.Write(p.Crack);
        writer.Write(p.BrokeAt);
        writer.Write(p.FinishTime);
        writer.Write(p.Breaks);
        writer.WriteBits(p.Carved);
    }

    public void ReadPlayer(int player, BinaryReader reader)
    {
        var p = _players[player];
        p.State = (CutState)reader.ReadByte();
        p.Needle = reader.ReadVector2();
        p.Stick = reader.ReadVector2();
        p.Crack = reader.ReadSingle();
        p.BrokeAt = reader.ReadDouble();
        p.FinishTime = reader.ReadDouble();
        p.Breaks = reader.ReadInt32();
        reader.ReadBits(p.Carved);
        p.CarvedCount = 0;
        foreach (bool carved in p.Carved)
            if (carved) p.CarvedCount++;
    }

    public void Start(double time)
    {
        if (!double.IsNaN(_startTime)) return;
        _startTime = time;
        _steps = 0;
        foreach (var p in _players) p.State = CutState.Cutting;
    }

    // The stick in candy space (length 0..1). Time first runs on with the previous stick,
    // so the result doesn't depend on how often this is called.
    public void SetStick(int player, Vector2 stick, double time)
    {
        Advance(time);
        _players[player].Stick = Vector2.ClampMagnitude(stick, 1f);
    }

    public void Advance(double time)
    {
        if (double.IsNaN(_startTime)) return;
        // The tolerance keeps frame times that land exactly on a substep from rounding either way.
        while (!IsOver && _startTime + (_steps + 1) * _step <= time + 1e-6)
        {
            _steps++;
            Step(_startTime + _steps * _step);
        }
    }

    void Step(double now)
    {
        float dt = (float)_step;
        for (int id = 0; id < _players.Length; id++)
        {
            var p = _players[id];
            if (!p.Simulated) continue;
            if (p.State == CutState.Broken && now - p.BrokeAt >= _settings.restartDelay - 1e-9)
            {
                FreshCandy(p);
                Raise(TachKeoEventKind.Restarted, id);
            }
            if (p.State == CutState.Cutting) Cut(id, p, now, dt);
        }

        bool allDone = Array.TrueForAll(_players, p => p.State == CutState.Done);
        if (allDone)
        {
            IsOver = true;
        }
        else if (now >= _startTime + _settings.roundSeconds - 1e-9)
        {
            IsOver = true;
            Raise(TachKeoEventKind.TimeUp, -1);
        }
    }

    void Cut(int id, Player p, double now, float dt)
    {
        float tilt = p.Stick.magnitude;
        bool active = tilt > ActiveStick;

        // Move, then let the line pull the needle gently onto it (never past it).
        p.Needle += p.Stick * (_settings.needleSpeed * dt);
        var nearest = NearestOnOutline(p.Outline, p.Needle);
        float distance = Vector2.Distance(p.Needle, nearest);
        if (distance < _settings.magnetRadius && distance > 0f)
            p.Needle = Vector2.MoveTowards(p.Needle, nearest, _settings.magnetStrength * dt);
        p.Needle = Vector2.ClampMagnitude(p.Needle, NeedleLimit);

        // The needle only presses into the candy while someone is steering it.
        if (!active) return;

        distance = Vector2.Distance(p.Needle, NearestOnOutline(p.Outline, p.Needle));
        Carve(p);

        float before = p.Crack;
        float tolerance = _settings.carveTolerance;
        bool onLine = distance <= tolerance;
        if (!onLine) p.Crack += _settings.offLineRate * (1f + (distance - tolerance) / tolerance) * dt;
        if (tilt > _settings.pushThreshold)
            p.Crack += _settings.pushRate * (tilt - _settings.pushThreshold) / (1f - _settings.pushThreshold) * dt;
        else if (onLine)
            p.Crack = Mathf.Max(0f, p.Crack - _settings.healRate * dt);

        foreach (float level in CrackLevels)
            if (before < level && p.Crack >= level && p.Crack < 1f) Raise(TachKeoEventKind.Cracking, id, level);

        if (p.Crack >= 1f)
        {
            p.State = CutState.Broken;
            p.BrokeAt = now;
            p.Breaks++;
            Raise(TachKeoEventKind.Broke, id);
            return;
        }

        if (p.CarvedCount == p.Carved.Length)
        {
            p.State = CutState.Done;
            p.FinishTime = now - _startTime;
            Raise(TachKeoEventKind.Finished, id);
        }
    }

    // Every segment whose midpoint is close to the needle, plus single gaps between carved ones.
    void Carve(Player p)
    {
        var outline = p.Outline;
        float tolerance = _settings.carveTolerance;
        for (int i = 0; i < outline.Length; i++)
        {
            if (p.Carved[i]) continue;
            var middle = (outline[i] + outline[(i + 1) % outline.Length]) / 2f;
            if ((middle - p.Needle).sqrMagnitude <= tolerance * tolerance) p.Carved[i] = true;
        }
        FillSingleGaps(p.Carved);
        p.CarvedCount = 0;
        foreach (bool carved in p.Carved)
            if (carved) p.CarvedCount++;
    }

    // An uncarved segment between two carved neighbours (around the loop) is carved too.
    public static void FillSingleGaps(bool[] carved)
    {
        int n = carved.Length;
        var before = (bool[])carved.Clone();
        for (int i = 0; i < n; i++)
            if (!before[i] && before[(i + n - 1) % n] && before[(i + 1) % n]) carved[i] = true;
    }

    public static Vector2 NearestOnOutline(IReadOnlyList<Vector2> outline, Vector2 point)
    {
        var best = outline[0];
        float bestDistance = float.MaxValue;
        for (int i = 0; i < outline.Count; i++)
        {
            var a = outline[i];
            var b = outline[(i + 1) % outline.Count];
            var ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / ab.sqrMagnitude);
            var candidate = a + ab * t;
            float distance = (candidate - point).sqrMagnitude;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }
        return best;
    }

    void FreshCandy(Player p)
    {
        Array.Clear(p.Carved, 0, p.Carved.Length);
        p.CarvedCount = 0;
        p.Crack = 0f;
        p.Needle = p.Outline[0];
        p.State = CutState.Cutting;
    }

    // Finishers first by time, then everyone else by how much of their current candy is carved.
    // 1 = best; equal times or equal progress share a rank.
    public int[] Ranking()
    {
        var ranks = new int[_players.Length];
        for (int i = 0; i < _players.Length; i++)
        {
            ranks[i] = 1;
            for (int j = 0; j < _players.Length; j++)
                if (Beats(j, i)) ranks[i]++;
        }
        return ranks;
    }

    bool Beats(int a, int b)
    {
        var pa = _players[a];
        var pb = _players[b];
        bool aDone = pa.State == CutState.Done;
        bool bDone = pb.State == CutState.Done;
        if (aDone != bDone) return aDone;
        if (aDone) return pa.FinishTime < pb.FinishTime;
        return CarvedForRanking(pa) > CarvedForRanking(pb);
    }

    static int CarvedForRanking(Player p) => p.State == CutState.Broken ? 0 : p.CarvedCount;

    void Raise(TachKeoEventKind kind, int player, float level = 0f) => Event?.Invoke(new TachKeoEvent(kind, player, level));
}
