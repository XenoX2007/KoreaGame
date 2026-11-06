using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public enum Ingredient
{
    Tteok,
    Eomuk,
    Kimchi,
    Enoki,
    Greens,
    Egg,
}

[Serializable]
public class LauTokbokkiSettings
{
    [Tooltip("Round length; with no perfect pot by then, the most correct ingredients wins.")]
    public float roundSeconds = 60f;
    public int recipeSize = 5;
    [Tooltip("At most this many of one ingredient in a recipe.")]
    public int maxSameType = 2;
    public int plateSlots = 12;
    public int platesAtStart = 8;
    [Tooltip("Seconds before a taken plate's slot gets a new ingredient.")]
    public float refillSeconds = 1.5f;
    [Tooltip("Chance that a new plate is something a player still needs, once nothing needed is missing from the table and floor.")]
    [Range(0f, 1f)] public float neededTypeChance = 0.7f;
    [Tooltip("While a needed ingredient is missing from the table and floor, a plate nobody needs turns into it this often.")]
    public float swapSeconds = 3f;
    [Tooltip("Clockwise spin of the table.")]
    public float tableSpinDegPerSec = 10f;
    [Tooltip("Arena units per second at full stick (the floor is 16 x 9).")]
    public float walkSpeed = 3.6f;
    public float acceleration = 18f;
    public float playerRadius = 0.38f;
    [Tooltip("How close a plate, floor item or pot edge must be to reach it.")]
    public float reach = 0.75f;
    [Tooltip("Closing speed above which running into someone knocks their ingredient loose.")]
    public float bumpSpeed = 2.8f;
    public float bumpStun = 0.4f;
    [Tooltip("Seconds an ingredient stays on the floor.")]
    public float floorSeconds = 8f;
    [Tooltip("Seconds a pot is safe after being stolen from.")]
    public float stealCooldown = 1.5f;
    [Tooltip("A press shorter than this, without dragging the stick, is a tap (view).")]
    public float tapSeconds = 0.25f;
    [Tooltip("Fixed simulation substep, so results don't depend on the frame rate.")]
    public float step = 1f / 120f;
}

public enum LtEventKind
{
    PickedUp,   // Player took Item from a plate or the floor
    Dropped,    // Player put Item on the floor
    Filled,     // Item went into Player's pot and fills a slot (Other = who put it there)
    Spoiled,    // Item went into Player's pot but isn't needed (Other = who put it there)
    Stolen,     // Player took Item out of Other's pot
    Removed,    // Player took a spoiled Item out of their own pot
    KnockedOff, // Player was bumped by Other (Item is what fell, if anything) and is stunned
    Refilled,   // plate Slot has a new Item
    Perfect,    // Player's pot is perfect: they win
    TimeUp,
}

public readonly struct LtEvent
{
    public readonly LtEventKind Kind;
    public readonly int Player;
    public readonly int Other;
    public readonly Ingredient? Item;
    public readonly int Slot;

    public LtEvent(LtEventKind kind, int player, int other = -1, Ingredient? item = null, int slot = -1)
    {
        Kind = kind;
        Player = player;
        Other = other;
        Item = item;
        Slot = slot;
    }
}

public struct PlateState
{
    public Ingredient? Item;
    public double RefillAt;  // when an empty slot gets a new ingredient
    public double FilledAt;  // when the current one arrived, for the pop-in
}

public struct FloorItem
{
    public Ingredient Item;
    public Vector2 Position;
    public double DroppedAt;
    public int BlockedPlayer;  // who just dropped it on purpose, so they don't pick it straight back up
    public double BlockedUntil;
}

// Where everything is, in arena units: the floor is 16 x 9, origin bottom-left, y up.
// Pens follow the seats (SeatLayout), so each pen sits next to its player's joystick.
public sealed class LauTokbokkiLayout
{
    public const float Width = 16f;
    public const float Height = 9f;
    public const float PotRadius = 0.62f;
    public const float PlateRing = 0.72f; // of the table radius

