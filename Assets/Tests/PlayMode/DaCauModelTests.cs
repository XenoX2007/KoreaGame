using System.Collections.Generic;
using NUnit.Framework;

public class DaCauModelTests
{
    const double Start = 10.0;
    const double Frame = 1.0 / 60.0;

    static DaCauSettings Calm() => new DaCauSettings { windChance = 0f };

    static DaCauModel NewModel(int players, DaCauSettings settings = null, int seed = 1) =>
        new DaCauModel(players, settings ?? Calm(), Start, seed);

    // Kicks the current flight exactly on arrival and returns that time.
    static double KickOnTime(DaCauModel model)
    {
        double time = model.Arrival;
        Assert.AreEqual(KickResult.Perfect, model.Tap(model.To, time));
        return time;
    }

    static void LetItDrop(DaCauModel model)
    {
        model.Tick(model.Arrival + 1.0);
    }

    // Lets the current flight drop, then waits for the next serve.
    static void LoseRally(DaCauModel model, DaCauSettings settings)
    {
        double dropped = model.Arrival + 1.0;
        model.Tick(dropped);
        if (!model.IsOver) model.Tick(dropped + settings.dropPauseSeconds + 0.01);
    }

    [Test]
    public void KickTiming_DependsOnDistanceFromArrival()
    {
        var settings = Calm();
        var perfect = NewModel(2, settings);
        var good = NewModel(2, settings);

        Assert.AreEqual(KickResult.Perfect, perfect.Tap(1, perfect.Arrival - 0.05));
        Assert.AreEqual(KickResult.Good, good.Tap(1, good.Arrival + 0.12));
    }

    [Test]
    public void WithFourPlayers_EveryKickerCanReachEveryOtherPlayer_ButNeverThemselves()
    {
        var model = NewModel(4);
        var seen = new HashSet<(int from, int to)>();

        for (int i = 0; i < 300; i++)
        {
            Assert.AreNotEqual(model.From, model.To);
            seen.Add((model.From, model.To));
            KickOnTime(model);
        }

        Assert.AreEqual(4 * 3, seen.Count, "Every kicker should reach all three other players");
    }

    [Test]
    public void Receivers_DoNotFollowAFixedOrder()
    {
        var first = NewModel(4, seed: 1);
        var second = NewModel(4, seed: 2);
        var firstOrder = new List<int>();
        var secondOrder = new List<int>();

        for (int i = 0; i < 20; i++)
        {
            firstOrder.Add(first.To);
            secondOrder.Add(second.To);
            KickOnTime(first);
            KickOnTime(second);
        }

        CollectionAssert.AreNotEqual(firstOrder, secondOrder);
    }

    [Test]
    public void WithTwoPlayers_TheShuttleGoesBackAndForth()
    {
        var model = NewModel(2);

        for (int i = 0; i < 20; i++)
        {
            Assert.AreEqual(1 - model.From, model.To);
            KickOnTime(model);
        }
    }

    [Test]
    public void OnlyThePlayerReceivingCanKick()
    {
        var model = NewModel(4);

        for (int id = 0; id < 4; id++)
        {
            if (id == model.To) continue;
            Assert.AreEqual(KickResult.Ignored, model.Tap(id, model.Arrival));
            Assert.AreEqual(3, model.LivesOf(id));
        }
    }

    [Test]
    public void TapBeforeTheZone_IsIgnoredWithoutPenalty()
    {
        var model = NewModel(2);

        Assert.AreEqual(KickResult.Ignored, model.Tap(1, model.ZoneStart - 0.05));
        Assert.AreEqual(3, model.LivesOf(1));
        Assert.IsTrue(model.IsFlying);
    }

    [Test]
    public void TapInTheZoneTooSoon_CostsALife()
    {
        var settings = Calm();
        var model = NewModel(2, settings);

        Assert.AreEqual(KickResult.TooEarly, model.Tap(1, model.Arrival - settings.goodWindow - 0.05));
        Assert.AreEqual(2, model.LivesOf(1));
        Assert.IsFalse(model.IsFlying);
    }

    [Test]
    public void NotKickingInTime_CostsALife()
    {
        var model = NewModel(2);
        var missed = new List<int>();
        model.Missed += missed.Add;

        model.Tick(model.Arrival);
        Assert.IsTrue(model.IsFlying, "Still inside the Good window");

        LetItDrop(model);

        Assert.AreEqual(2, model.LivesOf(1));
        CollectionAssert.AreEqual(new[] { 1 }, missed);
    }

    [Test]
    public void AfterADrop_ThePlayerWhoMissedServes()
    {
        var settings = Calm();
        var model = NewModel(4, settings);
        int missed = model.To;

        LoseRally(model, settings);

        Assert.IsTrue(model.IsFlying);
        Assert.AreEqual(missed, model.From);
        Assert.AreNotEqual(missed, model.To);
    }

