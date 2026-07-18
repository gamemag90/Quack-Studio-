# ADR-0002: Deterministic Fixed-Point Physics Engine for Super Ricochet

## Status
Proposed

## Date
2026-07-10

> **Supersedes an earlier rejected draft** (`adr-0002-super-ricochet-physics-api.md`).
> That draft framed the decision as "which Unity physics API" and proposed a
> float-based, time-stepped simulation as an "extension" of ADR-0001's RNG
> library. Independent review rejected it for three blocking reasons — all
> addressed here: (1) a fixed *time* step silently breaks the GDD's fixed
> *distance* tunnelling guarantee when ball speed varies; (2) single-precision
> float collision math cannot be relied on to agree on discrete hit/miss
> decisions across iOS-ARM and x86 server CPUs, and `tolerance_units` does not
> absorb a branch fork; (3) hand-rolling a physics engine is a first-class
> decision, not an RNG footnote. The prior file is retained only as a rejected-
> alternatives record.

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Unity 6.3 LTS (build `6000.3.0f1`) |
| **Domain** | Physics (2D) |
| **Knowledge Risk** | HIGH — project pin is post-LLM-cutoff per `VERSION.md` |
| **References Consulted** | `docs/engine-reference/unity/breaking-changes.md`, `current-best-practices.md`, `deprecated-apis.md` |
| **Post-Cutoff APIs Used** | None. The new `UnityEngine.LowLevelPhysics2D` (Box2D v3) is considered and rejected for outcome-critical simulation; the legacy `Rigidbody2D`/`Collider2D` used here (visual-only, kinematic) predates the cutoff and is well-documented |
| **Target Framework** | `SharedSimCore` stays `.NET Standard 2.1`. Fixed-point math uses `Int32` (Q16.16) with `Int64` multiply intermediates only — **no `Int128` dependency** (avoids any uncertainty about IL2CPP `Int128` support), no `float`/`double`, no `unsafe`, no `Span<T>` in the scored path |
| **Verification Required** | A cross-platform CI test vector must prove an **iOS-ARM64 IL2CPP client build and the x86 server CLI produce byte-identical hit sequences** for the same `{seed, inputs}` — this is the load-bearing proof and gates the whole approach (see Risk & Staffing Budget) |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-0001 (Deterministic RNG) — reuses its `SharedSimCore` library, its CLI-child-process replay mechanism, and its CI shared-test-vector discipline |
| **Enables** | Boss AI/Damage Model event architecture (future ADR); any future physics-based mini-game |
| **Blocks** | All Super Ricochet gameplay-code implementation (TD condition from `architecture.md`) |
| **Ordering Note** | The **spike gate** (see Risk & Staffing Budget) must pass before this ADR moves from Proposed to Accepted |

## Context

### Problem Statement
Super Ricochet (`super-ricochet.md`, Rule 3) requires ball physics that (a) make tunnelling through bricks *structurally impossible* via sub-stepping at half a ball radius, and (b) are re-simulable server-side for Anti-Cheat Tier-2 replay verification. ADR-0001's `SharedSimCore` has no `UnityEngine` dependency by design (it must run in a standalone server CLI), so **no Unity physics API — high-level or low-level — can produce the scored, replayable trajectory**; that logic must live in `SharedSimCore` as plain C#. The core difficulty is not "which Unity API" but "how to make ball-vs-brick collision outcomes bit-reproducible across an ARM mobile client and an x86 server."

### Why float + tolerance is not enough (the crux)
`tolerance_units` absorbs *continuous drift on a final scalar*. It does **not** absorb a *discrete hit/miss disagreement*: if, at a near-boundary sub-step, the ARM client computes "hit" and the x86 server computes "miss" (a legitimate possibility with IEEE-754 single precision plus FMA-contraction differences between compilers/architectures), the two trajectories fork and diverge without bound. No small integer tolerance can reconcile a branch fork. Therefore the scored simulation must be **bit-exact by construction**, which rules out floating point in the scored path.

