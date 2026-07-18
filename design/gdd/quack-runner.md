# Quack Runner

> **Status**: Designed
> **Author**: Abdulrahman Alenazan + Claude
> **Last Updated**: 2026-07-12
> **Implements Pillar**: Every mini-game is a real pillar, not a side loop
> **Creative Director Review (CD-GDD-ALIGN)**: APPROVED WITH CONDITIONS
> 2026-07-12 (3 conditions, all fixed same pass — C1: corrected Open
> Question 5, which had mislabeled the sibling GDD's inherited
> readability-gate condition as the unrelated coin-vs-dodge ratio
> question, and restored the actual condition [obstacle traversal time
> can drop below the spawn floor at high t, an unreadable-spike risk the
> sibling GDD explicitly flagged]; C2: fixed an internal mis-citation in
> Edge Cases [Rule 5 → Rules 1/2]; C3: added Open Question 6 stating
> explicitly that the prototype's 100-coin/run cap is removed entirely,
> and that Runner becomes a second uncapped, Coin-Value-multiplied coin
> faucet worsening game-concept.md's already-open coin-sink gap)
> **`/design-review` (2026-07-17)**: NEEDS REVISION — 2 blocking items
> (Core Rule 2's "AABB collision itself is simple" treats cross-platform
> collision determinism as settled when the sibling ramp GDD's own
> just-added Core Rule 6 names this exact framing as an open,
> unaddressed risk requiring its own ADR — and this GDD, not the sibling,
> is where the collision code actually lives; Core Rule 4's citation to
> "the sibling GDD's Core Rule 6" now points to the wrong rule after that
> sibling's renumbering, a citation-drift bug this session's own edits
> caused) plus 2 recommended and 1 nice-to-have item. All folded in
> below; re-review pending.

## Overview

Quack Runner is a continuous vertical obstacle-dodge mini-game — the
duck moves horizontally along the bottom of the playfield while
obstacles descend from the top, collecting coins and dodging
bombs/birds/clouds until a single collision ends the run. It consumes
Obstacle Spawn/Difficulty Ramp (Runner)'s speed/interval/RNG outputs
entirely — this GDD owns the duck itself (movement, collision
resolution, scoring, run lifecycle), not the difficulty curve driving
it.

On the player side, this is the elevation moment `game-concept.md`
calls for directly: in the prototype, Runner is a lightweight
side-activity — capped at 100 coins/run, no progression, absent from
the leaderboard entirely. This GDD is where that changes, carrying over
the prototype's proven, already-shipped core loop (`runner.ts`) as the
mechanical baseline while resolving the scope gaps the concept doc
explicitly deferred here: whether Runner gets its own currency/
progression interaction, and whether it needs its own leaderboard
presence.

Mechanically and emotionally, Runner is a deliberate contrast to Super
Ricochet: continuous and reflexive where Ricochet is turn-based and
aim-driven, one mistake ends everything where Ricochet's loss is more
forgiving and multi-turn — two mini-games delivering escalating tension
through genuinely different mechanics, not the same feeling twice.

## Player Fantasy

Direct engagement — this is the moment-to-moment act of playing itself,
sitting underneath (not competing with) the sibling ramp GDD's already-
established "the game raising the bar" escalation fantasy. Two things
happen at once: every coin is a small, low-stakes dare — drift toward it
for the pickup, or take the safer dodge-bonus path instead, greed vs.
caution as the real tension, not the game hunting the duck. And every
successful dodge reads as a small performance, a sidestep rather than a
save — coins are applause for moving well, not loot for surviving.

**The death instant, specifically** (a genuine pillar guardrail, not a
style note): it must land as *missing a step*, not *being caught*. A
stumble, not a hit. No harsh sting, no "gotcha" framing, no sense of a
threat closing in — the failure animation should read as endearing (a
little tumble, a surprised quack), consistent with the art bible's
"gentle disappointment, never grim" and with how the escalating ramp
itself is framed as a compliment, not a countdown to doom. A run ending
because the duck missed a step is a fundamentally different feeling than
a run ending because something caught it, and this GDD commits to the
former.

## Detailed Design

### Core Rules

1. **Movement is touch/drag only — no keyboard.** The prototype's
   keyboard path (arrow/AD keys, velocity-based) doesn't carry over;
   `technical-preferences.md` is explicit (Touch primary, no gamepad,
   "no hover-only interactions"). The duck's horizontal position snaps
   directly to the touch/drag X each input sample, clamped to
   `[0, 1-duckWidth]` in the sibling GDD's normalized coordinate space —
   direct positioning, not velocity-based movement.

2. **Collision detection runs on sim-tick-authoritative duck position,
   inside `SharedSimCore` — never on render-interpolated position.**
   Checking against a smoothed render position (rather than the last
   received input sample at the fixed sim tick) would silently desync
   client/server replay even though the sibling GDD already made spawn
   timing and obstacle position deterministic — the same class of risk
   `SharedSimCore` exists to close for Ricochet's physics. **[Corrected
   2026-07-17, then resolved the same day]** A prior version of this rule
   additionally asserted "AABB collision itself is simple," treating the
   overlap-test math as a non-issue — the same discrete-branch-fork risk
   ADR-0002 was built to solve for Ricochet's ball-vs-brick collision
   (float comparisons can't be trusted to agree on a hit/miss branch
   across ARM and x86). **Now resolved** by
   `adr-0013-runner-deterministic-collision.md`: duck/obstacle position
   and dimensions live in `Fix32` (ADR-0002's type) inside
   `SharedSimCore`, and the overlap test is an exact `Int32` comparison —
   see that ADR and Open Question 6 below. That ADR's own independent
   review also confirmed a separate, real exploit: `obstacleSpeed(t)`'s
   unbounded growth (sibling GDD) eventually produces a per-frame
   displacement large enough to skip past the duck undetected —
   mitigated at the engineering layer there, but the actual fix (capping
   the formula) is the sibling GDD's own required action.

3. **Coin collision**: +25 score, +1 `coinsCollected`, particle burst,
   obstacle removed, no damage.

4. **Non-coin collision** (bomb/bird/cloud, per the sibling GDD's Core
   Rule 7 — all three deal damage): health −1 (starts at 1), screen
   shake, particle burst. If health reaches 0, the run ends.
   **[Corrected 2026-07-17]** This citation previously pointed to "Core
   Rule 6," which was correct before the sibling GDD's own 2026-07-17
   revision inserted a new Core Rule 6 (the collision-determinism risk
   flagged above), shifting the damage rule down to Core Rule 7 — a
   citation-drift bug caused directly by that renumbering.

5. **Successful dodge** (a non-coin obstacle passes fully off-screen
   without hitting the duck): score += `10 + floor(t/5)`. A missed coin
   (goes off-screen uncollected) scores nothing — only removed.

6. **Score has two components that must stay separate, not two views of
   one number**: the `coinsCollected`-derived value (which becomes a
   real currency credit) and the dodge-bonus value (leaderboard-only,
   never currency). Conflating them would let a player's high-survival
   dodge bonus silently inflate their wallet — never converted.

7. **Coin credit routes through Currency System's existing
   `creditMultiplied` leg** (ADR-0004), sharing Ricochet's Coin Value
   upgrade — not a separate Runner-specific progression track. Coin
   Value is architecturally generic (it multiplies whatever passes
   through `creditMultiplied`); its current Ricochet-only *scoping* in
   the Shop was a gap, not a boundary to preserve. Only `coinsCollected`
   (not the dodge bonus, not raw `score`) is the amount credited.

8. **Every run submission routes through Anti-Cheat/Replay
   Verification**, exactly like Super Ricochet — never a client-trusted
   credit. This is now load-bearing (not just good practice) since Rule
   7 means a Runner run directly mints real currency.

9. **Gems: coins-only for MVP, not resolved as a milestone-gem system
   here.** Runner has no natural "hard win" moment to gate a gem reward
   on (unlike Ricochet's boss-kill) — inventing one now would be
   unscoped design work this GDD doesn't need to do to close the routed
   question. Flagged as a genuine future consideration in Open
   Questions, not silently dropped.

### States and Transitions

| State | Trigger | Result |
|---|---|---|
| Ready | Screen loads | Duck centered at spawn X, health=1, score=0, sim clock not yet ticking |
| Ready → Playing | First touch/drag input sample received | Sim clock and the sibling GDD's spawn timer begin |
| Playing → Paused | App-backgrounding (`OnApplicationPause(true)`) | `SharedSimCore` tick advancement freezes; per `runner-hud.md`'s Core Rule 7, freezing the tick inherently freezes `obstacleSpeed(t)`/`spawnInterval(t)` too, since both are pure functions of sim-tick `t` — closing the "pause can't freeze the ramp" anti-cheat concern by construction |
| Paused → Playing | App-foregrounding (`OnApplicationPause(false)`) within 120s | Sim clock resumes exactly where it froze |
| Paused → GameOver | 120s backgrounded cap exceeded (`runner-hud.md` Tuning Knobs) | Run forfeited, uncredited — a resource-hygiene bound, not an anti-cheat necessity |
| Playing → GameOver | Health reaches 0 (any non-coin collision) | Run clock stops, run-result payload assembled and submitted to Anti-Cheat |

**[Updated 2026-07-12]**: app-backgrounding's Playing→Paused branch,
previously flagged here as an open question with no trigger to design
against, is now resolved in `runner-hud.md`'s own Core Rule 7 (and
mirrored in this table) — that GDD is the canonical source for the
mechanism; this table exists for cross-doc consistency, not as a second
definition.

### Interactions with Other Systems

- **Obstacle Spawn/Difficulty Ramp (Runner)**: hard dependency —
  consumes `obstacleSpeed(t)`, `spawnInterval(t)`, and both seeded RNG
  rolls entirely; this GDD never redefines them.
- **`SharedSimCore`**: collision resolution must live here for the same
  determinism reasons ball physics does — sim-tick-authoritative duck
  position, not a render-layer concern.
- **Currency System**: `coinsCollected` credits via the existing
  `creditMultiplied` leg (Rule 7) — no new currency mechanism, no
  separate Runner progression track.
- **Anti-Cheat/Replay Verification**: every run submission is
  server-verified before any credit — a hard dependency now that real
  currency is at stake, not optional hardening.
- **Leaderboard** *(designed 2026-07-12, see `leaderboard.md`)*: consumes
  `runnerLeaderboardScore` as-is, never recomputing it (that GDD's own
  Core Rule 2), parallel to Ricochet's `ricochetBestScore` (formalized in
  `super-ricochet.md`) — the two score formulas are structurally
  incomparable (bounded board-clearing efficiency vs. unbounded
  survival-time scaling), so a naive unified/summed leaderboard would let
  one game's number dominate rank without meaning "better play" in any
  common unit; `leaderboard.md` formalizes this as separate, per-game
  boards rather than a unified one. A normalized cross-game composite
  remains a real, separately-scoped future feature, not required to give
  Runner leaderboard presence now.

