using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

public class PodiumCelebrationTests
{
    [Test]
    public void TheCupStaysDown_UntilTheColumnsHaveLanded()
    {
        Assert.AreEqual(0f, PodiumCelebration.RaiseAt(0f));
        Assert.AreEqual(0f, PodiumCelebration.RaiseAt(PodiumCelebration.StartSeconds));
    }

    [Test]
    public void TheCupEndsFullyRaised()
    {
        float raised = PodiumCelebration.StartSeconds + PodiumCelebration.RaiseSeconds;
        Assert.AreEqual(1f, PodiumCelebration.RaiseAt(raised));
        Assert.AreEqual(1f, PodiumCelebration.RaiseAt(raised + 60f));
    }

    [Test]
    public void EveryoneSwaysBothWays_AndNotInStep()
    {
        float min = 0f, max = 0f;
        for (float time = 2f; time < 6f; time += 0.05f)
        {
            float sway = PodiumCelebration.SwayAt(time, 0);
            min = Mathf.Min(min, sway);
            max = Mathf.Max(max, sway);
        }

        Assert.Less(min, -5f);
        Assert.Greater(max, 5f);
        Assert.AreNotEqual(PodiumCelebration.SwayAt(3f, 0), PodiumCelebration.SwayAt(3f, 1));
    }

    [Test]
    public void Confetti_BurstsFromThePool_AndClearsAway()
    {
        var layer = new VisualElement();
        var confetti = new Confetti(layer);

        confetti.Burst(Vector2.zero, 40);
        confetti.Update(0.1f);
        Assert.AreEqual(40, confetti.ActiveCount);

        for (int i = 0; i < 60; i++) confetti.Update(0.1f);
        Assert.AreEqual(0, confetti.ActiveCount);
    }

    [Test]
    public void Confetti_NeverOutgrowsItsPool()
    {
        var layer = new VisualElement();
        var confetti = new Confetti(layer);

        confetti.Burst(Vector2.zero, 5000);
        confetti.Update(0.016f);

        Assert.AreEqual(layer.childCount, confetti.ActiveCount);
    }
}
