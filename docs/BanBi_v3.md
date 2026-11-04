# Bắn Bi v3 — big circle zone (change spec)

Status: design approved 2026-11-04, **implemented 2026-11-04**. **Builds on `docs/BanBi_v2.md`, which is implemented.** Only the play field changes; every v2 rule stays.

> Tuning after playtest: `startArea` 0.6 → **0.65**, and **4** marbles per team in the BanBi scene (`startMarblesPerTeam`; the code default is 6). 3 per team was too easy and quick to empty.
>
> Implementation notes: the start-area ring is drawn as 32 chalk dots during the dice roll. The pause button, which sat over the top of the circle, moves to the top-left (5% in) in this scene only. The thumb icon is `Assets/UI/Art/icon-thumb.svg`. No extra code was needed for "the next turn waits for a release": the router only emits on 0↔1 transitions, so a button still held after an auto-release can only release, which the model ignores in `Aiming`.
Read `CLAUDE.md` first. Files touched: `Assets/Scripts/BanBi/BanBiModel.cs`, `BanBiGame.cs`, `Assets/UI/BanBi/BanBiHud.uxml/.uss`, `Assets/Tests/PlayMode/BanBiModelTests.cs`.

## What changes

1. **The zone is a circle, not a square.**
2. **The circle is as big as the screen allows**, and the seat buttons are pulled in to sit right next to its edge.
3. **Everything on the field scales up:** marbles (bigger on screen *and* a bit bigger relative to the zone), the hand, the held marble, the aim dots.
4. **One shared shoot button.** It's turn-based, so during turns everyone uses the same big button just right of the hand, at the bottom of the screen, instead of their own seat button. Players look at the hand and the button in one place while aiming. Seat buttons are only used for the dice roll.

Unchanged from v2: teams (red VN / blue KR), 3 marbles per team at the start, dice order, one hand per turn at the bottom (VN arm slice on red's turns, KR arm slice on blue's), slide → press → hold → release (now on the shared button), shot marbles stay, bonus shot for knocking a rival out (cap 2), 8 shots per team, empty-zone loss, count win, closest-to-center tiebreak, team ranking.

## Model (`BanBiModel.cs`)

Zone units: the circle has radius 1, center (0, 0), +y toward the top of the screen.

- **Out rule:** a marble is out when `Position.magnitude > 1` (was `|x| > 1 || |y| > 1`). Keep it in one helper (`IsOut(Vector2)`) used by `ResolveShot` and the tests.
- **Start placement:** start marbles go inside a circle of radius `startArea` (was a square), same spacing rule (2.5 × radius), same seeded RNG, teams still alternate while placing.
- **Settings changes:**

| Field | v2 | v3 | Why |
|---|---|---|---|
| `marbleRadius` | 0.07 | **0.085** | bigger marbles relative to the zone |
| `startArea` | 0.7 (square half-size) | **0.6** (circle radius) | keeps start marbles clear of the edge in a circle |
| `slideRange` | 0.85 | **0.75** | the circle is narrower near the bottom, so keep shots aimed into it |
| `launchY` | -1.2 | **-1.1** | the held marble sits just under the circle's bottom |
| `minShotSpeed` / `maxShotSpeed` | 1.0 / 2.8 | unchanged | min still just enters (0.45), max still overshoots the far side |

Nothing else in the model changes. Tiebreak "closest to center" already uses `Position.magnitude`.

## View (`BanBiGame.cs`, `BanBiHud.uxml/.uss`)

Reference panel is 1920 × 1080 (`MadangPanelSettings`, match 0.5). Phones are wider than 16:9, so **size the field from the stage height in code** instead of fixed USS numbers. This also removes the "keep USS in sync with constants" pairs for the zone and marbles.

### Field size (in code, every frame or on geometry change)

