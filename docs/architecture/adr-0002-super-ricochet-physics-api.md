# ADR-0002: Super Ricochet Physics API Choice + Deterministic Outcome-Physics

## Status
REJECTED (2026-07-10) — superseded by `adr-0002-deterministic-fixedpoint-physics.md`.

> Rejected on independent review for three blocking flaws: (1) a fixed *time*
> step silently breaks the GDD's fixed *distance* half-radius tunnelling
> guarantee when ball speed varies (e.g. after the min-vertical-velocity
> nudge); (2) single-precision float collision math is not reliably
> bit-consistent across iOS-ARM and x86 server CPUs, and `tolerance_units`
> cannot absorb a discrete hit/miss branch fork; (3) a custom physics engine
> was mis-scoped as an "extension" of the RNG ADR. Retained only as a
> rejected-alternatives record. Do not implement against this file.

## Date
2026-07-10

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Unity 6.3 LTS (build `6000.3.0f1`) |
| **Domain** | Physics (2D) |
| **Knowledge Risk** | HIGH — project pin is post-LLM-cutoff per `VERSION.md` |
| **References Consulted** | `docs/engine-reference/unity/breaking-changes.md`, `current-best-practices.md`, `deprecated-apis.md` |
| **Post-Cutoff APIs Used** | None directly — the new `UnityEngine.LowLevelPhysics2D` (Box2D v3) API was considered and deliberately **not** adopted for gameplay-critical simulation (see Decision); the legacy `Rigidbody2D`/`Collider2D` API used here predates the training cutoff and is well-documented |
| **Verification Required** | Same CI shared test-vector discipline as ADR-0001, extended to cover the deterministic physics module's ball-vs-brick collision outcomes, not just RNG draws |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-0001 (Deterministic RNG strategy) — this ADR extends `SharedSimCore` with a physics module using the same discipline (integer-only where possible, `.NET Standard 2.1`, CI test vectors) |
| **Enables** | Boss AI/Damage Model's per-hit event architecture (future ADR) — hit events now originate from `SharedSimCore`, not from Unity collision callbacks directly |
| **Blocks** | Any Super Ricochet gameplay-code implementation (same TD condition as ADR-0001) |
| **Ordering Note** | Must be Accepted before Super Ricochet's ball/brick GameObjects are implemented, since it changes which system owns ball position |

## Context

### Problem Statement
Super Ricochet's GDD (`super-ricochet.md`, Rule 3) requires ball physics with fixed sub-stepping (half a ball radius per sub-step) to make tunnelling structurally impossible and to make Anti-Cheat's Tier-2 replay verification possible. Unity 6.3 offers two coexisting 2D physics APIs — the mature high-level `Rigidbody2D`/`Collider2D`, and the new low-level `UnityEngine.LowLevelPhysics2D` (Box2D v3, advertised as more deterministic and multithreaded but with zero training-data coverage and a small community).

Initial framing of this ADR treated it as "which Unity API to use." That framing is **incomplete**: ADR-0001's `SharedSimCore` deliberately has no `UnityEngine` dependency (it must run in a standalone CLI executable server-side for Tier-2 replay). Neither Unity physics API — high-level or low-level — can execute inside that CLI at all. Whichever API drives the client's ball movement, the server still cannot replay it. This ADR therefore resolves two coupled questions: (1) which Unity API renders the client's physics, and (2) how gameplay-outcome physics (the trajectory that actually determines hit count, boss damage, and score) is made server-replayable.

### Constraints
- Same as ADR-0001: no dedicated Unity specialist subagent exists in this environment; favor mature, well-documented APIs over brand-new ones where the tradeoff is close, since debugging a HIGH-risk API alone is materially harder.
- `SharedSimCore` (ADR-0001) has no `UnityEngine` dependency — any gameplay-outcome-determining physics must live there, in plain C#, not in `Rigidbody2D`/`Box2D`.
- Must preserve GDD Rule 3's structural tunnelling-impossibility guarantee (half-ball-radius sub-stepping) exactly, not approximately.
- `tolerance_units` (ADR-0001, `anti-cheat-replay-verification.md`) already exists specifically to absorb legitimate float-precision drift — this ADR does not need to achieve bit-exact physics, only within-tolerance physics, consistent with that existing design.

