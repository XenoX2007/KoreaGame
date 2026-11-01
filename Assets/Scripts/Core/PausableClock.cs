// Game time that can stop. Rounds run on unscaled time, so Time.timeScale cannot pause them: this
// subtracts the time spent paused instead, and timelines generated up front stay valid after a pause.
public class PausableClock
{
    double _stoppedSeconds;
    double _pausedAt;

    public bool IsPaused { get; private set; }

    public double At(double realTime) => (IsPaused ? _pausedAt : realTime) - _stoppedSeconds;

    public void Pause(double realTime)
    {
        if (IsPaused) return;
        IsPaused = true;
        _pausedAt = realTime;
    }

    public void Resume(double realTime)
    {
        if (!IsPaused) return;
        IsPaused = false;
        _stoppedSeconds += realTime - _pausedAt;
    }
}
