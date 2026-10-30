using System.Collections;
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
    public IEnumerator TappingAComingSoonCard_StaysOnTheMenu()
    {
        yield return LoadMenu();
        var root = MenuRoot();

        Assert.IsFalse(root.Q("cardNguoiGo").enabledSelf);
        Assert.IsFalse(root.Q("tournament").enabledSelf);

        yield return Tap(root.Q("cardNguoiGo"), 1);
        yield return null;

        Assert.AreEqual("Menu", SceneManager.GetActiveScene().name);
    }
}