    public readonly Vector2 TableCenter = new Vector2(8f, 4.5f);
    public readonly float TableRadius;
    public readonly float PlateSize;
    public readonly Rect[] Pens;
    public readonly Vector2[] OpenSide; // the pen side without a fence, toward the table
    public readonly Vector2[] Pots;
    public readonly Vector2[] Spawns;

    public LauTokbokkiLayout(int playerCount, float playerRadius)
    {
        bool four = playerCount == 4;
        TableRadius = four ? 2.35f : 2.6f;
        PlateSize = four ? 0.72f : 0.8f;
        Pens = four
            ? new[]
            {
                Rect.MinMaxRect(2.9f, 0.25f, 6.4f, 2.75f),   // P1 bottom-left
                Rect.MinMaxRect(9.6f, 6.25f, 13.1f, 8.75f),  // P2 top-right
                Rect.MinMaxRect(2.9f, 6.25f, 6.4f, 8.75f),   // P3 top-left
                Rect.MinMaxRect(9.6f, 0.25f, 13.1f, 2.75f),  // P4 bottom-right
            }
            : new[]
            {
                Rect.MinMaxRect(2.2f, 2.75f, 4.7f, 6.25f),   // P1 left
                Rect.MinMaxRect(11.3f, 2.75f, 13.8f, 6.25f), // P2 right
            };

        OpenSide = new Vector2[playerCount];
        Pots = new Vector2[playerCount];
        Spawns = new Vector2[playerCount];
        for (int id = 0; id < playerCount; id++)
        {
            var pen = Pens[id];
            Pots[id] = pen.center;
            OpenSide[id] = four
                ? new Vector2(0f, Mathf.Sign(TableCenter.y - pen.center.y))
                : new Vector2(Mathf.Sign(TableCenter.x - pen.center.x), 0f);
            // Beside the pot, along the pen's long side (toward the table with 4 players, below it with 2).
            float gap = PotRadius + playerRadius + 0.2f;
            Spawns[id] = pen.center + (four ? new Vector2(Mathf.Sign(TableCenter.x - pen.center.x) * gap, 0f) : new Vector2(0f, -gap));
        }
    }
}

// Lẩu Tokbokki / Jeukseok tteokbokki rules, everyone at once and each for themselves. Players walk with
// a stick, grab the ingredients their recipe needs from a slowly spinning table and carry them to their
// own pot. A tap at a rival's pot steals from it (or spoils it with something they don't need), and
// running into someone knocks their ingredient loose. First perfect pot wins; otherwise the most correct
// ingredients when time runs out.
public sealed class LauTokbokkiModel
{
    const double BlockSeconds = 1.0;
    const float FacingSpeed = 0.05f;
    const float DropDistance = 0.25f;
    const float KnockDistance = 0.55f;
    const int CollisionPasses = 2;
    static readonly int TypeCount = Enum.GetValues(typeof(Ingredient)).Length;

    readonly LauTokbokkiSettings _settings;
    readonly double _step;
    readonly System.Random _random;
    readonly Player[] _players;
    readonly PlateState[] _plates;
    readonly List<FloorItem> _floor = new List<FloorItem>();
    double _nextSwapAt = double.NaN;
    double _startTime = double.NaN;
    long _steps;

    public event Action<LtEvent> Event;

    public LauTokbokkiLayout Layout { get; }
    public int PlayerCount => _players.Length;
    public bool IsOver { get; private set; }
    public int WinnerPlayer { get; private set; } = -1;
    public bool HasWinner => WinnerPlayer >= 0;
    public double StartTime => _startTime;
    public double SimulatedTime => double.IsNaN(_startTime) ? double.NaN : _startTime + _steps * _step;

    // Radians, clockwise, growing with time.
    public float TableAngle => double.IsNaN(_startTime) ? 0f : (float)(_steps * _step) * _settings.tableSpinDegPerSec * Mathf.Deg2Rad;

    public IReadOnlyList<PlateState> Plates => _plates;
    public IReadOnlyList<FloorItem> FloorItems => _floor;