### Requirements
- Ball-vs-brick collision outcomes (which bricks are hit, in what order/count per turn) must be reproducible server-side from `{seed, input sequence}` within `tolerance_units`.
- Client-side visual ball movement must not visibly desync from the scored/replayed outcome (a ball that visually misses a brick but is scored as a hit — or vice versa — breaks player trust).
- Sub-step granularity must preserve the GDD's half-ball-radius tunnelling guarantee.

## Decision

**Two-part decision:**

### Part 1 — `SharedSimCore` becomes authoritative for ball position and collision outcome
Extend `SharedSimCore` (ADR-0001, `.NET Standard 2.1`, no `UnityEngine` dependency) with a deterministic 2D physics module:
- Circle-vs-AABB collision (ball vs. brick, ball vs. board walls), fixed sub-stepping at `sub_step_dt = (0.5 × ball_radius) / ball_speed`. Since `ball_radius = 0.15 × cell_size` and `ball_speed = 11 × cell_size` (both from `super-ricochet.md`), `cell_size` cancels: **`sub_step_dt ≈ 0.006818s` (~146.7 sub-steps/sec), a fixed constant independent of screen resolution.**
- Deterministic elastic-bounce reflection (velocity reflected across the collision normal, no restitution/friction material variance).
- This module computes the **canonical, scored trajectory**: which bricks are hit, in what count, per turn — the same data Boss AI/Damage Model and the reward formula consume.
- Same discipline as ADR-0001's RNG: `unchecked` integer arithmetic where used, single-precision float elsewhere (accepting `tolerance_units`-bounded drift, consistent with the existing GDD constant — not a new gap), and a CI shared test-vector suite (fixed `{seed, inputs}` → expected hit sequence) run identically client-side and in the verification CLI.

### Part 2 — Unity's `Rigidbody2D`/`Collider2D` (high-level API) drives client-side rendering only, kinematically synced to `SharedSimCore`
- Ball `GameObject`s use `Rigidbody2D` in **Kinematic** mode (not full dynamic simulation) — each sub-step, the client reads `SharedSimCore`'s computed position and applies it directly (`Rigidbody2D.MovePosition`), rather than letting Unity's physics solver independently simulate the ball.
- `Collider2D`/`Rigidbody2D` in this kinematic role is chosen over the new low-level `UnityEngine.LowLevelPhysics2D` API specifically because, in this design, neither API is gameplay-outcome-authoritative — they're used only for trigger-collision convenience (visual feedback, e.g. brick-shatter debris particles) and for the mature, well-documented `MovePosition`/kinematic workflow. Given the HIGH knowledge-risk on the low-level API and the absence of a Unity specialist to debug it, the well-trodden high-level kinematic pattern is lower-risk for equivalent value here.
- This eliminates the sync-drift risk entirely: there is exactly **one** authoritative simulation (`SharedSimCore`); Unity's physics system never independently decides where a ball goes.

### Architecture Diagram
```
Unity Client                                    .NET Verification CLI (ADR-0001)
┌─────────────────────────────┐                 ┌───────────────────────────┐
│ Super Ricochet gameplay        │                │ replay-verifier.exe         │
│  turn loop (per sub-step):     │                │  receives {seed, inputs}    │
│   1. SharedSimCore.Step(dt) ───┼── same DLL ────┼─▶ SharedSimCore.Step(dt)     │
│      → new ball positions,     │  (asmdef ref)   │    → canonical hit sequence,│
│        collision/hit events    │                 │      final score            │
│   2. Rigidbody2D.MovePosition  │                 └───────────────────────────┘
│      (kinematic) — visual only │
│   3. Hit events → Boss AI       │
│      (−1 HP per hit, per GDD)   │
└─────────────────────────────┘
```

