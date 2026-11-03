# Người Gỗ · 무궁화 꽃이 피었습니다 · Statues — Mini game GDD

*Part of [Sân Chơi – Madang · Game Design Document](../SanChoi/SanChoi_GDD.md). The seats, round frame, scoring and modes described there apply here.*

| | |
|---|---|
| Names | Người Gỗ, "wooden person" (VI) · 무궁화 꽃이 피었습니다 *Mugunghwa kkochi pieotseumnida*, "the rose of Sharon has bloomed" (KO) · Statues / red light–green light (EN) |
| Menu subtitle | *Hai trò khác tên, một luật chơi*, "Two names, one game" |
| Players | 2 or 4 |
| Format | Free-for-all |
| Verb | **Hold** to run, **release** to freeze |
| Round length | Until someone reaches the caller; safety limit 75 s |
| Tournament slot | 1st of 3 (opens the tournament) |
| Online | ❌ Not yet |
| Code | `NguoiGoModel` (rules), `NguoiGoGame` (view), `Assets/UI/NguoiGo/` |

---

## 1. Fantasy

A villager stands in the middle of the courtyard with their eyes covered, chanting. While they chant, everyone creeps closer. When the chant ends they spin around, and anyone still moving is sent back to the start. The first to tag the caller wins.

The chant alternates between the two countries: ***"Một, hai, ba, người gỗ!"*** and ***"무궁화 꽃이 피었습니다!"***. Same game, two songs.

**The design idea:** this is a game of **reading** and **nerve**. The caller is a bluffer with tells and fakes, and players gamble on how much more ground they can gain before letting go.

---

## 2. Rules

### 2.1 Goal

Be the first to reach the caller. If 75 s pass first, the player closest to the caller wins.

### 2.2 Moving

- **Holding** your button makes your runner move along your lane toward the caller.
- Crossing the whole lane takes **7 s of holding** in total.
- **Releasing** stops your runner on the spot. Progress is kept.

### 2.3 The caller's cycle

Each cycle is generated in advance from a random seed:

```
Chanting ──▶ [optional: Tell ▶ Fake ▶ Chanting] ──▶ Tell ──▶ Turning ──▶ Watching ──▶ next cycle
  safe             mid-chant bluff, safe           warning   last chance   CAUGHT if held
```

| Phase | Duration | What players see | Holding is… |
|---|---|---|---|
| **Chanting** | depends on the chant style (below) | Caller's back turned; the chant appears syllable by syllable at every seat | Safe: you move |
| **Tell** | 0.25 s | Shoulders twitch slightly (no colour change) | Safe, but you should let go now |
| **Turning** | 0.2 s | Caller turns to face the field | Safe: reaction time |
| **Watching** | 1.2 – 2.2 s (random) | Caller faces the field; seats show ***Đứng im!*** (*Freeze!*) | **Caught**: sent back to the start |
| **Fake** *(mid-chant only)* | 0.35 s | A bigger shoulder twitch, but the back stays turned and the chant carries on | Safe: nobody can be caught |

**Caught:** a runner still held at any moment of Watching goes back to position 0, and *Bị bắt!* (*Caught!*) pops at their seat. A player can only be caught once per Watching phase, so letting go and pressing again doesn't send them back twice.

### 2.4 Chant styles

Each cycle switches language (Vietnamese first, then Korean, and so on) and randomly picks a style:

| Style | Chance | Chant length | Notes |
|---|---|---|---|
| **Normal** | default | 2.4 s | May get a stretched last syllable (30%) or a mid-chant fake (30%) |
| **Fast** | 25% | 1.1 s | Never stretched and never faked. It's a sprint trap. |
| **Stretched last syllable** | 30% of normal chants | +0.8 – 1.8 s | *"…người gỗỗỗỗ!"*: punishes players who time the chant from memory |
| **Fake turn** | 30% of normal chants | +0.6 s at the halfway syllable | A Tell followed by a Fake, then the chant resumes |

Syllables are spread evenly over the chant length. The Vietnamese chant has 5 syllables and the Korean chant has 10, so the Korean chant *sounds* faster at the same length.

### 2.5 Fairness rules (enforced by tests)