## Formulas

**`coinCredit`** (feeds Currency System's existing `creditMultiplied`
leg — not a new formula, this GDD's contribution is stating the
mapping):

| Variable | Type | Range | Description |
|---|---|---|---|
| coinsCollected | int | 0–∞ | Server-verified count of coins collected this run |

`creditMultiplied` amount = `coinsCollected` (1:1 base), then Currency
System's own existing multiplier applies (`× (1 +
coinValueUpgradeLevel)`, up to 5×, per the registry's `coin_credit`
entry) — this GDD does not define a new multiplier, it supplies the
base amount.

**`dodgeBonus(t)`**: `dodgeBonus(t) = 10 + floor(t / 5)`

| Variable | Type | Range | Description |
|---|---|---|---|
| t | int (sim ticks→seconds) | 0–∞ | Elapsed sim-time at the moment this specific obstacle passed off-screen |

Output Range: 10 (t<5) growing +1 per 5 seconds elapsed, unbounded.
Carried over exactly from the prototype's proven formula. Awarded once
per successfully dodged non-coin obstacle, summed across the run for
the leaderboard-facing total.

**`runnerLeaderboardScore`**: `runnerLeaderboardScore = coinsCollected ×
25 + Σ dodgeBonus(t_i)` over every successfully dodged obstacle `i` in
the run.

| Variable | Type | Range | Description |
|---|---|---|---|
| coinsCollected | int | 0–∞ | Total coins collected |
| t_i | int | 0–∞ | Sim-time of each individual successful dodge |

This is the leaderboard-facing number (Rule 6) — distinct from the
currency-facing `coinsCollected` value above, which never includes the
dodge-bonus term.

**Implementation note, carried over from the sibling GDD's own fix**:
the prototype's `getRunResult()` returns raw, unfloored `gameTime` (a
float). This GDD's run-result payload sends that raw float as-is —
**flooring/clamping happens server-side**, using the sibling GDD's
already-established `t = max(0, floor(t))` Edge Case guard, never
trusting a client-pre-floored value.

## Edge Cases

- **If a dodge-bonus obstacle exits off-screen and a separate lethal
  obstacle hits the duck in the same sim tick**: the dodge bonus
  registers before the run ends, ordered by ascending spawn-sequence ID
  — extending the sibling GDD's coin-before-death precedent to
  dodge-bonus-before-death. Same-tick death from one spawn event never
  voids score legitimately earned from a different spawn event that
  tick.
- **If two separate non-coin obstacles both intersect the duck in the
  same tick**: only the lower spawn-ID collision is evaluated — health
  is already 0 and the run already ending before the second is checked.
  No double-damage, no double-shake. The fatal collision itself grants
  no score by construction (Rule 4), so nothing from it needs
  registering.
- **[NEW 2026-07-17] If health reaches 0 and app-backgrounding are
  signaled in the same frame**: GameOver wins, not a true race —
  `OnApplicationPause` is delivered between frames, never mid-frame, so
  the frame's game logic (health→0, Playing→GameOver) fully resolves
  first. By the time backgrounding is handled, there is no active run
  left to pause — the Paused branch (States and Transitions) is moot and
  the app backgrounds normally from the run-result hand-off. This is the
  same ordering `runner-hud.md`'s own Edge Cases/Acceptance Criteria
  already state from the HUD's perspective; restated here since this GDD
  is the one that actually owns the Playing→GameOver/Playing→Paused
  transitions themselves, and previously had no matching Edge Case of
  its own.
- **Client-displayed coin moment vs. server-verified credit — ADR-0010's
  existing pattern applies directly, not a new mechanism.** The in-play
  particle burst and live `coinsCollected` counter are display-only,
  never a credit promise, identical to ADR-0010's "display-only, never
  authoritative for rewards" rule for other systems. If the
  server-verified count (post Tier-1/Tier-2) comes back lower than what
  the player watched accumulate live, the run-result screen shows the
  server-verified count as final with a visible reconciliation beat —
  never a silent redisplay of the client's live tally, and never a
  wallet total that contradicts what was shown seconds earlier without
  explanation.
- **Zero-duration runs are structurally impossible, confirmed not left
  implicit**: Ready never spawns obstacles, and the spawn timer only
  starts at Ready→Playing, so the earliest any obstacle can exist is
  `spawnInterval(0)` (1.5s) after first input, plus travel time to the
  duck's row. No separate grace-period mechanism is needed — stated
  explicitly as a confirmed property, not an unexamined assumption.
- **Health regeneration is an explicit non-goal for this GDD.** Health
  is fixed at exactly 1 for a run's full duration — no regen, heal, or
  extra-life mechanic exists or is implied. A future power-up proposing
  one requires a GDD revision, not a silent extension of Rule 4.
- **Coordinate-space unit reconciliation** (a genuine cross-GDD gap,
  resolved here since Runner is the actual consumer): the sibling GDD's
  `obstacleSpeed(t) = 250 + floor(t/5)×50` was stated in raw
  pixels/second, carried directly from the prototype's 600px-tall
  canvas — but Core Rules 1 (normalized movement) and 2 (collision
  determinism) here require normalized 0–1 coordinates throughout.
  Resolution: `obstacleSpeed(t)`'s value is
  reinterpreted as **normalized playfield-heights per second**, dividing
  the sibling GDD's raw constant by the prototype's reference
  `CANVAS_HEIGHT` (600): `normalizedObstacleSpeed(t) = obstacleSpeed(t)
  / 600`. At t=0: 250/600 ≈ 0.4167 heights/s (a full-screen traversal in
  2.4s). This doesn't change the sibling GDD's time-based growth curve,
  only clarifies the units its consumer (this GDD) must use.
  **[Corrected 2026-07-17]** The **unit reconciliation itself** is
  resolved (dividing by 600 is the right normalization) — but the sibling
  GDD's Core Rule 6 separately flags that this repeated per-tick float
  division is exactly the kind of pattern that risks cross-platform
  divergence in a discrete branch decision (collision hit/miss), not
  because the division's *result* is wrong. This Edge Case's "Resolution"
  label describes the coordinate-space math being settled; it does not
  mean the underlying determinism question is closed — that's tracked as
  its own Open Question below, not resolved by this note.

## Dependencies

**Depends on (hard)**: Obstacle Spawn/Difficulty Ramp (Runner) —
designed, supplies all timing/spawn/RNG; `SharedSimCore` (position/tick
authority) and `Pcg32Rng` (ADR-0001, RNG only — **[Corrected 2026-07-17]**
ADR-0001 governs deterministic RNG, not collision determinism; collision
detection's own determinism has no governing ADR yet, see Open Questions);
Currency System — designed,
`creditMultiplied` leg for coin credit; Anti-Cheat/Replay Verification —
designed, gates every credit; Save/Persistence — designed, stores run
history/best score.

**Depended on by**: Leaderboard *(designed 2026-07-12, see
`leaderboard.md` — consumes `runnerLeaderboardScore` as-is, never
recomputing it, per that GDD's Core Rule 2)*; Runner HUD *(designed
2026-07-12, see `runner-hud.md`)* — consumes live
score/health/coinsCollected display state, same dirty-check pattern the
prototype already proved).

## Tuning Knobs

| Knob | Value | Too Low | Too High |
|---|---|---|---|
| Coin score value | 25 | Coins feel like an afterthought next to dodge bonuses | Collecting dominates the run, dodging (the actual skill test) feels secondary |
| Dodge bonus base | 10 | Early dodges feel worthless | Early game rewards survival over skill disproportionately |
| Dodge bonus growth | +1 per 5s elapsed | Late-game dodges don't feel like they matter more | Dodge bonus swamps coin value too early, undercutting Rule 6's intentional separation |

All three are carried over from the prototype's proven, shipped values —
not fresh guesses, matching the sibling GDD's own precedent.
`/balance-check` should confirm the coin-vs-dodge value ratio still
feels right once Coin Value's multiplier (up to 5×) is actually applied
to `coinsCollected`, since that multiplier didn't exist in the
prototype's own tuning context.

## Visual/Audio Requirements

**Death moment — the highest-stakes visual in this GDD.** Health starts
at 1 with no regen, so "non-coin collision" and "death" are the same
event — there's no separate wounded state to stage. The carried-over
8-particle burst + 14px screen shake is Ricochet's *impact* vocabulary;
reused as-is it reads as "caught," contradicting Player Fantasy's
"missing a step, not being caught" guardrail. Decouple the physics
event from its presentation: keep the proven 8-particle burst
timing/count, but reskin the particles from a spark-style burst to a
soft feather-puff/dust-poof (warm, not electric); the duck's failure
pose is a stumble (tangled feet, pinwheeling arms), never a
flinch/recoil; audio is a character vocalization — a surprised "quack!"
plus a short, comedic descending slide — never an impact thud or synth
zap, deliberately distinct from Ricochet's tonal "lose" sawtooth so
Runner keeps its own character-based failure voice. **Screen shake
magnitude is flagged, not silently kept at the prototype's raw 14px** —
full-magnitude shake risks fighting "gentle disappointment"; attenuate
for this specific instant, or substitute a small camera "hop," pending
playtest confirmation.

**Dodge feedback is deliberately minimal, not celebrated.** Since a
successful dodge fires on every non-coin obstacle that passes
off-screen — near-constant at high difficulty (every 0.6–1.5s) — giving
each one particles/shake/SFX would violate the art bible's own
high-frequency-feedback discipline and quickly feel exhausting rather
than satisfying. Score-only feedback (a small floating "+N", HUD tick)
— no particles, no shake, no distinct sound per dodge.

**Coin collection** keeps the prototype's proven 6-particle burst as-is
(it's a genuine positive moment, not high-frequency in the same
punishing way) — spark/glint material, distinct from the death moment's
reskinned feather-puff.

**Obstacle read** (shape-first, per
`design/accessibility-requirements.md`'s never-color-alone rule): coin =
circular disc with idle glint/bob; bomb = round body + fuse-spike
silhouette, Brick Red paired with an icon, never red alone; bird =
wing/diamond silhouette with flap animation as a second motion cue;
cloud = see below.

**Production flag for `/asset-spec`**: cloud-as-lethal is the single
highest-risk asset in this GDD — it must NOT reuse whatever friendly
decorative cloud shapes exist in a boardwalk/sky backdrop elsewhere in
the game. It needs its own hazard treatment: jagged/spiky underside (not
soft puffs), a darker storm-gray tint, an embedded lightning/exclamation
icon for colorblind compliance. Recommend `/asset-spec` treat "hazard
cloud" and "decorative cloud" as two distinct asset entries, not
variants of one — conflating them risks a genuinely unfair, unreadable
hazard.

📌 **Asset Spec** — once this section lands, run `/asset-spec
system:quack-runner` after the art bible's already-approved status is
confirmed current, to produce per-asset visual specs and generation
prompts.

## UI Requirements

Full screen inventory: **Runner HUD** (new — live score, health
[binary: alive/dead, no meaningful bar at health=1], `coinsCollected`
count, elapsed time; dirty-checked whole-second granularity per the
prototype's proven pattern) and a **run-result screen** (server-verified
final score/coins, the reconciliation beat Edge Cases requires if the
verified count differs from the live tally). Detailed screen-by-screen
UX spec belongs in `/ux-design`, not this GDD — this section only
defines what data and states that spec needs to design against, matching
`ricochet-hud.md`'s own precedent.

📌 **UX Flag — Quack Runner**: this system has real UI requirements (a
new Runner HUD, not yet spec'd). In Pre-Production, run `/ux-design` for
it before writing epics/stories — stories referencing this UI should
cite the future `design/ux/runner-hud.md`, not this GDD directly. Should
reuse `hub-ui.md`/`ricochet-hud.md`'s existing currency-chip and
level-pill component styling per `interaction-patterns.md`'s established
reuse discipline, not invent new ones.

## Acceptance Criteria

- **GIVEN** the duck at rest, **WHEN** the player drags to X=0.7 of
  playfield width, **THEN** the duck's position snaps directly to that
  sample each input frame (no easing/velocity ramp), clamped so it never
  exceeds `[0, 1-duckWidth]`.
- **GIVEN** a duck position that differs between the last sim-tick sample
  and the current render-interpolated frame, **WHEN** collision is
  evaluated that tick, **THEN** resolution uses the sim-tick position
  only — replaying identical inputs client- and server-side produces
  identical collision outcomes.
- **GIVEN** a coin overlapping the duck's AABB, **WHEN** collision
  resolves, **THEN** score +25, coinsCollected +1, particle burst plays,
  obstacle removed, health unchanged.
- **GIVEN** health=1 and a bomb/bird/cloud (tested separately)
  overlapping the duck, **WHEN** collision resolves, **THEN** health→0,
  screen shake + particle burst play, run transitions to GameOver.
- **GIVEN** a non-coin obstacle exiting off-screen at elapsed time t
  without hitting the duck, **WHEN** it despawns, **THEN** score +=
  `10+floor(t/5)`. **GIVEN** a coin exiting uncollected, **THEN** score
  is unchanged.
- **GIVEN** a run with coins collected and dodges performed, **WHEN** the
  run-result payload assembles, **THEN** coinsCollected-value and
  dodge-bonus-value are two distinct fields, and only the former is sent
  to Currency System.
- **GIVEN** coinsCollected=10 and dodge-bonus total=40, **WHEN**
  credited, **THEN** exactly 10 (not 50) is submitted to
  `creditMultiplied`.
- **GIVEN** a run-result payload, **WHEN** submitted, **THEN** no wallet
  credit occurs until Anti-Cheat Tier-1/Tier-2 verification completes.
- **GIVEN** a completed run, **WHEN** the result screen renders, **THEN**
  no gem reward or gem UI appears anywhere.
- **GIVEN** coinsCollected=10, Coin Value level=0, **WHEN** credited,
  **THEN** wallet +10. **GIVEN** the same 10 coins at level=4 (5×),
  **THEN** wallet +50.
- **GIVEN** t=3, **THEN** `dodgeBonus`=10. **GIVEN** t=17, **THEN**
  bonus=13, evaluated per-dodge-event.
- **GIVEN** coinsCollected=5 and dodges at t=3,17 (bonuses 10+13),
  **WHEN** `runnerLeaderboardScore` is computed, **THEN** score =
  5×25+23 = 148, and this figure is never used for currency credit.
- **GIVEN** a dodge (spawn ID 5) and a lethal hit (spawn ID 6) same
  tick, **WHEN** resolved, **THEN** the dodge bonus is added before
  GameOver triggers.
- **GIVEN** two non-coin obstacles (IDs 7, 8) both overlapping the same
  tick, **WHEN** resolved, **THEN** only ID 7 registers (single
  damage/shake); ID 8 is not separately processed.
- **GIVEN** a client live tally of 12 coins, **WHEN** the server-verified
  count returns 10, **THEN** the result screen shows 10 with a visible
  reconciliation indicator — never a silent 12.
- **GIVEN** Ready state with no input yet, **THEN** no obstacle exists;
  the earliest possible obstacle presence is `spawnInterval(0)`=1.5s
  post-first-input plus travel time — no run can end before this.
- **GIVEN** a run in progress, **WHEN** any time elapses without a
  non-coin collision, **THEN** health stays exactly 1 (never increases).
- **GIVEN** `obstacleSpeed(t)` in raw px/s, **WHEN** used for
  normalized-coordinate collision, **THEN** the value used is
  `obstacleSpeed(t)/600`; at t=0 a full traversal takes 2.4s.
- **GIVEN** a player with Coin Value level 2 (×3) completes a run with 8
  coins / 0 dodges, **WHEN** the run passes Anti-Cheat, **THEN**
  `creditMultiplied` receives base 8, applies ×3, and the wallet balance
  increases by exactly 24 — verifiable via a before/after balance diff
  alone.
- **[NEW 2026-07-17] GIVEN** a run is Paused (backgrounded) for longer
  than 120s without foregrounding, **WHEN** the cap is exceeded, **THEN**
  the run transitions to GameOver as forfeited, and no `coinsCollected`
  or dodge-bonus value is submitted to Currency System or the
  leaderboard — previously named in the States/Transitions table with no
  corresponding test.
- **[NEW 2026-07-17] GIVEN** a run is Paused at sim-tick N, **WHEN** the
  app is foregrounded again within the 120s window, **THEN** the sim
  clock resumes exactly at tick N (never restarting from 0, never
  drifting to account for real elapsed background time) — the resume
  half of Rule 2's determinism guarantee, previously only tested for the
  freeze side.
- **[NEW 2026-07-17] GIVEN** health reaches 0 in the same frame
  app-backgrounding is signaled, **WHEN** the frame resolves, **THEN**
  the run transitions to GameOver, never Paused — `OnApplicationPause`
  is delivered between frames, so the frame's health→0 logic always
  resolves first.
- **[NEW 2026-07-17] GIVEN** the Ready state with no input ever received,
  **WHEN** any amount of real time elapses, **THEN** the run remains in
  Ready indefinitely — no obstacles spawn, no timeout forces a
  transition, and no resource-hygiene cap applies (unlike Paused's
  explicit 120s cap), since nothing is running yet to bound.

## Open Questions

1. **[RESOLVED 2026-07-12, see `runner-hud.md` Core Rule 7]**
   App-backgrounding: Playing gets a Paused branch, triggered solely by
   `OnApplicationPause`, freezing `SharedSimCore`'s sim-tick advancement
   — which inherently freezes the difficulty ramp too, since
   `obstacleSpeed(t)`/`spawnInterval(t)` are pure functions of sim-tick
   `t`, closing the anti-cheat concern by construction. 120s max
   backgrounded before auto-forfeit (resource hygiene, not anti-cheat).
2. **[RESOLVED 2026-07-12, see `runner-hud.md` Core Rule 8]**
   Death-moment screen-shake magnitude: 5px default (down from the
   prototype's raw 14px), flagged as an unplaytested Tuning Knob
   starting point pending `/balance-check` validation, not a final
   locked value.
3. **One-handed reachability of direct-drag movement.** Core Rule 1
   commits to direct touch-position movement (no keyboard). Direct-drag
   can be awkward one-handed on a tall phone — whether a fallback (e.g.
   tap-left/tap-right zones) is wanted is a UI Requirements/`/ux-design`
   decision, not a Core Rule, flagged so it isn't assumed solved.
4. **Gems for Runner (deferred, not dropped).** Core Rule 9 keeps Runner
   coins-only for MVP because it has no natural "hard win" moment to gate
   a gem reward on. Whether a later survival-time-threshold gem trickle
   is added is a real economy-design question for a future pass — named
   here so the "elevated to a real pillar" claim is honestly partial
   (Runner earns coins like Ricochet, but not gems, yet).
5. **This is the GDD that must actually honor the sibling GDD's binding
   readability condition — not just the coin-vs-dodge ratio.** The
   sibling GDD's own CD-GDD-ALIGN condition #2 required confirming
   `obstacleSpeed(t)`'s unbounded growth resolves as a *fair, readable*
   death, not an unreadable spike, before any GDD locks a design against
   that curve — this GDD is that lock. Concretely: at t≈120s, the
   normalized traversal time (≈0.4s at that speed) drops *below*
   `spawnInterval`'s 0.6s floor, meaning an obstacle can fully cross the
   playfield faster than the next one spawns — exactly the "unreadable
   spike" the sibling GDD named as a failure mode, not yet resolved
   here. `/balance-check` must validate readability at realistic
   survival times, not just the coin-vs-dodge value ratio (a separate,
   Runner-original tuning question, still also open).
6. **[RESOLVED 2026-07-17, see `adr-0013-runner-deterministic-collision.md`]**
   Collision-detection determinism (the AABB overlap test itself, not
   just the position it's tested against) now has its own ADR, extending
   ADR-0002's `Fix32` type to Runner's collision (comparison-only, no
   sub-stepping needed — Runner has no tunnelling risk at ordinary
   `t`). That ADR's own independent review also found and flagged a
   **separate, confirmed exploit**: `obstacleSpeed(t)`'s unbounded growth
   (Obstacle Spawn/Difficulty Ramp's own formula) eventually produces a
   per-frame displacement large enough to skip past the duck undetected —
   replayable, hence a real currency-farming exploit at extreme survival
   times, not just a readability concern. ADR-0013 adds an engineering
   safety net (clamp + degraded flag) but the actual fix (capping
   `obstacleSpeed(t)`) is routed to the sibling GDD's own Open Questions.
7. **Coin cap removal + coin-sink pressure, stated explicitly, not
   silently implied.** The prototype's Runner is hard-capped at 100
   coins/run; this GDD removes that cap entirely — the only per-run
   ceiling left is Anti-Cheat's `maxPlausibleScore(t)` plausibility
   bound, not a game-design cap. Combined with sharing Ricochet's
   Coin Value multiplier, Runner becomes a *second* uncapped,
   ×-multiplied coin faucet. `game-concept.md`'s own still-open
   coin-sink gap (once permanent upgrades are maxed, coins have nowhere
   left to go) gets measurably worse with two faucets instead of one —
   `/balance-check` should validate aggregate coin inflow across both
   mini-games against that gap, not just Runner's own internal ratios.
