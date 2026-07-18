# Super Ricochet

> **Status**: Designed
> **Author**: Abdulrahman Alenazan + Claude
> **Last Updated**: 2026-07-12
> **Implements Pillar**: Every mini-game is a real pillar; server-authoritative economy (determinism enables Anti-Cheat)
> **Creative Director Review (CD-GDD-ALIGN)**: APPROVED 2026-07-09 (self-reviewed — no `creative-director` subagent registered in this environment)
> **`/design-review` (2026-07-17)**: NEEDS REVISION — 6 blocking items
> (the coin/gem reward formula and a Tier-1 clamp were entirely absent from
> this GDD despite being its responsibility to own; the only place the
> reward formula existed elsewhere — `currency-system.md`'s citation — was
> itself stale, missing the level-30 cap that closed a farming-loop
> exploit, and `anti-cheat-replay-verification.md`'s own re-derivation had
> the same gap, meaning the system that actually gates the reward
> server-side would silently resurrect the exploit; the volley cap is
> stated as "12 seconds" wall-clock throughout when the accepted
> ADR-0002 requires it be enforced as 720 sim frames, never elapsed time)
> plus 5 blocking AC gaps and 2 recommended items. All folded in below;
> re-review pending. Companion fixes also applied to
> `anti-cheat-replay-verification.md` and `currency-system.md` (stale gem
> formula in both).
> **Follow-up fix (2026-07-17, found reviewing `leaderboard.md`)**: the
> `run_reward` formula's `bossDefeated` description ("server-replay-derived")
> was itself wrong, inherited from a stale claim in
> `anti-cheat-replay-verification.md` Rule 5 that contradicted that same
> document's own Rule 6 and ADR-0007 (reward is Tier-1-clamped and
> synchronous; Tier-2 replay is async and flag-only, never reward-gating).
> Corrected here and at the root cause in Rule 5, plus a matching fix in
> `boss-ai-damage-model.md`.

## Overview

Super Ricochet is the physics-driven "aim, fire, ricochet" core loop — a
multi-ball volley launcher that chips numbered bricks on a descending grid
while damaging the boss (via Boss AI/Damage Model). This is the game's
flagship, proven pillar, carried over essentially unchanged from the
prototype's `engine.ts` — itself already validated by a headless self-test
harness (`engine.selftest.ts`) that plays hundreds of volleys and asserts on
boss defeat, danger-line loss, and stat tracking. That engineering rigor is
worth preserving in the native port alongside the gameplay numbers, not
just the numbers themselves.

## Player Fantasy

Direct — "Ready, Aim, Fire!" per the concept doc's own tagline. The
satisfaction of a well-aimed volley ricocheting through a packed board,
chaining hits, watching the boss bar drain. Precision (aim) + chaos (physics
ricochet) + escalating tension (the danger line, the boss race) is the core
emotional loop this system exists to deliver.

## Detailed Design

### Core Rules

1. **Grid**: 7 columns; cell size derives from canvas width ÷ 7. Ball radius
   = 15% of a cell. Ball speed = 11× cell size per second — proportional to
   board size, not a fixed pixel value, so feel stays consistent across
   device screen sizes.
2. **Aim-and-fire**: the player aims from a single launcher position
   (drag/pointer); release fires a volley of N balls (N = `starting_balls`
   from Level/Difficulty Config, +1 per collected power-up), launched 0.05s
   apart along the aimed vector. Aim is constrained to an upward cone —
   blocked within ~8.6° of horizontal, preventing a degenerate flat shot
   that never usefully covers the board.
3. **Sub-stepped collision**: each physics step subdivides into sub-steps of
   half a ball radius — this is specifically what makes tunnelling through
   bricks structurally impossible, even at high ball speed. This same
   determinism/precision requirement is what makes Anti-Cheat's Tier-2
   replay verification possible at all: a non-deterministic or
   tunnelling-prone physics model couldn't be reliably re-simulated
   server-side.
4. **Minimum-vertical-velocity enforcement**: any ball whose vertical speed
   drops below 22% of total ball speed is nudged, preventing degenerate
   horizontal-skimming loops that never resolve.
