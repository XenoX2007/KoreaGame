using System.Collections;
using System.Linq;
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

    static Vector2 ScreenPoint(VisualElement element)
    {
        var bounds = element.worldBound;
        float scale = Screen.width / element.panel.visualTree.layout.width;
        return new Vector2(bounds.center.x * scale, Screen.height - bounds.center.y * scale);
    }

    IEnumerator Tap(VisualElement element, int touchId)
    {
        var position = ScreenPoint(element);

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

    static BanBiModel ModelOf(BanBiGame game) =>
        (BanBiModel)typeof(BanBiGame).GetField("_model", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(game);

    // Plays BanBi directly and lets the dice stop on their own.
    static IEnumerator LoadBanBiToFirstTurn()
    {
        yield return SceneManager.LoadSceneAsync("BanBi");
        float giveUp = Time.realtimeSinceStartup + 15f;
        var game = Object.FindAnyObjectByType<BanBiGame>();
        while ((ModelOf(game) == null || ModelOf(game).Phase != BanBiPhase.Aiming) && Time.realtimeSinceStartup < giveUp) yield return null;
        Assert.AreEqual(BanBiPhase.Aiming, ModelOf(game).Phase);
        // Let the shoot button lay out and register its zone.
        yield return null;
        yield return null;
    }

    IEnumerator Hold(Vector2 position, int touchId, float seconds)
    {
        BeginTouch(touchId, position);
        yield return new WaitForSecondsRealtime(seconds);
        EndTouch(touchId, position);
        yield return null;
    }

    [UnityTest]
    public IEnumerator BanBi_TheShootButtonShootsForTheActivePlayer_AndSeatsDoNot()
    {
        yield return LoadBanBiToFirstTurn();
        var game = Object.FindAnyObjectByType<BanBiGame>();
        var model = ModelOf(game);
        var hud = HudOf(game);
        int active = model.ActivePlayer;
        int marbles = model.Marbles.Count;

        var seat = hud.Q("seatLayer").Children().First(view => view.ClassListContains($"player--p{active + 1}"));
        yield return Hold(ScreenPoint(seat), 1, 0.3f);
        Assert.AreEqual(BanBiPhase.Aiming, model.Phase, "A seat press doesn't shoot during a turn");
        Assert.AreEqual(marbles, model.Marbles.Count);

        BeginTouch(2, ScreenPoint(hud.Q("shootButton")));
        yield return null;
        yield return null;
        Assert.AreEqual(BanBiPhase.Charging, model.Phase);
        EndTouch(2, ScreenPoint(hud.Q("shootButton")));
        yield return null;
        yield return null;

        Assert.AreEqual(marbles + 1, model.Marbles.Count);
        Assert.AreEqual(active, model.Marbles.Last().ShotBy);
    }

    [UnityTest]
    public IEnumerator BanBi_TheShootButtonZoneIsGoneAfterLeavingTheScene()
    {
        yield return LoadBanBiToFirstTurn();
        Assert.IsTrue(PlayerInputRouter.Instance.HasExtraZone(BanBiGame.ShootId));

        SceneManager.SetActiveScene(SceneManager.CreateScene("BanBiLeft"));
        yield return SceneManager.UnloadSceneAsync("BanBi");
        yield return null;

        Assert.IsFalse(PlayerInputRouter.Instance.HasExtraZone(BanBiGame.ShootId));
    }

    [UnityTest]
    public IEnumerator TappingTheBanBiCard_StartsBanBi()
    {
        yield return LoadMenu();
        var root = MenuRoot();

        yield return Tap(root.Q("twoPlayers"), 1);
        yield return Tap(root.Q("cardBanBi"), 2);
        yield return WaitForScene("BanBi");

        Assert.AreEqual("BanBi", SceneManager.GetActiveScene().name);
        var game = Object.FindAnyObjectByType<BanBiGame>();
        Assert.AreEqual(2, game.PlayerCount);
        while (!game.IsRunning) yield return null;
    }

    [UnityTest]
    public IEnumerator ChoosingALanguage_ChangesTheWholeMenu()
    {
        var before = Localization.Current;
        Localization.Current = Language.Vietnamese;
        yield return LoadMenu();
        var root = MenuRoot();
        var tournamentTitle = root.Q<Label>(className: "tournament__title");
        Assert.AreEqual("Giải đấu", tournamentTitle.text);

        yield return Tap(root.Q("languageButton"), 1);
        yield return new WaitForSecondsRealtime(0.4f);
        Assert.AreEqual(DisplayStyle.Flex, root.Q("languagePicker").resolvedStyle.display);

        yield return Tap(root.Q("languageEnglish"), 2);
        yield return new WaitForSecondsRealtime(0.2f);

        Assert.AreEqual(Language.English, Localization.Current);
        Assert.AreEqual(DisplayStyle.None, root.Q("languagePicker").resolvedStyle.display);
        Assert.AreEqual("Tournament", tournamentTitle.text);
        Assert.AreEqual("English", root.Q<Label>("languageName").text);
        Localization.Current = before;
    }

    // Stays offline: a code too short to be real is caught before anything is sent.
    [UnityTest]
    public IEnumerator OnlineCard_OpensToHostOrJoin_AndRejectsABadCode()
    {
        var before = Localization.Current;
        Localization.Current = Language.Vietnamese;
        yield return LoadMenu();
        var root = MenuRoot();

        yield return Tap(root.Q("onlineButton"), 1);
        yield return new WaitForSecondsRealtime(0.4f);
        Assert.AreEqual(DisplayStyle.Flex, root.Q("onlinePanel").resolvedStyle.display);
        Assert.AreEqual(DisplayStyle.Flex, root.Q("onlineChoose").resolvedStyle.display);

        yield return Tap(root.Q("joinButton"), 2);
        Assert.AreEqual(DisplayStyle.Flex, root.Q("onlineJoin").resolvedStyle.display);

        root.Q<TextField>("codeField").value = "ab";
        yield return Tap(root.Q("joinConfirmButton"), 3);
        yield return new WaitForSecondsRealtime(0.2f);

        Assert.AreEqual(OnlineState.Offline, OnlineSession.Instance.State);
        Assert.AreEqual("Nhập mã phòng", root.Q<Label>("onlineStatus").text);

        yield return Tap(root.Q("onlineCloseButton"), 4);
        Assert.AreEqual(DisplayStyle.None, root.Q("onlinePanel").resolvedStyle.display);
        Localization.Current = before;
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

    [UnityTest]
    public IEnumerator Pause_StopsTheRound_ThenContinuesOrGoesHome()
    {
        yield return LoadMenu();
        yield return Tap(MenuRoot().Q("cardDaCau"), 1);
        yield return new WaitForSecondsRealtime(0.5f);

        var game = Object.FindAnyObjectByType<DaCauGame>();
        var hud = HudOf(game);
        var router = PlayerInputRouter.Instance;
        while (!game.IsRunning) yield return null;

        // The press on the pause button must not reach a player.
        var button = ScreenPoint(hud.Q("pauseButton"));
        BeginTouch(2, button);
        yield return null;
        yield return null;
        for (int id = 0; id < game.PlayerCount; id++) Assert.IsFalse(router.IsPressed(id));
        EndTouch(2, button);
        yield return null;
        yield return null;

        Assert.IsTrue(game.IsPaused);
        var pause = hud.Q("pause");
        Assert.AreEqual(DisplayStyle.Flex, pause.resolvedStyle.display);

        double stoppedAt = GameTime(game);
        yield return new WaitForSecondsRealtime(0.4f);
        Assert.AreEqual(stoppedAt, GameTime(game));

        yield return Tap(hud.Q("resumeButton"), 3);
        Assert.IsFalse(game.IsPaused);
        Assert.AreEqual(DisplayStyle.None, pause.resolvedStyle.display);
        yield return new WaitForSecondsRealtime(0.1f);
        Assert.Greater(GameTime(game), stoppedAt);
        Assert.Less(GameTime(game), stoppedAt + 0.39, "The time spent paused must not count");

        yield return Tap(hud.Q("pauseButton"), 4);
        yield return new WaitForSecondsRealtime(0.4f);
        yield return Tap(hud.Q("homeButton"), 5);
        yield return new WaitForSecondsRealtime(0.5f);
        Assert.AreEqual("Menu", SceneManager.GetActiveScene().name);
        CollectionAssert.IsEmpty(PlayerInputRouter.Instance.IgnoredZones);
    }

    static double GameTime(PanelMiniGame game) =>
        (double)typeof(PanelMiniGame).GetProperty("Now", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(game);

    static IEnumerator EndRoundAfterCountdown(PanelMiniGame game)
    {
        while (!game.IsRunning) yield return null;
        typeof(PanelMiniGame).GetMethod("EndRound", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(game, new object[] { (System.Func<string>)(() => "Test") });
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
        foreach (var scene in new[] { "NguoiGo", "KeoCo", "DaCau", "BanBi" })
        {
            yield return WaitForScene(scene);
            Assert.AreEqual(scene, SceneManager.GetActiveScene().name);

            var game = Object.FindAnyObjectByType<PanelMiniGame>();
            var hud = HudOf(game);
            yield return EndRoundAfterCountdown(game);

            Assert.AreEqual(DisplayStyle.Flex, hud.Q("continueButton").resolvedStyle.display);
            Assert.AreEqual(DisplayStyle.None, hud.Q("playAgainButton").resolvedStyle.display);
            var continueButton = hud.Q("continueButton");
            var underFinger = continueButton.panel.Pick(continueButton.worldBound.center);
            Assert.AreEqual(continueButton, underFinger, $"{underFinger?.name} {underFinger?.GetType().Name} {string.Join(".", underFinger?.GetClasses() ?? new string[0])} is over Continue");
            yield return Tap(hud.Q("continueButton"), touch++);
        }

        yield return WaitForScene("Podium");
        Assert.AreEqual("Podium", SceneManager.GetActiveScene().name);
        Assert.AreEqual(SessionMode.Tournament, GameManager.Instance.Mode);
        CollectionAssert.IsNotEmpty(GameManager.Instance.Scores);
        Assert.Greater(GameManager.Instance.Scores[0], 0, "Each game should have added tournament points");
    }

    // Scene loads take longer some runs, so wait for the scene itself rather than a fixed time.
    static IEnumerator WaitForScene(string scene)
    {
        float giveUp = Time.realtimeSinceStartup + 5f;
        while (SceneManager.GetActiveScene().name != scene && Time.realtimeSinceStartup < giveUp) yield return null;
        yield return null;
    }

    static VisualElement HudOf(PanelMiniGame game) =>
        (VisualElement)typeof(PanelMiniGame).GetField("_hud", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(game);
}
