# Obstacle Spawn/Difficulty Ramp (Runner)

> **Status**: Designed
> **Author**: Abdulrahman Alenazan + Claude
> **Last Updated**: 2026-07-12
> **Implements Pillar**: Every mini-game is a real pillar, not a side loop
> **Creative Director Review (CD-GDD-ALIGN)**: APPROVED WITH CONDITIONS
> 2026-07-12 (2 conditions, both folded into Open Questions #1 and #3 as
> binding commitments/gates rather than soft follow-ups — Pillar 4's
> "elevated" claim stays unearned until Quack Runner's own future GDD
> delivers progression/currency/leaderboard parity; native-port feel of
> the unbounded speed curve is a required gate before that future GDD
> locks its design)
> **`/design-review` (2026-07-17)**: NEEDS REVISION — 4 blocking items
> (Core Rule 4 stated the per-type bucket **sizes** — 1800/2400/2600/3200 —
> as if they were the **cumulative** RNG thresholds; read literally, this
> produces an 18%/6%/2%/6% obstacle-type distribution with 68% of rolls
> unmapped, not the intended 18/24/26/32% mix — corrected cumulative
> thresholds are 1800/4200/6800/10000; no ADR — or GDD text — establishes
> that Runner's duck-vs-obstacle collision detection is
> fixed-point/deterministic, the same class of discrete-branch-fork risk
> ADR-0002 was built to close for Ricochet's ball-vs-brick collision, and
> `quack-runner.md`'s own coordinate-space edge case independently
> introduces exactly the repeated-float-division pattern that risk
> concerns; the `+1` rounding safety margin's false-positive-prevention
> claim was asserted but never demonstrated by an AC; the RNG thresholds
> had no boundary-exact ACs, only a statistical test three orders of
> magnitude too coarse to catch an off-by-one) plus 2 recommended and 2
> nice-to-have items. All folded in below; re-review pending.

## Overview

Obstacle Spawn/Difficulty Ramp (Runner) is the server-owned, elapsed-
time-driven configuration for Quack Runner — a pure function of session
duration that returns the current obstacle spawn interval, obstacle-type
mix, and duck movement speed multiplier. Every formula is carried over
from the prototype's proven `runner.ts`, unchanged: obstacle mix 18%
coin / 24% bomb / 26% bird / 32% cloud, speed ramp of +50 every 5
seconds, spawn interval tightening from 1.5s down to a 0.6s floor. It
exists as its own system, separate from Quack Runner's own future
gameplay GDD, for the same reason Level/Difficulty Config (Ricochet)
does: Anti-Cheat's Tier-1 plausibility clamps for Runner submissions must
derive from the *same* numbers gameplay uses, not a separately-maintained
duplicate that could quietly drift.

## Player Fantasy

Direct engagement — this *is* the ramp, the felt sense that "the game is
responding to how well you're doing." The escalating speed and
tightening spawn interval aren't an external threat closing in on the
duck; they're the game raising the bar because the player is clearing
it, one five-second threshold at a time. Each speed bump reads as an
implicit compliment ("you're doing great, here's more"), not a countdown
to doom — deliberately distinct from Super Ricochet's own
escalating-pressure fantasy (precision + chaos via aim-and-fire volleys,
a controlled buildup) so the two mini-games don't deliver the same
tension through two different mechanics.

The pacing language leans into tempo and rhythm ("dancing to a song that
keeps speeding up") for tone and audio character, without requiring
literal beat-synced spawn logic — that's a nice-to-have for Visual/Audio
Requirements to flag, not a mechanical commitment this GDD makes.

One-hit health stays deliberate and unapologetic (`game-concept.md`'s own
decision, restated here): the run ending in a single mistake is what
makes each five-second threshold feel earned, and what keeps a Runner
session honestly short and high-tension rather than diluting Super
Ricochet's more forgiving multi-turn loss into a second version of the
same thing.

## Detailed Design

### Core Rules

1. **Deterministic replay is REQUIRED, not a new decision** —
   `anti-cheat-replay-verification.md`'s own binding constraint (line
   118: "every mini-game engine... must be built deterministic — fixed
   timestep, seeded RNG") already names Quack Runner explicitly, alongside
   its own Tier-1 clamp placeholder (`maxScore = 50 + time×40`) deferred
   to this GDD. This system fulfills that pre-existing commitment; it
   isn't inventing a new architecture requirement.

2. **Fixed simulation timestep**, decoupled from render — matching
   ADR-0002's approach for Ricochet, all time-based values (the
   5-second speed threshold, the 0.6s spawn floor) are counted in
   integer sim ticks at a fixed rate, never wall-clock float seconds, so
   a client stutter can't desync a server replay.

