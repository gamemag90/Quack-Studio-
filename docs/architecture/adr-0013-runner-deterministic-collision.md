# ADR-0013: Deterministic Collision Detection for Quack Runner

## Status
Proposed

## Date
2026-07-17

> **Independently reviewed (2026-07-17, general-purpose agent standing in
> for a Unity/deterministic-physics specialist)**: 3 blocking issues found
> and fixed. (1) The original draft's `obstacleSpeedFix32(t)` conversion
> produced a **per-second** rate (dividing raw px/s by 600) but the
> Architecture Diagram then advanced `y` by that value **every frame**
> with no `×frame_dt` step — a 60× speed bug that alone would have made
> the "no tunnelling risk" claim false at completely ordinary values of
> `t`. Fixed by adding the missing frame_dt multiplication explicitly.
> (2) Once corrected, the reviewer proved the unbounded-growth claim is
> genuinely, not just hypothetically, exploitable: `obstacleSpeed(t)`
> grows forever (+50/5s, no cap, per the sibling ramp GDD), so per-frame
> displacement **will** eventually exceed obstacle+duck height at some
> large but reachable `t` — since collision here is fully deterministic
> and Tier-2-replayable, this is a genuine anti-cheat/economy exploit
> (guaranteed survival + coin farming past that `t`), not a cosmetic
> glitch. A defensive engineering safety net is added below (§2), and
> `obstacle-spawn-difficulty-ramp-runner.md` is flagged with a new,
> escalated (confirmed-structural, not speculative) requirement for its
> own owner to cap `obstacleSpeed(t)`, mirroring the Ricochet
> `boss_hp`/`max_brick_hp` precedent — no specific cap value is invented
> here since this ADR has no obstacle/duck dimension data to compute one
> from. (3) The "no new spike gate required" claim (§4) was factually
> wrong against ADR-0002's own text — `Fix32.FromRatio` (division) is new
> surface ADR-0002's spike never exercised (only `+`/`-`/`*`/`IntSqrt`
> were in scope). Corrected to require a small supplementary spike for
> this specific operation. A recommended fix (drop the per-t-step cache
> entirely, given the conversion is already "negligible" cost even
> uncached) is also folded in.

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Unity 6.3 LTS client + server-side `.NET` replay verifier (extends `SharedSimCore`, ADR-0001/0002) |
| **Domain** | Core / Gameplay (deterministic) — Quack Runner |
| **Knowledge Risk** | LOW-MEDIUM — reuses ADR-0002's `Fix32` type/`SharedSimCore` pattern for comparison (no new risk there), but adds one genuinely new operation (`Fix32.FromRatio`) needing its own narrow spike (see Decision §4, corrected from the original draft's "no new spike needed" claim) |
| **References Consulted** | `quack-runner.md`, `obstacle-spawn-difficulty-ramp-runner.md`, ADR-0001, ADR-0002 |
| **Post-Cutoff APIs Used** | None |
| **Verification Required** | Same cross-platform CI test-vector discipline ADR-0002 established, extended to Runner's overlap test (comparison only, no spike needed) **and** to `Fix32.FromRatio` specifically (division, not covered by ADR-0002's own spike scope — needs its own byte-identical proof on iOS-ARM64 IL2CPP vs. the x86 CLI) |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-0001 (`SharedSimCore`, `Pcg32Rng`), ADR-0002 (`Fix32` Q16.16 type, `FRAME_DT` constant, the "no float in the scored path" discipline this ADR extends to a second mini-game) |
| **Enables** | Anti-Cheat Tier-2 replay verification for Quack Runner (currently gated on this ADR — see Open Questions this closes in `quack-runner.md` and `obstacle-spawn-difficulty-ramp-runner.md`) |
| **Blocks** | Quack Runner gameplay-code implementation for collision resolution specifically, same TD-condition pattern ADR-0001/0002 set for Super Ricochet |
| **Ordering Note** | **[Corrected 2026-07-17 per independent review]** A narrower, lower-risk extension of ADR-0002's existing machinery, not a new physics engine — but it does need a small supplementary spike for `Fix32.FromRatio` specifically (Decision §4), not zero new spike as the original draft claimed. Also surfaced a confirmed-structural gap in `obstacle-spawn-difficulty-ramp-runner.md` (unbounded `obstacleSpeed(t)`) that this ADR mitigates at the engineering layer but routes to that GDD to actually fix. |

## Context