    public double TimeLeft(double time) =>
        double.IsNaN(_startTime) ? _settings.roundSeconds : Math.Max(0.0, _startTime + _settings.roundSeconds - time);

    sealed class Player
    {
        public Ingredient[] Recipe;
        public readonly List<Ingredient> Pot = new List<Ingredient>();
        public readonly List<bool> Spoiled = new List<bool>();
        public Vector2 Position;
        public Vector2 Velocity;
        public Vector2 Stick;
        public float Facing = Mathf.PI / 2f;
        public Ingredient? Carried;
        public double StunnedUntil = double.NegativeInfinity;
        public double StolenFromAt = double.NegativeInfinity;
    }

    public LauTokbokkiModel(LauTokbokkiSettings settings, int playerCount, int seed)
    {
        _settings = settings;
        _step = settings.step;
        _random = new System.Random(seed);
        Layout = new LauTokbokkiLayout(playerCount, settings.playerRadius);

        _players = new Player[playerCount];
        for (int id = 0; id < playerCount; id++)
        {
            _players[id] = new Player { Recipe = NewRecipe(), Position = Layout.Spawns[id] };
            _players[id].Facing = Mathf.Atan2(Layout.TableCenter.y - Layout.Spawns[id].y, Layout.TableCenter.x - Layout.Spawns[id].x);
        }

        // Some slots start empty; the rest get what players are likely to need.
        _plates = new PlateState[settings.plateSlots];
        for (int i = 0; i < _plates.Length; i++) _plates[i].RefillAt = double.PositiveInfinity;
        var slots = new List<int>();
        for (int i = 0; i < _plates.Length; i++) slots.Add(i);
        for (int filled = 0; filled < Math.Min(settings.platesAtStart, _plates.Length); filled++)
        {
            int pick = _random.Next(slots.Count);
            _plates[slots[pick]].Item = ChooseNewPlate();
            _plates[slots[pick]].FilledAt = double.NegativeInfinity;
            slots.RemoveAt(pick);
        }
        // The slots left empty stay empty: only a taken plate comes back.
    }

    Ingredient[] NewRecipe()
    {
        var recipe = new Ingredient[_settings.recipeSize];
        var counts = new int[TypeCount];
        for (int i = 0; i < recipe.Length; i++)
        {
            Ingredient type;
            do type = (Ingredient)_random.Next(TypeCount);
            while (counts[(int)type] >= _settings.maxSameType);
            counts[(int)type]++;
            recipe[i] = type;
        }
        Array.Sort(recipe);
        return recipe;
    }

    public IReadOnlyList<Ingredient> RecipeOf(int player) => _players[player].Recipe;
    public IReadOnlyList<Ingredient> PotOf(int player) => _players[player].Pot;
    public bool IsSpoiled(int player, int index) => _players[player].Spoiled[index];
    public Vector2 PositionOf(int player) => _players[player].Position;
    public Vector2 VelocityOf(int player) => _players[player].Velocity;
    public float FacingOf(int player) => _players[player].Facing;  // radians, 0 = right, counter-clockwise
    public Ingredient? CarriedBy(int player) => _players[player].Carried;
    public bool IsStunned(int player) => SimulatedTimeOrZero < _players[player].StunnedUntil;

    double SimulatedTimeOrZero => double.IsNaN(_startTime) ? 0.0 : SimulatedTime;

    public int CorrectCount(int player)
    {
        int count = 0;
        foreach (bool spoiled in _players[player].Spoiled)
            if (!spoiled) count++;
        return count;
    }

    public bool IsPerfect(int player) => CorrectCount(player) == _settings.recipeSize && !_players[player].Spoiled.Contains(true);

    // Recipe slots of this type that are still open (what's in hand doesn't count).
    public bool Needs(int player, Ingredient type) => OpenSlots(player, type) > 0;

