# ADR-0014: Deterministic Spawn-Probability and Brick-HP Rolls for Super Ricochet

## Status
Proposed

## Date
2026-07-17

> **Independently reviewed (2026-07-17, general-purpose agent standing in
> for a deterministic-simulation/anti-cheat specialist)**: 4 blocking
> issues found and fixed, 4 recommended fixes folded in. (1) Decision §5
> and the Architecture Diagram disagreed on whether the coin- and
> power-up-spawn rolls are conditional on a row actually spawning that
> turn — the diagram drew them as unconditional siblings while the prose
> only qualified the brick-HP roll with "if a row spawned." Since
> `super-ricochet.md` Rule 9 only makes sense tied to an actual row, and
> an unconditional roll would silently consume a different number of
> `NextUInt32()` draws on no-spawn turns depending on which reading an
> implementer picked, this is exactly the desync risk Runner's Core Rule
> 4 was written to prevent. Fixed by nesting both rolls under the
> row-spawn "yes" branch, explicitly, in both places. (2) The offline
> `BrickHpTable`'s "identical bytes on every platform by construction"
> claim was asserted, not mechanized — nothing pinned client and server
> to the *same* generated artifact, and the ADR's own Risks section
> already anticipated recurring regeneration (a real single-sourcing
> gap, not a one-time act). Fixed by specifying the table is checked in
> as generated C# source compiled directly into `SharedSimCore.dll` —
> the same "same compiled DLL, not independently reproduced" guarantee
> ADR-0001 already relies on for the RNG core itself — plus a CI
> golden-file check that regenerating from checked-in inputs reproduces
> the checked-in output byte-for-byte. (3) `BrickHpTable.Lookup`'s
> cumulative-threshold construction was never actually specified — no
> closed-form weight formula, no rounding/apportionment rule, no
> guarantee thresholds sum to exactly `Resolution`, and no boundary-exact
> acceptance criterion despite one being promised in the Risks section —
> the identical bug class (bucket sizes vs. cumulative thresholds, values
> falling through unmapped) already caught once this session in Runner's
> obstacle-type roll. Fixed by specifying the exact inverse-CDF weight
> formula, a largest-remainder apportionment rule, a build-time sum
> invariant, and boundary-exact ACs for the smallest and largest tables.
> (4) `SpawnDensityThreshold`'s raw `int` arithmetic can overflow (at
> `level` on the order of 7 million+), silently breaking the 7500 cap and
> risking a checked-vs-unchecked cross-platform divergence the same way
> ADR-0001 already flagged for PRNG arithmetic generally — fixed with an
> explicit `unchecked` block, matching that existing convention, with the
> upstream sane-`level` bound left to `level-difficulty-config-ricochet.md`'s
> own input-validation Edge Cases (this ADR doesn't own `level` validation,
> the same routing discipline ADR-0013 used for the `obstacleSpeed` cap).
> Recommended fixes also folded in: reworded the "matching Runner's
> established convention" claim to state this ADR *establishes* the
> modulo-mapping convention (Runner's own GDD never actually pinned the
> `NextUInt32()`-to-range mechanism in writing); made the per-row
> brick-cell count/order (7 columns, left-to-right, fully populated)
> explicit and cited rather than assumed; acknowledged the registry's
> broader (not collision-scoped) framing of ADR-0002's float ban instead
> of asserting the narrow reading as settled; and corrected "already
> proven by ADR-0001's own spike" to reflect ADR-0001's actual
> still-Proposed, pending-CI-validation status.

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Unity 6.3 LTS client + server-side `.NET` replay verifier (extends `SharedSimCore`, ADR-0001) |
| **Domain** | Core / Gameplay (deterministic) — Super Ricochet |
| **Knowledge Risk** | LOW — pure integer arithmetic throughout; no new `Fix32` surface, no new spike gate. **[Corrected per independent review]** `NextUInt32()`'s determinism rests on ADR-0001's own CI test-vector requirement, which is a stated requirement, not a confirmed-run result — ADR-0001's Status is still Proposed, the same "pending-spike" honesty ADR-0013 already used rather than treating it as settled. The brick-HP table is generated offline and shipped as compiled-in static data, so no runtime float/`pow()` exists to spike-test in the first place |
| **References Consulted** | `level-difficulty-config-ricochet.md`, `super-ricochet.md`, `obstacle-spawn-difficulty-ramp-runner.md`, ADR-0001, ADR-0002, ADR-0013 (precedent for the identical technique already applied to Quack Runner), `docs/registry/architecture.yaml` |
| **Post-Cutoff APIs Used** | None |
| **Verification Required** | CI test-vector coverage extending ADR-0001's existing `NextUInt32()` suite to this ADR's cumulative-threshold constants, including boundary-exact cases for the brick-HP tables (not just a statistical test — see Decision §4 and Validation Criteria); a CI golden-file check that the checked-in `BrickHpTable` source matches what the offline generator produces from its checked-in inputs, byte-for-byte |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-0001 (`SharedSimCore`, `Pcg32Rng.NextUInt32()`) — the RNG foundation this ADR consumes unchanged. The already-shipped (GDD-level, not ADR-gated) cumulative-threshold technique `obstacle-spawn-difficulty-ramp-runner.md` used for Runner's obstacle-type roll is this ADR's direct precedent, generalized here into a registered architectural pattern. |
| **Enables** | Anti-Cheat Tier-2 replay verification for Super Ricochet's row/coin/power-up spawn generation and brick-HP assignment (currently gated on this ADR — see Open Questions this closes in `level-difficulty-config-ricochet.md` and `super-ricochet.md`) |
| **Blocks** | Super Ricochet spawn-generation code specifically (row/coin/power-up rolls, brick-HP assignment), same TD-condition pattern ADR-0001/0002 set for Ricochet generally |
| **Ordering Note** | A straightforward generalization of an already-specified technique — no new numeric primitive, no new spike beyond what ADR-0001 already scopes for `NextUInt32()` itself (validation of that scope is ADR-0001's own open CI obligation, not a new one this ADR adds). The one genuinely new piece (an offline-precomputed brick-HP table) is a build-tooling addition, not a runtime risk. |

## Context

### Problem Statement
`level-difficulty-config-ricochet.md`'s `/design-review` pass (2026-07-17) flagged `spawn_density` — and "the row-spawn/brick-HP-roll/coin-spawn decisions it and similar thresholds drive" — as using floating-point comparisons or floating-point-derived computations to produce discrete, replay-critical branch outcomes. This is the same risk category ADR-0002 solved for ball-vs-brick collision. ADR-0002's own Decision §1 text (scoped to "scored-path position, velocity, and collision math") supports reading it narrowly as collision-only, and it was never *applied* to spawn-generation decisions in practice — but `docs/registry/architecture.yaml`'s forbidden-pattern entry for this rule (established by ADR-0002) is titled generically **"float / double anywhere in the scored simulation path,"** with no collision qualifier, and ADR-0002's own general argument against float in the scored path applies equally to any discrete branch decision. **This ADR does not resolve which reading is correct** — it's plausible Ricochet's spawn-generation float use was already out of compliance with the registry's own broader wording, not merely an unaddressed gap. Either way, the flag was left open, routed to "an ADR-level decision... before Tier-2 replay verification can be trusted for Super Ricochet," not resolved in that GDD.

Quack Runner hit the identical problem independently, for its obstacle-type roll, and fixed it directly inside its own GDD: converting a percent-chance decision into an integer cumulative-threshold roll against `Pcg32Rng.NextUInt32()`, avoiding float comparison entirely (see `obstacle-spawn-difficulty-ramp-runner.md` Core Rule 4). That fix was correct but ad hoc — applied in one GDD, for one decision, never registered as a general pattern other systems could cite.

Super Ricochet has **four** separate float-driven decisions needing this treatment, and one of them is materially harder than anything Runner had to solve:
1. **Row-spawn roll** — `spawn_density` (`min(0.45 + (level−1)×0.03, 0.75)`) gates whether a new row spawns each turn. A simple threshold, same shape as Runner's problem.
2. **Coin-spawn roll** — `coin_spawn_chance = 0.14` per row. Same shape.
3. **Power-up-spawn roll** — `power_up_spawn_chance = 0.05` per row. Same shape.
4. **`brick_hp_roll`** — `ceil(pow(random(), 1.6) × max_brick_hp)`. **Not** a threshold branch — a continuous, weighted, `pow()`-based formula producing a discrete integer output. Runner's fix doesn't cover this shape at all; it needs its own treatment.

This ADR does two things: (1) formally registers the integer-cumulative-threshold-vs-`NextUInt32()` technique as a project-wide architectural pattern, citing Runner's obstacle-type roll as the originating precedent rather than re-deriving it from scratch; (2) extends that pattern — plus one new technique for the harder case — to close all four of Ricochet's flagged gaps.

### Requirements
- All four decisions must be bit-exact (not `tolerance_units`-bounded) reproducible server-side from `{seed, input sequence}` — a discrete branch or discrete output value cannot be partially correct, the same reasoning ADR-0002 and ADR-0013 already established.
- No new `Fix32` surface, no new spike gate beyond what ADR-0001 already scopes for `NextUInt32()` itself.
- A canonical, pinned RNG roll order per turn/per row — per Runner's own established rationale (`obstacle-spawn-difficulty-ramp-runner.md` Core Rule 4), any implementation-convenience divergence in roll order would silently desync Tier-2 replay for *every* run, not just malformed ones, since client and server draw from the same shared RNG stream.
- `brick_hp_roll`'s weighted distribution must be reproduced with **zero** runtime float or `pow()` math in the scored path.

## Decision

### 1. General pattern — this ADR establishes it project-wide, not merely reuses it
Any probability-branch decision in a scored/replayed path is expressed as an integer cumulative threshold **out of `RESOLUTION = 10000`** (reusing the same resolution Runner's obstacle-type roll already used for its bucket thresholds — one mental model project-wide — and every constant in scope here is an exact multiple of `1/10000`, so no precision is lost), compared against `NextUInt32() % RESOLUTION`. Never a float comparison against `NextFloat01()` or any floating probability value, in any scored path, for any mini-game.

**[Corrected per independent review]** `obstacle-spawn-difficulty-ramp-runner.md` Core Rule 4 specifies the cumulative-threshold *values* (10000-based bucket boundaries) but never actually pins the `NextUInt32()`-to-range *mapping mechanism* (modulo vs. any alternative) in writing — no existing ADR covers it either. This ADR is therefore the first place that mechanism is formally specified, not an inheritor of a pre-existing written convention; Runner's own (still-unimplemented) obstacle-type-roll code must conform to the mapping specified here once it's built, ideally by calling the same shared helper (`RicochetSpawnRolls.Roll`-equivalent, generalized) rather than each mini-game asserting its own `% 10000` independently.

The modulo mapping carries a negligible bias (`2^32 mod 10000 = 7296`, so residues `0..7295` get one extra representative out of `429497` vs. `429496` for the rest — roughly 1 part in 430,000) — documented explicitly here rather than left implicit, and not worth a stricter unbiased-sampling method nobody has asked for.

### 2. Row-spawn roll — `spawn_density` reformulated as an exact integer threshold, no `Fix32` needed
`spawn_density`'s source constants are all exact multiples of `1/10000` (`0.45 → 4500`, `0.03 → 300`, `0.75 → 7500`), so the threshold is expressed directly in integers with **zero** precision loss and **zero** new numeric type:

```
spawn_density_threshold(level) = min(unchecked(4500 + (level - 1) * 300), 7500)   // out of 10000
```

Per turn: `roll = NextUInt32() % 10000; if (roll < spawn_density_threshold(level)) { spawn a new row }`.

**[Added per independent review]** The multiply is wrapped in `unchecked`, matching ADR-0001's own stated rationale for PRNG-adjacent arithmetic ("integer-overflow wraparound behavior cannot diverge based on each side's project-level overflow-checking settings") — without it, a client/server pair built with different overflow-checking settings could disagree (one throwing, one wrapping) at extreme `level` values. `Int32` overflow is only reachable at `level` on the order of 7 million or higher, far beyond any value this game curve produces — `unchecked` guarantees that even at such a value, both sides wrap identically rather than diverging, but does not itself guarantee the *result* still clamps to 7500 at that point. Keeping `level` within a sane range is `level-difficulty-config-ricochet.md`'s own responsibility (its Edge Cases already gate malformed low/non-integer `level` values) — this ADR does not own `level` validation, the same routing this project already used for `obstacleSpeed(t)`'s cap in ADR-0013.

### 3. Per-row coin/power-up rolls — same technique, two more constants
```
CoinSpawnThreshold    = 1400   // 0.14 × 10000, exact
PowerUpSpawnThreshold =  500   // 0.05 × 10000, exact
```
One roll each, in the pinned order given in Decision §5.

### 4. `brick_hp_roll` — offline-precomputed integer table replaces runtime `pow()`
The runtime formula `ceil(pow(random(), 1.6) × max_brick_hp)` is replaced by a **precomputed cumulative-weight table**, one per distinct `max_brick_hp` value the level curve can produce. `level-difficulty-config-ricochet.md`'s own formula caps `max_brick_hp` at 84 (permanently, from its capped level onward), so the total number of distinct tables needed is small (bounded by the number of levels before the cap, plus one for the capped plateau).

**[Fully specified per independent review — the original draft left the table-construction algorithm, sum invariant, and single-sourcing mechanism unstated, the same "asserted but not mechanized" gap already caught once this session in Runner's obstacle-type-roll thresholds]**

- **Weight formula.** For output value `v` (`1..max_brick_hp`), the exact bucket weight is the inverse-CDF of `pow(x, 1.6)` over `[（v−1)/max_brick_hp, v/max_brick_hp]`:
  ```
  weight(v) = (v / max_brick_hp)^(1/1.6) − ((v−1) / max_brick_hp)^(1/1.6)
  ```
  This sums to exactly `1.0` over `v = 1..max_brick_hp` by construction (telescoping sum), reproducing the original continuous distribution's shape exactly at the bucket level — verified directly for both `max_brick_hp = 6` (weights ≈ 0.3263/0.1770/0.1453/0.1275/0.1162/0.1077, no degenerate buckets) and `max_brick_hp = 84` (smallest bucket weight ≈ 0.0075, still non-zero).
- **Integer apportionment.** Each `weight(v) × 10000` is floored to an integer, and the **largest-remainder method** distributes the leftover units (`10000 − sum of floors`, always a small non-negative count less than `max_brick_hp`) one each to the buckets with the largest fractional remainder — a standard, deterministic apportionment rule, never ad hoc per-bucket rounding. This guarantees `Σ bucketWidth(v) == 10000` exactly, for every `max_brick_hp`.
- **Build-time invariant.** The generator asserts `cumulativeThresholds[max_brick_hp - 1] == Resolution` (10000) before a table is accepted — no roll `0..9999` can fall through unmapped, closing off the exact bug class (bucket sizes vs. cumulative thresholds confused) already caught in Runner's own fix.
- **Table generation is an offline build step, checked in as generated source, not regenerated ad hoc.** A build-time tool computes the weight formula above (ordinary double-precision math — safe here specifically because the *output* is a checked-in artifact, not independently recomputed by each consumer) and emits a generated `.cs` file containing the `cumulativeThresholds` arrays for every `max_brick_hp` value in use, committed to version control. `SharedSimCore.dll` compiles this generated file directly — client and server both link the *same compiled DLL*, the same "same binary, not independently reproduced" guarantee ADR-0001 already relies on for `Pcg32Rng` itself, rather than the tables existing as a separately-packaged data asset two build pipelines could pick up out of sync. CI includes a golden-file check: re-running the generator against the checked-in inputs must reproduce the checked-in generated file byte-for-byte, catching silent drift (e.g. a libm/FMA difference between the machine that last regenerated it and CI's) before it ships, not after.
- **At runtime**: given `max_brick_hp`, look up its precomputed table (a plain array access — `max_brick_hp` is server/client-agreed level config, not itself rolled); roll `NextUInt32() % 10000`; find the bucket via cumulative integer thresholds → `brick_hp`, a plain `int`. Zero runtime float, zero `pow()`, zero floating `random()` draw.
- This fully removes brick-HP assignment from the "discrete output derived from continuous float math" risk category — the same category ADR-0002's `Fix32` closed for collision, achieved here without needing any new `Fix32` primitive at all.

### 5. Canonical roll order per turn (pinned — mirrors Runner's exact rationale for why this must be explicit, not left to implementation convenience)
1. Row-spawn roll (§2).
2. **[Corrected per independent review — the original draft left this ambiguous]** *If, and only if,* a row spawned this turn:
   a. One `brick_hp_roll` (§4) per brick cell in that row — exactly **7 cells** (Rule 1's 7-column grid), column-indexed `0` (leftmost) through `6` (rightmost), ascending, every spawned row fully populated with no gaps (per Rule 1 and the "7-column grid fully populated" Acceptance Criterion).
   b. Coin-spawn roll for that row (§3).
   c. Power-up-spawn roll for that row (§3).
3. If no row spawned this turn: **no further rolls happen** — steps 2a–2c are skipped entirely, not merely "rolled and discarded." A turn with no row spawn consumes exactly one `NextUInt32()` draw (the row-spawn roll itself); a turn with a row spawn consumes `1 + 7 + 1 + 1 = 10` draws, always in this order.

The original draft's Architecture Diagram drew the coin- and power-up-spawn rolls as unconditional siblings of the row-spawn branch, disagreeing with this section's prose (which only qualified the brick-HP roll). This is exactly the ambiguity Runner's Core Rule 4 rationale warns about: if one implementer reads coin/power-up rolls as gated on row-spawn and another reads the diagram literally as unconditional, the two consume a different number of draws on no-spawn turns and desync every subsequent roll for the rest of the run. **Resolved**: all three (brick-HP, coin, power-up) are gated on the row-spawn outcome, consistently in this prose and in the Architecture Diagram below — there is no rule anywhere for what row an unconditional coin/power-up roll would even be placing a pickup into on a turn where none spawned, so gating is also the only reading that is game-logically coherent, not just the lower-risk one.

All rolls draw from the same `Pcg32Rng` stream, in this exact order, every turn, on both client and server — never reordered for implementation convenience.

### Architecture Diagram
```
Super Ricochet turn resolution (inside SharedSimCore, same assembly as ADR-0001/0002)
  1. roll = NextUInt32() % 10000
     roll < spawn_density_threshold(level)?  ← plain int compare, no Fix32, no float
       │
       ├─ yes → new row spawns at top
       │         for each of the 7 cells, column 0→6 ascending:
       │           roll = NextUInt32() % 10000
       │           brickHp = BrickHpTable[max_brick_hp].Lookup(roll)  ← offline table, int lookup only
       │         roll = NextUInt32() % 10000; roll < CoinSpawnThreshold?     → coin spawns (max 1/row)
       │         roll = NextUInt32() % 10000; roll < PowerUpSpawnThreshold? → power-up spawns (max 1/row)
       │         [10 NextUInt32() draws consumed this turn: 1 (row) + 7 (brick HP) + 1 (coin) + 1 (power-up)]
       │
       └─ no  → no row this turn; no brick-HP/coin/power-up rolls happen — NOT rolled-and-discarded, skipped
                [1 NextUInt32() draw consumed this turn: the row-spawn roll only]
        │
        ▼
  Board state (rows, brick HP, pickups) — fully deterministic, Tier-2-replayable

Server verifier: same SharedSimCore assembly, same integer rolls, same table data,
replays {seed, inputs} to the identical row/brick-HP/pickup sequence.
```

### Key Interfaces
```csharp
// Extends SharedSimCore.dll — no new assembly, no new numeric type.
public static class RicochetSpawnRolls
{
    public const int Resolution = 10000;

    public static int SpawnDensityThreshold(int level) =>
        Math.Min(unchecked(4500 + (level - 1) * 300), 7500);   // unchecked: cross-platform-identical wraparound at extreme `level`, see Decision §2

    public const int CoinSpawnThreshold = 1400;
    public const int PowerUpSpawnThreshold = 500;

    public static bool Roll(IDeterministicRng rng, int threshold) =>
        (int)(rng.NextUInt32() % Resolution) < threshold;

    public static int RollBrickHp(IDeterministicRng rng, BrickHpTable table) =>
        table.Lookup((int)(rng.NextUInt32() % Resolution));
}

// Generated OFFLINE by a build-time tool (weight formula + largest-remainder
// apportionment, see Decision §4), then CHECKED IN as generated C# source and
// compiled directly into SharedSimCore.dll — client and server link the same
// compiled assembly, never independently regenerate or load a separate data
// asset. CI golden-file check: regenerating from checked-in inputs must
// reproduce this file byte-for-byte.
public sealed class BrickHpTable
{
    // cumulativeThresholds[i] = upper bound (out of Resolution) mapping to brickHp = i + 1
    // Invariant, asserted at generation time: cumulativeThresholds[^1] == Resolution.
    private readonly int[] cumulativeThresholds;
    public int Lookup(int roll) { /* first index where roll < cumulativeThresholds[i], + 1 */ }
}
```

## Alternatives Considered

### Alternative A: Keep `spawn_density`/coin/power-up as `NextFloat01() < probability` float comparisons, relying on ADR-0001's test-vectored `NextFloat01()`
- **Pros**: No reformulation needed; `NextFloat01()` already exists and is test-vectored.
- **Cons**: `NextFloat01()`'s test-vector guarantee covers the conversion itself, not FMA/compiler-optimization divergence in the *subsequent* comparison against a second float (`spawn_density`) — the same category of risk ADR-0002 and ADR-0013 both refused to carve exceptions for, regardless of how each individual float value is produced.
- **Rejection Reason**: Inconsistent with the project's own established, deliberately conservative posture for a negligible convenience gain.

### Alternative B: Solve `brick_hp_roll` with a new `Fix32` power/nth-root primitive, computed at runtime in fixed point
- **Pros**: No offline build step, no static data asset to maintain.
- **Cons**: `pow(x, 1.6) = pow(x^8, 1/5)` needs a fixed-point 5th-root — a materially larger new numeric primitive than `Fix32.FromRatio` (ADR-0013's supplementary spike was a single division), requiring its own substantial spike-gate proof, for a formula that only ever needs to pick 1 of at most 84 discrete outcomes.
- **Rejection Reason**: The offline precomputed-table approach solves the identical problem with zero new `Fix32` surface and zero new spike gate — strictly cheaper for an equivalent result.

### Alternative C: Leave `brick_hp_roll` unresolved; treat Tier-2 replay as best-effort for brick HP specifically, relying on Tier-1 clamping alone
- **Pros**: No engineering work now.
- **Cons**: Brick HP feeds the danger-line loss condition and Boss AI's per-hit damage accounting directly — a wrong brick-HP value is a genuinely different board state, not cosmetic drift. The project's own established bar (Runner's currency-parity argument in ADR-0013) already rejected downgrading anti-cheat coverage just because a fix takes more work.
- **Rejection Reason**: Same standard already applied to Runner's collision, applied consistently here.

### Alternative D: Increase `RESOLUTION` beyond 10000 for finer probability granularity
- **Cons**: Every constant in scope (`0.45`/`0.03`/`0.75`/`0.14`/`0.05`) is an exact multiple of `1/10000`; a larger resolution buys nothing.
- **Rejection Reason**: Unnecessary complexity with no corresponding benefit.

## Consequences

### Positive
- Closes all four gaps `level-difficulty-config-ricochet.md`'s own `/design-review` flagged, and formally elevates what was ad hoc GDD-level convention (Runner's fix) into a registered architectural pattern future systems can cite directly.
- Zero new `Fix32` surface, zero new spike gate — the cheapest possible closure given `NextUInt32()`'s determinism will be validated by ADR-0001's own CI test-vector requirement (not itself a new obligation this ADR adds).
- `brick_hp_roll`'s runtime cost drops (integer table lookup vs. `pow()` + `ceil()`), a minor performance win alongside the determinism fix.

### Negative
- Introduces one new build-time artifact (the offline brick-HP tables) that must be regenerated whenever `max_brick_hp`'s formula or its level-30 cap changes — a small but real new piece of pipeline discipline.
- The discretized table only approximates the original continuous `pow(x, 1.6)` shape at integer buckets — for small `max_brick_hp` values (early levels, e.g. `max_brick_hp = 6`) the "weighted toward low values" curve is necessarily coarser, having fewer buckets to distribute weight across. This is a permanent, structural approximation, not a transitional gap — the Validation Criteria below bound it, but do not eliminate it.

### Risks
- **Risk**: the offline table-generation script itself has an off-by-one bug in cumulative-threshold construction — the exact bug class already caught in Runner's obstacle-type-roll thresholds during this session's `/design-review` pass.
  **Mitigation**: **[Strengthened per independent review — the original draft promised this AC without actually specifying it]** the closed-form weight formula, largest-remainder apportionment rule, and build-time `Σ == Resolution` invariant (Decision §4) remove the ambiguity that kind of bug needs to hide in; a boundary-exact acceptance criterion (every roll 0..9999 resolves to exactly one bucket) plus the statistical distribution test (Validation Criteria below) both now exist, matching the precedent set for Runner's own threshold fix.
- **Risk [confirmed real per independent review, not just hypothetical]**: two independent regenerations of a `BrickHpTable` (different engineer, machine, or OS, different libm/FMA behavior in the offline double-precision step) could silently produce different bytes for the same `max_brick_hp`, with nothing previously pinning client and server to the same artifact — the ADR's own expectation that tables are regenerated whenever `max_brick_hp` changes made this a recurring risk, not a one-time one.
  **Mitigation**: the table is checked in as generated source compiled directly into `SharedSimCore.dll` (Decision §4) — client and server link the same compiled binary, never independently regenerate at build time; a CI golden-file check catches any drift between a checked-in table and what the generator currently produces before it ships.
- **Risk [new, per independent review]**: `SpawnDensityThreshold`'s integer arithmetic overflows `Int32` at extreme `level` values (≳7.16 million), and without explicit `unchecked` wrapping, a client and server built with different overflow-checking settings could diverge (one throwing, one wrapping) at that point.
  **Mitigation**: the multiply is wrapped in `unchecked` (Decision §2), matching ADR-0001's existing convention for PRNG-adjacent arithmetic; keeping `level` within a sane range upstream is `level-difficulty-config-ricochet.md`'s own responsibility, not this ADR's.
- **Risk**: the pinned roll order (Decision §5) is implemented inconsistently between client and server despite being specified here — in particular, a client/server pair disagreeing on whether coin/power-up rolls are gated on the row-spawn outcome, the exact ambiguity the original draft of this ADR itself contained until corrected.
  **Mitigation**: `RicochetSpawnRolls` lives in `SharedSimCore` only — both sides call the identical compiled function, the same "same DLL, not a reimplementation" discipline ADR-0001 already established; Decision §5 and the Architecture Diagram now agree explicitly on exactly which rolls are gated and how many draws each turn shape consumes.

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|------------|-------------|---------------------------|
| level-difficulty-config-ricochet.md | `spawn_density` float-comparison flagged as an unaddressed determinism risk | Decision §2 — exact integer threshold, no `Fix32` needed |
| level-difficulty-config-ricochet.md / super-ricochet.md | "row-spawn/brick-HP-roll/coin-spawn decisions" flagged together | Decision §3 (coin/power-up), §4 (brick HP) |
| super-ricochet.md Rule 3 | "fixed sub-stepping, seeded RNG for row/coin/power-up spawn rolls" — binding constraint, no roll order previously pinned | Decision §5 |
| super-ricochet.md Rule 8 | `brick_hp_roll` formula (`ceil(pow(random(), 1.6) × max_brick_hp)`) | Decision §4 — offline table replaces runtime `pow()` entirely |

## Performance Implications
- **CPU**: Negligible either way — replacing `pow()` + `ceil()` with an integer table lookup (binary search over at most 84 entries) is, if anything, cheaper than what it replaces.
- **Memory**: One small `int[]` per distinct `max_brick_hp` value (at most 84 entries × roughly 30 distinct tables ≈ a few KB total) — negligible.
- **Load Time**: Tables are compiled directly into `SharedSimCore.dll` (Decision §4) — no separate asset load, no measurable cost.
- **Network**: None.

## Migration Plan
Greenfield — no Super Ricochet spawn-generation code exists yet (same footing as ADR-0013's Migration Plan). The offline table-generation tool is added before implementation begins, not a retrofit of shipped code — though, per the Risks above, it is re-run (and its checked-in output regenerated) whenever `max_brick_hp`'s formula changes, not used only once.

## Validation Criteria
- CI test-vector suite (extending ADR-0001's existing `NextUInt32()` suite) covers exact boundary rolls for `spawn_density_threshold`, `CoinSpawnThreshold`, and `PowerUpSpawnThreshold` at multiple representative levels — mirroring the boundary-exact acceptance criteria added for Runner's own thresholds after the cumulative-vs-bucket-size bug was caught there.
- **New, per independent review**: a boundary-exact AC for `BrickHpTable` — every roll `0..9999` resolves to exactly one bucket (no gaps, no overlaps), verified exhaustively for at least the `max_brick_hp = 6` and `max_brick_hp = 84` tables, plus `cumulativeThresholds[last] == 10000` asserted for every generated table.
- Offline-generated brick-HP tables additionally validated by a statistical test comparing empirical distribution (many simulated rolls) against the original `pow(random(), 1.6)` continuous shape, within a documented tolerance, for at minimum the smallest (`max_brick_hp = 6`, level 1) and largest (`max_brick_hp = 84`, capped plateau) table sizes.
- **New, per independent review**: a CI golden-file check — re-running the offline table generator against its checked-in inputs reproduces the checked-in generated `.cs` file byte-for-byte, catching silent drift from a future regeneration on a different machine/OS/libm before it ships.
- **New, per independent review**: `SpawnDensityThreshold` is confirmed to wrap (not throw) identically under both `checked` and `unchecked` project-level build settings at a synthetic extreme `level` value near the `Int32` overflow boundary, confirming the explicit `unchecked` block (Decision §2) actually prevents the cross-platform divergence it's meant to.
- **New, per independent review**: a turn with no row spawn is confirmed to consume exactly one `NextUInt32()` draw, and a turn with a row spawn exactly ten, on both client and server — directly testing the roll-order/conditionality fix in Decision §5.
- A recorded real Super Ricochet run replays to the identical sequence of row-spawn/coin-spawn/power-up-spawn/brick-HP outcomes server-side, `tolerance_units = 0` for this path.

## Related Decisions
- ADR-0001 — `SharedSimCore`/`Pcg32Rng.NextUInt32()`, the RNG foundation this ADR's threshold rolls and table lookups both consume unchanged.
- ADR-0002 — the "no float in the scored path" discipline this ADR applies to Ricochet's spawn-generation math; whether that discipline was already meant to cover spawn-generation (per the registry's generically-worded forbidden-pattern entry) or is being extended here for the first time is left open (see Context).
- ADR-0013 — the sibling ADR that closed the identical category of gap for Quack Runner; this ADR generalizes Runner's ad hoc obstacle-type-roll fix into a registered project-wide pattern and cites it as precedent.
- `level-difficulty-config-ricochet.md`, `super-ricochet.md` — the GDDs this ADR resolves flagged gaps in.

## Open Questions
- Exact table-generation tolerance (how closely the discretized brick-HP table must match the original `pow(x, 1.6)` shape) is not numerically pinned here — needs a `/balance-check`-style pass once real gameplay-feel data exists, the same placeholder status Ricochet's other tuned constants (`boss_hp` cap, `max_brick_hp` cap) already carry.
- Whether Runner's obstacle-type roll (currently hand-maintained integer constants, not table-generated) should retroactively adopt this ADR's table-generation tooling for consistency is a possible future cleanup, not required now — Runner's existing fix already works correctly as hand-maintained constants, and this ADR does not propose changing code that isn't broken.