3. **Speed and spawn interval are pure functions of elapsed sim-time**,
   recomputed fresh each tick — not incremental mutation. Carried over
   exactly from the prototype's proven formulas (confirmed from
   `runner.ts` source, not paraphrased):
   - `obstacleSpeed(t) = 250 + floor(t / 5) × 50` (stepped every 5
     seconds, unbounded)
   - `spawnInterval(t) = max(0.6, 1.5 - t / 30)` (smooth linear decay to
     a 0.6s floor, reached at t=27s)

4. **Two seeded RNG rolls per spawn event, both via `Pcg32Rng`**
   (ADR-0001), never `UnityEngine.Random`/`System.Random`, **in a fixed,
   pinned order — type roll first, then position roll.** This order must
   be specified explicitly, not left to implementation convenience:
   since both rolls draw from the same shared RNG stream, a client and
   server that each independently (and validly) chose a different
   ordering would silently desync Tier-2 replay for *every* run, not
   just malformed ones — Core Rule 1's determinism guarantee only holds
   if both sides consume the stream identically.
   - **Obstacle type** (rolled first): one `NextUInt32()` mapped via
     integer **cumulative** thresholds out of 10000. **[Corrected
     2026-07-17]** The per-type bucket *sizes* are 1800/2400/2600/3200
     (matching 18%/24%/26%/32%, and correctly summing to 10000) — but the
     **cumulative** comparison thresholds an implementation must actually
     use are the running sum of those sizes: **1800 / 4200 / 6800 /
     10000** (coin / bomb / bird / cloud upper bounds). The prior wording
     stated the bucket sizes themselves as if they were the cumulative
     thresholds — read literally (`roll < 1800` → coin, `roll < 2400` →
     bomb, `roll < 2600` → bird, `roll < 3200` → cloud), this would produce
     an 18%/6%/2%/6% distribution with 68% of rolls falling through
     unmapped, not the intended 18/24/26/32% mix. Not a float comparison
     either way, avoiding ADR-0001's flagged float-conversion drift risk.
   - **Lateral spawn position** (rolled second): one additional roll for
     horizontal placement — confirmed present in the prototype (`x =
     Math.random() × (width - obstacleWidth)`), previously unverified,
     now folded into the same deterministic treatment as the type roll.