    int OpenSlots(int player, Ingredient type)
    {
        var p = _players[player];
        int open = 0;
        foreach (var item in p.Recipe)
            if (item == type) open++;
        for (int i = 0; i < p.Pot.Count; i++)
            if (!p.Spoiled[i] && p.Pot[i] == type) open--;
        return open;
    }

    public Vector2 PlatePosition(int slot)
    {
        float angle = Mathf.PI / 2f - slot * 2f * Mathf.PI / _plates.Length - TableAngle;
        return Layout.TableCenter + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (Layout.TableRadius * LauTokbokkiLayout.PlateRing);
    }

    // Tooling and test hooks: put things exactly where a scenario needs them.
    public void SetPosition(int player, Vector2 position)
    {
        _players[player].Position = position;
        _players[player].Velocity = Vector2.zero;
    }

    public void SetVelocity(int player, Vector2 velocity) => _players[player].Velocity = velocity;
    public void SetCarried(int player, Ingredient? item) => _players[player].Carried = item;

    public void SetPlate(int slot, Ingredient? item)
    {
        _plates[slot].Item = item;
        _plates[slot].RefillAt = double.PositiveInfinity;
    }

    public void Start(double time)
    {
        if (!double.IsNaN(_startTime)) return;
        _startTime = time;
        _steps = 0;
    }

    // Everything that changes during a round (recipes and the layout come from the seed), so a joining
    // phone's copy matches the host's. The random generator stays behind: only the host's refills count.
    public void WriteState(BinaryWriter writer)
    {
        writer.Write(_startTime);
        writer.Write(_steps);
        writer.Write(_nextSwapAt);
        writer.Write(IsOver);
        writer.Write((sbyte)WinnerPlayer);

        foreach (var p in _players)
        {
            writer.Write(p.Position);
            writer.Write(p.Velocity);
            writer.Write(p.Stick);
            writer.Write(p.Facing);
            writer.WriteOptional((int?)p.Carried);
            writer.Write(p.StunnedUntil);
            writer.Write(p.StolenFromAt);
            writer.Write((byte)p.Pot.Count);
            for (int i = 0; i < p.Pot.Count; i++)
            {
                writer.Write((byte)p.Pot[i]);
                writer.Write(p.Spoiled[i]);
            }
        }

        foreach (var plate in _plates)
        {
            writer.WriteOptional((int?)plate.Item);
            writer.Write(plate.RefillAt);
            writer.Write(plate.FilledAt);
        }

        writer.Write((byte)_floor.Count);
        foreach (var item in _floor)
        {
            writer.Write((byte)item.Item);
            writer.Write(item.Position);
            writer.Write(item.DroppedAt);
            writer.Write((sbyte)item.BlockedPlayer);
            writer.Write(item.BlockedUntil);
        }
    }

    public void ReadState(BinaryReader reader)
    {
        _startTime = reader.ReadDouble();
        _steps = reader.ReadInt64();
        _nextSwapAt = reader.ReadDouble();
        IsOver = reader.ReadBoolean();
        WinnerPlayer = reader.ReadSByte();

        foreach (var p in _players)
        {
            p.Position = reader.ReadVector2();
            p.Velocity = reader.ReadVector2();
            p.Stick = reader.ReadVector2();
            p.Facing = reader.ReadSingle();
            p.Carried = (Ingredient?)reader.ReadOptional();
            p.StunnedUntil = reader.ReadDouble();
            p.StolenFromAt = reader.ReadDouble();
            p.Pot.Clear();
            p.Spoiled.Clear();
            int count = reader.ReadByte();
            for (int i = 0; i < count; i++)
            {
                p.Pot.Add((Ingredient)reader.ReadByte());
                p.Spoiled.Add(reader.ReadBoolean());
            }
        }

        for (int slot = 0; slot < _plates.Length; slot++)
        {
            _plates[slot].Item = (Ingredient?)reader.ReadOptional();
            _plates[slot].RefillAt = reader.ReadDouble();
            _plates[slot].FilledAt = reader.ReadDouble();
        }

        _floor.Clear();
        int floor = reader.ReadByte();
        for (int i = 0; i < floor; i++)
        {
            _floor.Add(new FloorItem
            {
                Item = (Ingredient)reader.ReadByte(),
                Position = reader.ReadVector2(),
                DroppedAt = reader.ReadDouble(),
                BlockedPlayer = reader.ReadSByte(),
                BlockedUntil = reader.ReadDouble(),
            });
        }
    }