5. **12-second hard cap per volley** *(player-facing framing)* — any balls
   still airborne past this are force-retired, guaranteeing every turn ends
   in bounded time — protects both player UX and server-side
   replay-simulation cost (directly relevant to Anti-Cheat's Open Question
   #1 about re-simulation CPU budget). **[Corrected 2026-07-17]** The
   *authoritative* enforcement is **720 sim frames at a fixed 60Hz
   simulation rate**, per ADR-0002 — never elapsed wall-clock time. This
   distinction is load-bearing, not pedantic: a client-side stutter or
   focus-loss changes elapsed wall-clock time without changing the sim's
   frame count, and Tier-2 replay must reproduce the same result from the
   same recorded input regardless of real-world timing hiccups. "12
   seconds" is the correct number to show players and to reason about
   design-wise (12s at 60Hz = 720 frames), but any implementation, edge
   case, or acceptance criterion described in frame-based terms elsewhere
   in this document is the authoritative version; a literal wall-clock
   timer would silently break the exact determinism guarantee Rule 3
   depends on.
6. The launcher **re-centers to the last ball's landing X** after each turn
   — not a fixed center — adding a light positioning/skill layer between
   turns.
7. The board descends one row per turn; a new row spawns at the top per
   `spawn_density` (from Level/Difficulty Config). The level ends in a loss
   if any brick crosses the "danger line" row.
8. Brick HP is **spawn-weighted toward low values**
   (`ceil(pow(random(), 1.6) × max_brick_hp)`) — most bricks die in 1-2
   hits, a few are tougher, avoiding a board that feels uniformly grindy.
9. Coins (14% chance per row, max 1 per row) and `+1 ball` power-ups (5%
   chance per row) spawn as collectible pickups, not physics obstacles.

### States and Transitions

| State | Trigger | Result |
|---|---|---|
| Ready | Level loads | → Board initialized per Level/Difficulty Config |
| Ready → Aiming | Player begins aiming | → Trajectory preview shown (longer if Aim Assist owned) |
| Aiming → Firing | Player releases | → Volley launched, balls in flight, 720-sim-frame cap active (720 frames @ 60Hz = 12s, per ADR-0002 — never a wall-clock timer) |
| Firing → Aiming | All balls resolved (landed/retired) before boss defeat/danger line | → Next turn: board descends, launcher re-centers |
| Firing/Aiming → Over (win) | Boss AI reaches 0 HP | → Win state, immediate — takes priority over a same-frame loss (see Edge Cases) |
| Firing/Aiming → Over (loss) | A brick crosses the danger line | → Loss state |

### Interactions with Other Systems

- **Level/Difficulty Config (Ricochet)**: supplies `initial_rows`,
  `max_brick_hp`, `spawn_density`, `starting_balls`.
- **Boss AI/Damage Model**: every brick hit is reported for its fixed
  1-per-hit boss damage rule.
- **Currency System** (via Anti-Cheat): coin pickups and boss defeats feed
  the `run_reward` formula (see Formulas) at run end — `coinsCollected` maps
  to the `creditMultiplied` leg, the boss-defeat bonus to the `creditFlat`
  leg, submitted together per `currency-system.md` Rule 5.
- **Anti-Cheat/Replay Verification**: this engine's determinism (Rule 3) —
  fixed sub-stepping, seeded RNG for row/coin/power-up spawn rolls — is a
  **binding constraint**, not an incidental property. Anti-Cheat's Tier-2
  verification cannot function if this engine ever becomes non-deterministic.
  **[Flagged 2026-07-17]** This determinism is delivered by ADR-0002's
  fixed-point physics module, whose **Status is still Proposed**, gated
  behind an unrun spike test (ARM-vs-x86 byte-identical hit sequences). If
  that spike fails, ADR-0002's own fallback is Alternative D — statistical/
  behavioral anti-cheat with client-authoritative physics, no replay — which
  would mean Rule 3's determinism requirement as stated here no longer
  holds and Anti-Cheat's Tier 2 would need to be re-scoped for this
  mini-game. Not a hypothetical: stated here so this GDD doesn't read as
  settled fact while its own governing physics ADR is still contingent.
- **Analytics/Event Tracking**: emits `run_start`/`run_complete` per
  Analytics' schema.

## Formulas

**`ball_speed`**: `ball_speed = 11 × cell_size` (cell_size = canvas_width ÷ 7)

**`ball_radius`**: `ball_radius = 0.15 × cell_size`

**`min_vertical_velocity`**: `min_vertical_velocity = 0.22 × ball_speed`

**`brick_hp_roll`**: `brick_hp = ceil(pow(random(), 1.6) × max_brick_hp)`
| Variable | Type | Range | Description |
|---|---|---|---|
| random() | float | 0.0–1.0 | Seeded RNG draw (deterministic per Anti-Cheat's replay requirement) |
| max_brick_hp | int | from Level/Difficulty Config | Level's brick-toughness ceiling |

Output: weighted toward low values — most rolls land well under
`max_brick_hp`, a minority approach the ceiling.

**Spawn chances**: `coin_spawn_chance = 0.14` per row (max 1 per row);
`power_up_spawn_chance = 0.05` per row.

**`run_reward`** [NEW 2026-07-17 — `/design-review` found this GDD, the
one responsible for owning it per Currency System's and Anti-Cheat's own
stated ownership boundaries, never actually stated it; the only other
place it existed was a stale citation in `currency-system.md`, missing the
level-30 cap `game-concept.md` applied to close a farming-loop exploit.
This is now the single authoritative source — `currency-system.md` and
`anti-cheat-replay-verification.md` should reference this entry, not
re-quote the formula independently]:

`coins = coinsCollected × (1 + coinValueUpgrade) + (bossDefeated ? 50 + min(level, 30) × 20 : 0)`
`gems = bossDefeated ? 5 + floor(min(level, 30) / 2) : 0`

| Variable | Type | Range | Description |
|---|---|---|---|
| coinsCollected | int | 0–∞, Anti-Cheat-validated | Coins actually picked up during the run (per-row 14% spawn chance) |
| coinValueUpgrade | int | 0–4 (Shop's cap) | Player's Coin Value upgrade tier |
| bossDefeated | bool | — | **[Corrected 2026-07-17]** Anti-Cheat's Tier-1-clamped view of the client-submitted value (per `anti-cheat-replay-verification.md` Rule 5) — used synchronously for reward crediting; Tier-2's later async replay only verifies it and never changes an already-credited reward (Rule 6), so "server-replay-derived" was the wrong framing |
| level | int | 1–∞ | Level reached this run; **capped at 30** in both terms above, matching `level-difficulty-config-ricochet.md`'s `boss_hp` plateau — beyond level 30 the boss bonus is fixed at 50+600=650 coins and 5+15=20 gems, never climbing further |

**Currency System two-path mapping** [NEW 2026-07-17]: `coinsCollected ×
(1 + coinValueUpgrade)` is the `creditMultiplied` leg; the `bossDefeated`
flat bonus is the `creditFlat` leg — submitted together as the two legs
of one atomic `mutateWallet` call (per `currency-system.md` Rule 5 /
ADR-0004), never combined into a single multiplied amount. This is the
exact split whose omission from a mini-game's own GDD previously caused a
reward-inflation bug (`/review-all-gdds`, 2026-07-09) — stating it here
explicitly, in the one document that defines both terms, closes that risk
at the source rather than relying on generic wording in Currency
System's/Anti-Cheat's own GDDs alone.

**Tier-1 plausibility clamp** [NEW 2026-07-17 — previously absent; flagged
as a gap when `level-difficulty-config-ricochet.md` was reviewed]:

`maxCoinsCollected = initialRows × 8 × (turnsElapsed + 1)`

A rough per-run ceiling on `coinsCollected`: at most one coin can spawn per
row (14% chance, capped at 1), and a board holds at most 8 rows
(`initial_rows`'s own cap) at any depth per turn, so `coinsCollected`
cannot plausibly exceed roughly one coin per row-slot per turn elapsed.
This is a **generous, structural** ceiling (not a tuned "expected" value) —
Anti-Cheat's Tier 1 rejects only implausible outliers (e.g. a client
reporting 10,000 coins in a 3-turn run), never a legitimate high-variance
run. The exact multiplier is a placeholder pending `/balance-check` against
real per-turn coin-collection telemetry, matching how `boss_hp`'s and
`max_brick_hp`'s cap values are also placeholders — see Open Questions.

**`ricochetBestScore`** *(backported from `leaderboard.md` during that GDD's
CD-GDD-ALIGN review, 2026-07-12 — this system, not Leaderboard, owns its own
leaderboard-facing score, mirroring how `quack-runner.md` owns
`runnerLeaderboardScore`)*:

`ricochetBestScore = (levelReached × 100,000) + min(bossesDefeated, 99,999)`

| Variable | Symbol | Type | Range | Description |
|---|---|---|---|---|
| Level Reached | `levelReached` | int | 1–unbounded (meaningful difficulty scaling caps at 30 per `level-difficulty-config-ricochet.md`, where `boss_hp` plateaus at 11,100) | Highest level the player has reached; a lifetime-cumulative stat persisted via Save/Persistence, incremented on this system's Firing/Aiming → Over (win) transition |
| Bosses Defeated | `bossesDefeated` | int | 0–unbounded lifetime cumulative, clamped to 99,999 inside the formula | Cumulative lifetime boss kills; persisted the same way, functions only as an in-level tiebreak |

**Output Range:** 100,000 (level 1, 0 bosses) to unbounded, growing in
fixed 100,000-wide bands per level. Within any level's band the value spans
exactly `[level×100,000, level×100,000 + 99,999]`.

**Overflow check**: `min(bossesDefeated, 99,999)` guarantees no overflow
into the next level's digit band — the clamp ceiling (99,999) is strictly
less than the level multiplier (100,000), so the boss term can never reach
100,000. No amount of lifetime boss kills, however large, lets a
lower-level player outrank a higher-level one. The clamp is load-bearing
for sort correctness, not cosmetic.

**Examples:**
- Mid-progression: level 10, 50 bosses → 10×100,000 + 50 = **1,000,050**
- Level-cap, modest kills: level 30, 200 bosses → 30×100,000 + 200 =
  **3,000,200**
- Level-cap, extreme kills: level 30, 150,000 bosses (exceeds clamp) →
  min(150,000, 99,999) = 99,999 → 30×100,000 + 99,999 = **3,099,999** —
  still ranks *below* a level-31 player with 0 bosses (3,100,000),
  confirming the tiebreak never crosses a level band even at extreme
  cumulative counts.

**Currency System**: no interaction whatsoever. `ricochetBestScore` is a
pure ranking value — never read by, written to, or converted into any
currency calculation. Consumed as-is by `leaderboard.md` (its own Core
Rule 1), which has no authority to redefine or adjust it — matching how
that GDD's Core Rule 2 treats `runnerLeaderboardScore`.

## Edge Cases

- **Tunnelling is structurally impossible by construction** — the
  sub-step size (half a ball radius) is specifically chosen to make this
  true, not an arbitrary tuning value. No edge-case handling needed beyond
  preserving that ratio in the native port.
- **If the last ball in a volley is still resolving exactly at the 720th
  sim frame (the cap)**: force-retired mid-flight rather than allowed to
  run indefinitely, per Rule 5. This is evaluated in frame count, not
  elapsed wall-clock seconds — a lag spike near the boundary does not
  change which frame the cap fires on.
- **If the player attempts to fire while the previous volley hasn't fully
  resolved**: blocked — fire input is only accepted in the Aiming state,
  never mid-Firing.
- **If a danger-line breach and a boss defeat occur in the same frame**:
  **win takes priority** — Boss AI's "defeated the instant HP hits 0" check
  must run before the danger-line loss check each frame, so a last-second
  lethal hit is never overridden by a simultaneous board-overflow loss.

## Dependencies

- **Depends on** (hard): Level/Difficulty Config (Ricochet), Boss AI/Damage
  Model, Anti-Cheat/Replay Verification (determinism requirement + replay
  data submission), Analytics/Event Tracking (emits `run_start`/`run_complete`
  events) *(added 2026-07-09 — `/review-all-gdds` found this system emits
  analytics events per its own Interactions section but never listed the
  dependency)*.
- **Depended on by**: Ricochet HUD (Presentation), Leaderboard *(consumes
  `ricochetBestScore` as owned here, per that GDD's own Core Rule 1 —
  added 2026-07-12)*.

**Consistency check**: Boss AI/Damage Model's GDD lists "Depended on by:
Super Ricochet" — matches this GDD's dependency on it. ✅

**Related Architecture [added 2026-07-17]**: `adr-0002-deterministic-
fixedpoint-physics.md` (the accepted version, superseding a rejected earlier
draft of the same number) governs Rule 3's determinism and Rule 4's
min-vertical-velocity nudge in implementation detail, including the
frame-vs-wall-clock distinction now reflected in Rule 5 above — read
alongside this GDD, not as a substitute for it.

## Tuning Knobs

| Knob | Value | Too Low | Too High |
|---|---|---|---|
| Ball speed multiplier | 11× cell size | Volleys feel sluggish, turns drag | Aiming feels uncontrollable, balls overshoot reads |
| Ball radius ratio | 0.15 of cell | Balls feel imprecise/hard to see | Balls feel oversized, board reads as cluttered |
| Min vertical velocity ratio | 0.22 of ball speed | Balls get stuck skimming horizontally | Balls feel unnaturally steep/bouncy |
| Volley cap | 720 sim frames @ 60Hz (= 12s, per ADR-0002 — frame count is authoritative, not elapsed time) | Turns cut off before naturally resolving | Slow/degenerate volleys drag turns out |
| Brick HP weighting exponent | 1.6 | Bricks feel uniformly tough (grindy) | Bricks feel uniformly trivial (no texture) |
| Coin/power-up spawn chance | 0.14 / 0.05 per row | Rewards feel too rare to matter | Rewards feel too abundant to be exciting |

## Visual/Audio Requirements

**[Self-review — art-director + technical-artist consult, performed
directly]**, grounded in the prototype's actual `sound.ts` implementation —
this is proven, already-shipped game feel, carried forward as the baseline
rather than redesigned:

- **fire**: soft sine blip, quick pitch slide.
- **hit**: rapid tick, throttled to ~1 per 28ms during a volley so dozens of
  simultaneous hits don't turn into noise.
- **destroy**: square wave with a pitch slide, distinct from a routine hit.
- **coin**: rising sine chime.
- **plus** (power-up): two-note triangle-wave chime.
- **win**: rising arpeggio (C5–E5–G5–C6).
- **lose**: descending sawtooth.
- **Screen shake**: magnitude escalates with hit intensity (bigger on brick
  destroy/boss-defeat than a routine hit), decays continuously each frame —
  must respect the reduced-motion accessibility setting already established
  in the prototype's later polish pass (gate the shake offset, not just the
  magnitude accumulation).

📌 **Asset Spec** — once the art bible is approved, run
`/asset-spec system:super-ricochet` for per-asset visual specs (ball trail
VFX, brick destruction particles, danger-line treatment).

## UI Requirements

The HUD (ball count, turn number, bricks destroyed, aim-hint text, boss
name/HP readout owned by Boss AI/Damage Model) is a separate Presentation-
layer system ("Ricochet HUD") — this GDD only specifies the data that HUD
needs, not its layout.

## Acceptance Criteria

- **GIVEN** a volley is fired through a dense brick cluster (7-column grid
  fully populated at `spawn_density=0.75`) at max ball speed (`11×
  cell_size`), **WHEN** simulated, **THEN** no ball passes through a brick
  without registering a hit (no tunnelling).
- **[REVISED 2026-07-17] GIVEN** 720 sim frames (60Hz) elapse with balls
  still airborne, **WHEN** the cap is reached, **THEN** all remaining balls
  are force-retired and the turn ends — verified by frame count, not by an
  elapsed-time measurement (a simulated lag spike must not change which
  frame the cap fires on).
- **GIVEN** a brick crosses the danger line, **WHEN** checked, **THEN** the
  level ends in a loss — unless the boss was defeated in that same frame, in
  which case win takes priority.
- **GIVEN** the same seed and identical input sequence are re-simulated
  server-side, **WHEN** compared to the client's reported result, **THEN**
  they match within Anti-Cheat's tolerance.
- **GIVEN** a brick HP roll is requested, **WHEN** generated, **THEN** the
  distribution is weighted toward low values per the `pow(random, 1.6)`
  formula, not uniform.
- **[NEW 2026-07-17] GIVEN** a completed run with coinsCollected=10,
  coinValueUpgrade=2, bossDefeated=true, level=20, **WHEN** `run_reward` is
  computed, **THEN** coins = 10×3 + (50+20×20) = 30+450 = 480, and gems =
  5+floor(20/2) = 15 — exercising the formula this GDD previously never
  stated. **GIVEN** the same inputs but level=45, **THEN** coins =
  30+(50+30×20)=30+650=680 and gems=5+floor(30/2)=20 — verifying the
  level-30 cap actually holds past level 30, matching
  `level-difficulty-config-ricochet.md`'s `boss_hp` plateau.
- **[NEW 2026-07-17] GIVEN** aim angles of 8°, exactly 8.6°, and 9° from
  horizontal, **WHEN** the player attempts to fire, **THEN** 8° and 8.6°
  are blocked (inclusive boundary) and 9° is allowed — replacing the
  previously untestable "~8.6°" with an exact, testable boundary.
- **[NEW 2026-07-17] GIVEN** a volley's last ball lands at a given X
  coordinate, **WHEN** the next turn begins, **THEN** the launcher's
  position equals that landing X exactly, not a fixed/default center.
- **[NEW 2026-07-17] GIVEN** a power-up is collected mid-run, **WHEN** the
  next volley is fired, **THEN** it launches with exactly one additional
  ball, and this increase persists for the remainder of the run (not just
  the immediately following volley).
- **[NEW 2026-07-17] GIVEN** a ball's vertical speed drops below 22% of
  total ball speed, **WHEN** the nudge applies, **THEN** its vertical speed
  is corrected to exactly 22% of total ball speed (the floor value), not an
  arbitrary or unspecified correction amount.
- **[NEW 2026-07-17] GIVEN** level=30, 150,000 lifetime bosses defeated
  (exceeding the clamp), **WHEN** `ricochetBestScore` is computed, **THEN**
  it equals 3,099,999 — and a level=31, 0-boss player's score (3,100,000)
  still ranks strictly higher, capturing the Formulas section's own
  overflow-safety worked example as an executable test rather than prose
  alone.

## Open Questions

1. Exact Unity rendering approach for the aim-assist trajectory preview
   (Line Renderer vs. a custom shader) isn't decided. *Target: resolve
   during `/create-architecture`.*
2. **[NEW 2026-07-17]** The Tier-1 `maxCoinsCollected` clamp formula's
   multiplier is a structural placeholder, not a playtested ceiling — needs
   real per-turn coin-collection telemetry from the Unity port before
   `/balance-check` can validate it, same treatment as `boss_hp`'s and
   `max_brick_hp`'s cap values in `level-difficulty-config-ricochet.md`.
3. **[NEW 2026-07-17]** Whether `tolerance_units` should be 0 (bit-exact,
   matching ADR-0002's fixed-point physics) rather than the generic
   suggested value of 2 for this mini-game's specific physics-derived
   score/hit-count path is flagged but unresolved in both this GDD and
   `anti-cheat-replay-verification.md` (see ADR-0002's own "GDD/ADR-0001
   SYNC REQUIRED" note). *Target: resolve as its own follow-up pass,
   owned by the Anti-Cheat GDD, once the ADR-0002 spike gate's outcome is
   known.*
