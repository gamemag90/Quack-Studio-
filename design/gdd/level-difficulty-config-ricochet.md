# Level/Difficulty Config (Ricochet)

> **Status**: Revised
> **Author**: Abdulrahman Alenazan + Claude
> **Last Updated**: 2026-07-09
> **Implements Pillar**: Server-authoritative economy (feeds Anti-Cheat's clamp ceilings)
> **Creative Director Review (CD-GDD-ALIGN)**: APPROVED 2026-07-09 (self-reviewed — no `creative-director` subagent registered in this environment)
> **`/design-review` (2026-07-17)**: NEEDS REVISION — 2 blocking design
> items (`max_brick_hp` proven via arithmetic to be the identical unbounded-
> growth bug class already fixed for `boss_hp`, previously left as an open
> question rather than fixed; `spawn_density`'s float arithmetic is an
> unacknowledged replay-determinism risk not covered by ADR-0001/ADR-0002)
> plus 3 blocking AC gaps and 2 recommended items. All folded in below;
> re-review pending.
> **Revision note (2026-07-09, superseded same day)**: `/review-all-gdds`
> found `boss_hp`'s unbounded linear growth diverges from the board
> substrate's hard caps (`initial_rows` caps at level 9, `spawn_density`
> caps at level 11), making late-game runs mathematically unwinnable
> regardless of skill. First fix: decelerate `boss_hp` growth from level 11.
> **Independent re-verification (2026-07-09) found deceleration alone still
> diverges unbounded**, just slower — mitigated, not resolved. **Final fix
> (2026-07-09)**: added a hard ceiling at level 30 (`boss_hp` caps at
> 11,100 permanently), bringing this formula in line with the same
> hard-cap philosophy `initial_rows`/`spawn_density` already use. This is a
> structural guarantee, not a tuning guess — see Core Rule 3 and the
> `boss_hp` formula below. Exact cap level/value remain placeholders for
> `/balance-check`, but the game can no longer become unwinnable purely from
> level scaling, regardless of what those exact numbers turn out to be.

## Overview