    // The stick in screen space (length 0..1). Time first runs on with the previous stick.
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

    // A tap on the joystick: put the carried ingredient in a pot or on the floor, or take one out of a pot.
    public void Action(int player, double time)
    {
        Advance(time);
        if (double.IsNaN(_startTime) || IsOver) return;
        double now = SimulatedTime;
        var p = _players[player];
        if (now < p.StunnedUntil) return;

        int pot = NearestPotInReach(p.Position);
        if (p.Carried.HasValue)
        {
            var item = p.Carried.Value;
            p.Carried = null;
            if (pot >= 0) PutInPot(pot, item, player, now);
            else DropOnFloor(item, p.Position + Facing(p) * (_settings.playerRadius + DropDistance), now, player);
            return;
        }

        if (pot < 0) return;
        var owner = _players[pot];
        if (pot == player)
        {
            // The owner fishes out something that spoils their pot.
            int spoiled = owner.Spoiled.LastIndexOf(true);
            if (spoiled < 0) return;
            p.Carried = TakeOut(owner, spoiled);
            Raise(new LtEvent(LtEventKind.Removed, player, player, p.Carried));
            if (IsPerfect(player)) Win(player);
            return;
        }

        if (now - owner.StolenFromAt < _settings.stealCooldown - 1e-9) return;
        int correct = owner.Spoiled.LastIndexOf(false);
        if (correct < 0) return;
        p.Carried = TakeOut(owner, correct);
        owner.StolenFromAt = now;
        Raise(new LtEvent(LtEventKind.Stolen, player, pot, p.Carried));
    }

    static Ingredient TakeOut(Player owner, int index)
    {
        var item = owner.Pot[index];
        owner.Pot.RemoveAt(index);
        owner.Spoiled.RemoveAt(index);
        return item;
    }

    static Vector2 Facing(Player p) => new Vector2(Mathf.Cos(p.Facing), Mathf.Sin(p.Facing));

    int NearestPotInReach(Vector2 position)
    {
        int best = -1;
        float bestDistance = float.MaxValue;
        for (int id = 0; id < _players.Length; id++)
        {
            float distance = Vector2.Distance(position, Layout.Pots[id]) - LauTokbokkiLayout.PotRadius - _settings.playerRadius;
            if (distance <= _settings.reach && distance < bestDistance)
            {
                best = id;
                bestDistance = distance;
            }
        }
        return best;
    }

    void PutInPot(int owner, Ingredient item, int by, double now)
    {
        var pot = _players[owner];
        bool needed = Needs(owner, item);
        pot.Pot.Add(item);
        pot.Spoiled.Add(!needed);
        Raise(new LtEvent(needed ? LtEventKind.Filled : LtEventKind.Spoiled, owner, by, item));
        if (needed && IsPerfect(owner)) Win(owner);
    }

    void DropOnFloor(Ingredient item, Vector2 position, double now, int blocked)
    {
        _floor.Add(new FloorItem
        {
            Item = item,
            Position = OnFreeFloor(position),
            DroppedAt = now,
            BlockedPlayer = blocked,
            BlockedUntil = blocked >= 0 ? now + BlockSeconds : double.NegativeInfinity,
        });
    }

    // Keeps a floor item inside the arena and off the table and pots, where nobody could reach it.
    Vector2 OnFreeFloor(Vector2 position)
    {
        const float margin = 0.3f;
        position = PushOutOf(position, Layout.TableCenter, Layout.TableRadius + margin);
        foreach (var pot in Layout.Pots) position = PushOutOf(position, pot, LauTokbokkiLayout.PotRadius + margin);
        return new Vector2(
            Mathf.Clamp(position.x, margin, LauTokbokkiLayout.Width - margin),
            Mathf.Clamp(position.y, margin, LauTokbokkiLayout.Height - margin));
    }

