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
        ForgetCupChoice();
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
        RestoreCupChoice();
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
    public IEnumerator TappingTheTachKeoCard_StartsTachKeo()
    {
        yield return LoadMenu();
        var root = MenuRoot();

        yield return Tap(root.Q("fourPlayers"), 1);
        yield return Tap(root.Q("cardTachKeo"), 2);
        yield return WaitForScene("TachKeo");

        Assert.AreEqual("TachKeo", SceneManager.GetActiveScene().name);
        var game = Object.FindAnyObjectByType<TachKeoGame>();
        Assert.AreEqual(4, game.PlayerCount);
        while (!game.IsRunning) yield return null;
    }

    [UnityTest]
    public IEnumerator TappingTheLauTokbokkiCard_StartsLauTokbokki_WithAllItsArt()
    {
        yield return LoadMenu();
        var root = MenuRoot();

        yield return Tap(root.Q("twoPlayers"), 1);
        yield return Tap(root.Q("cardLauTokbokki"), 2);
        yield return WaitForScene("LauTokbokki");

        Assert.AreEqual("LauTokbokki", SceneManager.GetActiveScene().name);
        var game = Object.FindAnyObjectByType<LauTokbokkiGame>();
        Assert.AreEqual(2, game.PlayerCount);

        var art = game.Art;
        Assert.AreEqual(6, art.ingredients.Length);
        foreach (var sprite in art.All)
        {
            Assert.IsNotNull(sprite, "Every piece of art is assigned");
            Assert.IsNotNull(sprite.texture, $"{sprite.name} has its texture");
        }
        Assert.AreEqual("VN_5", art.vnPlayer.name);
        Assert.AreEqual("KR_4", art.krPlayer.name);

        // The art is on screen: the table, a player and their pot.
        var hud = HudOf(game);
        Assert.AreEqual(art.table, hud.Q(className: "table").resolvedStyle.backgroundImage.sprite);
        Assert.AreEqual(art.flags, hud.Q(className: "flags").resolvedStyle.backgroundImage.sprite);
        Assert.IsTrue(hud.Query(className: "player-sprite").ToList().Any(e => e.resolvedStyle.backgroundImage.sprite == art.vnPlayer));
        Assert.IsTrue(hud.Query(className: "player-sprite").ToList().Any(e => e.resolvedStyle.backgroundImage.sprite == art.krPlayer));
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

    // The cup screen remembers the last cup and picks: each test starts from none, and the player's own come back after.
    string _savedCupKind;
    string _savedCupMini;
    int _touch = 100;

    void ForgetCupChoice()
    {
        _savedCupKind = PlayerPrefs.HasKey("cup.kind") ? PlayerPrefs.GetInt("cup.kind").ToString() : null;
        _savedCupMini = PlayerPrefs.HasKey("cup.mini") ? PlayerPrefs.GetString("cup.mini") : null;
        PlayerPrefs.DeleteKey("cup.kind");
        PlayerPrefs.DeleteKey("cup.mini");
    }

    void RestoreCupChoice()
    {
        if (_savedCupKind != null) PlayerPrefs.SetInt("cup.kind", int.Parse(_savedCupKind));
        else PlayerPrefs.DeleteKey("cup.kind");
        if (_savedCupMini != null) PlayerPrefs.SetString("cup.mini", _savedCupMini);
        else PlayerPrefs.DeleteKey("cup.mini");
        PlayerPrefs.Save();
    }

    IEnumerator OpenCupScreen()
    {
        yield return LoadMenu();
        yield return Tap(MenuRoot().Q("tournament"), _touch++);
        yield return new WaitForSecondsRealtime(0.4f);
    }

    // A tile's order number, or null when it isn't in the cup.
    static string Badge(VisualElement root, string scene)
    {
        var badge = root.Q($"cupTile{scene}").Q<Label>(className: "cup-badge");
        return badge.resolvedStyle.display == DisplayStyle.Flex ? badge.text : null;
    }

    static string[] Numbered(VisualElement root) =>
        CupPicker.StoryOrder.Where(scene => Badge(root, scene) != null).OrderBy(scene => int.Parse(Badge(root, scene))).ToArray();

    [UnityTest]
    public IEnumerator Tournament_OpensTheCupScreen_WithoutLoadingAGame()
    {
        yield return LoadMenu();
        var root = MenuRoot();
        Assert.IsTrue(root.Q("tournament").enabledSelf);
        Assert.IsFalse(root.Q("tournament").ClassListContains("is-soon"));
        Assert.AreEqual(DisplayStyle.None, root.Q("cupPanel").resolvedStyle.display);

        yield return Tap(root.Q("tournament"), _touch++);
        yield return new WaitForSecondsRealtime(0.4f);

        Assert.AreEqual(DisplayStyle.Flex, root.Q("cupPanel").resolvedStyle.display);
        Assert.AreEqual("Menu", SceneManager.GetActiveScene().name);
        Assert.IsTrue(root.Q("cupMini").ClassListContains("is-selected"), "Mini Cup is the default");
        CollectionAssert.IsEmpty(Numbered(root));
        Assert.IsFalse(root.Q("cupStart").enabledSelf);
    }

    [UnityTest]
    public IEnumerator CupScreen_BackClosesIt()
    {
        yield return OpenCupScreen();
        var root = MenuRoot();

        yield return Tap(root.Q("cupBack"), _touch++);
        Assert.AreEqual(DisplayStyle.None, root.Q("cupPanel").resolvedStyle.display);
        Assert.AreEqual("Menu", SceneManager.GetActiveScene().name);
    }

    [UnityTest]
    public IEnumerator MiniCup_TilesNumberInTapOrder_AFourthIsRefused_AndRemovingRenumbers()
    {
        yield return OpenCupScreen();
        var root = MenuRoot();

        foreach (var scene in new[] { "KeoCo", "DaCau", "BanBi" })
            yield return Tap(root.Q($"cupTile{scene}"), _touch++);
        CollectionAssert.AreEqual(new[] { "KeoCo", "DaCau", "BanBi" }, Numbered(root));
        Assert.AreEqual("1", Badge(root, "KeoCo"));
        Assert.AreEqual("3", Badge(root, "BanBi"));
        Assert.IsTrue(root.Q("cupStart").enabledSelf);

        yield return Tap(root.Q("cupTileTachKeo"), _touch++);
        Assert.IsNull(Badge(root, "TachKeo"), "A 4th game isn't added");
        CollectionAssert.AreEqual(new[] { "KeoCo", "DaCau", "BanBi" }, Numbered(root));

        yield return Tap(root.Q("cupTileDaCau"), _touch++);
        Assert.IsNull(Badge(root, "DaCau"));
        Assert.AreEqual("2", Badge(root, "BanBi"), "Later picks move down");
        Assert.IsFalse(root.Q("cupStart").enabledSelf);
    }

    [UnityTest]
    public IEnumerator MiniCup_TheDicePicksThree_AndATapSkipsTheRoll()
    {
        yield return OpenCupScreen();
        var root = MenuRoot();

        yield return Tap(root.Q("cupDice"), _touch++);
        Assert.IsFalse(root.Q("cupStart").enabledSelf, "Start waits for the roll");

        yield return Tap(root.Q("cupHint"), _touch++);
        var picked = Numbered(root);
        Assert.AreEqual(3, picked.Length);
        CollectionAssert.AreEqual(new[] { "1", "2", "3" }, picked.Select(scene => Badge(root, scene)));
        Assert.IsTrue(root.Q("cupStart").enabledSelf);
    }

    [UnityTest]
    public IEnumerator MiniCup_PlaysTheChosenGamesInOrder_ThenThePodium()
    {
        var before = Localization.Current;
        Localization.Current = Language.Vietnamese;
        yield return OpenCupScreen();
        var root = MenuRoot();

        var chosen = new[] { "DaCau", "KeoCo", "NguoiGo" };
        foreach (var scene in chosen)
            yield return Tap(root.Q($"cupTile{scene}"), _touch++);
        yield return Tap(root.Q("cupStart"), _touch++);

        yield return WaitForScene("DaCau");
        Assert.AreEqual("DaCau", SceneManager.GetActiveScene().name);
        var game = GameManager.Instance;
        Assert.AreEqual(SessionMode.Tournament, game.Mode);
        Assert.AreEqual(CupKind.Mini, game.Cup);
        CollectionAssert.AreEqual(chosen, game.TournamentScenes);

        foreach (var next in new[] { "KeoCo", "NguoiGo", "Podium" })
        {
            game.Continue();
            yield return WaitForScene(next);
            Assert.AreEqual(next, SceneManager.GetActiveScene().name);
        }

        yield return new WaitForSecondsRealtime(0.3f);
        var podium = (VisualElement)typeof(PodiumScreen).GetProperty("Root", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(Object.FindAnyObjectByType<PodiumScreen>());
        Assert.AreEqual(DisplayStyle.Flex, podium.Q("podiumCup").resolvedStyle.display);
        Assert.AreEqual("Cúp Nhỏ · 3 trò", podium.Q<Label>("podiumCupName").text);
        Assert.AreEqual(3, podium.Q("podiumCupGames").childCount);
        Localization.Current = before;
    }

    [UnityTest]
    public IEnumerator GrandCup_NumbersAllSix_ThenPlaysEveryGameInOrder_AndShowsThePodium()
    {
        var before = Localization.Current;
        Localization.Current = Language.Vietnamese;
        yield return OpenCupScreen();
        var root = MenuRoot();

        yield return Tap(root.Q("cupGrand"), _touch++);
        Assert.IsTrue(root.Q("cupGrand").ClassListContains("is-selected"));
        CollectionAssert.AreEqual(CupPicker.StoryOrder, Numbered(root));
        Assert.IsTrue(root.Q("cupStart").enabledSelf);

        yield return Tap(root.Q("cupTileKeoCo"), _touch++);
        CollectionAssert.AreEqual(CupPicker.StoryOrder, Numbered(root), "Grand Cup tiles can't be removed");

        yield return Tap(root.Q("cupStart"), _touch++);

        int stage = 1;
        foreach (var scene in CupPicker.StoryOrder)
        {
            yield return WaitForScene(scene);
            Assert.AreEqual(scene, SceneManager.GetActiveScene().name);

            var game = Object.FindAnyObjectByType<PanelMiniGame>();
            var hud = HudOf(game);
            yield return EndRoundAfterCountdown(game);

            Assert.AreEqual(DisplayStyle.Flex, hud.Q("resultStage").resolvedStyle.display);
            Assert.AreEqual($"Trò {stage}/6", hud.Q<Label>("resultStage").text);
            Assert.AreEqual(DisplayStyle.Flex, hud.Q("continueButton").resolvedStyle.display);
            Assert.AreEqual(DisplayStyle.None, hud.Q("playAgainButton").resolvedStyle.display);
            var continueButton = hud.Q("continueButton");
            var underFinger = continueButton.panel.Pick(continueButton.worldBound.center);
            Assert.AreEqual(continueButton, underFinger, $"{underFinger?.name} {underFinger?.GetType().Name} {string.Join(".", underFinger?.GetClasses() ?? new string[0])} is over Continue");
            yield return Tap(hud.Q("continueButton"), _touch++);
            stage++;
        }

        yield return WaitForScene("Podium");
        Assert.AreEqual("Podium", SceneManager.GetActiveScene().name);
        Assert.AreEqual(SessionMode.Tournament, GameManager.Instance.Mode);
        Assert.AreEqual(CupKind.Grand, GameManager.Instance.Cup);
        CollectionAssert.IsNotEmpty(GameManager.Instance.Scores);
        Assert.Greater(GameManager.Instance.Scores[0], 0, "Each game should have added tournament points");
        Localization.Current = before;
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
