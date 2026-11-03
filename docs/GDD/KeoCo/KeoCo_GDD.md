# Kéo Co · 줄다리기 · Tug of War — Mini game GDD

*Part of [Sân Chơi – Madang · Game Design Document](../SanChoi/SanChoi_GDD.md). The seats, round frame, scoring and modes described there apply here.*

| | |
|---|---|
| Names | Kéo Co (VI) · 줄다리기 Jul-darigi (KO) · Tug of War (EN) |
| Menu subtitle | *Một di sản chung* — "A shared heritage" |
| Players | 2 (1v1) or 4 (2v2) |
| Format | Teams: left = Vietnam (P1, P3), right = Korea (P2, P4) |
| Verb | **Tap on the beat** |
| Round length | 30 s (or earlier, if a team pulls the ribbon over its line) |
| Tournament slot | 2nd of 3 |
| Online | ✅ The only game playable online so far |
| Code | `KeoCoModel` (rules), `KeoCoGame` (view), `Assets/UI/KeoCo/` |

---

## 1. Fantasy

Two teams grip a rope in the village courtyard. Each side chants to keep its pull together: the Vietnamese side calls ***Hò – dô – ta!*** and the Korean side answers ***Yeong – cha!*** The team that stays on the rhythm and pulls *together* drags the ribbon over its line.

**The design idea:** tug of war isn't won by strength alone. It's won by pulling at the same moment. So the game rewards **rhythm** and **team sync**, never raw tapping speed.

---

## 2. Rules

### 2.1 Goal

Pull the ribbon in the middle of the rope across **your team's win line**. If time runs out first, the team with the ribbon on its side wins.

### 2.2 The beat

- A chant beat starts at the moment the countdown ends.
- The tempo is **96 BPM**, then in the last **10 s** it ramps up steadily to **126 BPM**. The timer pill turns urgent at the same moment.
- Each seat shows a **ring closing onto the button**. It meets the button exactly on the beat. The disc pulses on the beat, and the next syllable of that side's chant flashes and fades.

### 2.3 Tapping

Each tap is judged by how far it lands from the nearest beat:

| Judgement | Distance from beat | Pull (impulse) |
|---|---|---|
| **Perfect!** | ≤ 0.07 s | 1.0 |
| **Good** | ≤ 0.14 s | 0.6 |
| **Miss** | further away | 0.1, and grip is lost |

Further rules:

- **One scoring tap per beat.** A second tap on a beat that already scored counts as a Miss.
- **Losing grip (anti-mash):** a Miss makes the player lose grip for **0.25 s**. A tap while grip is lost scores nothing and **restarts** the 0.25 s, so mashing keeps you slipping. To get back on the rope, stop for a moment and come in on the next beat.
- **Đồng lòng! / "In sync!" (2v2 only):** if both teammates score on the **same beat** within **0.1 s** of each other, the second tap gets **+0.5** extra pull, and *Đồng lòng!* pops at both seats.

### 2.4 Rope movement

- The ribbon position runs from −1 (left team's line) to +1 (right team's line) and starts at 0.
- Each tap pushes the ribbon toward the tapping team's side. One unit of impulse moves it **0.06** of the half-track.
- The movement glides and settles instead of jumping (damping 5/s). The motion is calculated exactly, so the frame rate doesn't change how far a pull goes.

**Rough feel:** a team needs about **17 more Perfect taps** than the other team to win early. One 30 s round has about 50 beats, so an early win needs a clearly better team. Most even matches go to the timer.

### 2.5 End of round

| Condition | Result |
|---|---|
| Ribbon reaches ±1 | That team wins immediately |
| Timer hits 0, ribbon more than 0.01 from the centre | The team whose side it's on wins |
| Timer hits 0, ribbon within 0.01 of the centre | **Draw (Hòa!)**, with no fanfare |

**Ranking:** teammates share their team's result. The winning team gets rank 1, the losing team rank 2 in 1v1 or rank 3 in 2v2, and a draw gives everyone rank 1.