    static Vector2 PushOutOf(Vector2 position, Vector2 center, float radius)
    {
        var away = position - center;
        if (away.magnitude >= radius) return position;
        return center + (away.sqrMagnitude > 1e-8f ? away.normalized : Vector2.down) * radius;
    }

    void Step(double now)
    {
        float dt = (float)_step;

        for (int slot = 0; slot < _plates.Length; slot++)
        {
            if (_plates[slot].Item.HasValue || now < _plates[slot].RefillAt - 1e-9) continue;
            _plates[slot].Item = ChooseNewPlate();
            _plates[slot].FilledAt = now;
            _plates[slot].RefillAt = double.PositiveInfinity;
            Raise(new LtEvent(LtEventKind.Refilled, -1, item: _plates[slot].Item, slot: slot));
        }
        SwapUnneededPlate(now);

        _floor.RemoveAll(item => now - item.DroppedAt >= _settings.floorSeconds - 1e-9);

        foreach (var p in _players)
        {
            if (now < p.StunnedUntil)
            {
                p.Velocity = Vector2.zero;
                continue;
            }
            var change = p.Stick * _settings.walkSpeed - p.Velocity;
            p.Velocity += Vector2.ClampMagnitude(change, _settings.acceleration * dt);
            p.Position += p.Velocity * dt;
            if (p.Velocity.magnitude > FacingSpeed) p.Facing = Mathf.Atan2(p.Velocity.y, p.Velocity.x);
        }

        for (int pass = 0; pass < CollisionPasses; pass++)
        {
            CollidePlayers(now, pass == 0);
            foreach (var p in _players) CollideWorld(p);
        }

        for (int id = 0; id < _players.Length; id++)
        {
            var p = _players[id];
            if (now < p.StunnedUntil) continue;
            if (!p.Carried.HasValue) PickUp(id, p, now);
            // Walking into your own pen with something you need drops it in your pot.
            if (p.Carried.HasValue && Layout.Pens[id].Contains(p.Position) && Needs(id, p.Carried.Value))
            {
                var item = p.Carried.Value;
                p.Carried = null;
                PutInPot(id, item, id, now);
                if (IsOver) return;
            }
        }

        if (now >= _startTime + _settings.roundSeconds - 1e-9) TimeUp();
    }

    // Reach is the gap between the player's edge and the plate's: measured between centers, the table
    // would keep every plate out of reach.
    public float PickUpDistance => _settings.reach + _settings.playerRadius + Layout.PlateSize / 2f;

    void PickUp(int id, Player p, double now)
    {
        float reach = PickUpDistance;
        for (int slot = 0; slot < _plates.Length; slot++)
        {
            var item = _plates[slot].Item;
            if (!item.HasValue || !Needs(id, item.Value) || Vector2.Distance(PlatePosition(slot), p.Position) > reach) continue;
            p.Carried = item;
            _plates[slot].Item = null;
            _plates[slot].RefillAt = now + _settings.refillSeconds;
            Raise(new LtEvent(LtEventKind.PickedUp, id, item: item, slot: slot));
            return;
        }

        for (int i = 0; i < _floor.Count; i++)
        {
            var floor = _floor[i];
            if (floor.BlockedPlayer == id && now < floor.BlockedUntil) continue;
            if (!Needs(id, floor.Item) || Vector2.Distance(floor.Position, p.Position) > reach) continue;
            p.Carried = floor.Item;
            _floor.RemoveAt(i);
            Raise(new LtEvent(LtEventKind.PickedUp, id, item: floor.Item));
            return;
        }
    }

