# ADR-0001: Deterministic RNG Strategy for Anti-Cheat Replay Verification

## Status
Proposed

## Date
2026-07-10

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Unity 6.3 LTS (build `6000.3.0f1`) |
| **Domain** | Core / Anti-Cheat (cross-cutting: also touches Physics, Networking) |
| **Knowledge Risk** | HIGH — project pin is post-LLM-cutoff per `VERSION.md` |
| **References Consulted** | `docs/engine-reference/unity/VERSION.md`, `breaking-changes.md`, `deprecated-apis.md` |
| **Post-Cutoff APIs Used** | None — this ADR deliberately avoids `UnityEngine.Random` entirely, sidestepping any post-cutoff behavior-change risk in that API |
| **Target Framework** | Shared core targets **.NET Standard 2.1** (Unity's IL2CPP/Mono compatibility baseline) — no `Span<T>`/`unsafe`/reflection-heavy code that could AOT-compile differently on iOS IL2CPP vs. a standalone .NET runtime |
| **Verification Required** | CI shared test-vector suite (seed → expected output sequence) must pass identically in the Unity client build and the standalone CLI executable before this ADR's implementation is considered complete |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | None (first ADR — Foundation layer) |
| **Enables** | "Super Ricochet physics API choice" ADR — physics sub-stepping determinism must use a compatible discipline (integer/fixed-point where possible, documented float tolerance elsewhere) |
| **Blocks** | Any Super Ricochet gameplay-code implementation, per the TD condition recorded in `architecture.md`: "write RNG-determinism + physics-API ADRs before any Super Ricochet code" |
| **Ordering Note** | None |

## Context

### Problem Statement
Anti-Cheat/Replay Verification's Tier-2 defense (`anti-cheat-replay-verification.md`) requires the server to re-simulate a submitted run from `{seed, input sequence}` and compare its output to the client's reported result within `tolerance_units` (0–5 int). This only works if the RNG sequence produced from a given seed is bit-identical between the Unity client and wherever server-side re-simulation runs. `UnityEngine.Random` gives no cross-version/cross-platform bit-identical guarantee. `super-ricochet.md` already commits to "fixed sub-stepping, seeded RNG for row/coin/power-up spawn rolls" as a hard dependency of Tier 2 replay. Without a resolved strategy, Tier 2 is structurally unbuildable — `architecture.md` flags this as the single most consequential decision in the system.

### Constraints
- Backend is otherwise Node.js/TypeScript per the master prompt — a second server-side runtime is a real operational cost and must be justified, not just convenient.
- Physics-integration float drift is a separate, already-acknowledged concern — `tolerance_units` exists for that reason and is out of scope here; this ADR only targets RNG-sequence determinism.
- Must generalize across all 5 mini-games' engines as they're designed, not be a one-off for Super Ricochet.

### Requirements
- Given the same seed + input sequence, client and server RNG draws must match bit-for-bit.
- No dependency on any language/runtime's built-in "random" implementation (none are contractually stable across versions).
- Verifiable in CI, not just code-review confidence.
- A defined behavior when server-side verification is unavailable or times out (not previously specified — added after review, see Consequences).

## Decision

Adopt a **shared deterministic simulation core**, implemented once in C#:

1. A custom, portable, integer-only PRNG, pinned now as **PCG32** (canonical, well-documented reference implementation, straightforward to test-vector) with an explicit `algorithm_version` tag stored per seed/session — if the algorithm is ever changed later, old replays remain verifiable against the version they were recorded with. No calls to `UnityEngine.Random`, `System.Random`, or any other built-in random source anywhere in gameplay-affecting code. All PRNG arithmetic is wrapped in explicit `unchecked` blocks so integer-overflow wraparound behavior cannot diverge based on each side's project-level overflow-checking settings.
2. Packaged as a plain **.NET Standard 2.1** class library with no MonoBehaviour/UnityEngine dependencies — referenced via Assembly Definition (`.asmdef`) by the Unity client project, and also compiled into a small **self-contained .NET CLI executable** used only for verification.
3. The Node.js backend invokes this CLI executable as a **short-lived child process** per Tier-2 check — seed + input sequence passed via stdin, simulated result returned via stdout — rather than standing up a persistent networked microservice. This avoids a new deployment target, health-check surface, and network auth boundary for what is a narrow, stateless computation.
4. **Failure mode: fail-open.** If the child process errors, crashes, or exceeds a timeout, Tier-1 plausibility clamping still applies and the run is accepted provisionally; it is queued for async re-verification once the verifier is healthy again. If a mismatch is found on delayed re-verification, the run is flagged for human review — the reward, already credited at the Tier-1-clamped amount, is never clawed back (see ADR-0007's flag-only model, which this ADR adopts). Fail-closed (blocking legitimate players' rewards on every hiccup) is rejected as worse for a live economy than a bounded, logged, flag-only fail-open window.
   > **[Corrected 2026-07-18 per /architecture-review]** Originally read "the reward is clawed back and the run flagged" — superseded by ADR-0007 (2026-07-10), which establishes the flag-only reward model per `anti-cheat-replay-verification.md` Rule 6 + Acceptance; corrected inline above. See ADR-0007 §1 and the `reward_model_flag_only` registry note.
5. A CI-enforced shared test-vector suite: fixed seeds with expected output sequences, asserted identically in the Unity client test suite and the CLI executable's test suite, on every build.
6. `tolerance_units` (already defined in `anti-cheat-replay-verification.md`) is retained as-is for the floating-point physics-integration layer, which this ADR does not touch.

Because the core library is the *same compiled code* referenced from both places — not an independent reimplementation — this removes the largest practical risk in "two teams write the same algorithm twice" approaches, without the operational weight of a persistent second server.

### Architecture Diagram
```
Unity Client                              Node.js Backend
┌───────────────────────┐                 ┌─────────────────────────────┐
│ Super Ricochet          │                │ Tier-2 check requested       │
│  spawn/gameplay code    │                │        │                     │
│        │                │                │        ▼                     │
│        ▼                │                │ spawn child process:         │
│ SharedSimCore.dll ───────┼── same source──┼─▶ replay-verifier.exe        │
│  (PCG32 RNG, unchecked   │  (asmdef ref)  │    stdin: {seed, inputs}     │
│   integer math, no       │                │    stdout: {simulated_result}│
│   UnityEngine deps)      │                │        │                     │
└───────────────────────┘                 │        ▼                     │
                                            │ replay_match = abs(sim -     │
                                            │   client_reported) ≤         │
                                            │   tolerance_units             │
                                            │        │                     │
                                            │  [on process error/timeout]  │
                                            │        ▼                     │
                                            │  fail-open: Tier-1 only,      │
                                            │  queue async re-verify        │
                                            └─────────────────────────────┘
```

### Key Interfaces
```csharp
// SharedSimCore.dll — .NET Standard 2.1, no UnityEngine dependency.
// Referenced by the Unity client (asmdef) and by replay-verifier.exe (CLI host).
public interface IDeterministicRng
{
    void Seed(ulong seed, int algorithmVersion);
    uint NextUInt32();    // unchecked integer draw
    float NextFloat01();  // fixed, test-vectored integer→float conversion
}

public sealed class Pcg32Rng : IDeterministicRng { /* algorithm_version = 1 */ }
```
```
// replay-verifier.exe contract (invoked as a child process by the Node backend)
// stdin  (JSON): { "seed": "...", "algorithmVersion": 1, "inputs": [...] }
// stdout (JSON): { "simulatedResult": <int>, "ok": true }
// non-zero exit / stderr / timeout → treated as verifier failure (fail-open path)
```

## Alternatives Considered

### Alternative A: Independent reimplementation (TypeScript on the existing Node backend), integer-only algorithm
- **Description**: Same integer-only PRNG discipline, hand-ported to TypeScript instead of using a separate compiled C# artifact.
- **Pros**: No new runtime/executable in the server stack.
- **Cons**: Two independent implementations of the same algorithm in two languages is exactly the scenario where subtle porting bugs (bit-shift edge cases, `uint` vs. JS `Number`/`BigInt` overflow-behavior differences) produce silent divergence that test vectors may not catch until a production edge case.
- **Rejection Reason**: The entire point of this decision is eliminating divergence risk; an independent second implementation reintroduces the exact risk the shared-compiled-core approach removes for free.

### Alternative B: Server runs an actual headless Unity build
- **Description**: Server-side re-simulation via a real Unity Editor/Player in headless/batch mode.
- **Pros**: Maximum fidelity — same engine, same physics, same everything.
- **Cons**: Heavyweight (license considerations, slow cold-start, high per-verification CPU/memory) — directly conflicts with the Anti-Cheat GDD's own flagged concern about re-simulation cost at scale.
- **Rejection Reason**: Disproportionate operational cost for what a much lighter shared-core approach achieves for the RNG-specific problem.

### Alternative C: Simplified server-side physics/RNG approximation
- **Description**: A deliberately non-Unity, approximate deterministic model on the server, verified only to statistical/tolerance closeness.
- **Pros**: Cheapest to build.
- **Cons**: "Approximate by design" undermines confidence in what Tier 2 is supposed to prove.
- **Rejection Reason**: Trades away verification rigor for implementation ease in exactly the layer where rigor matters most.

### Alternative D: Compile shared core to WebAssembly, run identical WASM in both client and server
- **Description**: Write the deterministic core in a WASM-targetable form, embed via native plugin in Unity and a WASM runtime in Node.
- **Pros**: Byte-identical execution by construction, no reimplementation at all.
- **Cons**: Adds a WASM toolchain, an additional build target, and IL2CPP/WASM interop complexity this team doesn't have yet.
- **Rejection Reason**: Technically elegant but disproportionate build-pipeline complexity at this project's stage; the shared-C#-library approach gets the same "identical code" guarantee using tooling the team already has.

### Alternative E: Persistent networked .NET microservice (original draft of this ADR, revised after review)
- **Description**: A standing ASP.NET service the Node backend calls over HTTP/gRPC for every Tier-2 check.
- **Pros**: Standard service architecture, easy to scale horizontally.
- **Cons**: New deployment target, health-check surface, network auth boundary, and on-call burden for a narrow, stateless computation — this was flagged in review as disproportionate, arguably worse than the WASM option (D) it was originally drafted to be lighter than.
- **Rejection Reason**: The short-lived child-process CLI (this ADR's actual Decision) achieves the same code-identity guarantee with materially less operational surface — no persistent process, no network boundary to secure, simpler failure handling.

### Alternative F: Drop bit-exact replay; rely on Tier-1 clamping + statistical/behavioral anomaly detection only
- **Description**: No deterministic re-simulation; catch cheaters via score-rate outliers and suspicious timing patterns server-side.
- **Pros**: Eliminates the determinism problem entirely; far less engineering risk.
- **Cons**: Contradicts the GDD's explicit, named Tier 2 requirement and the master prompt's anti-fraud hardening goal.
- **Rejection Reason**: This ADR's job is to make the GDD's stated Tier 2 requirement buildable, not to re-scope the GDD. Whether Tier 2 is worth its ongoing cost is a product-level question, not decided here — carried forward as an Open Question.

## Consequences

### Positive
- Single source of truth for RNG-critical logic; no cross-language port to keep in sync.
- CI test-vector suite gives a concrete, automatable proof of the guarantee, not just review confidence.
- No persistent second server process — lower operational footprint than the microservice draft this ADR started from.
- Pattern generalizes cleanly to the other 4 mini-games as they're designed.

### Negative
- Still introduces a second server-side runtime dependency (.NET must be installed/available wherever the Node backend runs the CLI) — smaller than a full service, but not zero.
- Requires strict engineering discipline (no accidental `UnityEngine.Random`/`System.Random` calls) — needs a CI-enforced rule, not just documentation.
- Fail-open behavior means a bounded window where Tier-2 is effectively off during verifier outages; acceptable only because Tier-1 clamping governs the reward regardless (Tier-2 is flag-only per ADR-0007 — see the superseding note on Decision point 4).

### Risks
- **Risk**: A future contributor unknowingly calls `UnityEngine.Random` in new mini-game code, silently breaking determinism.
  **Mitigation**: CI grep-check (or Roslyn analyzer) banning `UnityEngine.Random`/`System.Random` in `SharedSimCore` and gameplay-spawn code paths; register as a Forbidden Pattern (see registry update below).
- **Risk**: The `NextFloat01()` integer→float conversion itself introduces platform-specific drift.
  **Mitigation**: Test-vector the float conversion explicitly, not just the raw integer draws.
- **Risk**: IL2CPP AOT compilation on iOS handles some modern .NET constructs differently than the standalone CLI's runtime.
  **Mitigation**: Target .NET Standard 2.1 only; avoid `unsafe`/`Span<T>`/heavy reflection in `SharedSimCore`; the CI test-vector suite running against an actual IL2CPP-built client (not just Editor Play Mode) is the real proof, not just a lint rule.
- **Risk**: Verifier child process spawn cost or timeout tuning is unproven under real load.
  **Mitigation**: Deferred to the future "server-side headless replay re-simulation architecture" ADR, which will define the concrete latency/timeout budget.

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|------------|-------------|---------------------------|
| anti-cheat-replay-verification.md | Tier 2 replay requires bit-identical (within `tolerance_units`) client/server RNG sequences | Shared compiled RNG core eliminates cross-language divergence risk; `tolerance_units` retained for the physics-float layer this ADR doesn't touch |
| super-ricochet.md | "fixed sub-stepping, seeded RNG for row/coin/power-up spawn rolls" (Rule 3) is a binding constraint for future GDDs | This ADR defines exactly which RNG implementation satisfies that constraint |

## Performance Implications
- **CPU**: Integer PRNG draws are nanosecond-scale; negligible client-side cost. Server-side cost is dominated by process-spawn overhead and the simulation itself, not the RNG — concrete budget deferred to the future replay re-simulation ADR.
- **Memory**: Negligible — PRNG state is a few bytes.
- **Load Time**: None.
- **Network**: None — verification is a local child-process call, not a network hop, per the revised Decision.

## Migration Plan
N/A — greenfield decision made before any Super Ricochet gameplay code is written, per the TD condition in `architecture.md`.

## Validation Criteria
- CI test-vector suite (fixed seeds → expected sequences) passes identically in an actual IL2CPP-built Unity client and the standalone CLI executable — not just Editor Play Mode.
- A manually recorded real Super Ricochet run replays to the same final result server-side within `tolerance_units`.
- Fail-open path is exercised in a test (kill/timeout the child process mid-check) and confirmed to fall back to Tier-1 clamping plus a queued re-verification entry.

## Related Decisions
- `architecture.md` (Master Architecture Document) — names this as the highest-priority ADR.
- Future: "Super Ricochet physics API choice" ADR — must align its determinism approach with this ADR's.
- ADR-0007 "Server-side headless replay re-simulation architecture" — details the concrete latency budget, warm-worker-pool + Postgres queue, and the **flag-only** (not clawback) reward model. Supersedes this ADR's clawback wording (see Decision point 4 note).

## Open Questions
- Whether Tier 2's bit-exact replay is worth its ongoing operational cost (a second runtime dependency, however lightweight) is a product/GDD-level question (see rejected Alternative F) — not resolved here, carried forward for a future product-priority conversation.
