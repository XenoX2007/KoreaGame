using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

public class LauTokbokkiModelTests
{
    const double Start = 5.0;
    const double Frame = 1.0 / 60.0;

    static LauTokbokkiModel NewModel(int players = 4, LauTokbokkiSettings settings = null, int seed = 1)
    {
        var model = new LauTokbokkiModel(settings ?? new LauTokbokkiSettings(), players, seed);
        model.Start(Start);
        return model;
    }

    // Plays frames from `from` to `to` seconds after Start with each player's scripted stick.
    static void Run(LauTokbokkiModel model, double from, double to, Func<int, double, Vector2> stick = null, double frame = Frame)
    {
        long first = (long)Math.Round(from / frame);
        long last = (long)Math.Round(to / frame);
        for (long f = first; f < last; f++)
        {
            double time = Start + f * frame;
            for (int p = 0; p < model.PlayerCount; p++) model.SetStick(p, stick?.Invoke(p, time - Start) ?? Vector2.zero, time);
        }
        model.Advance(Start + last * frame);
    }

    static Ingredient Unneeded(LauTokbokkiModel model, int player) =>
        Enum.GetValues(typeof(Ingredient)).Cast<Ingredient>().First(type => !model.RecipeOf(player).Contains(type));

    static void ClearTable(LauTokbokkiModel model)
    {
        for (int slot = 0; slot < model.Plates.Count; slot++) model.SetPlate(slot, null);
    }

    // Just outside the table, level with where a plate at this angle (radians) passes.
    static Vector2 AtTableEdge(LauTokbokkiModel model, float angle) =>
        model.Layout.TableCenter + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (model.Layout.TableRadius + new LauTokbokkiSettings().playerRadius + 0.01f);

    // Right beside a pot, on the side away from its pen's fence.
    static Vector2 BesidePot(LauTokbokkiModel model, int pot, float side = 1f) =>
        model.Layout.Pots[pot] + new Vector2(0f, side * (LauTokbokkiLayout.PotRadius + 0.45f));

    // `by` stands beside the pot and taps the item in.
    static void PutIn(LauTokbokkiModel model, int by, int pot, Ingredient item, double seconds)
    {
        model.SetPosition(by, BesidePot(model, pot));
        model.SetCarried(by, item);
        model.Action(by, Start + seconds);
    }

    [Test]
    public void Recipes_HaveFiveItems_AtMostTwoOfAKind_AndDependOnTheSeed()
    {
        var seen = new HashSet<string>();
        for (int seed = 0; seed < 30; seed++)
        {
            var model = NewModel(4, seed: seed);
            for (int p = 0; p < 4; p++)
            {
                var recipe = model.RecipeOf(p);
                Assert.AreEqual(5, recipe.Count);
                Assert.LessOrEqual(recipe.GroupBy(item => item).Max(group => group.Count()), 2);
                seen.Add(string.Join(",", recipe));
            }
            CollectionAssert.AreEqual(model.RecipeOf(0), NewModel(4, seed: seed).RecipeOf(0), "Same seed, same recipe");
        }
        Assert.Greater(seen.Count, 30, "Players and rounds get different recipes");
    }

    [Test]
    public void TheTable_StartsWithEightPlates_AndTurnsAtItsSpeed()
    {
        var settings = new LauTokbokkiSettings();
        var model = NewModel(4, settings);
        Assert.AreEqual(12, model.Plates.Count);
        Assert.AreEqual(8, model.Plates.Count(plate => plate.Item.HasValue));

        Run(model, 0, 3);
        Assert.AreEqual(3f * settings.tableSpinDegPerSec * Mathf.Deg2Rad, model.TableAngle, 1e-4f);
        Assert.AreEqual(8, model.Plates.Count(plate => plate.Item.HasValue), "Nobody took anything");
    }