### Problem Statement
`quack-runner.md` Core Rule 2 states collision detection must run on "sim-tick-authoritative duck position... inside `SharedSimCore`, never on render-interpolated position" — correctly identifying that *which position* is tested against is determinism-critical. But a `/design-review` pass (2026-07-17) found that both `quack-runner.md` and `obstacle-spawn-difficulty-ramp-runner.md` left a second, distinct question unaddressed: **is the overlap test itself (the AABB comparison) safe from cross-platform float drift?** A prior version of `quack-runner.md`'s Core Rule 2 asserted "AABB collision itself is simple," treating the comparison math as a non-issue — this is exactly the same category of claim ADR-0002's own Context section rejected for Super Ricochet's ball-vs-brick collision: `tolerance_units` absorbs continuous drift on a final scalar, never a discrete hit/miss disagreement at a near-boundary comparison, and IEEE-754 float comparison cannot be trusted to agree bit-for-bit across ARM and x86 at exactly that boundary.

This is not hypothetical for Runner specifically: `quack-runner.md`'s own Edge Cases already document a repeated per-tick float division (`obstacleSpeed(t) / 600`, converting the sibling GDD's raw-pixel speed constant into normalized playfield-height units) that both GDDs now flag as *itself* contributing to this risk if evaluated fresh every frame rather than once.

### Why this is a smaller problem than ADR-0002's
Super Ricochet needed full distance-based sub-stepping because a fast ball could tunnel through a thin brick between frames — solving that required both fixed-point math *and* a remaining-distance inner loop. Quack Runner has no tunnelling concern: the duck is stationary in Y (fixed lane position, only X moves via direct touch mapping) and obstacles descend slowly enough, relative to frame rate and obstacle size, that no prior review or the original prototype ever flagged a pass-through risk. **This ADR therefore reuses ADR-0002's `Fix32` type directly but does not need ADR-0002's distance-sub-stepping apparatus** — a single AABB overlap test per frame, in fixed-point, is sufficient.