### Constraints
- `SharedSimCore` must remain `UnityEngine`-free and `.NET Standard 2.1`.
- No dedicated Unity specialist subagent exists in this environment — but note this cuts toward *not* trusting a brand-new engine API, not toward avoiding custom code; a fully self-contained integer simulation is in fact *more* debuggable/testable in isolation than an opaque engine physics backend.
- The GDD's half-ball-radius rule is a **distance** invariant and must be preserved as one at all ball speeds (including after the `min_vertical_velocity` nudge, which can change speed magnitude).

### Requirements
- Same `{seed, input sequence}` → **byte-identical** hit sequence and final score on ARM client and x86 server (bit-exact, not tolerance-bounded).
- Client-rendered ball motion must not visibly desync from the scored outcome.
- Half-ball-radius tunnelling guarantee preserved at any speed.

## Decision

Build a **deterministic fixed-point 2D physics module inside `SharedSimCore`**, owned as a first-class engine (not an RNG add-on). Four load-bearing choices:

### 1. Fixed-point integer math, not float
All scored-path position, velocity, and collision math uses **Q16.16 fixed-point in `Int32`**, with `Int64` intermediates for multiplication (`(long)a * b >> 16`) and a shared deterministic **integer square root** (`Int64` Newton/binary-search) for the one place normalization needs it. All arithmetic is `unchecked` and range-analyzed to prove no overflow. Integer division truncates toward zero identically on every .NET target. Result: the same inputs produce the same *bits* on ARM and x86 — hit/miss becomes a deterministic integer comparison with **zero** cross-platform ambiguity. (Q16.16 resolution ≈ 1.5×10⁻⁵ of board width is ~700× finer than the half-radius sub-step; the exact Q-format is an implementation detail the CI test vectors validate, not frozen here.)

### 2. Distance sub-stepping, driven by *remaining* distance (not a per-frame count)
The simulation runs at a **fixed simulation rate** (recommend 60 Hz; `frame_dt = 1/60 s`, fixed and identical client/server — inputs are recorded against integer sim-frame indices, never wall-clock). Within each sim frame, a ball must traverse `speed × frame_dt` of travel, but that travel is consumed by an **inner loop driven by remaining distance**, not a sub-step count computed up front:
```
remaining = speed * frame_dt
while remaining > 0:
    step = min(0.5 * ball_radius, remaining)     # never advance more than half a radius
    advance ball by `step` along current velocity direction; test/resolve collision
    apply min_vertical_velocity nudge / bounce reflection here (may change velocity)
    remaining -= step
```
Because the cap is re-evaluated against `0.5 × ball_radius` on *every* iteration, the half-radius distance invariant holds even if a bounce or the `min_vertical_velocity` nudge changes the ball's speed **magnitude mid-frame** — the earlier "compute `ceil(...)` once per frame" formulation (reviewer-flagged) would have violated the invariant in exactly that case. Note: whether the nudge preserves speed magnitude is left to the tuning owner; this loop is correct either way. (Normalized-space sanity check, playfield width = 1.0: `cell = 1/7`, `ball_radius = 0.15·cell ≈ 0.0214`, `half_radius ≈ 0.0107`, `ball_speed = 11·cell ≈ 1.571 /s`; nominal frame travel ≈ 0.0262 → ~3 inner iterations, scaling up automatically if a ball is moving faster.)

