using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

// Drives the router with real Input System touch events on a simulated touchscreen,
// so the EnhancedTouch -> seat zone -> player event path is tested end to end.
public class PlayerInputRouterTests : InputTestFixture
{
    PlayerInputRouter _router;
    Keyboard _keyboard;
    readonly List<string> _events = new List<string>();

    public override void Setup()
    {
        base.Setup();
        InputSystem.AddDevice<Touchscreen>();
        _keyboard = InputSystem.AddDevice<Keyboard>();

        _router = PlayerInputRouter.Instance;
        _router.Configure(4);
        _events.Clear();
        _router.PlayerInput += (id, pressed) => _events.Add($"P{id + 1}{(pressed ? "+" : "-")}");
    }

    public override void TearDown()
    {
        Object.DestroyImmediate(_router.gameObject);
        base.TearDown();
    }

    static Vector2 At(float x, float y) => new Vector2(x * Screen.width, y * Screen.height);

    [Test]
    public void FourFingersAtOnce_EachLightsOnlyItsOwnSeat()
    {
        BeginTouch(1, At(0.1f, 0.1f)); // bottom-left  -> P1
        BeginTouch(2, At(0.9f, 0.9f)); // top-right    -> P2
        BeginTouch(3, At(0.1f, 0.9f)); // top-left     -> P3
        BeginTouch(4, At(0.9f, 0.1f)); // bottom-right -> P4

        for (int i = 0; i < 4; i++) Assert.IsTrue(_router.IsPressed(i), $"P{i + 1} should be held");
        Assert.AreEqual(4, _router.ActiveFingerCount);
        CollectionAssert.AreEqual(new[] { "P1+", "P2+", "P3+", "P4+" }, _events);

        EndTouch(2, At(0.9f, 0.9f));
        Assert.IsFalse(_router.IsPressed(1));
        Assert.IsTrue(_router.IsPressed(0) && _router.IsPressed(2) && _router.IsPressed(3));
    }

    [Test]
    public void FingerSlidingIntoAnotherZone_StaysWithItsOriginalPlayer()
    {
        BeginTouch(1, At(0.1f, 0.1f));
        MoveTouch(1, At(0.9f, 0.1f));

        Assert.IsTrue(_router.IsPressed(0));
        Assert.IsFalse(_router.IsPressed(3));

        EndTouch(1, At(0.9f, 0.1f));
        CollectionAssert.AreEqual(new[] { "P1+", "P1-" }, _events);
    }

    [Test]
    public void TwoFingersOnOneSeat_PressOnceAndReleaseAfterBothLift()
    {
        BeginTouch(1, At(0.1f, 0.1f));
        BeginTouch(2, At(0.3f, 0.3f));
        EndTouch(1, At(0.1f, 0.1f));
        Assert.IsTrue(_router.IsPressed(0));

        EndTouch(2, At(0.3f, 0.3f));
        CollectionAssert.AreEqual(new[] { "P1+", "P1-" }, _events);
    }

    [Test]
    public void TouchStartingInIgnoredZone_DoesNotPressAnySeat()
    {
        var center = new Rect(0.4f, 0.4f, 0.2f, 0.2f);
        _router.IgnoredZones.Add(center);

        BeginTouch(1, At(0.5f, 0.5f));

        Assert.IsEmpty(_events);
        Assert.AreEqual(0, _router.ActiveFingerCount);
        _router.IgnoredZones.Remove(center);
    }

    [Test]
    public void TwoPlayerLayout_SplitsScreenIntoLeftAndRightHalves()
    {
        _router.Configure(2);

        BeginTouch(1, At(0.2f, 0.9f));
        BeginTouch(2, At(0.8f, 0.1f));

        Assert.IsTrue(_router.IsPressed(0));
        Assert.IsTrue(_router.IsPressed(1));
        CollectionAssert.AreEqual(new[] { "P1+", "P2+" }, _events);
    }

    [Test]
    public void Configure_ReleasesEveryHeldPlayer()
    {
        BeginTouch(1, At(0.1f, 0.1f));
        BeginTouch(2, At(0.9f, 0.9f));

        _router.Configure(2);

        Assert.IsFalse(_router.IsPressed(0));
        Assert.IsFalse(_router.IsPressed(1));
        Assert.AreEqual(0, _router.ActiveFingerCount);
        CollectionAssert.AreEqual(new[] { "P1+", "P2+", "P1-", "P2-" }, _events);
    }

    [UnityTest]
    public IEnumerator SeatKeys_PressMatchingPlayers()
    {
        Press(_keyboard.zKey);
        Press(_keyboard.mKey);
        yield return null;

        Assert.IsTrue(_router.IsPressed(0));
        Assert.IsTrue(_router.IsPressed(3));

        Release(_keyboard.zKey);
        yield return null;

        Assert.IsFalse(_router.IsPressed(0));
        CollectionAssert.AreEqual(new[] { "P1+", "P4+", "P1-" }, _events);
    }
}