### Requirements
- Duck-vs-obstacle AABB overlap outcomes (hit/miss) must be reproducible server-side from `{seed, input sequence}`, bit-exact — not `tolerance_units`-bounded, for the same reason ADR-0002 chose bit-exact over tolerance for Ricochet: a discrete branch cannot be partially correct.
- The normalized coordinate-space conversion (`obstacleSpeed(t)/600`, per `quack-runner.md`'s Edge Cases) must be computed in a way that cannot introduce platform-dependent rounding into the scored position.
- No new spike gate, no new custom physics engine — this is a direct, narrow extension of already-Proposed machinery.

## Decision

### 1. Duck and obstacle positions/dimensions live in `Fix32` (ADR-0002's Q16.16 type), inside `SharedSimCore` — reused verbatim, not reinvented
- The duck's X position (Core Rule 1: direct touch-position mapping, clamped to `[0, 1-duckWidth]`) and every obstacle's `(x, y, width, height)` are `Fix32` values, computed and stored inside `SharedSimCore`'s Runner sim state — never `float`/`double` in the scored path, per the registry's existing forbidden pattern (established by ADR-0002, now extended to a second consumer rather than narrowly scoped to Ricochet).
- **No new numeric type is introduced.** `Fix32`'s existing arithmetic (`+`, `-`, `*` with `Int64` intermediates, `unchecked`) is sufficient — Runner's collision math needs no square root, no trigonometry, nothing `Fix32` doesn't already support for Ricochet.

### 2. AABB overlap test is exact `Fix32` comparison — no distance sub-stepping needed
```csharp
// Inside SharedSimCore, Runner module. Exact fixed-point comparison — no
// tolerance, no branch-fork risk, since Fix32.Raw is a plain Int32 compare.
bool Overlaps(Fix32Rect duck, Fix32Rect obstacle) =>
    duck.Left   < obstacle.Right  &&
    duck.Right  > obstacle.Left   &&
    duck.Top    < obstacle.Bottom &&
    duck.Bottom > obstacle.Top;
```
- This runs **once per sim frame**, checked against that frame's `Fix32` duck/obstacle positions — not sub-stepped, per the Context section's rationale (no tunnelling risk exists at the game's *early-run* speed/geometry profile). **[Corrected 2026-07-17 per independent review — this assumption does NOT hold forever, confirmed by direct calculation, not just flagged as a future possibility]**: `obstacleSpeed(t) = 250 + floor(t/5)×50` grows without bound (no cap exists anywhere in `obstacle-spawn-difficulty-ramp-runner.md`), so per-frame Y-displacement (`obstacleSpeed(t) × frame_dt`, see the corrected §3 below) also grows without bound as `t` increases. At some large-but-reachable `t`, that displacement **will** exceed obstacle height + duck height, at which point an obstacle can cross the duck's row between two consecutive frames with the AABBs never overlapping in either — a true logical skip-through, not a rendering artifact. Because this collision model is fully deterministic and Tier-2-replayable, a skip-through at this point is **indistinguishable from a legitimate dodge** to the replay verifier — a deliberate long-survival strategy (scripted or human) could reach this `t` and then survive indefinitely, and Runner mints real currency (`quack-runner.md` Rule 7). This is a genuine anti-cheat/economy exploit path, not a cosmetic edge case. Two things follow, addressed separately since they're different layers of the same problem:
  1. **Engineering safety net (this ADR, this section)**: `SharedSimCore`'s Runner module computes each frame's Y-displacement and compares it against a configured maximum-safe-displacement constant (derived from the smallest obstacle/duck dimension once those are finalized at implementation — not invented here, since this ADR has no dimension data). If a frame's displacement would exceed that constant, the frame is treated as an anomaly: the sim clamps the displacement to the safe maximum for collision-testing purposes (never silently allowing an unchecked jump) and emits the same `mode=degraded` signal Anti-Cheat's other cap-exceeded paths already use (per `anti-cheat-replay-verification.md`'s Rule 9 pattern), rather than crashing or silently permitting the skip. This protects the *engineering* invariant (collision math never silently breaks) regardless of what the game-design formula does.
  2. **Game-design fix (routed to `obstacle-spawn-difficulty-ramp-runner.md`, not this ADR)**: the actual root cause is `obstacleSpeed(t)`'s unbounded growth itself — the same structural bug class this session already found and fixed for Ricochet's `boss_hp` and `max_brick_hp` (both capped, after being proven — not just suspected — to eventually break the game). `obstacleSpeed(t)` needs an analogous cap. No specific cap value is proposed here, since it depends on obstacle/duck dimensions this ADR doesn't own; flagged as a confirmed-structural (not speculative) requirement in that GDD's own Open Questions.
- Because `Fix32.Raw` is a plain `Int32`, the four comparisons above are ordinary integer comparisons — **bit-identical on every platform by construction**, with no separate proof needed the way `Fix32`'s multiply/sqrt operations needed one in ADR-0002. Comparison is the one `Fix32` operation that carries zero cross-platform risk once the operands themselves are already `Fix32`.

### 3. The coordinate-space conversion happens in `Fix32` — never a float divide, and never conflated with the per-frame time step
- `obstacleSpeed(t)` (the sibling GDD's integer formula, `250 + floor(t/5)×50`) is a rate in **pixels per second**, matching the prototype's original units. Converting it to normalized units is **one** fixed-point division: `Fix32 obstacleSpeedFix32PerSecond(int rawSpeed) => Fix32.FromRatio(rawSpeed, 600)` — computed via the same `Int64`-intermediate discipline `Fix32`'s multiply already uses, never `float`/`double` at any point.
- **[Corrected 2026-07-17 per independent review — a real bug in the original draft, not a labeling nitpick]**: this value is a **per-second** rate. Advancing an obstacle's Y position once per sim frame requires multiplying it by the fixed sim timestep: `y += obstacleSpeedFix32PerSecond(t) * FRAME_DT`, where `FRAME_DT = Fix32.FromRatio(1, 60)` (ADR-0002's existing 60Hz constant, reused here unchanged). The original draft's Architecture Diagram omitted this multiplication entirely, advancing `y` by the *per-second* value on *every frame* — a 60× speed error that would have made every obstacle fall off-screen in roughly one second at any `t`, and made the "no tunnelling risk" claim false immediately rather than only at some large `t`. Both the Decision text and the Architecture Diagram below are corrected to show `FRAME_DT` explicitly.
- **[Simplified 2026-07-17 per independent review]** The original draft cached `obstacleSpeedFix32PerSecond(t)` "once per distinct `t`-step" to avoid repeated conversion cost. Given the Performance Implications section already calls this conversion "negligible," caching adds a real state-management risk (cache-invalidation timing must itself be deterministic and identically triggered on client and server, or the two sides could momentarily disagree) for no measurable benefit. **The cache is dropped entirely** — `obstacleSpeedFix32PerSecond(t)` is recomputed fresh every frame from that frame's `t`, which is simpler, has one fewer piece of state to keep synchronized, and costs nothing worth optimizing away.

### 4. A small supplementary spike is required for `Fix32.FromRatio` specifically — corrected from the original "no new spike" claim
**[Corrected 2026-07-17 per independent review]** The original draft claimed no new spike was needed because "this ADR introduces no new operation `Fix32` doesn't already cover." That claim does not hold against ADR-0002's own text: ADR-0002's `Fix32` Key Interfaces and its spike-gate scope cover only `+`, `-`, `*` (with `Int64` intermediates), and a deterministic `IntSqrt` — **division is not among them**, and ADR-0002's spike explicitly targets signed-overflow/wraparound behavior and `IntSqrt`'s rounding convention specifically, never a ratio/division operation. `Fix32.FromRatio` (needed only by this ADR, not by Ricochet's ball physics) is genuinely new surface.
- **Comparison** (`Overlaps()`, Decision §2) needs no spike — comparing two `Int32` raw values for ordering is unconditionally bit-identical on every platform; there is no rounding or overflow mode involved at all, unlike multiply/divide/sqrt.
- **`Fix32.FromRatio`** (Decision §3) does need verification: a small supplementary CI test vector proving `Fix32.FromRatio(rawSpeed, 600)` produces byte-identical results on an actual iOS-ARM64 IL2CPP build and the x86 verification CLI, across the range of `rawSpeed` values `obstacleSpeed(t)` can actually produce. This is a narrow, bounded addition to ADR-0002's existing spike infrastructure (same CI harness, one more operation's test vectors) — not a new custom-engine-scale spike with its own staffing budget.
- **If this narrow division spike fails** (unlikely given `FromRatio` is a single fixed-point divide, far simpler than `IntSqrt`, but not assumed): the fallback is the same Alternative D (statistical anti-cheat, client-authoritative physics) ADR-0002 already established, applied to Runner's collision path specifically — it does not need to imply Ricochet's own spike failed too, since the two are now independently verified operations sharing one CI harness, not one monolithic pass/fail gate.

### Architecture Diagram
```
Quack Runner sim frame (inside SharedSimCore, same assembly as Ricochet's physics)
  duck.X (Fix32, from touch input mapped to Fix32 once per sample)
  perSecondRate = Fix32.FromRatio(obstacleSpeed(t), 600)   ← recomputed fresh every frame, not cached
  frameDelta    = perSecondRate * FRAME_DT                 ← FRAME_DT = Fix32.FromRatio(1, 60), ADR-0002's constant
  obstacle[i].y += frameDelta                              ← [CORRECTED 2026-07-17: was missing ×FRAME_DT]
        │
        │  safety net: if frameDelta > maxSafeDisplacement (TBD at implementation,
        │  from actual obstacle/duck dimensions) → clamp for collision-testing,
        │  emit mode=degraded (per anti-cheat-replay-verification.md Rule 9 pattern)
        ▼
  Overlaps(duckRect, obstacleRect)?  ← plain Fix32.Raw (Int32) comparison,
        │                              bit-identical by construction, no spike needed
        ├─ yes, coin       → +score, +coinsCollected, remove obstacle
        ├─ yes, non-coin   → health -1, run ends if 0 (per quack-runner.md Rules 3-4)
        └─ no              → dodge-bonus path if obstacle exits off-screen (Rule 5)

Client (visual): renders from the same Fix32 state, converted to float ONLY for
display (matching ADR-0002's kinematic-rendering pattern) — never fed back.
Server verifier: same SharedSimCore assembly, same Fix32 comparisons, replays
{seed, inputs} to the identical hit/miss sequence.
```

### Key Interfaces
```csharp
// Extends SharedSimCore.dll — same assembly, same Fix32 type as ADR-0002.
// No new project/assembly, no new numeric type except FromRatio (new spike-
// vector coverage required, see Decision §4).
public readonly struct Fix32Rect {
    public readonly Fix32 Left, Top, Right, Bottom;
}

public interface IRunnerCollision {
    bool Overlaps(Fix32Rect a, Fix32Rect b);                    // exact Fix32 comparison, no spike needed
    Fix32 ObstacleSpeedFix32PerSecond(int rawObstacleSpeed);    // Fix32.FromRatio(raw, 600) — recomputed every frame, NOT cached
}
// FRAME_DT (Fix32.FromRatio(1, 60)) is ADR-0002's existing 60Hz constant, reused unchanged.
```

## Alternatives Considered

### Alternative A: Keep AABB collision in float, rely on `tolerance_units` for any observed drift
- **Cons**: This is the exact reasoning ADR-0002 already rejected for Ricochet ("the crux" section) — a discrete hit/miss branch fork cannot be partially absorbed by a numeric tolerance. Applying the same rejected reasoning to a second mini-game just because its geometry is simpler doesn't make the underlying float-comparison risk go away.
- **Rejection Reason**: Structurally identical to ADR-0002's own rejected Alternative A; no new argument favors it here just because Runner's collision shape is simpler.

### Alternative B: A full ADR-0002-style distance-sub-stepping treatment for Runner too
- **Cons**: Runner has no tunnelling risk (Context section) — sub-stepping would add real implementation complexity (an inner loop, a remaining-distance accumulator) to solve a problem Runner doesn't have.
- **Rejection Reason**: Disproportionate. `Fix32` alone (comparison-only, no sub-stepping) closes the actual risk (float branch-fork) without importing machinery sized for a problem (tunnelling) this mini-game doesn't have.

### Alternative C: Leave Runner's collision in float, accept it as an unverified risk, and gate Tier-2 replay coverage down for Runner specifically (statistical anti-cheat only for this mini-game)
- **Pros**: No engineering work now.
- **Cons**: Runner mints real currency (per `quack-runner.md` Rule 7 — coins credit via `creditMultiplied`), the same stakes Ricochet has. Downgrading its anti-cheat coverage because its physics is simpler, rather than because a real risk assessment supports it, is an inconsistent bar between the two mini-games sharing the same economy.
- **Rejection Reason**: The fix here (reuse `Fix32`, comparison-only) is cheap enough that accepting the risk isn't justified by the cost of closing it.

## Consequences

### Positive
- Runner's Tier-2 replay verification becomes trustworthy on the same footing as Ricochet's, closing the gap two GDDs independently flagged this session.
- Almost all of this rides on ADR-0002's already-Proposed (pending-spike) `Fix32` machinery — only one genuinely new operation (`FromRatio`) needs its own narrow CI test-vector addition (§4).
- The coordinate-conversion fix (§3) also closes a real, separate risk (repeated float division, and a since-corrected frame-timestep error) that was flagged as still-live even after the unit choice itself was already correct.
- The independent review's discovery that `obstacleSpeed(t)`'s unbounded growth eventually breaks collision determinism entirely — not just a hypothetical — surfaced a real anti-cheat/economy exploit before any code existed to exploit it.

### Negative
- `SharedSimCore` now serves two mini-games' collision needs instead of one — a marginally larger shared-library surface, though no new consumer-specific complexity (Runner's `Fix32Rect`/`Overlaps` is simpler than Ricochet's ball physics, not more).
- The engineering safety net (Decision §2) treats a symptom (unsafe per-frame displacement) that has a game-design root cause (`obstacleSpeed(t)` uncapped) this ADR cannot itself fix — the net prevents a crash/exploit but does not restore the intended difficulty curve at extreme `t`; the actual fix is the ramp GDD's own required cap, routed there, not solved here.

### Risks
- **Risk [confirmed real, not hypothetical, per independent review]**: `obstacleSpeed(t)`'s unbounded growth eventually produces a per-frame displacement exceeding obstacle/duck height, letting an obstacle skip past the duck's row undetected — replayable, hence indistinguishable from a legitimate dodge to Tier-2, and exploitable for unlimited survival + coin farming on a currency-minting system.
  **Mitigation**: Engineering safety net (Decision §2: displacement clamped for collision-testing + `mode=degraded` signal past a configured maximum) prevents a silent break; the actual game-design fix (capping `obstacleSpeed(t)`, mirroring Ricochet's `boss_hp`/`max_brick_hp` precedent) is routed to `obstacle-spawn-difficulty-ramp-runner.md` as a confirmed-structural requirement, not an optional tuning nice-to-have.
- **Risk**: `Fix32.FromRatio(rawSpeed, 600)` is implemented as a fresh float divide-then-convert internally, silently reintroducing the exact risk this ADR closes.
  **Mitigation**: The narrow supplementary CI test vector required by Decision §4 specifically targets this operation across the range of values `obstacleSpeed(t)` can produce, asserted byte-identical between the Unity client build and the verification CLI.
- **Risk**: `Overlaps()` boundary semantics (strict `<`/`>` vs. `<=`/`>=`) differ between a naive client implementation and the shared `SharedSimCore` version if a contributor doesn't realize both must call the same function.
  **Mitigation**: `Overlaps()` lives in `SharedSimCore` only — both client and server call the identical compiled function, the same "same DLL, not a reimplementation" pattern ADR-0001/0002 already established.

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|------------|-------------|---------------------------|
| quack-runner.md | Core Rule 2: collision determinism — "AABB collision itself is simple" claim | Corrected: comparison math is safe only because operands are `Fix32`, not because AABB tests are inherently simple in any numeric type |
| quack-runner.md | Edge Case: `obstacleSpeed(t)/600` coordinate conversion, flagged as a live risk contributor | §3: `Fix32` ratio conversion, explicit `×FRAME_DT` step, never a repeated float divide |
| quack-runner.md | Open Question 6: collision-detection determinism needs its own ADR | This ADR |
| obstacle-spawn-difficulty-ramp-runner.md | Core Rule 6 / Open Question 4: same gap, flagged from the ramp GDD's side | This ADR (shared resolution), plus a new confirmed-structural requirement routed back to that GDD (§2) |

## Performance Implications
- **CPU**: Negligible — one `Fix32` comparison (four `Int32` compares) per duck/obstacle pair per frame; the ratio conversion (recomputed every frame, uncached per §3's simplification) is a single fixed-point multiply/divide, also negligible.
- **Memory**: Negligible — `Fix32Rect` is four `Int32`s per rect; no cache state to maintain.
- **Load Time**: None.
- **Network**: None new.

## Migration Plan
Greenfield — no Quack Runner collision code exists yet (per this session's `/design-review` pass, Runner's GDDs were reviewed before implementation began). This ADR lands before the first line of Runner collision code, not as a retrofit.

## Validation Criteria
- CI test-vector suite (extending ADR-0002's existing suite) covers Runner-specific cases: duck-at-obstacle-edge (boundary overlap), duck-fully-inside-obstacle, obstacle-just-exiting-screen — asserted byte-identical between an actual IL2CPP-built Unity client and the standalone verification CLI, not just Editor Play Mode.
- **New, per independent review**: `Fix32.FromRatio` test vectors covering the range of `rawSpeed` values `obstacleSpeed(t)` can produce, asserted byte-identical on both platforms — this operation was not covered by ADR-0002's own spike scope and needs its own proof.
- `obstacleSpeedFix32PerSecond(t) * FRAME_DT` produces identical `Fix32` per-frame displacement values on both platforms at representative `t` values, confirming the corrected (frame_dt-inclusive) math, not the original draft's 60×-too-fast version.
- **New, per independent review**: at a deliberately large `t` (simulating extended survival), the engineering safety net (Decision §2) actually engages — displacement is clamped and `mode=degraded` fires — rather than silently allowing an unchecked skip-through.
- A recorded real Runner run replays to the same hit/miss sequence and final score server-side, `tolerance_units = 0` for this path (matching Ricochet's precedent once both ADR-0002's spike and this ADR's supplementary `FromRatio` spike pass).

## Related Decisions
- ADR-0001 — `SharedSimCore` foundation, `Pcg32Rng` this ADR's obstacle-type/position rolls already depend on (per the sibling ramp GDD).
- ADR-0002 — `Fix32` type and the "no float in the scored path" discipline this ADR extends to a second mini-game; the 60Hz `FRAME_DT` constant reused unchanged; the spike-gate pattern this ADR's supplementary spike follows (narrower scope, same CI harness).
- `quack-runner.md`, `obstacle-spawn-difficulty-ramp-runner.md` — the GDDs this ADR resolves an Open Question in (and, for the ramp GDD, adds a new confirmed-structural requirement to).

## Open Questions
- **Exact `obstacleSpeed(t)` cap value** — routed to `obstacle-spawn-difficulty-ramp-runner.md` as a new, confirmed-structural (not speculative) requirement; this ADR cannot propose a number without real obstacle/duck dimension data it doesn't own.
- **Exact maximum-safe-displacement constant** for the engineering safety net (Decision §2) — depends on the same dimension data, set at implementation once obstacle/duck sprites are finalized.
- Exact `Fix32Rect` construction from the duck's touch-input sample (how the raw input coordinate is quantized to `Fix32` at the point of sampling) — an implementation detail, not load-bearing for this ADR's decision, deferred to implementation.