### Key Interfaces
```csharp
// SharedSimCore.dll — extends ADR-0001's library, still .NET Standard 2.1, no UnityEngine deps
public interface IDeterministicPhysics2D
{
    void Step(float subStepDt);                 // sub_step_dt = (0.5 * ball_radius) / ball_speed
    IReadOnlyList<BallState> Balls { get; }
    IReadOnlyList<HitEvent> ConsumeHitEvents();  // bricks hit this step, count only — order not scored (see Boss AI GDD: simultaneous hits just sum)
}

public readonly struct BallState { public float X, Y, Vx, Vy; public bool Retired; }
public readonly struct HitEvent { public int BrickId; }
```
```csharp
// Unity client (SuperRicochetController.cs) — references SharedSimCore.dll via .asmdef
void FixedUpdate() {
    _core.Step(SharedSimConstants.SubStepDt);
    foreach (var ball in _core.Balls)
        _ballRigidbodies[ball.Id].MovePosition(new Vector2(ball.X, ball.Y)); // kinematic, visual only
    foreach (var hit in _core.ConsumeHitEvents())
        BossDamageModel.ReportHit(hit.BrickId); // per boss-ai-damage-model.md Core Rule 1
}
```

## Alternatives Considered

### Alternative A: New low-level `UnityEngine.LowLevelPhysics2D` (Box2D v3) as the authoritative simulation
- **Description**: Use the new low-level API client-side, and separately reimplement the same Box2D v3 physics in `SharedSimCore` for server replay.
- **Pros**: Explicitly advertised as more deterministic/multithreaded; the low-level API's manual-stepping design is a natural fit for the GDD's sub-stepping requirement.
- **Cons**: Box2D v3 itself would need to be reimplemented in `SharedSimCore` to be server-replayable anyway (Unity's implementation can't run outside Unity) — same reimplementation-divergence risk ADR-0001 rejected for RNG (Alternative A there), now applied to a much more complex system (full 2D physics, not a PRNG). Also HIGH knowledge-risk with no community/documentation depth to lean on.
- **Rejection Reason**: Doesn't avoid the reimplementation problem (physics still needs a `SharedSimCore` port regardless of which Unity API is chosen), while adding real API-maturity risk for no corresponding benefit.