- **A Tell always comes before Watching.** No player is ever caught without a warning.
- **A visible face always means danger.** A fake keeps the caller's back turned, so if you can see the caller's face, you can be caught.
- **A finger already down counts.** When a round restarts or resumes from pause, a finger that is still pressing is treated as holding.

### 2.6 End of round

| Condition | Result |
|---|---|
| A runner reaches the caller | Round ends. The player(s) furthest along win. |
| 75 s timer runs out | The player(s) furthest along win |
| Nobody ever left the start | **Draw (Hòa!)**, with no fanfare |

**Ranking:** players are ranked by how far along their lane they are. Players at the same position share a rank.

---

## 3. Presentation

| Element | Description |
|---|---|
| Field | Side-on courtyard. The caller (a neutral villager, `NPC.png` from the front and `NPC_back.png` from behind, with no face) stands centre-back at 72% of the stage height. |
| Lanes | Dashed paper-edge-coloured lines from each player's side of the courtyard to just beside the caller. Players on the far side start near the horizon and appear smaller. |
| Runners | Each player's character faces along their lane and bobs while running. Players further back are drawn first, so nearer ones overlap them. |
| Seat | Disc + ring (shown while that player is moving) + the chant text as it is sung + feedback pop |
| Caller states | Back turned (Chanting/Tell/Fake), shoulder twitch of 3° (Tell) or 7° (Fake), facing the field (Turning/Watching) |
| Timer | Seconds left of the 75 s limit; turns urgent in the last 10 s |
| Countdown | 3 · 2 · 1 · ***Chạy!*** (*달려! / Run!*) |
| Results | *"P2 thắng!"* or shared *"P1 + P4 thắng!"* / *"Hòa!"* |
| Fun fact | "In Korea the caller chants 'The mugunghwa has bloomed', and in Vietnam 'One, two, three, wooden person!'" The mugunghwa is Korea's national flower. |

**Deliberate subtlety:** the Tell is only a small shoulder twitch, with no colour flash or sound. Players who watch the caller instead of their own thumb get a reward.

**Content rule:** the caller is a friendly villager, not a doll, and getting caught sends you back to the start rather than out of the game. There is nothing that resembles Squid Game.

---

## 4. Tuning (`NguoiGoSettings`, Inspector on `NguoiGoGame`)

| Setting | Default | Effect |
|---|---|---|
| `runSeconds` | 7 | Seconds of holding to cross the lane |
| `roundSeconds` | 75 | Safety time limit |
| `normalChantSeconds` / `fastChantSeconds` | 2.4 / 1.1 | Chant lengths |
| `fastChance` | 0.25 | Chance a chant is fast |
| `stretchChance` | 0.3 | Chance a normal chant holds its last syllable |
| `stretchMinSeconds` / `stretchMaxSeconds` | 0.8 / 1.8 | Length of that hold |
| `tellSeconds` | 0.25 | Shoulder-twitch warning length |
| `turningSeconds` | 0.2 | Grace period after the Tell |
| `watchMinSeconds` / `watchMaxSeconds` | 1.2 / 2.2 | How long the caller watches |
| `fakeChance` | 0.3 | Chance a normal chant has a mid-chant fake |
| `fakeSeconds` | 0.35 | Fake turn length |

**Tuning levers:**

- `runSeconds` sets round length. At 7 s, and assuming players hold through most chants, a round lasts about 4–6 cycles.
- `tellSeconds + turningSeconds` (0.45 s total) is the reaction budget. Below about 0.35 s the game starts to feel unfair.
- Raising `fastChance` or `stretchChance` makes the caller less predictable.

---

## 5. Design notes and open questions

- **Why hold instead of tap:** holding makes "not moving" an action, letting go, which is the core tension of statues. A tap-to-step version rewards mashing between turns.
- **Why it opens the tournament:** it is the easiest game to understand by watching, it needs no rhythm, and every caught player gets a big laugh. That makes it a good warm-up.
- *Open:* there's no audio for the chant yet. A recorded chant in each language (with the stretch) would let players listen instead of reading the seat text.
- *Open:* online play needs host-authoritative catches with timestamped hold/release, the same pattern as Kéo Co.

---

← Back to [Sân Chơi – Madang GDD](../SanChoi/SanChoi_GDD.md) · Other games: [Kéo Co](../KeoCo/KeoCo_GDD.md) · [Đá Cầu](../DaCau/DaCau_GDD.md)