    [Test]
    public void ATakenPlate_RefillsExactlyRefillSecondsLater()
    {
        var settings = new LauTokbokkiSettings();
        var model = NewModel(2, settings);
        ClearTable(model);
        var need = model.RecipeOf(0)[0];
        model.SetPlate(0, need);
        model.SetPosition(0, AtTableEdge(model, Mathf.PI / 2f));
        double tookAt = double.NaN, refilledAt = double.NaN;
        model.Event += e =>
        {
            if (e.Kind == LtEventKind.PickedUp) tookAt = model.SimulatedTime;
            if (e.Kind == LtEventKind.Refilled && e.Slot == 0) refilledAt = model.SimulatedTime;
        };

        Run(model, 0, 0.1);
        Assert.AreEqual(need, model.CarriedBy(0));
        Assert.IsFalse(model.Plates[0].Item.HasValue);

        Run(model, 0.1, 3);
        Assert.AreEqual(settings.refillSeconds, refilledAt - tookAt, settings.step / 2f);
        Assert.IsTrue(model.Plates[0].Item.HasValue);
        Assert.LessOrEqual(model.Plates.Count(plate => plate.Item.HasValue), 12);
    }

    [Test]
    public void ATableOfPlatesNobodyNeeds_GetsOneOfEveryMissingIngredient()
    {
        var settings = new LauTokbokkiSettings();
        var model = NewModel(2, settings);
        var surplus = model.RecipeOf(0)[0];
        for (int slot = 0; slot < model.Plates.Count; slot++)
            if (model.Plates[slot].Item.HasValue) model.SetPlate(slot, surplus);

        Run(model, 0, settings.swapSeconds - 0.1);
        Assert.AreEqual(8, model.Plates.Count(plate => plate.Item == surplus), "No swap before swapSeconds");

        Run(model, settings.swapSeconds - 0.1, 8 * settings.swapSeconds + 0.1);
        Assert.AreEqual(8, model.Plates.Count(plate => plate.Item.HasValue), "Swaps keep the plate count");
        foreach (Ingredient type in Enum.GetValues(typeof(Ingredient)))
        {
            int needed = Enumerable.Range(0, 2).Sum(p => model.RecipeOf(p).Count(item => item == type));
            Assert.GreaterOrEqual(model.Plates.Count(plate => plate.Item == type), Math.Min(needed, 1), $"{type} is needed but not on the table");
        }
    }

    [Test]
    public void NewPlates_BringWhatIsMissingFirst()
    {
        for (int seed = 0; seed < 30; seed++)
        {
            var model = NewModel(2, seed: seed);
            foreach (Ingredient type in Enum.GetValues(typeof(Ingredient)))
            {
                int needed = Enumerable.Range(0, 2).Sum(p => model.RecipeOf(p).Count(item => item == type));
                int onTable = model.Plates.Count(plate => plate.Item == type);
                Assert.GreaterOrEqual(onTable, Math.Min(needed, 1), $"Seed {seed}: {type} is needed but not on the table at the start");
            }
        }
    }

    [Test]
    public void PickingUp_TakesOnlyWhatTheRecipeStillNeeds_OneAtATime()
    {
        var model = NewModel(2);
        ClearTable(model);
        model.SetPlate(0, Unneeded(model, 0));
        model.SetPosition(0, AtTableEdge(model, Mathf.PI / 2f));
        Run(model, 0, 0.2);
        Assert.IsNull(model.CarriedBy(0), "Not on the recipe");

        var held = Unneeded(model, 0);
        model.SetCarried(0, held);
        model.SetPlate(0, model.RecipeOf(0)[0]);
        Run(model, 0.2, 0.4);
        Assert.AreEqual(held, model.CarriedBy(0), "Hands full");
        Assert.IsTrue(model.Plates[0].Item.HasValue);
    }

    [Test]
    public void APlateOutOfReach_TurnsIntoReachAsTheTableSpins()
    {
        var model = NewModel(2);
        ClearTable(model);
        var need = model.RecipeOf(0)[0];
        model.SetPlate(3, need); // starts at the table's right (0 rad), then turns clockwise
        model.SetPosition(0, AtTableEdge(model, -50f * Mathf.Deg2Rad));

        Run(model, 0, 1);
        Assert.IsNull(model.CarriedBy(0), "Still too far round the table");

        Run(model, 1, 6);
        Assert.AreEqual(need, model.CarriedBy(0));
    }

