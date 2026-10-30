using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

// Taps the real Menu scene on a simulated touchscreen, through the EventSystem into UI Toolkit.
public class MenuFlowTests : InputTestFixture
{
    public override void Setup()
    {
        base.Setup();
        InputSystem.AddDevice<Touchscreen>();
    }

    // Runs before TearDown. A UI panel left loaded keeps Unity's input-for-UI provider alive past this
    // test's fake input system, and the next test's touches then hit a provider that has shut down.
    [UnityTearDown]
    public IEnumerator UnloadLoadedScene()
    {
        var loaded = SceneManager.GetActiveScene();
        SceneManager.SetActiveScene(SceneManager.CreateScene("MenuFlowTestsEmpty"));
        yield return SceneManager.UnloadSceneAsync(loaded);
        yield return null;
    }

    public override void TearDown()
    {
        if (GameManager.Instance != null) Object.DestroyImmediate(GameManager.Instance.gameObject);
        var router = Object.FindAnyObjectByType<PlayerInputRouter>();
        if (router != null) Object.DestroyImmediate(router.gameObject);
        base.TearDown();
    }

    static IEnumerator LoadMenu()
    {
        yield return SceneManager.LoadSceneAsync("Menu");
        // Let the panel lay out and the cards' enter transitions finish.
        yield return new WaitForSecondsRealtime(1f);
    }

    static VisualElement MenuRoot()
    {
        var panel = Object.FindAnyObjectByType<MainMenu>().GetComponent<PanelRenderer>();
        var field = typeof(MainMenu).GetField("_root", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (VisualElement)field.GetValue(panel.GetComponent<MainMenu>());
    }

    IEnumerator Tap(VisualElement element, int touchId)
    {
        var bounds = element.worldBound;
        float scale = Screen.width / element.panel.visualTree.layout.width;
        var position = new Vector2(bounds.center.x * scale, Screen.height - bounds.center.y * scale);

        BeginTouch(touchId, position);
        yield return null;
        yield return null;
        EndTouch(touchId, position);
        yield return null;
        yield return null;
    }

    [UnityTest]
    public IEnumerator TappingPlayerCountAndCard_StartsThatGameWithThosePlayers()
    {
        yield return LoadMenu();
        var root = MenuRoot();

        yield return Tap(root.Q("fourPlayers"), 1);
        Assert.IsTrue(root.Q("fourPlayers").ClassListContains("is-selected"));
        Assert.IsFalse(root.Q("twoPlayers").ClassListContains("is-selected"));

        yield return Tap(root.Q("cardKeoCo"), 2);
        yield return new WaitForSecondsRealtime(0.5f);

        Assert.AreEqual("KeoCo", SceneManager.GetActiveScene().name);
        Assert.AreEqual(4, GameManager.Instance.PlayerCount);
        Assert.AreEqual(SessionMode.Single, GameManager.Instance.Mode);
        Assert.AreEqual(4, Object.FindAnyObjectByType<KeoCoGame>().PlayerCount);
    }

    [UnityTest]
    public IEnumerator ResultsButtons_ReplayTheRoundAndReturnToTheMenu()
    {
        yield return LoadMenu();
        yield return Tap(MenuRoot().Q("cardNguoiGo"), 1);
        yield return new WaitForSecondsRealtime(0.5f);
        Assert.AreEqual("NguoiGo", SceneManager.GetActiveScene().name);

        var game = Object.FindAnyObjectByType<NguoiGoGame>();
        var hud = (VisualElement)typeof(PanelMiniGame).GetField("_hud", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(game);
        var results = hud.Q("results");
        Assert.AreEqual(DisplayStyle.None, results.resolvedStyle.display);

        yield return EndRoundAfterCountdown(game);
        Assert.AreEqual(DisplayStyle.Flex, results.resolvedStyle.display);

        yield return Tap(hud.Q("playAgainButton"), 2);
        yield return null;
        Assert.AreEqual(DisplayStyle.None, results.resolvedStyle.display);
        Assert.IsFalse(game.IsRunning, "A replay starts with a new countdown");

        yield return EndRoundAfterCountdown(game);
        yield return Tap(hud.Q("backToMenuButton"), 3);
        yield return new WaitForSecondsRealtime(0.5f);
        Assert.AreEqual("Menu", SceneManager.GetActiveScene().name);
    }

    static IEnumerator EndRoundAfterCountdown(PanelMiniGame game)
    {
        while (!game.IsRunning) yield return null;
        typeof(PanelMiniGame).GetMethod("EndRound", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(game, new object[] { "Test" });
        // Let the results card lay out and finish its enter transition.
        yield return new WaitForSecondsRealtime(0.6f);
    }

    [UnityTest]
    public IEnumerator Tournament_PlaysEveryGameInOrder_ThenShowsThePodium()
    {
        yield return LoadMenu();
        var root = MenuRoot();
        Assert.IsTrue(root.Q("tournament").enabledSelf);
        Assert.IsFalse(root.Q("tournament").ClassListContains("is-soon"));

        yield return Tap(root.Q("tournament"), 1);

        int touch = 2;
        foreach (var scene in new[] { "NguoiGo", "KeoCo", "DaCau" })
        {
            yield return new WaitForSecondsRealtime(0.5f);
            Assert.AreEqual(scene, SceneManager.GetActiveScene().name);

            var game = Object.FindAnyObjectByType<PanelMiniGame>();
            var hud = HudOf(game);
            yield return EndRoundAfterCountdown(game);

            Assert.AreEqual(DisplayStyle.Flex, hud.Q("continueButton").resolvedStyle.display);
            Assert.AreEqual(DisplayStyle.None, hud.Q("playAgainButton").resolvedStyle.display);
            yield return Tap(hud.Q("continueButton"), touch++);
        }

        yield return new WaitForSecondsRealtime(0.5f);
        Assert.AreEqual("Podium", SceneManager.GetActiveScene().name);
        Assert.AreEqual(SessionMode.Tournament, GameManager.Instance.Mode);
        CollectionAssert.IsNotEmpty(GameManager.Instance.Scores);
        Assert.Greater(GameManager.Instance.Scores[0], 0, "Each game should have added tournament points");
    }

    static VisualElement HudOf(PanelMiniGame game) =>
        (VisualElement)typeof(PanelMiniGame).GetField("_hud", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(game);
}
