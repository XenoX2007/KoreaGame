using NUnit.Framework;

public class KeoCoModelTests
{
    const double Start = 10.0;
    const float Frame = 1f / 60f;

    static KeoCoModel NewModel(int players, KeoCoSettings settings = null) =>
        new KeoCoModel(players, settings ?? new KeoCoSettings(), Start);

    [Test]
    public void FunFact_SwitchesLanguageEveryTwoSeconds()
    {
        string vietnamese = ResultsOverlay.FactAt(KeoCoGame.Facts, 0.0);
        string korean = ResultsOverlay.FactAt(KeoCoGame.Facts, 2.0);

        StringAssert.StartsWith("Bạn có biết?", vietnamese);
        StringAssert.StartsWith("알고 있나요?", korean);
        Assert.AreEqual(vietnamese, ResultsOverlay.FactAt(KeoCoGame.Facts, 1.99));
        Assert.AreEqual(vietnamese, ResultsOverlay.FactAt(KeoCoGame.Facts, 4.0));
        Assert.AreEqual(korean, ResultsOverlay.FactAt(KeoCoGame.Facts, 7.5));
    }

    // Advances the model frame by frame until the given time.
    static void RunUntil(KeoCoModel model, ref double now, double until)
    {
        while (now < until && !model.IsOver)
        {
            now += Frame;
            model.Tick(now, Frame);
        }
    }

    [Test]
    public void TapJudgement_DependsOnDistanceFromBeat()
    {
        var model = NewModel(2);
        double beat = model.BeatTime(4);

        Assert.AreEqual(TapJudgement.Perfect, model.Tap(0, beat + 0.03).Judgement);
        Assert.AreEqual(TapJudgement.Good, model.Tap(1, beat - 0.12).Judgement);
        Assert.AreEqual(TapJudgement.Miss, model.Tap(0, model.BeatTime(6) + 0.3).Judgement);
    }

    [Test]
    public void TapsBeforeStartOrAfterEnd_AreIgnored()
    {
        var model = NewModel(2);
        Assert.AreEqual(TapJudgement.Ignored, model.Tap(0, Start - 1.0).Judgement);

        double now = Start;
        RunUntil(model, ref now, model.EndTime + 0.1);
        Assert.IsTrue(model.IsOver);
        Assert.AreEqual(TapJudgement.Ignored, model.Tap(0, now).Judgement);
    }

    [Test]
    public void SecondTapOnSameBeat_DoesNotScoreAgain()
    {
        var model = NewModel(2);
        double beat = model.BeatTime(3);

        Assert.AreEqual(TapJudgement.Perfect, model.Tap(0, beat).Judgement);
        Assert.AreEqual(TapJudgement.Miss, model.Tap(0, beat + 0.05).Judgement);
    }

    [Test]
    public void OffBeatTap_LosesGripUntilThePlayerStopsTapping()
    {
        var settings = new KeoCoSettings();
        var model = NewModel(2, settings);
        double beat = model.BeatTime(5);
        double offBeat = beat - settings.goodWindow - 0.05;

        Assert.AreEqual(TapJudgement.Miss, model.Tap(0, offBeat).Judgement);
        var duringSlip = model.Tap(0, beat); // on the beat, but grip is still lost
        Assert.AreEqual(TapJudgement.Miss, duringSlip.Judgement);
        Assert.AreEqual(0f, duringSlip.Impulse);

        Assert.AreEqual(TapJudgement.Perfect, model.Tap(0, model.BeatTime(6)).Judgement, "Grip returns after a pause");
    }

    [Test]
    public void LeftTeamPullsTowardMinusOne_RightTeamTowardPlusOne()
    {
        var model = NewModel(2);
        double now = Start;
        model.Tap(0, model.BeatTime(0));
        RunUntil(model, ref now, Start + 1.0);
        Assert.Less(model.RopePosition, 0f);

        model.Tap(1, model.BeatTime(2));
        model.Tap(1, model.BeatTime(3));
        RunUntil(model, ref now, Start + 3.0);
        Assert.Greater(model.RopePosition, 0f);
    }