    [Test]
    public void WalkingIntoYourPen_PutsANeededIngredientInYourPot_ButNotOneYouDontNeed()
    {
        var model = NewModel(4);
        var pen = model.Layout.Pens[0];
        var outside = new Vector2(pen.xMax + 0.6f, pen.center.y);

        model.SetPosition(0, outside);
        model.SetCarried(0, Unneeded(model, 0));
        Run(model, 0, 1, (p, t) => p == 0 ? Vector2.left : Vector2.zero);
        Assert.IsTrue(pen.Contains(model.PositionOf(0)));
        Assert.IsNotNull(model.CarriedBy(0));
        Assert.AreEqual(0, model.PotOf(0).Count);

        var need = model.RecipeOf(0)[0];
        model.SetPosition(0, outside);
        model.SetCarried(0, need);
        Run(model, 1, 2, (p, t) => p == 0 ? Vector2.left : Vector2.zero);
        Assert.IsNull(model.CarriedBy(0));
        CollectionAssert.AreEqual(new[] { need }, model.PotOf(0));
        Assert.AreEqual(1, model.CorrectCount(0));
    }

    [Test]
    public void TappingAtARivalsPot_FillsTheirSlot_OrSpoilsIt_AndOnlyTheOwnerCanTakeTheSpoiledOneOut()
    {
        var model = NewModel(4);
        var events = new List<LtEvent>();
        model.Event += events.Add;
        var recipe = model.RecipeOf(1);

        PutIn(model, 0, 1, recipe[0], 0.1);
        Assert.AreEqual(1, model.CorrectCount(1));
        Assert.AreEqual(LtEventKind.Filled, events.Last().Kind);

        PutIn(model, 0, 1, Unneeded(model, 1), 0.2);
        Assert.AreEqual(LtEventKind.Spoiled, events.Last().Kind);
        Assert.AreEqual(1, model.CorrectCount(1), "A spoiled item doesn't count");
        Assert.IsTrue(model.IsSpoiled(1, 1));

        for (int i = 1; i < recipe.Count; i++) PutIn(model, 0, 1, recipe[i], 0.2 + i * 0.1);
        Assert.AreEqual(5, model.CorrectCount(1));
        Assert.IsFalse(model.IsPerfect(1), "The spoiled item blocks a perfect pot");
        Assert.IsFalse(model.IsOver);

        // A rival takes a correct one, never the spoiled one.
        model.SetPosition(0, BesidePot(model, 1));
        model.SetCarried(0, null);
        model.Action(0, Start + 2.0);
        Assert.AreEqual(recipe[recipe.Count - 1], model.CarriedBy(0));
        Assert.IsTrue(model.IsSpoiled(1, 1), "The spoiled item stays");

        // The owner takes the spoiled one out, and that makes the pot perfect.
        var model2 = NewModel(4);
        foreach (var item in model2.RecipeOf(1).Take(4)) PutIn(model2, 0, 1, item, 0.1);
        PutIn(model2, 0, 1, Unneeded(model2, 1), 0.2);
        PutIn(model2, 0, 1, model2.RecipeOf(1)[4], 0.3);
        Assert.IsFalse(model2.IsOver);

        model2.SetPosition(0, new Vector2(8f, 8.5f));
        model2.SetPosition(1, BesidePot(model2, 1, -1f));
        model2.Action(1, Start + 0.4);
        Assert.AreEqual(Unneeded(model2, 1), model2.CarriedBy(1));
        Assert.IsTrue(model2.IsPerfect(1));
        Assert.IsTrue(model2.IsOver);
        Assert.AreEqual(1, model2.WinnerPlayer);
    }