- `ZoneRadius = stage.y * 0.435f` (470 px at 1080 tall, diameter 940).
- `ZoneCenter = (stage.x / 2, stage.y * 0.45f)` (a little above middle, leaving room for the held marble and fingertips under the circle; the arm runs off the bottom of the screen, as if it comes from the player).
- `ToStage(zone) = ZoneCenter + (zone.x, -zone.y) * ZoneRadius`.
- `MarbleSize = 2 * marbleRadius * ZoneRadius` (≈ 80 px at 1080 tall; was 42).
- Set `.zone`'s `left/top/width/height` and `border-radius = ZoneRadius` from code. USS keeps only its look.
- Set each marble, shadow and the held marble's `width/height/border-radius` from `MarbleSize`. The shine is 26% of the size, offset 17% / 14%. Shadow offset becomes `MarbleSize * (0.1, 0.12)`.

### Zone look

- Same sand fill and dark chalk border (border 12 px). Round (`border-radius` = radius).
- Add a faint chalk ring at `startArea` (dashed look by ~32 small dots or a 2 px border at 15% opacity) only during the dice roll, so players see where the marbles start. Optional.

### Seats pulled in next to the circle (this scene only)

The seat buttons stay where `SeatLayout` puts them in other games; `BanBi` moves the visuals only. Touch zones are still full-screen halves/quadrants, so input is unaffected.