    [Test]
    public void TeammatesHittingTheSameBeatTogether_GetDongLongBonus()
    {
        var model = NewModel(4);
        double beat = model.BeatTime(2);

        var first = model.Tap(0, beat - 0.02);
        var second = model.Tap(2, beat + 0.03);
        var opponent = model.Tap(1, beat + 0.01);

        Assert.IsFalse(first.Synced);
        Assert.IsTrue(second.Synced);
        Assert.Greater(second.Impulse, first.Impulse);
        Assert.IsFalse(opponent.Synced, "Opponents never share the bonus");
    }

    [Test]
    public void OneVsOne_HasNoDongLongBonus()
    {
        var model = NewModel(2);
        double beat = model.BeatTime(2);
        model.Tap(0, beat);
        Assert.IsFalse(model.Tap(1, beat).Synced);
    }

    [Test]
    public void PlayerOnTheBeat_BeatsPlayerMashing()
    {
        var model = NewModel(2);
        double now = Start;
        int nextBeat = 0;
        double nextMash = Start;

        while (!model.IsOver && now < model.EndTime + 1.0)
        {
            now += Frame;
            if (now >= model.BeatTime(nextBeat)) model.Tap(0, model.BeatTime(nextBeat++)); // P1: perfect rhythm
            if (now >= nextMash) { model.Tap(1, now); nextMash += 1.0 / 8.0; }           // P2: 8 taps per second
            model.Tick(now, Frame);
        }

        Assert.IsTrue(model.IsOver);
        Assert.AreEqual(0, model.WinningTeam);
    }

    [Test]
    public void SoloPerfectPuller_WinsBeforeTimeAgainstIdleOpponent()
    {
        var model = NewModel(2);
        double now = Start;
        int beat = 0;

        while (!model.IsOver && now < model.EndTime)
        {
            now += Frame;
            if (now >= model.BeatTime(beat)) model.Tap(1, model.BeatTime(beat++));
            model.Tick(now, Frame);
        }

        Assert.IsTrue(model.IsOver);
        Assert.AreEqual(1, model.WinningTeam);
        Assert.Less(now, model.EndTime, "Should cross the line before the timer runs out");
        Assert.AreEqual(1f, model.RopePosition);
    }

    [Test]
    public void Timeout_GoesToTheSideTheRibbonIsOn()
    {
        var model = NewModel(2);
        double now = Start;
        model.Tap(0, model.BeatTime(1));
        RunUntil(model, ref now, model.EndTime + 0.1);

        Assert.IsTrue(model.IsOver);
        Assert.AreEqual(0, model.WinningTeam);
    }

    [Test]
    public void Timeout_WithRibbonCentered_IsADraw()
    {
        var model = NewModel(2);
        double now = Start;
        RunUntil(model, ref now, model.EndTime + 0.1);

        Assert.AreEqual(-1, model.WinningTeam);
        CollectionAssert.AreEqual(new[] { 0f, 0f }, model.PlayerScores());
    }

    [Test]
    public void TwoVsTwo_WinnersShareFirstPlace()
    {
        var model = NewModel(4);
        double now = Start;
        int beat = 0;

        while (!model.IsOver)
        {
            now += Frame;
            if (now >= model.BeatTime(beat))
            {
                model.Tap(0, model.BeatTime(beat));
                model.Tap(2, model.BeatTime(beat) + 0.01);
                beat++;
            }
            model.Tick(now, Frame);
        }

        Assert.AreEqual(0, model.WinningTeam);
        CollectionAssert.AreEqual(new[] { 1f, 0f, 1f, 0f }, model.PlayerScores());
    }

    [Test]
    public void Tempo_SpeedsUpOnlyInTheFinalSeconds()
    {
        var settings = new KeoCoSettings();
        var model = NewModel(2, settings);

        Assert.AreEqual(settings.startBpm, model.BpmAt(5.0));
        Assert.AreEqual(settings.startBpm, model.BpmAt(settings.roundSeconds - settings.rampSeconds));
        Assert.AreEqual(settings.endBpm, model.BpmAt(settings.roundSeconds), 0.01f);
    }

    [Test]
    public void PullDistance_DoesNotDependOnFrameRate()
    {
        float Pull(float frame)
        {
            var model = NewModel(2);
            model.Tap(1, model.BeatTime(0));
            for (double t = Start; t < Start + 2.0; t += frame) model.Tick(t, frame);
            return model.RopePosition;
        }

        Assert.AreEqual(Pull(1f / 30f), Pull(1f / 120f), 1e-4f);
    }
}