### 3. `SharedSimCore` is sole authority; Unity physics is visual-only
Ball `GameObject`s use high-level `Rigidbody2D` in **Kinematic** mode with interpolation **off**, positioned each rendered frame from the fixed-point state (converted to `float` *only* for display) via `MovePosition`. Unity's solver never independently simulates a ball, so there is exactly one authoritative simulation and no visual/scored divergence is possible. The high-level API is chosen over `UnityEngine.LowLevelPhysics2D` because, in a visual-only role, its maturity/documentation wins and the low-level API's advertised determinism is irrelevant (Unity isn't the authority here).

### 4. Corrected client integration (fixed-timestep accumulator)
The prior draft's bug — calling the core once per 20 ms `FixedUpdate` while sub-steps are ~6.8 ms — is replaced by an accumulator that advances the sim in whole `frame_dt` steps as real time accrues, rendering interpolated between the two latest sim frames:
```csharp
// Unity client — decouples render rate from the fixed sim rate
_accumulator += Time.deltaTime;
_accumulator = Mathf.Min(_accumulator, SimConstants.MaxCatchUp); // spiral-of-death clamp:
                                                                 // after a lag spike / focus loss,
                                                                 // drop un-simulatable time rather
                                                                 // than stalling. Does NOT affect
                                                                 // determinism — the sim only ever
                                                                 // advances in whole FrameDt steps.
while (_accumulator >= SimConstants.FrameDt) {      // FrameDt = 1/60
    _prevStates = _core.SnapshotBalls();
    _core.AdvanceFrame();                            // internally does distance sub-stepping
    foreach (var hit in _core.ConsumeHitEvents())
        BossDamageModel.ReportHit(hit.BrickId);      // per boss-ai-damage-model.md Rule 1
    _accumulator -= SimConstants.FrameDt;
}
float alpha = _accumulator / SimConstants.FrameDt;   // render interpolation only, never scored
RenderBallsInterpolated(_prevStates, _core.Balls, alpha);
```
The server CLI runs the same `AdvanceFrame()` loop with no accumulator (it steps the recorded frame count directly), yielding the identical hit sequence.

**All time-bounded GDD rules are counted in sim frames, never wall-clock.** The 12-second volley hard cap (`super-ricochet.md` Rule 5) is enforced as **720 sim frames @ 60 Hz**, not elapsed seconds — otherwise a client-side stutter or focus-loss (which the accumulator clamp above intentionally *drops*) would change a run's frame count and desync the server replay. Frame count is the single clock both sides agree on.

### Key Interfaces
```csharp
// SharedSimCore.dll — .NET Standard 2.1, no UnityEngine dep. Fixed-point only in scored path.
public readonly struct Fix32 {           // Q16.16 wrapper; +,-,* (Int64 intermediate), IntSqrt
    public readonly int Raw;
}
public interface IDeterministicPhysics2D {
    void AdvanceFrame();                  // fixed FrameDt; internally distance-sub-steps per ball
    IReadOnlyList<BallState> Balls { get; }
    IReadOnlyList<HitEvent> ConsumeHitEvents();  // count-based; order not scored (Boss AI Rule)
}
public readonly struct BallState { public Fix32 X, Y, Vx, Vy; public bool Retired; }
public readonly struct HitEvent  { public int BrickId; }
```

### Architecture Diagram
```
Unity Client (visual)                         .NET Verification CLI (ADR-0001 mechanism)
┌───────────────────────────────┐             ┌───────────────────────────────┐
│ accumulator → AdvanceFrame()×N ─┼─ same DLL ──┼─▶ AdvanceFrame() × recorded N  │
│   (fixed-point sim = authority) │  (asmdef)   │   (fixed-point sim = authority)│
│ Rigidbody2D.MovePosition        │             │   → byte-identical hit seq,    │
│   (kinematic, float, VISUAL)    │             │     final score (tolerance = 0)│
│ HitEvents → Boss AI (−1 HP)     │             └───────────────────────────────┘
└───────────────────────────────┘
```

## Alternatives Considered

### Alternative A: Float physics + `tolerance_units` (the rejected prior draft)
- **Description**: Single-precision float circle-vs-AABB, fixed time-step, rely on `tolerance_units` for cross-platform agreement.
- **Cons**: Fixed *time* step breaks the fixed *distance* tunnelling guarantee when speed varies; float cannot be trusted to agree on discrete hit/miss across ARM/x86, and tolerance cannot absorb a branch fork.
- **Rejection Reason**: Fails the core correctness requirement (see Context "the crux"). This is why the present ADR exists.

### Alternative B: Low-level `UnityEngine.LowLevelPhysics2D` (Box2D v3) as authority
- **Cons**: Box2D v3 still cannot run in the server CLI, so it would have to be reimplemented in `SharedSimCore` anyway — the exact reimplementation-divergence risk ADR-0001 rejected, now applied to full physics. Plus HIGH engine-API-maturity risk.
- **Rejection Reason**: Doesn't remove the server-side reimplementation need; adds risk for no benefit.

### Alternative C: Headless Unity re-simulation server-side
- **Cons**: Heavyweight second runtime; conflicts with Anti-Cheat's own flagged re-sim cost concern (same basis ADR-0001 rejected its Alternative B).
- **Rejection Reason**: Consistent with ADR-0001 precedent.

### Alternative D: Drop physics replay; Tier-1 clamping + statistical anomaly detection only
- **Description**: Don't re-simulate trajectories server-side at all; verify only RNG-derived values, and catch physics-outcome cheating via score-rate/timing heuristics.
- **Pros**: Eliminates the entire deterministic-physics problem; dramatically less engineering risk for a small team.
- **Cons**: Weaker anti-cheat; contradicts the GDD's explicit Tier-2 requirement.
- **Rejection Reason**: Retained as the **explicit fallback if the spike gate fails** (see Risk & Staffing Budget). Not chosen now, but consciously held in reserve rather than dismissed — the honest hedge against fixed-point physics proving too costly.

## Consequences

### Positive
- Scored outcomes are **bit-exact** across platforms → `tolerance_units` for the physics-derived scored path can be **0** (stronger anti-cheat than the float+tolerance story, and see the GDD-sync note below).
- The half-radius tunnelling guarantee is a true distance invariant, correct at any speed.
- One authoritative simulation → zero visual/scored desync by construction.
- A self-contained integer engine is highly unit-testable in isolation — easier to CI-verify than an opaque engine backend.

### Negative
- This is a genuine **custom deterministic physics engine** — materially more implementation effort and correctness risk than "call Unity's physics." The Risk & Staffing Budget below treats this honestly rather than burying it.
- Fixed-point arithmetic is less ergonomic; every new physics feature must respect the no-float discipline.
- Two consumers of `SharedSimCore` (RNG + physics) enlarge that library's blast radius (mitigated: separate interfaces/files, independent tests).

### Risks
- **Risk**: A fixed-point overflow or an incorrect `IntSqrt`/collision edge case silently diverges.
  **Mitigation**: Range-analysis proof for the chosen Q-format; CI test vectors covering corner hits, simultaneous multi-brick hits, near-boundary sub-steps, and max-speed nudged balls — asserted byte-identical on an *actual IL2CPP ARM build* vs the x86 CLI, not just editor Play Mode.
- **Risk**: Rendering framerate leaks into the sim (non-determinism).
  **Mitigation**: Sim advances only in whole fixed `frame_dt` steps via the accumulator; inputs recorded against integer frame indices; rendering interpolation is display-only and never fed back.
- **Risk**: Effort overrun — a solo/small team underestimates deterministic-physics cost.
  **Mitigation**: The spike gate below caps exposure before the whole game commits.

### Risk & Staffing Budget (spike gate — BLOCKING for Accepted status)
Before this ADR is marked **Accepted** and before Super Ricochet gameplay is built on it, a **time-boxed spike** must prove the single riskiest assumption. The spike must:
> 1. Build the fixed-point core + one ball, one brick, one bounce.
> 2. **Freeze the Q-format** (Q16.16 is the recommendation) and **complete the overflow range-analysis proof** against that frozen format — the "no overflow" claim in §1 is only valid once the format is pinned.
> 3. Produce a CI test vector and demonstrate an **iOS-ARM64 IL2CPP build and the x86 server CLI emit byte-identical results** — and that assertion must **specifically exercise signed `unchecked` overflow/wraparound and the `IntSqrt` negative/rounding convention**, because those are the two places where IL2CPP's C++ codegen (C++ signed overflow is undefined behavior) could theoretically diverge from the CLR. This can only be proven empirically on-device, not on paper — which is the entire reason this gate exists.

If the spike **passes** → promote to Accepted, proceed. If it **fails or overruns its box** → fall back to **Alternative D** (statistical anti-cheat, physics client-authoritative) and re-scope Anti-Cheat's Tier-2 accordingly. This makes the "strictly harder than the Box2D I rejected" concern explicit and bounded, rather than an open-ended commitment.

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|------------|-------------|---------------------------|
| super-ricochet.md | Rule 3: half-ball-radius sub-stepping → tunnelling structurally impossible + replayable | Distance sub-stepping (Decision §2) preserves it as a true invariant at any speed; fixed-point (§1) makes it replayable bit-exact |
| super-ricochet.md | Rule 4: min-vertical-velocity nudge | Applied inside `AdvanceFrame`; §2's distance-stepping absorbs the resulting speed change safely |
| boss-ai-damage-model.md | Every hit = 1 damage; simultaneous hits each count; order irrelevant | `ConsumeHitEvents()` is count-based/order-independent by design |
| anti-cheat-replay-verification.md | Tier-2 replay reproducibility | Bit-exact fixed-point sim via ADR-0001's CLI mechanism; enables `tolerance_units = 0` for this path |

## ⚠️ GDD / ADR-0001 SYNC REQUIRED
`anti-cheat-replay-verification.md` and ADR-0001 both justify `tolerance_units`
(0–5) as absorbing *"legitimate float-to-int rounding in an otherwise-identical
deterministic simulation."* If Super Ricochet's scored physics is fixed-point
and **bit-exact**, that justification no longer applies to this mini-game's
physics path. This must be reconciled (do not silently change the constant):
either keep a small tolerance strictly as defense-in-depth for any *display-*
*derived* values that leak into a submission, or set it to 0 for the physics
path. Flagged as an Open Question for the Anti-Cheat GDD owner — **not** decided
here.

## Performance Implications
- **CPU**: ~3 sub-steps/ball/frame × 60 Hz during volleys (≤12 s per GDD Rule 5), client and (for verification) server. Fixed-point integer ops are cheap, but server-side cost at concurrency is unowned here — deferred to the future "server-side replay re-simulation architecture" ADR's latency budget.
- **Memory / Load / Network**: Negligible / none / none (local child-process verification, per ADR-0001).

## Migration Plan
N/A — greenfield; no gameplay code exists yet.

## Validation Criteria
- Spike gate passes (ARM IL2CPP == x86 CLI, byte-identical) — the go/no-go.
- Full CI test-vector suite (edge cases above) green on both targets.
- Playtest: no visible desync between interpolated render and scored hits.

## Related Decisions
- ADR-0001 — `SharedSimCore` foundation, CLI replay, CI discipline this ADR reuses.
- Rejected `adr-0002-super-ricochet-physics-api.md` — the float/time-step draft this supersedes.
- Future: "Boss AI damage event architecture"; "Server-side replay re-simulation architecture" (owns the server perf budget).
- **ADR-0013** — extends `Fix32` to Quack Runner's duck-vs-obstacle collision (a second mini-game, not Super Ricochet). Reuses `Fix32` comparison and `FRAME_DT` unchanged; adds `Fix32.FromRatio` as new surface needing its own narrow supplementary spike, since this ADR's own spike scope never exercised division.

## Open Questions
- `tolerance_units` reconciliation (see GDD sync block) — Anti-Cheat GDD owner's call.
- Final Q-format and exact `SIM_HZ` — validated by the spike, not frozen here.
- Perceptible input-lag of kinematic render-interpolation on real mobile hardware — playtest once a build exists.
