# Cup selection — tournament setup screen (spec)

Status: design approved 2026-11-04 (version 1). Not implemented yet.
Read `CLAUDE.md` first. Design page: `docs/CupSelect.html`.

## Summary

Madang now has 6 mini games. Pressing **Giải đấu** on the menu no longer starts all games straight away. It opens a **cup screen** where players choose:

- **Cúp Nhỏ — 3 trò (Mini Cup):** pick any 3 of the 6 games by tapping them, or roll the **dice** to pick 3 at random. The order you pick (or the dice lands) is the play order.
- **Cúp Lớn — 6 trò (Grand Cup):** all 6 games. The default order is the story order; the dice shuffles the order.

Then **Bắt đầu** starts the tournament. Scoring (3/2/1/0), results screens and the podium work as now; the podium shows which cup was played.

## Where it lives

A full-screen panel inside the **Menu scene** (like `onlinePanel` and `languagePicker`), named `cupPanel`, not a separate Unity scene. It keeps the menu's player count, needs no extra scene loading, and opens instantly. Back closes it and returns to the menu.

## Files

```
Assets/Scripts/Core/CupPicker.cs        pure: game list, dice roll, validation (no Unity lifecycle)
Assets/Scripts/UI/CupPanel.cs           binds cupPanel (like OnlinePanel)
Assets/Scripts/Core/GameManager.cs      tournament takes a scene list + cup kind
Assets/Scripts/UI/MainMenu.cs           tournament button opens cupPanel
Assets/UI/Menu/Menu.uxml / Menu.uss     cupPanel markup and styles
Assets/Scripts/UI/PodiumScreen.cs       shows the cup name
Assets/Tests/PlayMode/CupPickerTests.cs
Assets/Tests/PlayMode/MenuFlowTests.cs  add cup flow cases
```

## Game list (single source of truth)

Move the list out of `GameManager.tournamentScenes` into `CupPicker`:

```csharp
public enum CupKind { Mini, Grand }

public readonly struct GameInfo
{
    public readonly string Scene;      // "NguoiGo"
    public readonly string NameVi;     // "Người Gỗ" (localized via Localization like the menu cards)
    public readonly string NameKo;     // "무궁화 꽃이 피었습니다"
    public readonly string IconClass;  // "card-icon--mugunghwa", reused from Menu.uss
}

public static class CupPicker
{
    // Story order. Grand Cup default order.
    public static readonly GameInfo[] AllGames = { NguoiGo, KeoCo, DaCau, BanBi, TachKeo, LauTokbokki };
    public static int GameCount(CupKind kind) => kind == CupKind.Mini ? 3 : 6;
    public static string[] Roll(System.Random random, IReadOnlyList<string> playable, int count); // distinct, random order
    public static string[] Shuffle(System.Random random, IReadOnlyList<string> scenes);
    public static bool IsValid(CupKind kind, IReadOnlyList<string> scenes, Func<string, bool> playable);
}
```

`playable` is `Application.CanStreamedLevelBeLoaded` in the game and a fake in tests. Menu cards keep their own markup; only the tournament uses `AllGames`.

## GameManager changes

- Remove `tournamentScenes` as a serialized field.
- `StartTournament(int playerCount, CupKind kind, IReadOnlyList<string> scenes)`: validates with `CupPicker.IsValid` (throws on a bad list), stores `Cup` and `TournamentScenes`, then loads `scenes[0]`.
- `public CupKind Cup { get; }`, `public IReadOnlyList<string> TournamentScenes { get; }`, `public int Stage { get; }` (0-based) for "Trò 2/3".
- `Continue()` walks `TournamentScenes`, then the podium (unchanged logic).
- `TournamentReady` becomes `CupReady(CupKind kind)`: Mini needs at least 3 playable games, Grand needs all 6.

## Cup screen layout (`cupPanel`)

Landscape, inside the safe area, using the menu's look (`Madang.uss` tokens, `.btn`, Mali/Jua fonts):

- **Top bar:** back button (left), title "Chọn cúp" (center), player-count chip "2 người" / "4 người" (right, read-only; change it on the menu).
- **Left column — two cup cards** (tap to choose; one is always selected, `is-selected`):
  - Cúp Nhỏ: small cup icon, "3 trò · ~5 phút".
  - Cúp Lớn: big cup icon (`Trophy Cup – VN x KR.png`), "6 trò · ~10 phút".
- **Center — game grid:** 6 game tiles in 3 × 2, each with the game's icon (same `card-icon--*` classes as the menu), Vietnamese name and Korean name. A tile in the cup shows its **order number** badge (1, 2, 3…). Games whose scene isn't in the build are greyed (`is-soon`) and can't be picked or rolled.
- **Right column:**
  - **Dice button** (big, round, die face): "Tung xúc xắc".
  - Summary: the chosen games in order as small icons with arrows (1 → 2 → 3).
  - **Bắt đầu** (primary): enabled only when the selection is valid.

### Mini Cup behaviour

