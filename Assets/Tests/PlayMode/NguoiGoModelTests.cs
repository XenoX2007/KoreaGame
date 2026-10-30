using System.Collections.Generic;
using NUnit.Framework;

public class NguoiGoModelTests
{
    const double Start = 10.0;
    const float Frame = 1f / 60f;

    // Everything predictable unless a test asks otherwise.
    static NguoiGoSettings Plain() => new NguoiGoSettings { fastChance = 0f, stretchChance = 0f, fakeChance = 0f };

    static NguoiGoModel NewModel(int players, NguoiGoSettings settings = null, int seed = 1) =>
        new NguoiGoModel(players, settings ?? Plain(), Start, seed);

    static void Step(NguoiGoModel model, ref double now)
    {
        now += Frame;
        model.Tick(now, Frame);
    }

    static void RunUntilPhase(NguoiGoModel model, ref double now, CallerPhase phase)
    {
        for (int i = 0; i < 60 * 60 && model.Phase != phase && !model.IsOver; i++)
            Step(model, ref now);
        Assert.AreEqual(phase, model.Phase, "Never reached the phase");
    }

    [Test]
    public void HoldingRuns_LettingGoStandsStill()
    {
        var model = NewModel(2);
        double now = Start;

        model.SetHeld(0, true);
        for (int i = 0; i < 30; i++) Step(model, ref now);

        Assert.Greater(model.PositionOf(0), 0f);
        Assert.AreEqual(0f, model.PositionOf(1));

        model.SetHeld(0, false);
        float stopped = model.PositionOf(0);
        for (int i = 0; i < 30; i++) Step(model, ref now);
        Assert.AreEqual(stopped, model.PositionOf(0));
    }

    [Test]
    public void StillHoldingWhenCallerWatches_GoesBackToTheStart()
    {
        var model = NewModel(2);
        var caught = new List<int>();
        model.Caught += caught.Add;
        double now = Start;

        model.SetHeld(0, true);
        RunUntilPhase(model, ref now, CallerPhase.Watching);

        Assert.AreEqual(NguoiGoModel.CaughtPosition, model.PositionOf(0));
        CollectionAssert.AreEqual(new[] { 0 }, caught);
    }

    [Test]
    public void LettingGoDuringTheTurn_KeepsYourPlace()
    {
        var model = NewModel(2);
        var caught = new List<int>();
        model.Caught += caught.Add;
        double now = Start;

        model.SetHeld(0, true);
        RunUntilPhase(model, ref now, CallerPhase.Turning);
        model.SetHeld(0, false);
        float position = model.PositionOf(0);
        RunUntilPhase(model, ref now, CallerPhase.Watching);

        Assert.AreEqual(position, model.PositionOf(0));
        Assert.IsEmpty(caught);
    }

    [Test]
    public void PressingWhileTheCallerWatches_GetsYouCaught()
    {
        var model = NewModel(2);
        double now = Start;

        model.SetHeld(1, true);
        RunUntilPhase(model, ref now, CallerPhase.Tell);
        model.SetHeld(1, false);
        float position = model.PositionOf(1);
        Assert.Greater(position, 0f);
        RunUntilPhase(model, ref now, CallerPhase.Watching);

        model.SetHeld(1, true);
        Step(model, ref now);

        Assert.AreEqual(0f, model.PositionOf(1));
    }

    [Test]
    public void CaughtOncePerWatch_EvenIfThePlayerKeepsHolding()
    {
        var model = NewModel(2);
        int caught = 0;
        model.Caught += _ => caught++;
        double now = Start;

        model.SetHeld(0, true);
        RunUntilPhase(model, ref now, CallerPhase.Watching);
        for (int i = 0; i < 30; i++) Step(model, ref now);

        Assert.AreEqual(1, caught);
    }

    [Test]
    public void FakeTurn_NeverCatchesAnyone()
    {
        var settings = Plain();
        settings.fakeChance = 1f;
        var model = NewModel(2, settings);
        var caught = new List<int>();
        model.Caught += caught.Add;
        double now = Start;

        model.SetHeld(0, true);
        RunUntilPhase(model, ref now, CallerPhase.Fake);
        float position = model.PositionOf(0);
        RunUntilPhase(model, ref now, CallerPhase.Chanting);

        Assert.Greater(model.PositionOf(0), position);
        Assert.IsEmpty(caught);
    }

