# Bắn Bi v2 — top-down team marbles (change spec)

Status: design approved 2026-11-03, implemented 2026-11-03. **Supersedes `docs/BanBi.md` (v1)**, which is kept for history.

> Implementation notes: there are no separate `VN_4.png` / `KR_3.png` files; those are the arm slices already inside `Assets/UI Assets/VN.png` and `KR.png`, so `BanBiHud.uss` references the slices directly (rotated 180° to point up) instead of copying files. The dice are still placeholder numbered squares and the sfx in v1's Audio list are not added yet. The `Shot` event also carries the new marble's index in `Other`.
Read `CLAUDE.md` first. v1 is already implemented in `Assets/Scripts/BanBi/BanBiModel.cs`, `BanBiGame.cs`, `Assets/UI/BanBi/BanBiHud.uxml/.uss` and its tests; this spec lists what changes. Keep the parts marked **unchanged**.

## Summary of v2

- **View:** top-down. The screen shows only a square play zone in the middle. No characters, no ground band, no side-on depth.
- **Teams:** Red = left team = Vietnam (ids 0, 2). Blue = right team = Korea (ids 1, 3). `team = id % 2` as before.
- **Marbles belong to teams, not players.** Each team starts with 3 marbles inside the square. No neutral marbles, no per-player shooter, no elimination, no points.
- **One hand, one place.** The hand always sits below the bottom edge of the square. On red's turn it is the Vietnamese hand holding a red marble; on blue's turn the Korean hand holding a blue marble.
- **Aim by sliding:** the hand slides left–right along the bottom edge. Press = stop, hold = pull back (power), release = shoot straight up.
- **The shot marble stays** where it stops (if inside the square) and becomes a team marble, so the zone fills up.
- **Win:** a team with no marbles in the square loses. Otherwise, after 8 regular shots per team, more marbles in the square wins.

Dice roll for order, turn timer, power triangle wave, bonus-shot cap, fixed-step physics, `RanksFromScores` by team: **unchanged**.

## Coordinates

Zone units: the square spans x ∈ [-1, 1], y ∈ [-1, 1], center (0, 0), +y toward the top of the screen (away from the hand).
A marble is **out** when its center is outside the square: `|x| > 1 || |y| > 1`.

## Model changes (`BanBiModel.cs`)

### Settings (`BanBiSettings`)

Remove: `neutralMarbles`, `aimSweepDegrees`, `aimSweepHz`, `neutralPoints`, `rivalPoints`, `turnsPerPlayer`.

Add / change:

| Field | Default | Notes |
|---|---|---|
| `startMarblesPerTeam` | 3 | placed at start, seeded |
| `startArea` | 0.7 | start marbles go inside \|x\|,\|y\| ≤ this, min spacing 2.5 × radius, teams mixed |
| `marbleRadius` | 0.07 | zone units (was ring units) |
| `slideRange` | 0.85 | hand x sweeps within ±this |
| `slideHz` | 0.5 | sine sweep frequency |
| `launchY` | -1.2 | the marble leaves the hand here, just below the bottom edge |
| `minShotSpeed` / `maxShotSpeed` | 1.0 / 2.8 | stopping distance is v²/(2·friction): 0.45 (just reaches inside) to 3.56 (overshoots the top). Tune in the Inspector. |
| `shotsPerTeam` | 8 | regular shots per team; skipped turns count, bonus shots don't |

Unchanged: dice fields, `turnSeconds`, `chargeSeconds`, `maxHoldSeconds`, `friction`, `stopSpeed`, `restitution`, `physicsStep`, `maxRollSeconds`, `maxBonusShots`.

### State

- `MarbleState`: replace `Owner` (player id / -1) with `Team` (0 red, 1 blue). Add `ShotBy` (player id or -1 for start marbles) for the view. Keep `Position`, `Velocity`, `InPlay`, `Moved`.
- Remove `_startSpots`, `_eliminated`, `TeamPoints`, `ShooterOf`, `StartSpotOf`, `StartSpots(...)`, `IsEliminated`, `AimBase`, `PlaceAtStart`, `PlaceNeutrals`, `TeamOut`, `ClosestToCenter` (replace below).
- Constructor: `BanBiModel(BanBiSettings settings, int playerCount, int seed)`; places `startMarblesPerTeam` per team with the seeded RNG (alternate red/blue while placing so teams are mixed).
- Add `_shotsTaken[2]` per team. `Round` becomes the active team's shot number, 1..`shotsPerTeam`.
- Add `public int TeamCount(int team)`: marbles in play of that team.