    // Players push each other apart. Running into someone hard knocks the slower one's ingredient loose.
    void CollidePlayers(double now, bool canBump)
    {
        float minDistance = 2f * _settings.playerRadius;
        for (int a = 0; a < _players.Length; a++)
        {
            for (int b = a + 1; b < _players.Length; b++)
            {
                var pa = _players[a];
                var pb = _players[b];
                var between = pb.Position - pa.Position;
                float distance = between.magnitude;
                if (distance >= minDistance || distance < 1e-6f) continue;

                var normal = between / distance;
                float closing = Vector2.Dot(pa.Velocity - pb.Velocity, normal);
                if (canBump && closing > _settings.bumpSpeed)
                {
                    float speedA = pa.Velocity.magnitude;
                    float speedB = pb.Velocity.magnitude;
                    if (speedA > speedB) Bump(b, a, normal, now);
                    else if (speedB > speedA) Bump(a, b, -normal, now);
                }

                float overlap = minDistance - distance;
                pa.Position -= normal * (overlap / 2f);
                pb.Position += normal * (overlap / 2f);
                if (closing > 0f)
                {
                    pa.Velocity -= normal * (closing / 2f);
                    pb.Velocity += normal * (closing / 2f);
                }
            }
        }
    }

    // `away` points from the faster player toward the one they hit.
    void Bump(int hit, int by, Vector2 away, double now)
    {
        var p = _players[hit];
        if (now < p.StunnedUntil) return;
        p.StunnedUntil = now + _settings.bumpStun;
        var item = p.Carried;
        if (item.HasValue)
        {
            p.Carried = null;
            DropOnFloor(item.Value, p.Position + away * KnockDistance, now, -1);
        }
        Raise(new LtEvent(LtEventKind.KnockedOff, hit, by, item));
    }

    void CollideWorld(Player p)
    {
        float r = _settings.playerRadius;
        PushOut(p, Layout.TableCenter, Layout.TableRadius + r);
        foreach (var pot in Layout.Pots) PushOut(p, pot, LauTokbokkiLayout.PotRadius + r);

        if (p.Position.x < r) { p.Position.x = r; p.Velocity.x = Mathf.Max(0f, p.Velocity.x); }
        if (p.Position.x > LauTokbokkiLayout.Width - r) { p.Position.x = LauTokbokkiLayout.Width - r; p.Velocity.x = Mathf.Min(0f, p.Velocity.x); }
        if (p.Position.y < r) { p.Position.y = r; p.Velocity.y = Mathf.Max(0f, p.Velocity.y); }
        if (p.Position.y > LauTokbokkiLayout.Height - r) { p.Position.y = LauTokbokkiLayout.Height - r; p.Velocity.y = Mathf.Min(0f, p.Velocity.y); }
    }

    static void PushOut(Player p, Vector2 center, float radius)
    {
        var away = p.Position - center;
        float distance = away.magnitude;
        if (distance >= radius) return;
        var normal = distance > 1e-6f ? away / distance : Vector2.down;
        p.Position = center + normal * radius;
        float inward = Vector2.Dot(p.Velocity, normal);
        if (inward < 0f) p.Velocity -= normal * inward;
    }

    // Recipe slots of this type still open across all players, minus what they already carry.
    int Demand(Ingredient type)
    {
        int open = 0;
        for (int id = 0; id < _players.Length; id++)
        {
            if (_players[id] == null) continue;
            int slots = OpenSlots(id, type) - (_players[id].Carried == type ? 1 : 0);
            open += Math.Max(0, slots);
        }
        return open;
    }

    // How many of this type can be picked up right now, on the table and the floor.
    int Supply(Ingredient type)
    {
        int count = 0;
        foreach (var plate in _plates)
            if (plate.Item == type) count++;
        foreach (var item in _floor)
            if (item.Item == type) count++;
        return count;
    }

    // Needed types with none on the table or floor; failing that, ones with fewer than players still need,
    // weighted by how many are short. The table can hold less than everyone's recipes, so having one of
    // each comes first.
    List<Ingredient> MissingTypes()
    {
        var absent = new List<Ingredient>();
        var missing = new List<Ingredient>();
        for (int type = 0; type < TypeCount; type++)
        {
            int demand = Demand((Ingredient)type);
            int supply = Supply((Ingredient)type);
            if (demand > 0 && supply == 0) absent.Add((Ingredient)type);
            for (int i = supply; i < demand; i++) missing.Add((Ingredient)type);
        }
        return absent.Count > 0 ? absent : missing;
    }