5. **Coordinates are normalized (0–1 playfield width), not fixed
   pixels.** The prototype uses a hardcoded 400×600 canvas; the native
   port follows Super Ricochet's precedent (`cell_size = canvas_width /
   7`-style normalization) so obstacle size, duck width, and movement
   speed stay consistent across real device screen sizes rather than
   being tuned to one fixed resolution. **[Corrected 2026-07-17]**
   Obstacle *descent* speed (`obstacleSpeed(t)`) is normalized against
   playfield **height**, not width — descent is vertical motion, and
   `quack-runner.md`'s own Edge Cases already silently made this
   correction (dividing by 600, the prototype's canvas height, not 400,
   its width) without this GDD — the system of record for the formula —
   ever being amended to say so. Lateral spawn position (Rule 4) remains
   width-normalized, since that placement is horizontal.
6. **[RESOLVED 2026-07-17, see `adr-0013-runner-deterministic-collision.md`]**
   Duck-vs-obstacle collision detection determinism now has its own ADR,
   extending ADR-0002's `Fix32` type into `SharedSimCore` for Runner: an
   exact `Int32` comparison, no cross-platform branch-fork risk, no
   sub-stepping needed (Runner's geometry/speed profile has no
   tunnelling risk at ordinary `t` — see Core Rule 7 below for the
   exception). `quack-runner.md`'s coordinate-space conversion
   (`obstacleSpeed(t)/600`) is likewise resolved: a single fixed-point
   `Fix32.FromRatio` conversion, recomputed fresh every frame, never a
   repeated float divide.
7. **[NEW 2026-07-17, confirmed structural — not speculative — during
   ADR-0013's own independent review] `obstacleSpeed(t)`'s unbounded
   growth must be capped.** This formula (`250 + floor(t/5)×50`, Rule 3)
   has no ceiling, unlike every other capped formula in this GDD family
   (Ricochet's `boss_hp`/`max_brick_hp`, both capped earlier this
   session after the identical class of proof). Direct calculation
   during ADR-0013's review confirmed — not merely suspected — that
   per-frame obstacle displacement (`obstacleSpeed(t) × frame_dt`)
   eventually exceeds obstacle+duck height at some reachable `t`,
   letting an obstacle skip past the duck's row **without the AABBs
   ever overlapping in either frame**. Because Runner's collision is
   now fully deterministic (Rule 6) and Tier-2-replayable, this
   skip-through is **indistinguishable from a legitimate dodge** to the
   verifier — a deliberate long-survival strategy could reach this `t`
   and then survive indefinitely, farming coins on a currency-minting
   system (`quack-runner.md` Rule 7). ADR-0013 adds an engineering
   safety net (clamping the displacement for collision-testing purposes
   and flagging `mode=degraded` past a configured maximum) so the
   simulation never silently breaks — but that net only bounds the
   *symptom*. **The actual fix — capping `obstacleSpeed(t)`, mirroring
   the Ricochet precedent exactly — is this GDD's own required action,
   not resolved here or in ADR-0013.** No specific cap value is proposed
   in this entry: unlike Ricochet's boss_hp/max_brick_hp fix, this GDD
   has never specified concrete obstacle/duck dimensions to compute a
   cap point from — that data (plus a target maximum plausible survival
   time) is needed before a real number can be chosen, via
   `/balance-check` once it exists. Until this cap lands, the
   engineering safety net is the only thing preventing an actual
   skip-through — it is a stopgap, not a substitute for the design fix.

7. **All three non-coin obstacle types deal exactly 1 damage on
   collision** (bomb, bird, *and* cloud alike) — confirmed from source,
   not just bombs as a name-based assumption might suggest. Since
   health = 1, any single non-coin collision ends the run. Coins never
   deal damage; they're collected and removed. **[NEW 2026-07-17]** If
   **two** non-coin (lethal) obstacles both intersect the duck on the same
   sim tick, collision processing stops after the first resolves (by the
   same ascending-spawn-ID ordering as the coin-before-death rule below) —
   the run ends once, never a double-processed death or a duplicated
   mismatch/analytics event from the second lethal hit.

8. **Tier-1 plausibility ceiling** (per
   `anti-cheat-replay-verification.md`'s explicit deferral to this GDD)
   derives from the same score formula the client uses, never a
   separately-maintained duplicate: `maxPlausibleScore(t) =
   maxCoinsPossible(t) × 25 + maxDodgeBonus(t)`, both terms bounded by
   the spawn-rate formula in Rule 3 — exact derivation in Formulas below.

### States and Transitions

None — like its Ricochet sibling, this is a stateless pure function
(input: elapsed sim-time; output: speed, spawn interval, and the two RNG
rolls at each spawn event), not a state machine of its own. The
run-level state machine (Ready/Playing/GameOver) belongs to Quack
Runner's own future GDD, which consumes this system's outputs.

### Interactions with Other Systems

- **Anti-Cheat/Replay Verification**: consumes this system's Rule 8
  formula as its Tier-1 plausibility ceiling for Runner submissions, and
  depends on Rules 1–4's determinism guarantees for Tier-2 replay — the
  same relationship Level/Difficulty Config (Ricochet) has with
  Anti-Cheat's Tier-1 clamps for Super Ricochet. **[Clarified 2026-07-17]**
  The ceiling applies specifically to `coinsCollected` — the one value
  that mints real currency (per `quack-runner.md` Rule 7) — not the
  HUD-only live "score" display or the leaderboard-only
  `runnerLeaderboardScore`, which are related but distinct quantities.
- **`SharedSimCore` / `Pcg32Rng`** (ADR-0001): this system is a
  *consumer* of the existing RNG implementation, not a new RNG algorithm
  — no new engine dependency introduced.
- **Quack Runner** (designed — `quack-runner.md`): consumes `obstacleSpeed(t)`,
  `spawnInterval(t)`, and both seeded RNG rolls to drive its own
  gameplay loop (duck movement, collision, scoring); this system does
  not own or simulate the duck itself.
- **Currency System**: no direct dependency — how `coinsCollected`/score
  convert into actual wallet rewards is Quack Runner's own job (now
  resolved in `quack-runner.md`), consistent with how Boss AI/Damage Model stays scoped to
  damage/defeat state and leaves reward conversion to Super Ricochet +
  Anti-Cheat.

## Formulas

**`obstacleSpeed(t)`**: `obstacleSpeed(t) = 250 + floor(t / 5) × 50`

| Variable | Type | Range | Description |
|---|---|---|---|
| t | int (sim ticks→seconds) | 0–∞ | Elapsed survival time this run |

Output Range: 250 (t=0) growing unbounded, +50 every 5 seconds. Example:
t=30s → 250 + 6×50 = 550.

**`spawnInterval(t)`**: `spawnInterval(t) = max(0.6, 1.5 - t / 30)`

| Variable | Type | Range | Description |
|---|---|---|---|
| t | int (sim ticks→seconds) | 0–∞ | Elapsed survival time this run |

Output Range: 1.5s (t=0) decaying linearly to a 0.6s floor, reached at
t=27s and held thereafter. Example: t=15s → max(0.6, 1.5-0.5) = 1.0s.

**`maxPlausibleScore(t)`** (Anti-Cheat's Tier-1 ceiling, per Core Rule 8):

| Variable | Type | Range | Description |
|---|---|---|---|
| t | int | 0–∞ | Elapsed survival time this run |
| N(t) | int | derived | Maximum possible spawn-event count in `t` seconds |

`N(t) = ceil(30 × ln(1.5 / (1.5 - t/30))) + 1` for t≤27; `ceil(30 ×
ln(2.5) + (t-27)/0.6) + 1` for t>27 — derived by integrating the spawn
rate `1/spawnInterval(τ)` over `[0, t]`, plus a +1 rounding safety
margin so Tier-1 never rejects a legitimate run over a discretization
fraction.

`maxPlausibleScore(t) = 25 × N(t)` for t<75 (the common case — assumes
every spawn was a coin, the best-case bound, since Tier-1 doesn't replay
the actual RNG sequence and must never false-positive-reject a
legitimately lucky run). For t≥75, dodge-bonus scoring
(`10+floor(t/5)`) can exceed the flat coin value, so the ceiling
switches to `25 × N(75) + Σ max(25, 10+floor(τ/5))` over the remaining
spawns — an edge case, not the common path, given health=1 makes very
long survival rare.

Output Range: unbounded, growing with `t`. Worked example: t=30s →
N(30) = ceil(30×ln(2.5) + 5.0) + 1 = 34 → `maxPlausibleScore(30) = 850`.
Any client-reported score above 850 at 30 seconds survived is
mathematically impossible even under perfect all-coin play and is Tier-1
rejected outright; Tier-2 separately verifies whether a *lower*,
plausible-looking reported score actually matches that run's true seeded
RNG sequence.

**Explicitly excluded from this formula, deferred to Tier-2**: obstacle
despawn-window timing (whether a dodge geometrically "completed" in
time) — including it in Tier-1 risks false-positive rejections of
legitimate plays, since it depends on exact spawn position/geometry that
only a full replay can verify.

**Implementation note — division type**: `t` is measured in integer
sim-ticks (Core Rule 2), but every formula above (`t / 5`, `t / 30`, the
`ln(...)` terms) requires **real (floating-point or fixed-point)
division**, never integer division. A naive C# implementation using an
`int t` with `/` performs integer division, which truncates `t / 30` to
0 for all `t < 30` — silently breaking `spawnInterval`'s entire decay
curve for the first 30 seconds of every run. `t` is int-*valued* but
must be real-*divided*.

## Edge Cases

- **If `t` arrives negative, non-integer, or malformed**: clamp to
  `t = max(0, floor(t))` before any formula runs — mirrors
  `level-difficulty-config-ricochet.md`'s own `Math.max(1,
  Math.floor(level))` guard. Unclamped, a negative `t` pushes
  `1.5 − t/30` above 1.5, making `ln(1.5/(>1.5))` negative and `N(t)`
  come out negative or zero — silently producing a ceiling below the
  true minimum and rejecting a legitimate score of 0 as implausible.
- **If `t` is reported as an extreme, adversarial value** (e.g. a
  claimed 10,000-second run): the log/linear math itself stays
  numerically bounded, but a naive server-side implementation of the
  t≥75 summation branch runs `N(t)` iterations — linear in the claimed
  `t`, meaning an absurd `t` becomes a per-request CPU cost concern, not
  just a scoring bug. Resolution: hard-cap accepted `t` at a real
  maximum session length before Tier-1 evaluation runs at all, and
  implement the t≥75 sum via its closed-form arithmetic-series
  equivalent rather than a per-spawn loop.
- **If a coin and a lethal (non-coin) obstacle both intersect the duck
  within the same sim tick** (from two separate spawn events, not the
  same one — Rule 4's type roll is mutually exclusive per spawn):
  resolution order is fixed as **coin-before-death** — the coin's +25
  registers, then the lethal collision ends the run. This must be
  deterministic by spawn-sequence ID (lower ID resolves first), not
  incidental array/iteration order, since Core Rule 1's determinism
  guarantee requires client and server to resolve same-tick collisions
  identically.
- **t=27 boundary (verified, not a gap)**: both `N(t)` branches evaluate
  to 29 at `t=27` exactly — confirmed continuous, no off-by-one at the
  ramp-to-floor transition. **[NICE-TO-HAVE 2026-07-17]** `spawnInterval`
  itself (a single `max(0.6, ...)` clamp, not a branched formula like
  `N(t)`) has lower discontinuity risk by construction, but double-precision
  floating point may not land bit-exact on `0.6` at the exact `t=27`
  crossing (`1.5 - 27/30`) — worth a tick-adjacent check (t=26 vs. 27 vs.
  28) at implementation time, lower priority than the blocking items above.

## Dependencies

**Depends on**: none. Unlike Level/Difficulty Config (Ricochet), which
reads a soft input (Extra Balls upgrade level), this system is a pure
function of elapsed time alone — `game-concept.md` names no equivalent
Runner-specific upgrade modifier, so there's no external input to
depend on.

**Depended on by**: Anti-Cheat/Replay Verification (consumes the Tier-1
`maxPlausibleScore(t)` ceiling per Core Rule 8, and depends on Core
Rules 1–4's determinism guarantees for Tier-2 replay); Quack Runner
(designed, `quack-runner.md` — consumes `obstacleSpeed(t)`, `spawnInterval(t)`, and both
seeded RNG rolls to drive its gameplay loop).

## Tuning Knobs

| Knob | Value | Too Low | Too High |
|---|---|---|---|
| Base spawn interval | 1.5s | Board feels overwhelming from second one | Opening feels dead/empty, no early tension |
| Spawn interval floor | 0.6s | Becomes unreadable/unfair at high speed | Ramp plateaus too early, late-game feels stale |
| Spawn interval decay rate | t/30 (reaches floor at t=27s) | Difficulty ramps up before a player can get their bearings | Runs feel too easy for too long, undercuts the "raising the bar" fantasy |
| Obstacle speed base / growth | 250 / +50 per 5s | Duck has too much reaction time, no tension | Obstacles become unreadable, feels unfair not skillful |
| Obstacle type mix | 18/24/26/32 (coin/bomb/bird/cloud) | Too many coins trivializes scoring; too few makes collection feel rare and frustrating | Too many hazards makes survival feel like pure luck, not skill |

All five are carried over from the prototype's proven, shipped values
(`systems-index.md`'s own "post-bugfix" note) — not fresh guesses.
Unlike Mascot Database's placeholder numbers, these already have real
play validation from the web prototype; `/balance-check` should confirm
they still feel right at native frame-rate and touch-input latency, not
re-derive them from scratch.

## Visual/Audio Requirements

N/A — pure config/data system, matching Level/Difficulty Config
(Ricochet)'s own precedent. Obstacle rendering, duck animation, and
audio feedback are Quack Runner's own future Visual/Audio requirements,
which consume this system's speed/interval/RNG outputs.

## UI Requirements

None directly — this system has no screen of its own. Its output
(current speed, spawn interval, next obstacle) feeds Quack Runner's
future gameplay HUD.

## Acceptance Criteria

- **GIVEN** identical seed and identical input sequence, **WHEN** client
  and server each simulate a run independently, **THEN** both produce
  byte-identical spawn sequences, damage events, and final score.
- **GIVEN** two runs with the same integer sim-tick count but different
  real-time frame pacing, **WHEN** `obstacleSpeed`/`spawnInterval` are
  computed, **THEN** outputs are identical — proving derivation from
  sim-ticks, not wall-clock float seconds.
- **GIVEN** t=30 computed twice independently with no prior-tick state
  passed in, **WHEN** `obstacleSpeed(30)` and `spawnInterval(30)` are
  evaluated, **THEN** both calls return identical results, confirming
  pure-function behavior.
- **GIVEN** a spawn event with an instrumented `Pcg32Rng`, **WHEN** the
  spawn resolves, **THEN** the first `NextUInt32()` call determines
  obstacle type and the second determines lateral position, in that
  fixed order, and no `UnityEngine.Random`/`System.Random` call occurs
  anywhere in the path.
- **GIVEN** two different playfield widths, **WHEN** an obstacle's
  horizontal spawn position is read, **THEN** it is expressed as a 0–1
  normalized fraction producing the same relative obstacle/duck size
  ratio on both.
- **GIVEN** duck health=1, **WHEN** it collides with a bomb, a bird, and
  a cloud (tested separately), **THEN** each deals exactly 1 damage and
  ends the run. **GIVEN** collision with a coin, **THEN** 0 damage, +25
  score, obstacle removed, run continues.
- **GIVEN** t=0, **THEN** `obstacleSpeed`=250. **GIVEN** t=4, **THEN**
  speed=250 (no early step). **GIVEN** t=30, **THEN** speed=550.
- **GIVEN** t=15, **THEN** `spawnInterval`=1.0s. **GIVEN** t=27, **THEN**
  interval=0.6s. **GIVEN** t=100, **THEN** interval remains 0.6s, never
  negative.
- **GIVEN** t=30, **THEN** N(30)=34 and the ceiling=850. **GIVEN** a
  submitted score of 851 at t=30, **WHEN** Tier-1 evaluates, **THEN** it
  is rejected. **GIVEN** a submitted score of 850, **THEN** it is
  accepted.
- **[NEW 2026-07-17] GIVEN** a simulated run using the actual discrete
  per-tick spawn schedule (not the continuous integral approximation),
  **WHEN** the true maximum achievable spawn count by a given `t` is
  compared against `N(t)` computed both with and without the `+1` margin,
  **THEN** the true count never exceeds the margined `N(t)` but *can*
  exceed the un-margined `ceil(30×ln(...))` value at some reachable `t` —
  constructing at least one concrete boundary case where a legitimately
  achieved spawn count would be false-rejected without the margin,
  demonstrating the `+1` does real work rather than only asserting that it
  does. (Exact `t`/count values depend on the discrete tick schedule, not
  yet worked out in this document — the property to test, and that it must
  be backed by a concrete constructed case, is specified here even though
  the number isn't.)
- **[NEW 2026-07-17, corrected] GIVEN** RNG rolls of exactly 1799, 1800,
  1801 (the coin/bomb boundary), 4199, 4200, 4201 (the bomb/bird boundary),
  and 6799, 6800, 6801 (the bird/cloud boundary) — using the corrected
  cumulative thresholds (1800/4200/6800/10000), not the previously-stated
  bucket sizes — **WHEN** each roll is mapped to obstacle type, **THEN**
  each resolves to the exact type its boundary specifies (stated explicitly
  as inclusive/exclusive per boundary: e.g. roll<1800→coin, roll=1800→bomb),
  catching both the corrected-vs-original threshold confusion and an
  ordinary off-by-one — either of which the existing ±2% statistical test
  (a 10,000-roll sample) is three orders of magnitude too coarse to detect.
- **GIVEN** t=90 (past the t≥75 threshold), **WHEN**
  `maxPlausibleScore(90)` is evaluated, **THEN** it uses the switched
  formula (`25×N(75) + Σ max(25, 10+floor(τ/5))` over the remaining
  spawns), not the flat `25×N(t)` common-case formula.
- **GIVEN** t=-5 and t=3.7 (tested separately), **WHEN** clamped, **THEN**
  t becomes 0 and 3 respectively, and a reported score of 0 is never
  rejected as implausible.
- **GIVEN** a claimed t=10,000s, **WHEN** Tier-1 evaluates, **THEN** t is
  hard-capped before evaluation runs, and the t≥75 branch executes via
  closed-form summation with evaluation time independent of the claimed
  t.
- **GIVEN** a coin (spawn ID=5) and a bomb (spawn ID=6) both intersecting
  the duck on the same sim tick, **WHEN** collisions resolve, **THEN**
  the coin's +25 registers before the bomb ends the run, ordered by
  ascending spawn ID.
- **[NEW 2026-07-17] GIVEN** a bomb (spawn ID=6) and a bird (spawn ID=7),
  both non-coin/lethal, intersect the duck on the same sim tick, **WHEN**
  collisions resolve, **THEN** the run ends exactly once (via the
  lower-spawn-ID bomb), with no double-processed death and no duplicate
  mismatch/analytics event from the bird's collision.
- **GIVEN** t=27 evaluated via both `N(t)` branches, **THEN** both
  return 29.
- **GIVEN** a submitted (t, score) pair, **WHEN** Anti-Cheat Tier-1
  evaluates it, **THEN** it calls this system's shared
  `maxPlausibleScore(t)` implementation directly, never a duplicated
  formula.
- **GIVEN** 10,000 simulated spawn-type rolls with a fixed seed, **WHEN**
  tallied, **THEN** the coin/bomb/bird/cloud distribution falls within
  ±2% of the 18/24/26/32 thresholds — verifying the cumulative-threshold
  mapping, not just its stated intent.
- **GIVEN** an obstacle spawn roll producing a horizontal position within
  one obstacle-width of the playfield edge, **WHEN** placed, **THEN**
  the full obstacle sprite remains within `[0, 1]` normalized bounds —
  never partially off-screen.

## Open Questions

1. **Two open questions `game-concept.md` explicitly deferred to
   "`/design-system runner`" belong to Quack Runner's own future GDD,
   not this one.** Whether Runner gets its own progression/currency
   interactions (vs. sharing the blaster's Coin Value upgrade), and
   whether Runner needs its own leaderboard or a unified cross-game
   score — both are genuinely important, but this system's Dependencies
   section already scopes currency conversion out (that's Quack Runner's
   job, matching how Boss AI/Damage Model stays scoped away from
   Currency). Routed explicitly, not silently dropped. **CD-GDD-ALIGN
   condition**: this is a binding commitment, not an optional follow-up —
   Pillar 4's "elevated to a real pillar" claim for Runner stays unearned
   until the future Quack Runner GDD actually delivers progression/
   currency/leaderboard parity, since this GDD alone (pure difficulty
   config) can't fulfill that pillar on its own.
2. **The t≥75 dodge-bonus branch's exact closed-form arithmetic isn't
   fully derived.** Edge Cases requires a closed-form summation (not a
   per-spawn loop) for performance, but the exact algebraic expansion of
   `Σ max(25, 10+floor(τ/5))` over a stepped-value sequence wasn't worked
   out here — a genuinely rare case (very long survival is unlikely
   given health=1), but needs derivation before implementation, not left
   as a runtime loop by default.
3. **Native-port feel validation, not re-derivation.** Unlike Mascot
   Database's placeholder numbers, every value here is the prototype's
   proven, shipped formula — but `/balance-check` should still confirm
   the ramp feels right at native frame-rate and touch-input latency (vs.
   the prototype's keyboard/mouse + `requestAnimationFrame`), since
   "proven" was proven in a different input/timing context. **CD-GDD-ALIGN
   condition**: formalize this as a required gate, not an optional
   check — specifically confirm `obstacleSpeed(t)`'s unbounded growth
   resolves as a fair, readable death rather than an unreadable spike,
   before Quack Runner's own gameplay GDD locks its design against this
   curve.
4. **[RESOLVED 2026-07-17, see `adr-0013-runner-deterministic-collision.md`]**
   Duck-vs-obstacle collision detection now has its own determinism ADR,
   extending ADR-0002's `Fix32` type — see Core Rule 6. Anti-Cheat's
   Tier-2 replay is trustworthy for Runner on the same footing ADR-0002
   already gives Super Ricochet.
5. **[NEW 2026-07-17, escalated during ADR-0013's own independent
   review] `obstacleSpeed(t)` needs an explicit cap — genuinely open,
   no value chosen yet.** See Core Rule 7: the formula's unbounded
   growth is now confirmed (not merely suspected) to eventually produce
   a deterministic, Tier-2-replayable skip-through once collision
   detection is exact (Question 4). This is a distinct, harder
   requirement than Question 3's "feels fair" balance check — a cap
   is needed regardless of how the curve feels, to close a real
   currency-farming exploit. Choosing the actual number requires
   obstacle/duck hitbox dimensions (not yet specified anywhere in this
   GDD) and a target maximum plausible survival time; both should come
   from `/balance-check` together with Question 3, since they're
   measuring related things at the same sitting.
