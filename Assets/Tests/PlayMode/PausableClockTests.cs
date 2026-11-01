using NUnit.Framework;

public class PausableClockTests
{
    [Test]
    public void RunsWithRealTime_UntilPaused()
    {
        var clock = new PausableClock();
        Assert.AreEqual(10.0, clock.At(10.0));

        clock.Pause(10.0);
        Assert.AreEqual(10.0, clock.At(25.0));
    }

    [Test]
    public void ContinuesFromWhereItStopped()
    {
        var clock = new PausableClock();
        clock.Pause(10.0);
        clock.Resume(25.0);

        Assert.AreEqual(10.0, clock.At(25.0));
        Assert.AreEqual(12.0, clock.At(27.0));
    }

    [Test]
    public void SeveralPausesAddUp_AndRepeatedCallsAreHarmless()
    {
        var clock = new PausableClock();
        clock.Pause(10.0);
        clock.Pause(12.0);
        clock.Resume(15.0);
        clock.Resume(16.0);
        clock.Pause(20.0);
        clock.Resume(30.0);

        Assert.AreEqual(20.0, clock.At(35.0));
    }
}