- After `PanelMiniGame` places the seats, move each seat disc along the line from `ZoneCenter` to its normal position, so the disc's edge sits `SeatGap = 36` px outside the circle's edge:
  `target = ZoneCenter + direction * (ZoneRadius + SeatGap + 85)` (85 = half of `.seat`'s 170).
- Apply it as an extra `translate` on the seat root (its rotation stays). Clamp to the safe area.
- Expected at 1920 × 1080: 2-player seats move in from x ≈ 154 to x ≈ 369 (left) and ≈ 1551 (right) at mid height; 4-player corner seats land roughly at (448, 789), (1472, 789), (448, 183), (1472, 183).
- Dice and order badges ride on the seat as before, so they move with it. Check that dice don't overlap the circle; if they do, flip `.die` to the outer side of the disc for this scene.

### Side columns (counters, timer, hint)

The circle now covers the top center and bottom center, so the HUD moves to the two side strips (x < circle's left edge, x > circle's right edge):

- **Team counters** (`VN ● 3`, `KR ● 3`): centered in each side strip, at 50% height with 4 players (the corners are taken by seats), at 18% height with 2 players (above the seats).
- **Timer pill:** directly under the active team's counter (moves sides with the turn). Same text as v2.
- **Hint** ("Giữ để kéo, thả để búng"): under the timer on the active side, 26 px; show it only for each player's first turn.
- Center messages (`Lượt của P1`, `Thêm lượt!`, `Hụt!`) stay centered over the circle.
- Make sure the counters don't overlap the 2-player seats at mid height or the 4-player seats in the corners.

### Hand and aim (scaled up)

- Hand sprite 70 × 186 → **98 × 260** (×1.4). The shadow scales with it. The name chip under the hand is removed (the name now sits under the shoot button).
- Held marble = `MarbleSize`, at the fingertips: `ToStage((handX, launchY))`.
- `PullBack` 60 → **84** px, `FlickUp` 20 → **28** px, `HandTipGap` 10 → **14** px.
- Aim dots 10 → **14** px, still ~14 of them, straight up from the held marble. Dots whose point is outside the circle (`IsOut`) are faded (`.is-over`), so the line shows where a too-strong shot leaves the far side.
- The slide line is straight (not along the arc). At `slideRange` 0.75 the held marble is well under the circle's lower edge; the shot enters through the curved edge.

### Shared shoot button (new)

**Why:** in a turn-based game only one player acts at a time, so one button next to the hand is easier to aim with and to watch than a seat button in a far corner.

**Look and place**
- A round button, 150 px (scaled with the stage height like the field: `stage.y * 0.139`), fixed just right of the hand's slide range at the bottom: center at `ToStage((slideRange, launchY)) + (ShootButtonGap = 130 px, 0)`, clamped inside the safe area. At 1920 × 1080 that's about (1442, 1003). It must not touch the circle or the bottom-right seat (P4 in 4-player games, at about (1472, 789) after the v3 pull-in); check at 16:9 and on a tall phone.
- Fill in the active team's color (red / blue) with the `Madang.uss` `.btn` look (thick bottom edge), label `Bắn!` and a small thumb icon. It shows the active player's name under it (`P1`), which replaces the name chip under the hand.
- Pressed state: sinks (bottom edge shrinks), as `.btn` does.
- Hidden during the countdown and the dice roll. Visible but greyed while marbles roll and during the turn change.

**Input (keep the rule that mini games never read touches directly)**
- Extend `PlayerInputRouter` with **extra zones**: `RegisterExtraZone(int id, Rect normalizedRect)` / `ClearExtraZones()`. A touch that *starts* inside an extra zone belongs to that zone's id (sticky until lift, like seats) and takes priority over the seat zones underneath. The router emits `PlayerInput(id, pressed)` with the same 0↔1 rules. Extra ids start at `SeatLayout.MaxPlayers` (so the shoot button is id `4`).
- Editor: `Space` (and the mouse inside the zone) drive extra id 4.
- `BanBiGame` registers the zone from the button's resolved layout (converted to normalized screen coordinates, y up) after the first layout and whenever it changes, and clears it in `OnDisable`/`OnDestroy` and when the round ends, so other scenes never see it.
- The pause button stays an `IgnoredZones` entry and wins over everything.

**Who can press it**
- During `Aiming`/`Charging`, `BanBiGame.OnPlayerInput(4, pressed)` calls `_model.Press(_model.ActivePlayer, pressed, Now)`. Whoever's turn it is presses the shared button; the model is unchanged and still only listens to the active player.
- During turns, presses from **seat** ids are ignored, so a stray touch on a seat can't fire a shot.
- During `DiceRoll`, only seat ids count (each player stops their own die); the shoot button is hidden and its zone is not registered.
- If the active player is holding when the turn ends (auto-release at `maxHoldSeconds`), the next turn waits for a release before a new press counts (no accidental double shot).

**Seats during turns:** the active player's seat still glows and the others dim (`is-waiting`), so everyone sees whose turn it is, but the seats are not pressable for shooting.

**Localization**

| Vietnamese | Korean | English |
|---|---|---|
| Bắn! | 튕겨요! | Flick! |
| Giữ nút để kéo, thả để bắn | 버튼을 꾹 눌러 당기고, 떼서 튕겨요 | Hold the button to pull, release to flick |

The new hint replaces "Giữ để kéo, thả để búng" in this game. Run `LocalizationTests` (Korean atlas check) after adding.

### Out marbles

Fade + shrink as in v2. A marble rolling out past the circle can go off screen on the sides; that's fine.

## Tests

### `PlayerInputRouterTests.cs` (router change)
- A touch that starts in a registered extra zone emits `PlayerInput(4, true/false)` and no seat input, even though a seat zone covers the same spot.
- The touch stays id 4 when it moves out of the zone, until lift.
- After `ClearExtraZones()` the same touch goes to the seat again.
- An `IgnoredZones` entry over an extra zone still swallows the touch.
- `Space` drives id 4 in the keyboard path.

### `BanBiModelTests.cs`

Update:
- Start placement: all start marbles inside radius `startArea`, spacing unchanged, same seed → same layout.
- Out rule: a marble at (0.8, 0.8) is out (outside the circle, inside the old square); (0.7, 0) is in; (0, -1.05) is out.
- Min-power shot from `HandX = 0` stops inside the circle near the bottom; max-power shot leaves the top and is `Missed`.
- A shot from `HandX = slideRange` with medium power enters the circle through the curved edge and stays.

All other v2 tests stay.

### Play-mode flow (`MenuFlowTests` style, with `InputTestFixture`)
- In `BanBi`, after the dice: a touch on the shoot button with P2 active starts and ends a shot for P2; a touch on P2's seat does nothing during the turn.
- The shoot button zone is gone after leaving the scene (unload in `[UnityTearDown]`).

## Done when

- Playing `BanBi` directly with 2 and 4 players shows a large round zone filling most of the height, seat buttons sitting just outside its edge, big marbles, the bigger hand at the bottom with its arm running off screen, the counters/timer in the side strips, and the shared shoot button right of the hand that fires for whoever's turn it is.
- Checked at 16:9 and at a tall phone (e.g. 2400 × 1080 Game view): nothing overlaps, nothing is cut by the safe area.
- All PlayMode tests in `KoreaGame.Tests.PlayMode` pass.