    [Test]
    public void PerfectKick_FliesFasterThanGood()
    {
        var settings = Calm();
        var perfect = NewModel(2, settings);
        var good = NewModel(2, settings);

        double perfectAt = perfect.Arrival;
        perfect.Tap(1, perfectAt);
        double goodAt = good.Arrival + 0.12;
        good.Tap(1, goodAt);

        Assert.Less(perfect.Arrival - perfectAt, good.Arrival - goodAt);
    }

    [Test]
    public void LongerRally_FliesFaster_DownToTheMinimum()
    {
        var settings = Calm();
        var model = NewModel(2, settings);

        double first = KickOnTime(model);
        double firstFlight = model.Arrival - first;

        for (int i = 0; i < 80; i++) KickOnTime(model);
        double kick = KickOnTime(model);

        Assert.Less(model.Arrival - kick, firstFlight);
        Assert.AreEqual(settings.minFlightSeconds, model.Arrival - kick, 1e-6);
    }

    [Test]
    public void RallyResetsAfterADrop()
    {
        var settings = Calm();
        var model = NewModel(2, settings);
        KickOnTime(model);
        KickOnTime(model);
        Assert.AreEqual(2, model.Rally);

        LoseRally(model, settings);

        Assert.AreEqual(0, model.Rally);
    }

    [Test]
    public void ShuttleKindAlternatesAfterEachDrop_AndJegiFliesQuicker()
    {
        var settings = Calm();
        var model = NewModel(2, settings);
        Assert.AreEqual(ShuttleKind.Feather, model.Kind);
        double featherServe = model.Arrival - model.FlightStart;

        LoseRally(model, settings);
        Assert.AreEqual(ShuttleKind.Jegi, model.Kind);
        Assert.Less(model.Arrival - model.FlightStart, featherServe);

        LoseRally(model, settings);
        Assert.AreEqual(ShuttleKind.Feather, model.Kind);
    }

    [Test]
    public void WindOnlyArrivesInALongEnoughRally_AndChangesTheFlight()
    {
        var settings = new DaCauSettings { windChance = 1f, windStartRally = 3 };
        var calm = NewModel(2, Calm());
        var windy = NewModel(2, settings);

        for (int i = 0; i < 2; i++)
        {
            KickOnTime(calm);
            KickOnTime(windy);
            Assert.AreEqual(0, windy.Wind);
        }

        double calmKick = KickOnTime(calm);
        double windyKick = KickOnTime(windy);

        Assert.AreNotEqual(0, windy.Wind);
        double expected = (calm.Arrival - calmKick) * (1.0 - windy.Wind * settings.windScale);
        Assert.AreEqual(expected, windy.Arrival - windyKick, 1e-6);
    }

    [Test]
    public void ServeIsAlwaysCalm()
    {
        var settings = new DaCauSettings { windChance = 1f, windStartRally = 0 };
        var model = NewModel(2, settings);
        KickOnTime(model);
        Assert.AreNotEqual(0, model.Wind);

        LoseRally(model, settings);

        Assert.AreEqual(0, model.Wind);
    }

    [Test]
    public void KnockedOutPlayersNeverGetTheShuttle()
    {
        var settings = Calm();
        settings.startingLives = 1;
        var model = NewModel(4, settings);
        int knockedOut = model.To;

        LoseRally(model, settings);
        Assert.AreEqual(0, model.LivesOf(knockedOut));

        for (int i = 0; i < 100; i++)
        {
            Assert.AreNotEqual(knockedOut, model.From);
            Assert.AreNotEqual(knockedOut, model.To);
            KickOnTime(model);
        }
    }

    [Test]
    public void LastPlayerStanding_Wins_AndOthersRankByHowLongTheyLasted()
    {
        var settings = Calm();
        settings.startingLives = 1;
        var model = NewModel(4, settings);
        var knockedOut = new List<int>();
        model.Missed += knockedOut.Add;

        for (int i = 0; i < 3; i++) LoseRally(model, settings);

        Assert.IsTrue(model.IsOver);
        Assert.AreEqual(3, knockedOut.Count);
        Assert.IsFalse(knockedOut.Contains(model.Winner));

        var ranks = MiniGameBase.RanksFromScores(model.PlayerScores());
        Assert.AreEqual(1, ranks[model.Winner]);
        Assert.AreEqual(2, ranks[knockedOut[2]]);
        Assert.AreEqual(3, ranks[knockedOut[1]]);
        Assert.AreEqual(4, ranks[knockedOut[0]]);
    }

    [Test]
    public void TimedPlayer_BeatsPlayerMashing()
    {
        var settings = Calm();
        var model = NewModel(2, settings);
        double now = Start, nextMash = Start;

        while (!model.IsOver && now < Start + 120.0)
        {
            now += Frame;
            if (now >= nextMash)
            {
                model.Tap(1, now); // P2: 8 taps per second
                nextMash += 1.0 / 8.0;
            }
            if (model.IsFlying && model.To == 0 && now >= model.Arrival) model.Tap(0, now); // P1: on time
            model.Tick(now);
        }

        Assert.IsTrue(model.IsOver);
        Assert.AreEqual(0, model.Winner);
    }
}
