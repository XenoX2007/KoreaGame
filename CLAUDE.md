# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

KoreaGame ("Sân Chơi – Madang") is a 2D local-multiplayer mobile party game: 2 or 4 players share one landscape phone lying flat on a table, each with one button at their seat. Three folk mini games: Kéo Co (tug of war), Người Gỗ / Mugunghwa (red light–green light), Đá Cầu / Jegichagi (shuttlecock kicking). Don't use Squid Game imagery (doll, red suits, masks).

- **Unity version:** 6000.5.4f1 (Unity 6.5), installed at `C:\Program Files\Unity\Hub\Editor\6000.5.4f1\`. Several other editor versions are installed side by side; always use this one.
- **Render pipeline:** URP with the 2D Renderer (`Assets/Settings/UniversalRP.asset`, `Renderer2D.asset`). Use URP 2D-compatible materials/shaders (e.g. `Sprite-Lit-Default`), not Built-in shaders.
- **Input:** Project is set to the new Input System only (`activeInputHandler: 1`). The legacy `UnityEngine.Input` API will throw at runtime; use `UnityEngine.InputSystem` and the actions in `Assets/Settings/InputSystem_Actions.inputactions`.
- **UI:** game UI is UI Toolkit (see below). uGUI remains only for the `InputTest` dev scene (`SeatCanvas`, `UiBuilder` image/text helpers).
- **2D packages:** Sprite, Tilemap (+ extras), SpriteShape, 2D Animation, Aseprite and PSD importers.
- **Player settings:** locked to Landscape Left, Android fullscreen, iOS edge gestures deferred (keeps corner buttons from triggering system gestures).

## Architecture (`Assets/Scripts/Core`)

- `SeatLayout`: per player count (2 or 4 only; 3 is intentionally unsupported) defines each seat's touch zone (normalized full-screen rect), UI anchor (inside safe area) and UI rotation. Ids are arranged so `team = id % 2` (even = left half, odd = right half).
- `PlayerInputRouter`: self-creating `DontDestroyOnLoad` singleton. Maps EnhancedTouch fingers to players by the zone they started in (sticky until lift), plus keyboard (Z/P/Q/M = P1..P4 by screen corner) and mouse for Editor testing. Emits `PlayerInput(id, pressed)` only on 0↔1 transitions of held sources. Mini games must never read touches directly.
- `MiniGameBase`: subclasses implement `Setup(playerCount)`, `OnPlayerInput`, `GetRanking()` (rank per player id, 1 = best, ties share rank; use `RanksFromScores`), and call `Begin()`/`Finish()`. Playing a mini-game scene directly uses its `editorPlayerCount`; otherwise `GameManager.Instance.PlayerCount`.
- `GameManager`: persistent, lives on its own object in the `Menu` scene (a duplicate destroys its whole GameObject on returning to the menu, so don't put other components on it). Session mode (single/tournament), scores (3/2/1/0 points), scene flow via `Continue()`/`ReturnToMenu()`. Scenes `NguoiGo` and `DaCau` don't exist yet: menu cards and the tournament button unlock automatically via `Application.CanStreamedLevelBeLoaded` once a scene is in the build.
- `SeatPanels`: UI Toolkit seat placement (`Place` negates seat rotation because USS rotates clockwise), `FitSafeArea`, and `Reveal` (adds a class after first layout so USS enter transitions play).
- `SeatCanvas`/`UiBuilder`: uGUI helpers for `InputTest`. `UiBuilder.EnsureEventSystem` is also used by UI Toolkit scenes: panels receive input through an `EventSystem` with `InputSystemUIInputModule`, created at runtime.

### UI Toolkit (`Assets/UI`, scripts in `Assets/Scripts/UI`)

- Screens use the Unity 6.5 `PanelRenderer` component (not `UIDocument`). There is no `rootVisualElement`: get the root from `RegisterUIReloadCallback((panel, root) => ...)`, which may fire before or after `Start`. Register in `Awake`, unregister in `OnDestroy`. An exception in the callback leaves the panel blank.
- `MadangPanelSettings.asset` uses our own `MadangTheme.tss` → `Madang.uss` (design tokens, fonts, `.btn`), not Unity's default theme, so nothing is styled by default and the theme must itself stretch `.unity-ui-document__root`.
- Fonts: `MaliBoldSDF` (Latin + Vietnamese) with `JuaSDF` fallback (Hangul), both OFL. Art is native SVG (`Assets/UI/Art`, imported as `VectorImage`).
- `filter` and `transition-property: filter` are valid USS in this version. Filters render the subtree to a texture, so apply them only to static moments (`.is-finished .field`), never during a round. A panel's HUD elements use `picking-mode="Ignore"`; gameplay input always comes from `PlayerInputRouter`, never UI Toolkit.
- Screens are UXML/USS in feature folders: `Menu/`, `KeoCo/` (`KeoCoHud`, plus the `KeoCoSeat` template instantiated per player), `Podium/`.

Each mini game is split into a pure rules model (explicit `double time` arguments, no Unity lifecycle, unit-tested) and a `MiniGameBase` view/glue component:

- `Assets/Scripts/KeoCo/KeoCoModel`: beats precomputed from a BPM curve (tempo ramps up in the last `rampSeconds`); taps are judged Perfect/Good/Miss by distance to the nearest beat; one scoring tap per beat; an off-beat tap loses grip for `slipSeconds` (re-armed by further taps) so mashing can't beat rhythm; teammates hitting the same beat within `syncWindow` get the "Đồng lòng" bonus; ribbon position in [-1, 1] uses exactly integrated damped velocity (frame-rate independent); win at ±1 or by side at timeout. All tuning is in the serializable `KeoCoSettings` on `KeoCoGame` (Inspector).
- `KeoCoGame`: countdown → round → results overlay (Chơi lại / Về menu, or Tiếp tục in a tournament); when played without a GameManager, a bottom-center button switches 1v1/2v2 outside rounds. `TrackHalfWidth` must match `.win-line` in `KeoCoHud.uss`.
- `Assets/Scripts/Prototype/InputTestGame` + `Assets/Scenes/InputTest.unity`: multi-touch device test.
- Menu `KoreaGame/` (`Assets/Editor/ProjectSetup.cs`): `Create UI Assets` (font assets, panel settings), `Create UI Scenes` (regenerates Podium, KeoCo, Menu; Menu becomes the first build scene), mobile player settings. Scene creation regenerates the scene from scratch; load assets inside the scene step, because `NewScene` unloads unreferenced ones.

Enter Play Mode Options have **domain reload disabled**, so every static field needs a `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` reset.

## Commands

There is no CLI build/lint script; work is normally done in the Unity Editor. `.csproj`/`.slnx` files are generated by Unity — don't edit them by hand (regenerate via Editor > Preferences > External Tools).

The Unity CLI (`unity`) can drive the open Editor through the `com.unity.pipeline` package: `unity command console status`, `unity command eval --code '...'` (code runs inside a method body: no `using` lines, fully qualify types), `unity command start`/`stop` (Play Mode). When the Editor window is unfocused, run `unity command set_auto_tick --enable true` or frames won't advance; disable it afterwards. Input injected from outside the game (`simulate_key`, queued touch events) doesn't reach Play Mode unless the Game view has focus, so test input through the test fixture below instead.

### Tests

Game code is in the `KoreaGame` assembly (`Assets/Scripts/KoreaGame.asmdef`). PlayMode tests live in `Assets/Tests/PlayMode` (`KoreaGame.Tests.PlayMode`). Input tests derive from the Input System's `InputTestFixture` (enabled through `"testables": ["com.unity.inputsystem"]` in `Packages/manifest.json`). It fakes touchscreens and keyboards with `BeginTouch`/`MoveTouch`/`EndTouch`/`Press` on an isolated, always-focused runtime. `MenuFlowTests` uses it to tap real scenes through the EventSystem into UI Toolkit, which is the way to test buttons, since `simulate_pointer` doesn't reach an unfocused Game view.

With the Editor open (PlayMode tests must run async; results land in `Temp/pipeline_test_status.json`):

```
unity command set_auto_tick --enable true
unity command test run --mode PlayMode --filter KoreaGame.Tests.PlayMode --filter_type assembly --async_tests true
unity command test status
```

Pass a test's full name as `--filter` (without `--filter_type assembly`) to run a single test. With the Editor closed: `"C:\Program Files\Unity\Hub\Editor\6000.5.4f1\Editor\Unity.exe" -batchmode -projectPath . -runTests -testPlatform PlayMode -assemblyNames KoreaGame.Tests.PlayMode -testResults TestResults.xml -logFile -` (use `-testFilter <name>` for one test).

## Conventions

- Every asset and folder under `Assets/` has a `.meta` file holding its GUID. When creating, moving, or deleting files outside the Editor, keep the `.meta` file alongside it (and commit both); never regenerate or hand-edit GUIDs, or scene/prefab references will break.
- `Library/`, `Temp/`, `Logs/`, `UserSettings/` are generated and git-ignored.
- Scene and prefab files are YAML; prefer making changes through the Editor rather than hand-editing them.