---

## 3. Presentation

| Element | Description |
|---|---|
| Field | A horizontal rope across the courtyard with a ribbon at its centre and a win line on each side |
| Pullers | Every player is drawn in side view with the `*_side_pull` art and a separate arm on the rope. They lean back 6° on their team's side, front puller nearest the ribbon. They move with the rope. |
| Seat | Disc + closing beat ring + chant syllable (*Hò / dô / ta!* left, *Yeong / cha!* right) + judgement pop |
| Feedback colours | Perfect, Good, Miss and *Đồng lòng!* each have their own colour class |
| Countdown | 3 · 2 · 1 · ***Kéo!*** (*당겨! / Pull!*) |
| Results | *"P1 thắng!"* / *"P1 + P3 thắng!"* / *"Hòa!"* |
| Fun fact | "In 2015, UNESCO inscribed tug of war as a shared heritage of Vietnam, Korea, Cambodia and the Philippines." (VI / KO / EN) |

---

## 4. Online play

Kéo Co is the reference online game:

1. The host picks the player count and creates a room, and the others join with the code or link.
2. Every phone loads the scene and reports *ready*. The host then broadcasts a countdown start time 0.5 s in the future, so all phones count down together on the shared clock.
3. Each phone shows only **its own** seat, at the bottom of that player's team side.
4. Joining phones send their presses with network timestamps. The host judges them (a press is treated as at most 0.5 s old, so a stalled phone can't rewrite the past).
5. The host broadcasts each judged tap (so every phone shows *Perfect! / Đồng lòng!*), the ribbon position every frame, and the final result.
6. Joining phones smooth the ribbon toward the host's position. *Play again* from the host restarts everyone.

---

## 5. Tuning (`KeoCoSettings`, Inspector on `KeoCoGame`)

| Setting | Default | Effect |
|---|---|---|
| `roundSeconds` | 30 | Round length |
| `startBpm` / `endBpm` | 96 / 126 | Chant tempo before and at the end of the ramp |
| `rampSeconds` | 10 | Length of the final speed-up (also when the timer turns urgent) |
| `perfectWindow` / `goodWindow` | 0.07 / 0.14 s | Judgement windows |
| `perfectImpulse` / `goodImpulse` / `missImpulse` | 1 / 0.6 / 0.1 | Pull per tap |
| `slipSeconds` | 0.25 s | Grip-loss time after a Miss (restarted by each further tap) |
| `syncBonus` / `syncWindow` | 0.5 / 0.1 s | *Đồng lòng* bonus and how close teammates must be |
| `pullPerImpulse` | 0.06 | How far one unit of impulse moves the ribbon (win at 1.0) |
| `damping` | 5 | How fast a pull settles (higher = snappier) |
| `drawMargin` | 0.01 | Distance from the centre counted as a draw at timeout |

**Tuning levers:** for more comebacks, lower `damping` or `pullPerImpulse`. To make 2v2 lean more on coordination, raise `syncBonus`. If rounds feel too hard for casual players, widen `goodWindow`.

---

## 6. Design notes and open questions

- **Why rhythm and not mashing:** mashing favours the fastest thumb and ends in sore fingers. Tapping on the beat lets kids and adults compete fairly and gives the team something to chant together.
- **Why the ramp:** the last 10 s build tension and can flip an even match, because the faster beat shrinks the time between scoring taps.
- *Open:* there are no sounds for the beat or for judgements yet, only the visual ring and the background music. Audible chant beats would help players who can't watch their ring.
- *Open:* in 2v2 a lone strong player can't add sync bonuses alone. Is that the intended way to push teamwork? (Current answer: yes.)

---

← Back to [Sân Chơi – Madang GDD](../SanChoi/SanChoi_GDD.md) · Other games: [Người Gỗ](../NguoiGo/NguoiGo_GDD.md) · [Đá Cầu](../DaCau/DaCau_GDD.md)