### Aiming → sliding

- Rename `AimAngle(time)` → `HandX(time)`: while `Aiming`, `slideRange * sin(2π · slideHz · (time - phaseStart))`; otherwise the locked x.
- Press in `Aiming` locks `_lockedX = HandX(time)` → `Charging` (turn timer freezes, as now).
- `Power(time)` unchanged.

### Launch

- Spawn a new `MarbleState(team of ActivePlayer, (lockedX, launchY))` with `ShotBy = ActivePlayer`, velocity `(0, lerp(minShotSpeed, maxShotSpeed, power))`. Append it to `Marbles` (indices of existing marbles never change; the view adds an element on the `Shot` event).
- Clear `Moved` on all marbles, then `Rolling` as now.

### Physics

`BanBiPhysics.Step` unchanged (equal-mass circles, friction, restitution, no walls).

### ResolveShot (rewrite)

For every in-play marble with `Moved`, set velocity 0; if out (`|x| > 1 || |y| > 1`): `InPlay = false`, emit `MarbleOut(index, shooter)`. Count rival marbles removed this shot.
- This also covers a shot marble that never reached the square or went out of the far side (emit `Missed(shooter)` when the removed marble is the one just shot and it never collided, for the "Hụt!" message).
- Own-team marbles knocked out are simply removed. No points, no return.

Then:
1. **Win check:** `red = TeamCount(0)`, `blue = TeamCount(1)`. If one is 0 and the other isn't → the non-empty team wins. Both 0 → draw.
2. **Bonus:** if at least one rival marble went out and `_bonusShots < maxBonusShots` → same player aims again (`BonusShot` event), not counted as a shot.
3. Otherwise `EndTurn`.

### EndTurn / finish

- `_shotsTaken[team of ActivePlayer]++` (skips count).
- When both teams reached `shotsPerTeam` → `FinishOnCount()`: more marbles in the square wins; equal → team with the single marble closest to the center (Euclidean `Position.magnitude`) wins; still equal → draw.
- Next player: next in `_turnOrder` (no elimination skipping any more). Order alternates teams, so in 2v2 each player takes 4 of their team's 8 shots.

### Events

Remove `Returned`, `Eliminated`. Add `Missed` (Player = shooter). `MarbleOut`: Player = marble index, Other = shooter. Others unchanged.

### Dice

Unchanged (`ResolveDice`, team alternation, reroll rules).

## View changes (`BanBiGame.cs`, `BanBiHud.uxml/.uss`)

Remove: `CharacterView` players and `playerLayer`, ring ellipse constants (`RingRadiusX/Y`, `RingCenter`, `StandRadius`), `ToStage`'s side-on mapping, `ScreenDirection`, depth scaling of marbles and shadows, the rotating aim bar.

### Layout (panel units; keep the constants and the USS in sync and comment both sides, like `TrackHalfWidth` / `.win-line`)

- `ZoneHalf = 300f`: half the side of the square (`.zone` width/height 600).
- `ZoneCenterY = 0.42f`: fraction of stage height for the square's center (leaves room below for the hand).
- `ToStage(zone, stage) = (stage.x/2 + zone.x * ZoneHalf, stage.y * ZoneCenterY - zone.y * ZoneHalf)`.
- `MarbleSize = 2 * marbleRadius * ZoneHalf` (= 42 with the defaults), matches `.marble`.
- The square's seats/`seatLayer` stay where `PanelMiniGame` puts them; the square must stay clear of the seat discs at 16:9 and at tall phones (check with the safe area).

### Zone

- `.zone`: light sand fill with a thick dark chalk border so red and blue marbles both stand out. Faint scratch marks optional. No ellipse squash.

### Marbles (easy to see)

- Red team `#E5383B`, blue team `#2B6CF6`: high contrast on sand and against each other, also for red/green colour-blind players because of the brightness gap. Add a 3px white rim, a 2px dark outline and the existing `.marble-shine`. These replace the `player--pN` colors on marbles.
- Shadow: a small soft dark circle offset (4, 5) px, no depth scaling.
- A newly shot marble gets an element on the `Shot` event (pooled list grows with `Marbles`).
- Out: keep the fade (`OutFadeSeconds`), plus a small scale-down.

### Hand