    [Test]
    public void Stealing_TakesTheLastCorrectIngredient_WithACooldown()
    {
        var settings = new LauTokbokkiSettings();
        var model = NewModel(4, settings);
        var events = new List<LtEvent>();
        model.Event += events.Add;
        var recipe = model.RecipeOf(1);
        PutIn(model, 2, 1, recipe[0], 0.1);
        PutIn(model, 2, 1, recipe[1], 0.2);
        PutIn(model, 2, 1, Unneeded(model, 1), 0.3);
        model.SetPosition(2, new Vector2(8f, 8.5f));
        model.SetCarried(2, null);

        // The owner can't steal from their own pot.
        model.SetPosition(1, BesidePot(model, 1, -1f));
        model.Action(1, Start + 0.4);
        Assert.AreEqual(Unneeded(model, 1), model.CarriedBy(1), "The owner only takes out the spoiled item");
        model.SetPosition(1, new Vector2(8f, 0.5f));

        model.SetPosition(0, BesidePot(model, 1));
        model.Action(0, Start + 0.5);
        Assert.AreEqual(recipe[1], model.CarriedBy(0), "The most recently added correct one");
        Assert.AreEqual(LtEventKind.Stolen, events.Last().Kind);
        Assert.AreEqual(1, events.Last().Other);
        Assert.AreEqual(1, model.CorrectCount(1));

        // Someone else right after: the pot is safe for a moment.
        model.SetPosition(3, BesidePot(model, 1, -1f));
        model.Action(3, Start + 0.5 + settings.stealCooldown - 0.1);
        Assert.IsNull(model.CarriedBy(3));
        model.Action(3, Start + 0.5 + settings.stealCooldown + 0.05);
        Assert.AreEqual(recipe[0], model.CarriedBy(3));
        Assert.AreEqual(0, model.PotOf(1).Count);
    }

    [Test]
    public void TappingWithIngredientAwayFromPots_DropsItOnTheFloor_WhereItExpires()
    {
        var settings = new LauTokbokkiSettings();
        var model = NewModel(2, settings);
        model.SetPosition(0, new Vector2(8f, 8.3f));
        model.SetCarried(0, model.RecipeOf(0)[0]);
        model.Action(0, Start + 0.1);

        Assert.IsNull(model.CarriedBy(0));
        Assert.AreEqual(1, model.FloorItems.Count);
        Run(model, 0.1, 0.5);
        Assert.IsNull(model.CarriedBy(0), "The one who dropped it doesn't grab it straight back");

        model.SetPosition(0, new Vector2(1f, 8.5f));
        Run(model, 0.5, 0.1 + settings.floorSeconds - 0.1);
        Assert.AreEqual(1, model.FloorItems.Count);
        Run(model, 0.1 + settings.floorSeconds - 0.1, 0.1 + settings.floorSeconds + 0.1);
        Assert.AreEqual(0, model.FloorItems.Count);
    }

    // P2 runs at P1 from the left at the given speed (fraction of walkSpeed).
    static LauTokbokkiModel BumpScenario(float speed, out List<LtEvent> events)
    {
        var settings = new LauTokbokkiSettings();
        var model = NewModel(2, settings);
        var log = new List<LtEvent>();
        model.Event += log.Add;
        events = log;
        model.SetPosition(0, new Vector2(8f, 8.2f));
        model.SetCarried(0, Unneeded(model, 0));
        model.SetPosition(1, new Vector2(7.1f, 8.2f));
        model.SetCarried(1, Unneeded(model, 1));
        model.SetVelocity(1, new Vector2(speed * settings.walkSpeed, 0f));
        Run(model, 0, 0.2, (p, t) => p == 1 ? new Vector2(speed, 0f) : Vector2.zero);
        return model;
    }

    [Test]
    public void RunningIntoSomeone_KnocksTheirIngredientLoose_AndStunsThem()
    {
        var settings = new LauTokbokkiSettings();
        var model = BumpScenario(1f, out var events);

        Assert.IsNull(model.CarriedBy(0));
        Assert.IsNotNull(model.CarriedBy(1), "The faster player keeps theirs");
        Assert.AreEqual(1, model.FloorItems.Count);
        var knock = events.Single(e => e.Kind == LtEventKind.KnockedOff);
        Assert.AreEqual(0, knock.Player);
        Assert.AreEqual(1, knock.Other);
        Assert.IsTrue(model.IsStunned(0));

        model.SetPosition(1, new Vector2(3f, 8.5f));
        var before = model.PositionOf(0);
        Run(model, 0.2, 0.25, (p, t) => p == 0 ? Vector2.up : Vector2.zero);
        Assert.AreEqual(before, model.PositionOf(0), "Stunned players can't move");
    }