Level/Difficulty Config is the server-owned, per-level difficulty
configuration for Super Ricochet — a pure function of level number (plus
the player's Extra Balls upgrade) that returns the level's starting
parameters (boss HP, initial rows, brick toughness, spawn density, starting
balls). Every formula is carried over **exactly** from the prototype's
proven `getLevelConfig` in `gameRules.ts`, unchanged. It exists as its own
system (not embedded in Super Ricochet's own future GDD) because
Anti-Cheat/Replay Verification's Tier-1 plausibility clamps must derive from
the *same* numbers gameplay uses — a separately-maintained duplicate set of
difficulty numbers would let the two drift apart and quietly break
verification.

## Player Fantasy

Direct — this **is** the progression curve, the literal feeling of "the
game getting harder" as a player advances. A boss with more HP, a fuller
board, tougher bricks: the escalation a player feels level-to-level is this
system's formulas made visible.

## Detailed Design

### Core Rules

1. Level difficulty is a **pure, deterministic function** of level number
   `L` (plus the player's current Extra Balls upgrade level) — server-
   computed, never randomized session-to-session beyond what spawn density
   already governs within a level's board generation.
2. Formulas carried over **exactly** from the prototype (see Formulas
   below).
3. `initialRows`, `spawnDensity`, `bossHp`, **and now `maxBrickHp`** all cap
   out (level 9, 11, 30, and 30 respectively) — the board's *structural*
   difficulty, the boss's HP total, and brick toughness now all share the
   same hard-cap philosophy. **Revised decision (2026-07-09, supersedes the
   original `/map-systems` "endless scaling is fine" call)**: `bossHp`
   growth cannot stay unbounded forever, because `/review-all-gdds` proved
   that against a fully-capped board, any unbounded growth — even
   decelerated — eventually makes runs mathematically unwinnable *on the
   boss-HP axis*. `bossHp` now grows at the original rate through level 11
   (matching `spawnDensity`'s cap point), decelerates through level 30,
   then **hard-caps at 11,100 permanently** — see the `boss_hp` formula
   below.
   **[Corrected 2026-07-17]** `/design-review` found `maxBrickHp` was the
   structurally identical bug, left unfixed: `6 + (level-1)×4`, fully
   unbounded, while `initialRows`/`spawnDensity` both freeze the board's
   brick *count* by level 11 — meaning total board brick-HP scaled purely
   off this one uncapped term forever, diverging even faster (no
   deceleration phase at all) than `bossHp` ever did pre-fix, and driving
   the *other* loss condition (danger-line overflow from bricks clearing
   too slowly). This was previously left as Open Question 2 pending
   telemetry that doesn't exist — inconsistent with this GDD's own
   precedent, since `bossHp`'s identical divergence was caught and fixed by
   arithmetic alone, no telemetry required. `maxBrickHp` now uses the same
   three-segment shape as `bossHp` (full rate through level 11, decelerated
   through level 30, then a permanent hard cap of **84** from level 30
   onward) — see the `max_brick_hp` formula below. Both `bossHp` and
   `maxBrickHp` caps are now placeholders needing `/balance-check`
   validation for their exact numbers, not their *shape* — see Open
   Questions.
   `startingBalls` is unaffected by either cap and keeps climbing slowly
   forever per its own formula, which is a genuine (if narrow) easing for
   players who've already cleared levels 1–29 — but this is **not** a
   catch-up mechanic for a player stuck earlier, since it only helps players
   who already succeeded that far (see the "no catch-up" warning, still
   open, in the cross-review addendum).
4. Boss identity cycles through a fixed 6-name roster (`level % 6`) — named
   bosses repeat past level 6, only their HP differs. Covered by the same
   "endless scaling is fine for now" decision, not treated as unfinished
   content requiring immediate expansion.
5. This system's output feeds Anti-Cheat's future Super Ricochet-specific
   Tier-1 clamp ceilings **directly** — those ceilings derive from these
   same numbers, never a separately-maintained duplicate.

### States and Transitions

None — this is a stateless pure function (input: level + upgrade level;
output: config), not a state machine.

### Interactions with Other Systems

- **Anti-Cheat/Replay Verification**: consumes this system's output as the
  input to Super Ricochet's Tier-1 plausibility clamp ceilings. **[Flagged
  2026-07-17]** `super-ricochet.md` (now designed) does not yet define an
  actual Tier-1 clamp/ceiling formula anywhere in its own Formulas or
  Dependencies sections — the stated reason this system exists as a
  separate GDD ("so Anti-Cheat's clamps derive from the same numbers") is
  currently unfulfilled downstream. Tracked for resolution when
  `super-ricochet.md` is itself reviewed, not fixed here.
  **[Flagged 2026-07-17]** `spawn_density` (and the row-spawn/brick-HP-roll/
  coin-spawn decisions it and similar thresholds drive) uses floating-point
  comparison to produce a discrete branch outcome — the same category of
  problem ADR-0002 solved for ball-vs-brick collision, but ADR-0002's scope
  is explicitly limited to that collision math and does not cover these
  spawn-generation decisions. Since board state (row count, brick toughness)
  feeds directly into the danger-line loss condition Tier-2 replay must
  reproduce bit-exact, an unaddressed float-comparison branch here is a real
  determinism risk, not a hypothetical one — this needs an ADR-level
  decision (extending ADR-0002's fixed-point discipline to spawn-generation
  math, or an equivalent treatment) before Tier-2 replay verification can be
  trusted for Super Ricochet. Not resolved in this GDD; surfaced here so it
  isn't silently assumed away.
- **Currency System / Shop** (Shop not yet a GDD): `startingBalls` reads the
  player's Extra Balls upgrade level — this system reads that value, never
  owns or mutates it.
- **Super Ricochet** (`design/gdd/super-ricochet.md`, Designed): this system
  supplies the level's starting parameters that the mini-game's own
  engine/gameplay GDD consumes to initialize a run.
- **Boss AI/Damage Model** (`design/gdd/boss-ai-damage-model.md`, Revised):
  consumes `bossHp` and `bossName` from this system.

## Formulas

**`boss_hp`** [REVISED 2026-07-09, twice — first to decelerate at the
board's cap point (level 11), then to add a **hard ceiling** at level 30
after re-verification showed deceleration alone still diverges unbounded
against the board's fully-frozen capacity. See revision note at top of this
document]:

`boss_hp = 800 + min(level − 1, 10) × 650 + min(max(0, level − 11), 19) × 200`

| Variable | Type | Range | Description |
|---|---|---|---|
| level | int | 1–∞ | Level number, clamped to ≥1 (see Edge Cases) |

Output: 800 at level 1, growing at +650/level through level 11 (matching the
original rate, and matching where `spawnDensity` also caps), then +200/level
from level 12 through level 30, then **flat at 11,100 forever from level 30
onward**. Example: level 10 → 6,650 (unchanged from the pre-fix formula).
Level 20 → 9,100. Level 30 and beyond → 11,100, permanently. Independently
re-verified twice (arithmetic recomputed from scratch both times) — the
formula is internally consistent and does exactly what it claims.

**What this cap does and does not guarantee** *(corrected 2026-07-09 after a
second independent review found the original wording overclaimed; updated
2026-07-17 now that `max_brick_hp` below is also capped)*: capping
`boss_hp` guarantees the ratio between "damage needed" and "damage
deliverable per turn" **stops widening** past level 30 on the *boss-HP*
axis; capping `max_brick_hp` does the same for the *danger-line-overflow*
axis (board brick-HP stops growing once brick count is already frozen by
`initial_rows`/`spawn_density`). Neither cap by itself guarantees every
level past 30 is winnable — both plateau values (11,100 and 84) are
unvalidated placeholders that could turn out too high for what's actually
achievable, in which case a dead zone could exist even after both
divergences stop widening. Both axes require real
average-boss-damage-per-turn *and* danger-line-survival data from the Unity
port before either can be called truly resolved — see Open Questions.
`startingBalls` keeps growing slowly forever (+1 per 3 levels) independent
of this cap, which is a genuine win-more easing for players who already
clear levels 1–29 — **not** a catch-up mechanic for a player stuck earlier
(that warning remains open, see the cross-review addendum).

**`initial_rows`**: `initial_rows = min(4 + floor((level − 1) / 2), 8)`
Output: 4 to 8, caps at level 9.

**`max_brick_hp`** [REVISED 2026-07-17, mirroring `boss_hp`'s three-segment
shape after `/design-review` proved this formula was the same unbounded-
growth bug class, previously left unfixed]:

`max_brick_hp = 6 + min(level − 1, 10) × 4 + min(max(0, level − 11), 19) × 2`

Output: 6 at level 1, growing at +4/level through level 11 (unchanged from
the original rate — value 46 at level 11, matching the pre-fix formula
exactly, so there's no discontinuity at the transition point), then
+2/level (decelerated) from level 12 through level 30, then **flat at 84
forever from level 30 onward**. Example: level 10 → 6 + 9×4 = 42
(unchanged pre-cap). Level 20 → 6 + 10×4 + 9×2 = 64. Level 30 and beyond →
84, permanently. The cap's magnitude (6→84, a ~14× multiple) closely
matches `boss_hp`'s own base-to-cap multiple (800→11,100, ~13.9×) —
reassuring as a rough consistency check, not a substitute for real
`/balance-check` validation of the exact numbers, which remain
placeholders exactly as `boss_hp`'s were (see Open Questions).

**`spawn_density`**: `spawn_density = min(0.45 + (level − 1) × 0.03, 0.75)`
Output: 0.45 to 0.75, caps at level 11.

**`starting_balls`**: `starting_balls = 3 + floor((level − 1) / 3) + extra_balls_upgrade_level`
| Variable | Type | Range | Description |
|---|---|---|---|
| extra_balls_upgrade_level | int | 0–5 (Shop's cap) | Player's current Extra Balls upgrade tier, read from Save/Persistence |

Output: 3 at level 1 with no upgrade, growing slowly with level plus 0–5
from the upgrade.

**Boss name**: `boss_name = BOSS_ROSTER[(level − 1) mod 6]` — a fixed
6-entry roster, cycling.

## Edge Cases

- **If `level` is 0, negative, or non-integer** (malformed input): clamp to
  level 1's config — matches the prototype's existing `Math.max(1,
  Math.floor(level))` guard. Never returns a nonsensical negative-row
  config.
- **If `extra_balls_upgrade_level` arrives malformed** (shouldn't happen
  since Shop owns and validates that 0–5 range, but defensively): clamp to
  0 rather than let a negative or huge value produce an invalid
  `starting_balls` count.
- **If a request comes in for a level far beyond a player's actual unlocked
  progress** (e.g. a tampered client requesting level 500's config): this
  system does **not** gate access — it's a pure function, not an
  authorization boundary. Enforcing which levels a player may legitimately
  *play* is a future progress-tracking system's job, not this one's.

## Dependencies

- **Depends on** (soft — reads a value, doesn't require it to exist to
  return a default): Save/Persistence (Extra Balls upgrade level).
- **Depended on by**: Super Ricochet, Boss AI/Damage Model — both now
  designed *(stale "future GDD" references corrected 2026-07-17)*.
  Consumed **transitively** by Anti-Cheat/Replay Verification's
  Tier-1 clamp derivation, via Super Ricochet's own clamp formulas — not a
  direct edge *(corrected 2026-07-09 — `/review-all-gdds` found this GDD
  claimed a direct Anti-Cheat dependency that Anti-Cheat's own GDD didn't
  reciprocate; Anti-Cheat's Formulas section already modeled this as
  transitive-via-mini-game, so this list is updated to match rather than
  the other way around)*.

## Tuning Knobs

| Knob | Value | Too Low | Too High |
|---|---|---|---|
| Boss HP base / growth per level (levels 1–11) | 800 / +650 | Bosses die too fast, no sense of escalation | Runs become grindy walls of attrition |
| Boss HP growth per level (levels 12–30) | +200 (placeholder — see Open Questions) | Late-game stops feeling escalatory too early | Reintroduces the unwinnable-late-game divergence this revision fixed |
| Boss HP hard cap level / value | Level 30 / 11,100 (placeholder — see Open Questions) | Game plateaus too early, feels static for too many levels | Cap arrives too late to actually guarantee winnability at levels players realistically reach |
| Initial rows base / cap | 4 / caps at 8 (level 9) | Board feels empty at higher levels | Board overwhelms the danger line too fast |
| Max brick HP base / growth (levels 1–11) | 6 / +4 | Bricks feel like non-obstacles late-game | Individual bricks become tedious damage sponges |
| Max brick HP growth (levels 12–30) | +2 (placeholder — see Open Questions) | Late-game brick toughness stops escalating too early | Reintroduces the unwinnable-late-game divergence this revision fixed |
| Max brick HP hard cap level / value | Level 30 / 84 (placeholder — see Open Questions) | Bricks plateau too early, feel static for too many levels | Cap arrives too late to guarantee danger-line survivability at levels players realistically reach |
| Spawn density base / cap | 0.45 / caps at 0.75 (level 11) | Board under-fills, runs feel too easy | Board over-fills, no room to maneuver |
| Starting balls base / growth | 3 / +1 per 3 levels | Not enough volley volume to clear a full board | Trivializes the aim-and-fire core loop |

## Visual/Audio Requirements

N/A — pure config/data system; the boss HP bar, brick rendering, etc. are
Super Ricochet's own future Visual/Audio requirements, which consume this
system's numbers.

## UI Requirements

None directly — this system has no screen of its own. Its output (boss
name, boss HP) feeds Super Ricochet's future HUD.

## Acceptance Criteria

- **GIVEN** level=1, **WHEN** config is computed, **THEN** bossHp=800,
  initialRows=4, maxBrickHp=6, spawnDensity=0.45, startingBalls=3 (matching
  the prototype's exact baseline).
- **GIVEN** level=11 or higher, **WHEN** spawnDensity is computed, **THEN**
  it's capped at 0.75 regardless of how high level goes.
- **GIVEN** level=9 or higher, **WHEN** initialRows is computed, **THEN**
  it's capped at 8.
- **GIVEN** a player with Extra Balls upgrade level 3, **WHEN**
  startingBalls is computed for level=1, **THEN** it equals 3+0+3=6.
- **GIVEN** level=0 or a negative value, **WHEN** config is requested,
  **THEN** it's clamped to level 1's config, never crashing or returning a
  negative-row config.
- **GIVEN** level=30 or higher, **WHEN** `boss_hp` is computed, **THEN** it
  equals 11,100 regardless of how high level goes — verifying the hard cap
  actually holds and doesn't silently resume growing.
- **[NEW 2026-07-17] GIVEN** level=20 (the decelerated middle segment,
  levels 12–29), **WHEN** `boss_hp` is computed, **THEN** it equals 9,100 —
  exercising the piecewise formula's middle branch, which no prior AC
  tested (only the base case and the post-cap case were covered).
- **[NEW 2026-07-17] GIVEN** level=11 and level=12, **WHEN** `boss_hp` is
  computed for both, **THEN** the values are 7,300 and 7,500 respectively —
  verifying no off-by-one discontinuity at the growth-rate transition point
  (the `min(level-1,10)` term saturates at level 11; the `+200` decelerated
  term switches on at level 12).
- **[NEW 2026-07-17] GIVEN** level=1, 10, 20, and 30+, **WHEN**
  `max_brick_hp` is computed, **THEN** it equals 6, 42, 64, and 84
  respectively — exercising all three segments of the newly-capped formula,
  which previously had zero acceptance-criteria coverage despite being one
  of the five core difficulty formulas.
- **[NEW 2026-07-17] GIVEN** a request for level=500 (far beyond any
  player's realistic unlocked progress), **WHEN** config is computed,
  **THEN** it returns a valid, non-error config (per this system's explicit
  non-gating design, Edge Cases) — never a rejection, exception, or
  authorization error.
- **[NEW 2026-07-17] GIVEN** `extra_balls_upgrade_level` arrives malformed
  (e.g. -1 or 999), **WHEN** `starting_balls` is computed, **THEN** the
  upgrade term is clamped to 0 rather than producing a negative or
  implausibly large ball count.
- **[NEW 2026-07-17] GIVEN** level=2.7 (non-integer), **WHEN** config is
  requested, **THEN** it's floored to level 2's config — exercising the
  non-integer branch of Edge Case 1, which was previously listed but never
  covered by an AC (only the 0/negative branch was tested).

## Open Questions

1. **[NEW 2026-07-09, updated twice]** The `boss_hp` curve's exact numbers —
   the level-12-to-30 deceleration rate (+200/level) and the level-30 cap
   value (11,100) — are structural placeholders, not playtested numbers.
   The *shape* (grow, decelerate, hard cap) is locked in as a design
   principle; the specific numbers need real average-boss-damage-per-turn
   telemetry from the Unity port, tuned via `/balance-check`. **This is a
   required pre-launch gate.** **[Updated 2026-07-17]** `maxBrickHp` is now
   also capped (Question 2), addressing the *shape* of its divergence — but
   `/balance-check` must still validate danger-line survival at high levels
   as its own item, separately from boss-damage-per-turn validation; a
   correctly-shaped cap with a badly-tuned value could still leave a dead
   zone on this axis.
2. **[RESOLVED 2026-07-17]** Should `maxBrickHp` also get a hard cap,
   matching the pattern used by `initialRows`, `spawnDensity`, and `bossHp`?
   Yes — `/design-review` applied the same three-segment shape (see
   Core Rule 3 and the `max_brick_hp` formula), since arithmetic alone
   already showed this was the identical unbounded-growth bug class already
   fixed for `boss_hp`. The **exact numbers** (the +2/level deceleration
   rate and the 84 cap value) remain placeholders needing real
   danger-line-survival telemetry from the Unity port, same as `boss_hp`'s
   numbers — that part of the question is still open, folded into Question
   1 above, not resolved separately.
3. Should `startingBalls` also cap at level 30 (matching `boss_hp`), or is
   its continued slow growth past that point the intended "game gets easier
   late" behavior noted in Core Rule 3? Leaning toward: keep it uncapped,
   since a mild late-game easing is a deliberate, low-risk feature, not a
   bug — but confirm during `/balance-check`.
4. Is level 30 too early or too late for a hard cap, given a typical
   player's actual play session length and skill curve? No usage data
   exists yet to answer this — resolve alongside Question 1.