    // The fairness promise: over many random chants, watching never starts without the full warning.
    [Test]
    public void CallerAlwaysGivesTheTellBeforeWatching()
    {
        var settings = new NguoiGoSettings { roundSeconds = 600f };
        for (int seed = 0; seed < 40; seed++)
        {
            var model = new NguoiGoModel(2, settings, Start, seed);
            double now = Start, phaseStart = now;
            var previous = model.Phase;
            double warning = 0.0;

            while (now < Start + 60.0)
            {
                now += Frame;
                model.Tick(now, Frame);
                if (model.Phase == previous) continue;

                if (previous == CallerPhase.Tell || previous == CallerPhase.Turning) warning += now - phaseStart;
                if (model.Phase == CallerPhase.Watching)
                {
                    Assert.AreEqual(CallerPhase.Turning, previous);
                    Assert.GreaterOrEqual(warning, settings.tellSeconds + settings.turningSeconds - 3 * Frame);
                }
                if (model.Phase == CallerPhase.Chanting) warning = 0.0;

                previous = model.Phase;
                phaseStart = now;
            }
        }
    }

    [Test]
    public void Chant_AlternatesBetweenVietnameseAndKorean()
    {
        var model = NewModel(2);
        double now = Start;

        Assert.AreEqual(ChantVersion.Vietnamese, model.Version);
        RunUntilPhase(model, ref now, CallerPhase.Watching);
        Assert.AreEqual(ChantVersion.Vietnamese, model.Version);
        RunUntilPhase(model, ref now, CallerPhase.Chanting);
        Assert.AreEqual(ChantVersion.Korean, model.Version);
        RunUntilPhase(model, ref now, CallerPhase.Watching);
        RunUntilPhase(model, ref now, CallerPhase.Chanting);
        Assert.AreEqual(ChantVersion.Vietnamese, model.Version);
    }

    [Test]
    public void SungText_GrowsSyllableBySyllable()
    {
        var settings = Plain();
        var model = NewModel(2, settings);
        double step = settings.normalChantSeconds / 5.0;

        Assert.AreEqual("Một ", model.SungText(Start));
        Assert.AreEqual("Một hai ", model.SungText(Start + step * 1.1));
        Assert.AreEqual("Một hai ba người gỗ!", model.SungText(Start + step * 4.1));
    }

    [Test]
    public void FastChant_LeavesLessTimeThanANormalOne()
    {
        var fast = Plain();
        fast.fastChance = 1f;
        var normal = NewModel(2);
        var quick = NewModel(2, fast);
        double normalTime = Start, quickTime = Start;

        RunUntilPhase(normal, ref normalTime, CallerPhase.Tell);
        RunUntilPhase(quick, ref quickTime, CallerPhase.Tell);

        Assert.Less(quickTime, normalTime);
    }

    [Test]
    public void StretchedChant_HoldsTheLastSyllableLonger()
    {
        var stretched = Plain();
        stretched.stretchChance = 1f;
        var normal = NewModel(2);
        var slow = NewModel(2, stretched);
        double normalTime = Start, slowTime = Start;

        RunUntilPhase(normal, ref normalTime, CallerPhase.Tell);
        RunUntilPhase(slow, ref slowTime, CallerPhase.Tell);

        Assert.GreaterOrEqual(slowTime, normalTime + stretched.stretchMinSeconds - 2 * Frame);
    }

    [Test]
    public void FirstToReachTheCaller_Wins()
    {
        var model = NewModel(4);
        double now = Start;

        // P2 only moves in safety; P4 ignores the caller and keeps getting sent back.
        model.SetHeld(3, true);
        while (!model.IsOver)
        {
            model.SetHeld(1, model.Phase == CallerPhase.Chanting);
            Step(model, ref now);
        }

        Assert.AreEqual(1f, model.PositionOf(1));
        CollectionAssert.AreEqual(new[] { 1 }, model.Winners());
        Assert.AreEqual(1, MiniGameBase.RanksFromScores(model.PlayerScores())[1]);
        Assert.Less(model.PositionOf(3), 1f);
    }

    [Test]
    public void Timeout_GoesToTheClosestPlayer()
    {
        var settings = Plain();
        settings.roundSeconds = 1f;
        var model = NewModel(2, settings);
        double now = Start;

        model.SetHeld(1, true);
        for (int i = 0; i < 20; i++) Step(model, ref now);
        model.SetHeld(1, false);
        while (!model.IsOver) Step(model, ref now);

        CollectionAssert.AreEqual(new[] { 1 }, model.Winners());
    }

    [Test]
    public void Timeout_WithNobodyMoving_HasNoWinner()
    {
        var settings = Plain();
        settings.roundSeconds = 1f;
        var model = NewModel(2, settings);
        double now = Start;

        while (!model.IsOver) Step(model, ref now);

        Assert.IsEmpty(model.Winners());
        CollectionAssert.AreEqual(new[] { 1, 1 }, MiniGameBase.RanksFromScores(model.PlayerScores()));
    }
}