- Tap a tile to add it (it gets the next number). Tap a numbered tile to remove it; the numbers after it shift down.
- A 4th tap while 3 are chosen does nothing except a small shake on the tile and the hint "Đã chọn đủ 3 trò".
- **Dice:** picks 3 distinct playable games at random, replacing the current selection, with the roll animation below.

### Grand Cup behaviour

- All 6 tiles are numbered in story order. Tiles can't be toggled (tapping one does a small shake).
- **Dice:** shuffles the order of all 6, with the roll animation.

### Dice animation (1.4 s, skippable)

1. The die spins (rotate + face change) on the dice button.
2. A highlight ring hops between tiles, fast at first and slowing down (ease-out over ~14 hops).
3. It lands on the 1st pick (badge "1" pops in), hops on, lands on the 2nd, and so on.
4. Bắt đầu enables when done. Tapping anywhere during the animation finishes it immediately with the same result (the result is decided before the animation starts, by `CupPicker.Roll` / `Shuffle`).
5. Uses unscaled time; respects nothing else (the menu has no pause).

### Remembering

Save the last cup kind and the last Mini Cup selection in `PlayerPrefs` (`cup.kind`, `cup.mini` as a comma-separated scene list). On open, restore them if still valid; otherwise Mini Cup with an empty selection. Wrap reads in a validity check (a removed scene name is dropped).

## Menu changes

- The `tournament` button text: title "Giải đấu", subtitle "Chọn 3 hoặc 6 trò, tính điểm, lên bục vinh quang".
- It's enabled when `CupReady(CupKind.Mini)`; clicking opens `cupPanel` (with `SeatPanels.Reveal`), it no longer starts a tournament directly.
- Online play is unchanged (Kéo Co only); the cup screen is local only.

## During the tournament

- Results overlay: in tournament mode, show "Trò 2/3" (or "/6") above the title, from `GameManager.Stage` and `TournamentScenes.Count`.
- Podium: subtitle shows the cup ("Cúp Nhỏ · 3 trò" / "Cúp Lớn · 6 trò") and the small icons of the games played, in order.
- Tiebreak for the podium stays as it is now.

## Localization

| Vietnamese | Korean | English |
|---|---|---|
| Chọn cúp | 컵 고르기 | Choose a cup |
| Cúp Nhỏ | 작은 컵 | Mini Cup |
| Cúp Lớn | 큰 컵 | Grand Cup |
| 3 trò · ~5 phút | 3게임 · 약 5분 | 3 games · ~5 min |
| 6 trò · ~10 phút | 6게임 · 약 10분 | 6 games · ~10 min |
| Tung xúc xắc | 주사위 굴리기 | Roll the dice |
| Chọn 3 trò hoặc tung xúc xắc | 3게임을 고르거나 주사위를 굴려요 | Pick 3 games or roll the dice |
| Đã chọn đủ 3 trò | 3게임을 다 골랐어요 | 3 games chosen |
| Bắt đầu | 시작 | Start |
| Trò {0}/{1} | {0}/{1} 게임 | Game {0}/{1} |
| {0} người | {0}명 | {0} players |
| Chọn 3 hoặc 6 trò, tính điểm, lên bục vinh quang | 3게임이나 6게임을 골라 점수를 모아 시상대에 올라요 | Pick 3 or 6 games, score points, reach the podium |

Run `LocalizationTests` (Korean atlas check) after adding.

## Tests

`CupPickerTests` (pure):
- `AllGames` has 6 distinct scenes in story order.
- `Roll` returns `count` distinct scenes, all from `playable`; same seed → same result; over 600 seeded rolls every game appears and the first slot is roughly uniform (each game 60–140 times).
- `Roll` with fewer than `count` playable games throws.
- `Shuffle` is a permutation of the input.
- `IsValid`: Mini needs exactly 3 distinct playable scenes; Grand needs all 6 exactly once; unknown or duplicate scenes are invalid.

`MenuFlowTests` (with `InputTestFixture`, unload scenes in `[UnityTearDown]`):
- Tapping Giải đấu opens `cupPanel` and doesn't load a game.
- Mini Cup: tapping three tiles numbers them 1–3 and enables Bắt đầu; tapping a 4th doesn't add it; tapping tile 2 removes it and renumbers.
- Dice in Mini Cup produces 3 numbered tiles and enables Bắt đầu (skip the animation with a tap).
- Grand Cup: all 6 numbered; Bắt đầu loads `TournamentScenes[0]`.
- Bắt đầu in Mini Cup loads the first chosen scene; `GameManager.TournamentScenes` matches the chosen order; after 3 `Continue()` calls the podium loads.
- Back closes the panel.

## Done when

- From the menu, Giải đấu opens the cup screen; players can pick 3 games by hand or with the dice, or choose the Grand Cup (optionally shuffled), and start.
- A Mini Cup plays exactly the 3 chosen games in order, then the podium; a Grand Cup plays all 6.
- Results show "Trò x/y"; the podium names the cup.
- All PlayMode tests pass.