### Alternative B: Full independent `Rigidbody2D` dynamic simulation client-side, reconciled against `SharedSimCore` only for scoring (not position)
- **Description**: Let Unity's physics solver fully own ball movement (dynamic `Rigidbody2D`, not kinematic); run `SharedSimCore` as a parallel "shadow" simulation purely to compute the scored outcome, accepting that visuals and scored outcome may diverge slightly.
- **Pros**: Simpler client code — no manual `MovePosition` synchronization each sub-step.
- **Cons**: Two independent simulations (Unity's solver + `SharedSimCore`) can visibly diverge — a ball that looks like it missed a brick could be scored as a hit. This is exactly the "visible desync breaks player trust" failure mode identified in Requirements.
- **Rejection Reason**: The kinematic-sync approach (chosen Decision) achieves the same rendering quality without the desync risk, at a modest implementation cost (one `MovePosition` call per sub-step).

### Alternative C: Headless Unity physics re-simulation server-side (extends ADR-0001's rejected Alternative B)
- **Description**: Instead of a shared deterministic physics module, run an actual headless Unity build server-side to re-simulate ball physics using real `Rigidbody2D`/Box2D.
- **Pros**: True fidelity — no separate physics reimplementation needed.
- **Cons**: Same rejection basis as ADR-0001's Alternative B — heavyweight, conflicts with the Anti-Cheat GDD's own flagged re-simulation cost concern, and reintroduces the "second heavyweight runtime" problem ADR-0001 specifically avoided.
- **Rejection Reason**: Consistent with ADR-0001's precedent; no new argument favors it here.

## Consequences

### Positive
- Exactly one authoritative simulation (`SharedSimCore`) — no possibility of visual/scored-outcome divergence.
- Reuses ADR-0001's established discipline (shared C# core, CI test vectors, `.NET Standard 2.1` target) rather than inventing a new pattern.
- Avoids the HIGH-risk low-level physics API entirely for gameplay-critical code, consistent with this project's "no Unity specialist available" constraint.
- The half-ball-radius sub-stepping guarantee (tunnelling impossibility) is enforced once, in `SharedSimCore`, not duplicated/re-verified across two physics systems.

### Negative
- `SharedSimCore` now owns real gameplay logic (collision, bounce), not just RNG — a larger, more consequential shared-library surface than ADR-0001 anticipated.
- Kinematic `Rigidbody2D` sync adds a small per-sub-step coupling cost (`MovePosition` call per ball, ~147 times/sec during active volleys) — expected negligible at this ball-count scale, but not yet profiled.
- Writing a correct deterministic 2D collision/bounce model is nontrivial custom code, not "just call Unity's physics" — real implementation risk, mitigated by the CI test-vector requirement.

### Risks
- **Risk**: `SharedSimCore`'s custom collision math has a bug (e.g., incorrect circle-vs-AABB edge case) that Unity's battle-tested Box2D would not have had.
  **Mitigation**: CI test-vector suite must include edge cases (corner hits, simultaneous multi-brick hits, near-boundary sub-step landings), not just happy-path seeds.
- **Risk**: Kinematic `MovePosition` visual sync introduces a one-sub-step (~6.8ms) rendering lag versus `SharedSimCore`'s internal state.
  **Mitigation**: Acceptable at ~147Hz sub-step rate (imperceptible); revisit only if playtesting shows visible stutter.
- **Risk**: `SharedSimCore` growing to own both RNG and physics increases the blast radius of any bug in that one library.
  **Mitigation**: Keep `IDeterministicRng` and `IDeterministicPhysics2D` as separate interfaces/files within the same assembly, independently unit-tested.

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|------------|-------------|---------------------------|
| super-ricochet.md | Rule 3: fixed sub-stepping (half ball radius) makes tunnelling structurally impossible and enables Tier-2 replay | `SharedSimCore.IDeterministicPhysics2D.Step()` enforces this exactly, once, shared client+server |
| super-ricochet.md | Rule 4: minimum-vertical-velocity enforcement | Implemented inside `SharedSimCore`'s `Step()`, same authoritative location |
| boss-ai-damage-model.md | Core Rule 1: every brick hit deals exactly 1 boss damage, regardless of order; simultaneous hits in one tick each count individually | `HitEvent` list from `ConsumeHitEvents()` is order-independent by design — matches the GDD's explicit "count, not order" rule |
| anti-cheat-replay-verification.md | Tier 2 replay requires reproducible physics outcomes, not just RNG | `SharedSimCore` extension makes ball trajectory/collision replayable via the same CLI mechanism ADR-0001 established |

## Performance Implications
- **CPU**: `SharedSimCore.Step()` at ~147Hz during active volleys (max 12s per GDD Rule 5) is the main new cost, client-side and (for verification) server-side. Not yet profiled — flagged for the future "server-side headless replay re-simulation architecture" ADR's latency budget.
- **Memory**: Negligible — ball/brick state is small structs.
- **Load Time**: None.
- **Network**: None — consistent with ADR-0001 (local child-process verification, no new network surface).

## Migration Plan
N/A — greenfield, same TD condition as ADR-0001 (written before any Super Ricochet gameplay code exists).

## Validation Criteria
- CI test-vector suite covers: straight shots, corner/edge hits, simultaneous multi-brick hits, near-boundary landings — asserted identical between the Unity client build and the verification CLI.
- Manual playtest confirms no visible desync between rendered ball position and scored hit events.
- A recorded real run replays to the same hit count/score server-side within `tolerance_units`.

## Related Decisions
- ADR-0001 (Deterministic RNG strategy) — `SharedSimCore` foundation this ADR extends.
- Future: "Boss AI damage event architecture" ADR — will detail how `HitEvent`s flow from `SharedSimCore` into the boss HP state machine in more implementation depth.
- Future: "Server-side headless replay re-simulation architecture" ADR — will define the concrete performance budget for `SharedSimCore.Step()` under server load.

## Open Questions
- Whether kinematic `MovePosition` sync introduces any perceptible input-lag on real mobile hardware (vs. desktop/editor testing) is unverified — flagged for playtest once a build exists.
- Exact Unity rendering approach for the aim-assist trajectory preview (`super-ricochet.md` Open Question) is unaffected by this ADR and remains open there.
