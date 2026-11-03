# Đá Cầu · 제기차기 · Shuttlecock Kick — Mini game GDD

*Part of [Sân Chơi – Madang · Game Design Document](../SanChoi/SanChoi_GDD.md). The seats, round frame, scoring and modes described there apply here.*

| | |
|---|---|
| Names | Đá Cầu (VI) · 제기차기 *Jegichagi* (KO) · Shuttlecock Kick (EN) |
| Menu subtitle | *Chơi cùng nhau, chuyền cầu cho nhau*, "Play together, pass it around" |
| Players | 2 or 4 |
| Format | Free-for-all, last player standing |
| Verb | **Tap** as the shuttle lands |
| Round length | Until only one player has lives left (usually 30–90 s) |
| Tournament slot | 3rd of 3 (the finale) |
| Online | ❌ Not yet |
| Code | `DaCauModel` (rules), `DaCauGame` (view), `Assets/UI/DaCau/` |

---

## 1. Fantasy

Friends stand in a circle and keep a shuttlecock in the air with their feet. In Vietnam it's a *cầu* made of chicken feathers. In Korea it's a *jegi*, a coin wrapped in paper or cloth. Each kick goes to someone else, the rally speeds up, the wind picks up, and someone eventually lets it drop.

**The design idea:** this is a **reaction and attention** game. Nobody knows who the next kick goes to, so everybody has to watch every flight. Mashing to be safe costs you, because tapping too early loses a life.

---

## 2. Rules

### 2.1 Goal

Keep your lives. Each player starts with **3 lives** (dots at their seat). A player who loses all three is out. The last player with lives left wins.

### 2.2 Flights

- The first serve comes from P1. Every kick or serve flies to a **random other player who is still in the game**. With two players left it goes back and forth.
- The **receiver** is the only player who can kick. Everyone else's taps are ignored.
- When the shuttle gets close, the receiver's **ring starts closing** onto their button. *It meets the button exactly when the shuttle lands.*

### 2.3 Kicking

All timing is measured from the moment the shuttle lands:

```
   ignored    │   TOO EARLY (−1 life)  │ GOOD │PERFECT│ GOOD │ dropped (−1 life)
──────────────┼────────────────────────┼──────┼───────┼──────┼──────────────────▶ time
          zone start              −0.15 s  −0.07  landing  +0.07  +0.15 s
     (ring appears; ≤ 0.55 s before landing)
```

| Tap timing | Result |
|---|---|
| Before the zone opens | Ignored (no penalty, so a reflex tap far too early is forgiven) |
| In the zone but more than 0.15 s early | ***Sớm quá!*** (*Too early!*): **lose a life** |
| Within 0.07 s of landing | ***Perfect!***: the shuttle flies on **20% faster** |
| Within 0.15 s of landing | ***Good***: the shuttle flies on at normal speed |
| No tap by 0.15 s after landing | The shuttle drops: ***Trượt!*** (*Missed!*), **lose a life** |

The zone opens 0.55 s before landing, or at 60% of the flight on short flights, whichever is later. Players can't tap their way through a short flight.

**Perfect as an attack:** a Perfect kick doesn't earn points. It sends a faster shuttle to the next player, putting pressure on them.

### 2.4 Rally speed-up

- Every kick in a rally makes the next flight **3.5% shorter** (× 0.965 per kick).
- Base flight: **1.25 s**. It never goes below **0.55 s**.
- The timer pill shows the **rally count** instead of seconds.

### 2.5 Wind

- From the **3rd kick** of a rally, each kick has a **20%** chance to bring wind. It's shown as ***Gió xuôi!*** (*Tailwind!*) or ***Gió ngược!*** (*Headwind!*).
- A tailwind makes the flight 15% shorter and a headwind makes it 15% longer. The shuttle's path visibly bends sideways.
- Wind clears when the shuttle drops or is served again.

### 2.6 Feather and jegi

The shuttle **switches type after every drop**, so the feel changes all through the match:

| | Feather *cầu* (first) | Jegi |
|---|---|---|
| Flight time | × 1.0 | × 0.9 (quicker) |
| Arc height | High | Low and flat |
| Motion | Floats up, then **drops late** (eased travel) | Even travel, **spins** |

### 2.7 After a drop

- The shuttle rests on the ground where it fell for **1 s**.
- Then the player who missed **serves**, or a random remaining player if the one who missed is out. A serve takes 1.6 s (× 0.9 for a jegi) and goes to a random other player.
- The rally count resets to 0.

### 2.8 End of round

The round ends when only one player has lives left. That player wins (*"P3 thắng!"*). There are no draws.

**Ranking:** the survivor is 1st. The other players are ranked by **how long they lasted**: the last one knocked out is 2nd, and the first one out is last.

---

## 3. Presentation

| Element | Description |
|---|---|
| Field | Side-on courtyard. Each player stands at a landing spot on the ground on their side, scaled by depth. |
| Facing | In 2-player games the two players face each other across the middle. In 4-player games each one faces across, toward the far or near side. |
| Shuttle | Sprite with a **separate ground shadow**. Its height is drawn above the shadow, and the shadow shrinks as the shuttle rises. Its height is limited near the top edge so it stays on screen. |
| Kick animation | The player hops 26 px and swings back, then through (24° tilt), over 0.4 s |
| Seat | Disc + closing ring (receiver only) + 3 life dots + feedback pop. A player who is out greys out at both their seat and their character. |
| Countdown | 3 · 2 · 1 · ***Đá!*** (*차! / Kick!*) |
| Fun fact | "A Vietnamese shuttlecock is usually made of chicken feathers, while a Korean jegi is a coin wrapped in paper or cloth." |

---

## 4. Tuning (`DaCauSettings`, Inspector on `DaCauGame`)

| Setting | Default | Effect |
|---|---|---|
| `startingLives` | 3 | Lives per player |
| `serveSeconds` | 1.6 | Serve flight time |
| `baseFlightSeconds` | 1.25 | First kick's flight time |
| `minFlightSeconds` | 0.55 | Fastest possible flight |
| `rallySpeedup` | 0.965 | Flight-time multiplier per kick in a rally |
| `perfectScale` | 0.8 | Flight-time multiplier after a Perfect |
| `jegiScale` | 0.9 | Flight-time multiplier for the jegi |
| `perfectWindow` / `goodWindow` | 0.07 / 0.15 s | Kick judgement windows |
| `zoneSeconds` | 0.55 | How early the tap zone (and ring) opens |
| `dropPauseSeconds` | 1 | Pause after a drop before the next serve |
| `windChance` | 0.2 | Chance per kick that wind starts |
| `windStartRally` | 3 | First rally kick where wind can appear |
| `windScale` | 0.15 | How much wind changes the flight time |

**Tuning levers:**

- `startingLives` sets match length the most directly. Use 2 lives for a quick game and 5 for a long one.
- `rallySpeedup` and `minFlightSeconds` decide how intense long rallies get. The floor is reached after about 24 kicks (or fewer with Perfects or a jegi).
- `zoneSeconds` versus `goodWindow` controls how harshly early taps are punished.

---

## 5. Design notes and open questions

- **Why random targets:** in a fixed rotation players only pay attention on their own turn. Random targets keep all four players watching every flight, which is the tension of a real kicking circle.
- **Why early taps cost a life:** without that penalty the best strategy is to mash whenever the ring appears. The penalty only applies inside the zone, so a stray early tap is still forgiven.
- **Why swap feather and jegi:** after each drop the timing feel changes, which keeps a long match from turning into rote muscle memory, and it shows off both cultures' versions.
- *Open:* there's no audio for kicks or drops yet. A *tock* sound on each kick would help players time their taps.
- *Open:* 4-player games are a free-for-all, not 2v2. Should there be a team variant (for example, shared lives)?

---

← Back to [Sân Chơi – Madang GDD](../SanChoi/SanChoi_GDD.md) · Other games: [Người Gỗ](../NguoiGo/NguoiGo_GDD.md) · [Kéo Co](../KeoCo/KeoCo_GDD.md)