    // Whatever is missing first, so every recipe can always be finished; otherwise usually something
    // a player still needs (counting pots and hands), otherwise anything.
    Ingredient ChooseNewPlate()
    {
        var missing = MissingTypes();
        if (missing.Count > 0) return missing[_random.Next(missing.Count)];

        var needed = new List<Ingredient>();
        for (int type = 0; type < TypeCount; type++)
            if (Demand((Ingredient)type) > 0) needed.Add((Ingredient)type);
        bool pickNeeded = _random.NextDouble() < _settings.neededTypeChance;
        return pickNeeded && needed.Count > 0 ? needed[_random.Next(needed.Count)] : (Ingredient)_random.Next(TypeCount);
    }

    // Plates only change when taken, so a table of things nobody needs would never bring what's missing.
    // Every swapSeconds, while something is missing and no refill is on its way, the surplus plate
    // farthest from everyone becomes a missing type.
    void SwapUnneededPlate(double now)
    {
        if (double.IsNaN(_nextSwapAt)) _nextSwapAt = now + _settings.swapSeconds;
        if (now < _nextSwapAt - 1e-9) return;
        _nextSwapAt = now + _settings.swapSeconds;

        foreach (var plate in _plates)
            if (!plate.Item.HasValue && !double.IsPositiveInfinity(plate.RefillAt)) return;
        var missing = MissingTypes();
        if (missing.Count == 0) return;

        int best = -1;
        float bestDistance = -1f;
        for (int slot = 0; slot < _plates.Length; slot++)
        {
            var item = _plates[slot].Item;
            if (!item.HasValue || Supply(item.Value) <= Demand(item.Value)) continue;
            float nearest = float.MaxValue;
            foreach (var p in _players) nearest = Mathf.Min(nearest, Vector2.Distance(PlatePosition(slot), p.Position));
            if (nearest > bestDistance)
            {
                bestDistance = nearest;
                best = slot;
            }
        }
        if (best < 0) return;

        _plates[best].Item = missing[_random.Next(missing.Count)];
        _plates[best].FilledAt = now;
        Raise(new LtEvent(LtEventKind.Refilled, -1, item: _plates[best].Item, slot: best));
    }

    void Win(int player)
    {
        IsOver = true;
        WinnerPlayer = player;
        Raise(new LtEvent(LtEventKind.Perfect, player));
    }

    // The single highest correct count wins; a shared top is a draw.
    void TimeUp()
    {
        IsOver = true;
        int best = -1;
        int bestCount = -1;
        bool shared = false;
        for (int id = 0; id < _players.Length; id++)
        {
            int count = CorrectCount(id);
            if (count > bestCount)
            {
                best = id;
                bestCount = count;
                shared = false;
            }
            else if (count == bestCount)
            {
                shared = true;
            }
        }
        WinnerPlayer = shared ? -1 : best;
        Raise(new LtEvent(LtEventKind.TimeUp, WinnerPlayer));
    }

    // A perfect pot ranks first; everyone else by correct ingredients. 1 = best, ties share.
    public int[] Ranking()
    {
        var scores = new int[_players.Length];
        for (int id = 0; id < _players.Length; id++)
            scores[id] = CorrectCount(id) + (IsOver && WinnerPlayer == id && IsPerfect(id) ? 1000 : 0);
        var ranks = new int[_players.Length];
        for (int i = 0; i < ranks.Length; i++)
        {
            ranks[i] = 1;
            for (int j = 0; j < ranks.Length; j++)
                if (scores[j] > scores[i]) ranks[i]++;
        }
        return ranks;
    }

    void Raise(LtEvent e) => Event?.Invoke(e);
}