    [Test]
    public void ABumpWhileStunned_StopsPickingUp()
    {
        var model = BumpScenario(1f, out _);
        Assert.IsTrue(model.IsStunned(0));
        model.SetPosition(1, new Vector2(3f, 8.5f));

        // A needed plate right at the stunned player's hand.
        ClearTable(model);
        var need = model.RecipeOf(0)[0];
        model.SetPlate(0, need);
        var plate = model.PlatePosition(0) - model.Layout.TableCenter;
        model.SetPosition(0, AtTableEdge(model, Mathf.Atan2(plate.y, plate.x)));

        Run(model, 0.2, 0.25);
        Assert.IsNull(model.CarriedBy(0), "Still stunned");
        Run(model, 0.25, 0.8);
        Assert.AreEqual(need, model.CarriedBy(0), "Picks it up once the stun wears off");
    }

    [Test]
    public void ASlowBump_KnocksNothingLoose()
    {
        var model = BumpScenario(0.5f, out var events);

        Assert.IsNotNull(model.CarriedBy(0));
        Assert.AreEqual(0, model.FloorItems.Count);
        Assert.IsFalse(events.Any(e => e.Kind == LtEventKind.KnockedOff));
        Assert.Less(Vector2.Distance(model.PositionOf(0), model.PositionOf(1)), 0.77f, "They touched");
    }

    [Test]
    public void PlayersStayOutOfTheTable_ThePots_AndInsideTheArena()
    {
        var settings = new LauTokbokkiSettings();
        var model = NewModel(4, settings);
        var table = model.Layout.TableCenter;
        Func<int, double, Vector2> stick = (p, t) =>
            p == 0 ? (table - model.PositionOf(0)).normalized                          // into the table
            : p == 1 ? (model.Layout.Pots[1] - model.PositionOf(1)).normalized          // into their pot
            : p == 2 ? Vector2.up                                                     // into the wall
            : new Vector2(1f, -1f).normalized;                                         // into the corner
        float r = settings.playerRadius;

        for (double t = 0; t < 4; t += 0.5)
        {
            Run(model, t, t + 0.5, stick);
            Assert.GreaterOrEqual(Vector2.Distance(model.PositionOf(0), table), model.Layout.TableRadius + r - 1e-3f);
            for (int p = 0; p < 4; p++)
            {
                foreach (var pot in model.Layout.Pots)
                    Assert.GreaterOrEqual(Vector2.Distance(model.PositionOf(p), pot), LauTokbokkiLayout.PotRadius + r - 1e-3f);
                var at = model.PositionOf(p);
                Assert.That(at.x, Is.InRange(r - 1e-3f, LauTokbokkiLayout.Width - r + 1e-3f));
                Assert.That(at.y, Is.InRange(r - 1e-3f, LauTokbokkiLayout.Height - r + 1e-3f));
            }
        }
        Assert.AreEqual(LauTokbokkiLayout.Height - r, model.PositionOf(2).y, 1e-3f);
    }

    [Test]
    public void TheFirstPerfectPot_WinsAtOnce()
    {
        var model = NewModel(4);
        var events = new List<LtEvent>();
        model.Event += events.Add;
        PutIn(model, 3, 0, model.RecipeOf(0)[0], 0.1);
        var recipe = model.RecipeOf(2);
        for (int i = 0; i < recipe.Count; i++) PutIn(model, 2, 2, recipe[i], 0.2 + 0.1 * i);

        Assert.IsTrue(model.IsOver);
        Assert.AreEqual(2, model.WinnerPlayer);
        Assert.IsTrue(model.HasWinner);
        Assert.AreEqual(LtEventKind.Perfect, events.Last().Kind);
        CollectionAssert.AreEqual(new[] { 2, 3, 1, 3 }, model.Ranking());

        var time = model.SimulatedTime;
        Run(model, 1, 3);
        Assert.AreEqual(time, model.SimulatedTime, "Nothing runs after the end");
    }

