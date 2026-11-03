# Sân Chơi – Madang · Game Design Document

*Project GDD. Each mini game has its own GDD, linked in [§6 Mini games](#6-mini-games).*

| | |
|---|---|
| Working title | **Sân Chơi – Madang** (Vietnamese *sân chơi* "playground" + Korean *마당 madang* "courtyard") |
| Genre | Local-multiplayer party game, one-button mini games |
| Platform | Android / iOS phones, landscape; desktop build for testing |
| Players | 2 or 4 on one phone (3 is not supported); online, one phone per player (Kéo Co only) |
| Session length | 30 s – 75 s per mini game; about 5 minutes for a full tournament |
| Engine | Unity 6.5 (6000.5.4f1), URP 2D, UI Toolkit, new Input System |
| Languages | Vietnamese, Korean, English (switchable in game) |
| Last updated | 2026-11-03 |

---

## 1. Concept

A phone lies flat on a table between friends. Each player sits at one edge or corner and owns **one button**. Together they play folk games that children in **Vietnam and Korea** share under different names: tug of war, red light–green light and shuttlecock kicking.

The game celebrates the overlap between the two cultures. Every game has two names, one in each language, and two chants. In team games the left side always plays as Vietnamese characters and the right side as Korean characters. After each round a results card gives a short fact about the game's history in both countries.

### Pillars

1. **One button, zero tutorial.** Each game uses one verb (tap on the beat, hold to run, tap as it lands) that a new player picks up within a single countdown.
2. **Face each other, not the screen.** The phone lies between the players and everyone plays at once on the same screen. Rounds are short and loud, so there is plenty of laughter and trash talk.
3. **Two cultures, one courtyard.** Vietnamese and Korean names, chants, characters and objects (a feather shuttlecock and a jegi) sit side by side, with equal weight.
4. **Skill over mashing.** Every game punishes spamming the button, through lost grip, early-tap penalties or getting caught. Timing and reading the game win.

### Out of scope / content rules

- **No Squid Game imagery.** No doll, red tracksuits, masks, guards, marble bags or "odd or even" betting. Người Gỗ uses a friendly villager as the caller, and being caught only sends you back to the start.
- No accounts, ads or purchases in the current scope.

---

## 2. Target audience and setting

- **Who:** friends, families and classmates aged about 8 and up, sitting together around a table, many of them non-gamers.
- **Where:** cafés, classrooms, family gatherings and cultural-exchange events between Vietnamese and Korean groups.
- **Why they play:** a quick shared laugh, plus the fun of finding out that the other country plays "your" game.

---

## 3. Physical setup and controls

### 3.1 Seats

The phone is locked to landscape and lies flat. Player IDs are set up so that **team = id % 2**: even IDs sit on the left half (Vietnamese team) and odd IDs on the right half (Korean team).

**2 players**: one at each short end, facing each other.

```
        ┌────────────────────────────────┐
  P1 ▶  │ (●)                        (●) │  ◀ P2
        └────────────────────────────────┘
        left half = P1          right half = P2
```

**4 players**: two on each long side, one button per corner. Teammates (P1 + P3, P2 + P4) share the same half of the screen.

```
               P3                P2
        ┌────────────────────────────────┐
        │ (●)                        (●) │
        │                                │
        │ (●)                        (●) │
        └────────────────────────────────┘
               P1                P4
```

| Player | Colour | 2-player seat | 4-player seat | Team |
|---|---|---|---|---|
| P1 | Coral `#E94F4F` | left end | bottom-left | 0 – Vietnam |
| P2 | Blue `#3D8BFF` | right end | top-right | 1 – Korea |
| P3 | Green `#3CC57A` | — | top-left | 0 – Vietnam |
| P4 | Yellow `#FFC23D` | — | bottom-right | 1 – Korea |

Every seat's UI (button disc, chant text, feedback) is rotated to face its player, so text always reads the right way up from where they sit.

### 3.2 Input rules

- **The whole screen is the controller.** Each seat owns a touch zone (its half or quarter of the screen). A finger belongs to the zone it first touched until it lifts, so a thumb that slides doesn't switch players.
- Games only ever see **press** and **release** per player. No game reads raw touches.
- The pause button is excluded from the touch zones, so tapping it never counts as a player tap.
- Testing in the Editor: keys **Z / P / Q / M** = P1–P4 (by screen corner), and the mouse works as a finger.

---

## 4. Game modes

| Mode | How it works | Ends with |
|---|---|---|
| **Single game** | Pick 2 or 4 players, then tap a game card on the menu. | Results card → *Play again* or *Menu* |
| **Tournament** (*Giải đấu*) | All three games in a fixed order, with points carried over. | Podium ceremony |
| **Online** (*Chơi online*) | Each player on their own phone. Kéo Co only so far. | Results card on every phone |

### 4.1 Tournament

The order follows the story of a day in the courtyard: **Người Gỗ → Kéo Co → Đá Cầu → Podium**.

Points per mini game, by rank (ties share a rank):

| Rank | 1st | 2nd | 3rd | 4th |
|---|---|---|---|---|
| Points | 3 | 2 | 1 | 0 |

In team games teammates share their team's rank. In a 2v2 Kéo Co, for example, both winners rank 1st and get 3 points, while both losers rank 3rd and get 1 point. A draw puts everyone 1st.

On the **Podium** the columns read 2nd · 1st · 3rd · 4th. The champion (or tied champions) lifts the **VN × KR trophy cup**, everyone sways, confetti bursts from the cup with light rays behind it, and the victory fanfare plays as the cup reaches the top.

### 4.2 Online

- The host creates a private room (with as many slots as the chosen player count) and shares a **room code** or a `madang://join/CODE` link.
- When the room is full, the host taps *Start* and every phone loads the game. Each phone shows only its own player's seat, upright at the bottom of that player's team side.
- The host's phone runs the rules. The other phones send presses stamped with the shared network clock, so everyone sees the same beats.
- There's no pause online, so one player can't freeze everyone else. If the host leaves, the game ends for everyone.

---

## 5. Core loop and shared round structure

```
Menu ─▶ choose 2 / 4 players ─▶ game card / Tournament / Online
   ▲                                    │
   │                                    ▼
   │                        Countdown 3 · 2 · 1 · GO!
   │                                    │
   │                                    ▼
   │                         Round (one button each)
   │                                    │
   │                                    ▼
   └──── Menu ◀── Results card ──▶ Play again / Continue (tournament) ─▶ next game / Podium
```

Every mini game shares the same frame, which comes from the `PanelMiniGame` base:

| Element | Behaviour |
|---|---|
| **Countdown** | 3 seconds, then a game-specific "go" word: *Kéo!* / *Chạy!* / *Đá!* |
| **Seat disc** | The player's button art (`Button_P1..4`), in their colour, with a ring that shows timing |
| **Timing ring** | Shrinks onto the disc. *Press when they meet.* Used by Kéo Co (each beat) and Đá Cầu (each arrival). In Người Gỗ the ring shows while the player is running. |
| **Feedback pop** | Short word at the seat: *Perfect! / Good / Miss / Đồng lòng! / Bị bắt! / Sớm quá! / Trượt! / Hết mạng!* |
| **Timer pill** | Seconds left (it turns urgent in the final stretch); in Đá Cầu it shows the rally count instead |
| **Pause** | Corner button (offline only). The game clock stops, and presses during the pause are dropped. |
| **Results card** | Winner title (*"P1 thắng!"*, or *"Hòa!"* for a draw) plus a fun fact in the current language. Field visuals fade/blur behind it. |
| **1v1 / 2v2 switch** | Only when a scene runs directly in the Editor without a menu, for testing |

### Ranking contract

Every mini game reports a **rank per player** (1 = best, ties share a rank). The tournament turns ranks into points. Each mini game GDD says how its ranks are worked out.

---

## 6. Mini games

| # | Game | Vietnamese / Korean | Verb | Format | Length | GDD |
|---|---|---|---|---|---|---|
| 1 | **Statues** | Người Gỗ / 무궁화 꽃이 피었습니다 | **Hold** to run, release to freeze | Free-for-all | up to 75 s | [NguoiGo_GDD.md](../NguoiGo/NguoiGo_GDD.md) |
| 2 | **Tug of War** | Kéo Co / 줄다리기 | **Tap** on the beat | Teams (1v1 / 2v2) | 30 s | [KeoCo_GDD.md](../KeoCo/KeoCo_GDD.md) |
| 3 | **Shuttlecock Kick** | Đá Cầu / 제기차기 | **Tap** as it lands | Free-for-all, lives | until one player is left | [DaCau_GDD.md](../DaCau/DaCau_GDD.md) |
| — | *Marbles (planned)* | Bắn Bi / 구슬치기 | Press–hold–release to flick | Teams, turn-based | 2–3 min | [BanBi.md spec](../../BanBi.md) *(approved, not implemented)* |

The three shipped games use three different skills: **reading** an opponent (Người Gỗ), **rhythm and teamwork** (Kéo Co), and **reaction under shifting attention** (Đá Cầu).

---

## 7. Presentation

### 7.1 Art direction

- **Warm, hand-drawn courtyard.** One side-on background (`BACKGROUND.png`) with its horizon at 60% of the screen height. Every character stands on the ground band below it, and is scaled by depth so the far side looks smaller.
- **Characters:** the left team is drawn in Vietnamese dress (`VN.png`) and the right team in Korean dress (`KR.png`), each with front, back and side views. Extra poses: `*_side_pull` (tug of war, with a separate arm piece) and `*_front_cheer` (podium). The Người Gỗ caller is a neutral villager (`NPC.png` / `NPC_back.png`).
- **Palette** (design tokens in `Madang.uss`): ink `#3B2A1A`, paper `#FCF4E3`, paper-edge `#E3CFA6`, wood `#8A6A45`, rose `#D6336C`, gold `#F2B233`, jade `#1F7A6D`, plus the four player colours.
- **Icons:** vector SVGs for the rope, the mugunghwa flower, the shuttlecock and the jegi.
- **Fonts:** *Mali Bold* (Latin and Vietnamese) with a *Jua* fallback for Hangul. Both are under the SIL Open Font License.

### 7.2 Audio

| Clip | Use |
|---|---|
| `SanChoi_BGM_Loop` | Background music. It starts at launch and keeps playing across scene changes. |
| Victory bell fanfare | Plays at the end of a round that has a winner (not on a draw) and when the podium cup reaches the top. The music ducks under it. |

*Gap:* there are no per-action sound effects yet (tap judgements, kicks, getting caught). See [§10](#10-open-questions-and-roadmap).

### 7.3 Localization

- All UI text follows the language picked with the globe button on the menu. The first launch follows the device language, and the choice is saved.
- **Chants stay in their own language** whatever the UI language, because they are what the players sing along to: *Hò dô ta! / Yeong-cha!*, *Một hai ba người gỗ! / 무궁화 꽃이 피었습니다!*
- Each fun fact is written three times (Vietnamese, Korean, English) and shown in the current language.
- Each menu card shows the game's name in the current language, with the other country's name underneath.

---

## 8. Menu and screen flow

| Screen | Contents |
|---|---|
| **Menu** | Title, a 2 / 4 players selector, three game cards (each with its subtitle and the other-language name), the *Tournament* button, *Play online*, and the language globe. A game card unlocks by itself once that game's scene is in the build; until then it shows *Coming soon*. |
| **Mini game** | The shared frame from [§5](#5-core-loop-and-shared-round-structure) plus the game's own field. |
| **Results card** | Title, fun fact, then *Play again* + *Menu* (single game) or *Continue* (tournament). |
| **Podium** | Standings, points, the cup ceremony, *Menu*. |
| **Online card** | Host / Join → room code, share or copy → waiting slots → *Start*. Shows errors: room not found, room full, host left, connection lost. |

---

## 9. Technical summary (for designers)

- Each mini game is split into a **pure rules model**, which is unit-tested with explicit timestamps, and a **view** that draws it.
- All tuning values are in a `*Settings` block on the game object in each scene, editable in the Inspector. Each mini game GDD lists its defaults.
- Rules run on their own clock, which stops while paused and is the shared network clock online, so timing doesn't depend on frame rate.
- Tests: rules models, scoring, input routing, localization (including a check that every Korean letter fits the font atlas), menu flow, online, podium.

---

## 10. Open questions and roadmap

| Item | Status |
|---|---|
| Bắn Bi / Guseul-chigi (marbles) as the 4th game | Design approved 2026-11-03 → [BanBi.md](../../BanBi.md). The tournament order needs a decision once it ships. |
| Online play for Người Gỗ and Đá Cầu | Not started. Kéo Co's host-authoritative pattern can be reused. |
| Sound effects for judgements, kicks, catches and countdown | Not started |
| Tournament scoring in 2v2 team games (losers still get 1 point each) | Decide whether this is intended |
| 3-player support | Out of scope by design (the seat layout requires 2 or 4) |