- Sprites: **`VN_4.png`** (red team / Vietnam) and **`KR_3.png`** (blue team / Korea). They are not in `Assets/` yet: copy them into `Assets/UI Assets/BanBi/`, import as Sprite (2D and UI), keep the `.meta` files.
- One `hand` element, always at `ToStage((handX, launchY))`, fingers pointing up at the square. Swap the background image by the active team (`hand--vn` / `hand--kr`). If a sprite isn't drawn pointing up, rotate it in USS, not in code.
- The marble in the hand is drawn at the fingertip in the team color while aiming/charging.
- Charging: the hand (and its marble) pulls down by `power * 60` px. Release: the hand flicks up 20 px for `FlickSeconds` (0.15 s), the marble element takes over.
- Visibility: if a hand blends into the background, add a soft drop shadow element behind it (same sprite, tinted black, 40% opacity, offset 6 px). The hands' own colors may also be adjusted in the image to read clearly against the HUD.
- Under the hand, a small chip in the team color with the active player's name ("P1").

### Aim line (from the sketch)

- A dashed line straight up from the marble in the hand. Build it from ~14 pooled dot elements (UI Toolkit has no dashed borders).
- While aiming: fixed length up to the square's top edge.
- While charging: its length follows the predicted stopping distance `v²/(2·friction)` for the current power, ignoring collisions, so players can read the power. Dots past the top edge turn semi-transparent (that power overshoots).

### HUD

- Team counters replace points: `VN ● 3` and `KR ● 3` (marbles currently in the square), in team colors.
- Timer pill: turn countdown while aiming/charging; otherwise `Lượt x/8` for the active team (`_model.Round` / `shotsPerTeam`).
- Dice and order badges on seats: unchanged. Seat dimming with `is-waiting`: unchanged.
- Center messages: `Lượt của P{0}`, `Thêm lượt!` (unchanged), `Hụt!` on `Missed`. Remove the `Loại!` feedback and `is-out` classes.

### Remove from UXML

`playerLayer`. Add `zone` (inside `stageLayer`), `aimDots`, keep `marbleLayer` above `zone`, `hand` above marbles.

## Localization

| Vietnamese | Korean | English |
|---|---|---|
| Hụt! | 빗나갔어요! | Missed! |

All other strings are reused from v1. `Loại!` may stay in the table but is no longer shown. Run `LocalizationTests.EveryKoreanLetterFitsOnOneAtlasPage` after adding the Korean.

Fun fact stays (`BanBiGame.Facts`).

## Tests (`Assets/Tests/PlayMode/BanBiModelTests.cs`)

Remove tests for neutral scoring, elimination, returning to start spots, aim sweep angles, points tiebreak.

Keep: dice tests, non-active presses ignored, power triangle wave, auto-release, turn timeout skip, physics collision/momentum/stopping distance, frame-rate independence (re-check with the new launch).

Add:
- Start: `startMarblesPerTeam` per team, all inside `startArea`, no two closer than 2.5 × radius; same seed → same layout.
- `HandX` stays within ±`slideRange` and is locked on press.
- Launch spawns one marble at `(lockedX, launchY)` with velocity `(0, speed)`, team = active team, `ShotBy` = active player.
- Minimum-power shot with nothing in the way stops inside the square near the bottom edge; maximum power with nothing in the way goes out the top and is removed with `Missed`.
- A shot that stops inside → `TeamCount` +1.
- Knocking a rival marble out → removed and a bonus shot; capped at `maxBonusShots`; bonus shots don't increase `_shotsTaken`.
- Knocking your own marble out → removed, no bonus.
- After a shot, a team with 0 marbles loses at once; both 0 → draw.
- After both teams take `shotsPerTeam` shots: more marbles wins; equal count → closest to center wins; full tie → draw.
- 2v2: order alternates teams and each player takes 4 shots when nobody gets bonus shots.
- `PlayerScores` / ranking: teammates share rank (unchanged).

Update any `MenuFlowTests` case for Bắn Bi only if the scene's UXML names it relies on changed.

## Done when

- Playing `BanBi` directly (editorPlayerCount 2 and 4) shows the top-down square, 3 red + 3 blue marbles, the VN hand on red's turns and the KR hand on blue's turns, the sliding hand, dashed aim line, power pull-back, and marbles staying in the zone.
- All PlayMode tests in `KoreaGame.Tests.PlayMode` pass.