    [Test]
    public void AtTimeUp_TheMostCorrectIngredientsWin()
    {
        var settings = new LauTokbokkiSettings();
        var model = NewModel(2, settings);
        PutIn(model, 0, 0, model.RecipeOf(0)[0], 0.1);
        PutIn(model, 1, 1, model.RecipeOf(1)[0], 0.2);
        PutIn(model, 1, 1, model.RecipeOf(1)[1], 0.3);
        PutIn(model, 1, 1, Unneeded(model, 1), 0.4);
        foreach (int p in new[] { 0, 1 }) model.SetPosition(p, model.Layout.Spawns[p]);

        Run(model, 0.4, settings.roundSeconds - 0.1);
        Assert.IsFalse(model.IsOver);
        Run(model, settings.roundSeconds - 0.1, settings.roundSeconds + 0.5);

        Assert.IsTrue(model.IsOver);
        Assert.AreEqual(1, model.WinnerPlayer);
        CollectionAssert.AreEqual(new[] { 2, 1 }, model.Ranking());
    }

    [Test]
    public void AtTimeUp_ASharedTopCountIsADraw_AndTiesShareARank()
    {
        var settings = new LauTokbokkiSettings();
        var model = NewModel(4, settings);
        PutIn(model, 0, 0, model.RecipeOf(0)[0], 0.1);
        PutIn(model, 1, 1, model.RecipeOf(1)[0], 0.2);
        PutIn(model, 1, 1, model.RecipeOf(1)[1], 0.3);
        PutIn(model, 2, 2, model.RecipeOf(2)[0], 0.4);
        PutIn(model, 2, 2, model.RecipeOf(2)[1], 0.5);
        for (int p = 0; p < 4; p++) model.SetPosition(p, model.Layout.Spawns[p]);

        Run(model, 0.5, settings.roundSeconds + 0.5);

        Assert.IsTrue(model.IsOver);
        Assert.AreEqual(-1, model.WinnerPlayer);
        Assert.IsFalse(model.HasWinner);
        CollectionAssert.AreEqual(new[] { 3, 1, 1, 4 }, model.Ranking());
    }

    // Changes only on the half second, which both frame rates land on.
    static Vector2 Scripted(int player, double seconds)
    {
        Vector2[] moves = { new Vector2(1f, 0.2f), new Vector2(0.3f, 0.9f), new Vector2(-0.7f, 0.4f), new Vector2(0.2f, -1f), new Vector2(1f, 1f) };
        int part = (int)Math.Floor(seconds * 2.0 + 1e-6);
        return part < 10 ? moves[(part + player) % moves.Length] : Vector2.zero;
    }

    [Test]
    public void TheSameInput_GivesTheSameResult_At30And144Fps()
    {
        var slow = NewModel(4, seed: 7);
        var fast = NewModel(4, seed: 7);
        var slowEvents = new List<string>();
        var fastEvents = new List<string>();
        slow.Event += e => slowEvents.Add($"{e.Kind} {e.Player} {e.Other} {e.Item} {e.Slot}");
        fast.Event += e => fastEvents.Add($"{e.Kind} {e.Player} {e.Other} {e.Item} {e.Slot}");

        Run(slow, 0, 6, Scripted, 1.0 / 30.0);
        Run(fast, 0, 6, Scripted, 1.0 / 144.0);

        for (int p = 0; p < 4; p++)
        {
            Assert.AreEqual(0f, Vector2.Distance(slow.PositionOf(p), fast.PositionOf(p)), 1e-4f);
            Assert.AreEqual(slow.CarriedBy(p), fast.CarriedBy(p));
        }
        CollectionAssert.AreEqual(slowEvents, fastEvents);
        Assert.IsNotEmpty(slowEvents, "The script makes something happen");
    }
}
