# ADR-0011: Boss Damage / Defeat Event Architecture

## Status
Proposed

## Date
2026-07-10

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Unity 6.3 LTS client (visual boss bar) + `.NET` SharedSimCore (authoritative HP) + server replay |
| **Domain** | Core / Gameplay (deterministic) |
| **Knowledge Risk** | LOW — composes ADR-0002 (deterministic sim) and ADR-0004/0006/0007; no new engine surface |
| **References Consulted** | `boss-ai-damage-model.md`, `super-ricochet.md`, ADR-0002 (HitEvents), ADR-0004 (reward), ADR-0006 (analytics), ADR-0007 (replay) |
| **Post-Cutoff APIs Used** | None |
| **Verification Required** | Same seed+inputs reproduce identical boss-defeat frame server-side (bit-exact, ADR-0002 spike); simultaneous multi-hit tick applies correct damage; win-before-loss ordering holds |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-0002 (`ConsumeHitEvents()` + deterministic frame model), ADR-0007 (win/loss is a replayed scored outcome), ADR-0004 (boss-defeat flat reward), ADR-0006 (server-authoritative `mascot_acquired`-style events) |
| **Enables** | Super Ricochet win/loss resolution; boss-defeat reward + analytics |
| **Blocks** | Boss fight implementation |
| **Ordering Note** | Sits inside ADR-0002's SharedSimCore; gated by its determinism spike |

## Context

### Problem Statement
`boss-ai-damage-model.md` defines the boss as a damage/defeat **state machine**: every brick hit deals exactly **1** boss damage regardless of the brick's own HP (decoupled, Core Rule 1); simultaneous hits in one tick **each** count (a double-hit tick = 2 damage); boss defeat at HP 0 triggers the win **immediately, even with balls airborne**, and **takes priority over a same-frame loss** (a brick crossing the danger line). Because **win/loss is a scored outcome the server replays** (ADR-0007 Tier-2), the boss-HP machine cannot be client-authoritative — it must be part of the deterministic, replayable simulation. This ADR places the machine and defines the event flow, frame ordering, and the visual/authoritative split.

## Decision

### 1. Boss HP/defeat lives in the deterministic SharedSimCore (authoritative, replayable)
A small **`BossDamageModel`** inside SharedSimCore (ADR-0002), not a `MonoBehaviour`:
- `boss_hp` is initialized from **Level/Difficulty Config** (`level-difficulty-config-ricochet.md`) — an input to the seeded run, so the server replays from the same starting HP.
- Each sim frame, after physics resolves, it consumes **`ConsumeHitEvents()`** (ADR-0002 — count-based, order-independent) and applies `boss_hp -= hitCount`. **1 hit = 1 damage, decoupled from brick HP** (never reads brick HP — Core Rule 1). Simultaneous hits in one tick each count because the model decrements by the event **count** (Core Rule / edge).
- HP is clamped display-wise at 0; there is no overkill/negative case (each hit is exactly −1; defeat is the first frame `boss_hp ≤ 0`).
- **`boss_hp ≥ 1` at init is required** — a config value of 0 would trigger an instant WIN on frame 0 with zero hits (degenerate). `boss_hp = 0` from Level/Difficulty Config is treated as an invalid config (validation criterion).

### 2. Frame-granular resolution order: accumulate across sub-steps, win before loss
The physics frame runs **multiple distance sub-steps** (ADR-0002). HitEvents **accumulate across ALL sub-steps of the frame**; boss damage is applied **once at the frame boundary**, and win/loss are evaluated **at that same frame boundary — never per sub-step**:
```
1. run ALL physics sub-steps of the frame → accumulate HitEvents; latch "a brick crossed the danger line" as a frame flag
2. boss_hp -= (accumulated hitCount)          ← once, at frame end
3. if boss_hp ≤ 0 → WIN (immediate, even with balls still airborne)   ← checked FIRST
4. else if the danger-line flag is set → LOSS
```
- **Cross-ADR constraint on Super Ricochet (BLOCKING to honor)**: the danger-line **loss** condition (owned by `super-ricochet.md`) must be evaluated at this **same frame boundary, after boss-damage apply, with the win checked first**. **No per-sub-step early loss termination may preempt the frame-end win check** — otherwise a boss that dies on sub-step 2 could be wrongly overridden by a brick crossing on sub-step 5 of the *same* frame, violating "win takes priority over a same-frame loss." Super Ricochet's implementation must latch the danger-line condition as a frame flag and defer the loss decision to the frame boundary, not terminate mid-frame. (Surfaced as a registry constraint so `super-ricochet` implements the compatible seam.)
- This ordering is determinism-relevant and lives in the shared frame step, so client and server agree.

### 2a. Turn-end (boss survives the volley) is a third, non-loss outcome — owned by Super Ricochet
If the boss is **not** defeated when the 12-second / 720-sim-frame volley cap (ADR-0002 / `super-ricochet.md`) is reached, the **turn simply ends and the next turn begins — this is neither a win nor a loss.** Turn resolution is owned by `super-ricochet.md`; named here only so an implementer doesn't treat "volley ended with boss alive" as an ambiguous loss.

### 3. Client renders the boss bar + per-hit VFX from the SAME events (visual-only)
- The Unity client subscribes to the same `HitEvents` / boss-HP state to drive the **HP-bar drain**, the **per-hit trace VFX** (brick→boss portrait), and the **defeat sting** (`boss-ai-damage-model.md` Visual/Audio) — exactly the ADR-0002 pattern where Unity renders from SharedSimCore's authoritative state and never decides outcomes.
- **This system owns only the damage/defeat state machine — not the boss's visuals, name, or roster** (`boss-ai-damage-model.md` boundary; the 6-entry boss-name list is owned elsewhere; see also the `quack-blaster` IP note about the "Honktyson" name — new mascots/names are a separate concern, not decided here).

### 4. Defeat outcome is server-authoritative (reward + analytics)
- `boss_defeated` is a **replayed, server-confirmed** outcome (ADR-0007). The **flat boss-defeat coin bonus** is credited via ADR-0004's `creditFlat` leg (never the multiplied path — locks the two-path rule), and any gem grant likewise.
- The `boss_defeated`/reward analytics events are **server-emitted** (ADR-0006 server-authoritative split), not client-emitted — unspoofable and exactly-once.
- **Loss discards boss HP** — no partial-progress carryover to the next attempt (`boss-ai-damage-model.md`).

### Architecture Diagram
```
SharedSimCore frame (authoritative, replayable — client AND server run identically)
  physics sub-steps ─► HitEvents (count) ─► BossDamageModel: boss_hp -= count
                                              │
                                              ├─ boss_hp ≤ 0 ? ─► WIN (immediate)     [checked before loss]
                                              └─ else danger-line? ─► LOSS
        │                                          │
        ▼ (visual only, same events)               ▼ (server-authoritative outcome)
  Unity: HP-bar drain, per-hit VFX,          boss_defeated ─► ADR-0004 creditFlat bonus
         defeat sting                                       ─► ADR-0006 server-emitted analytics
  (boss visuals/name owned elsewhere)          Loss ─► boss HP discarded (no carryover)
```

## Alternatives Considered

### Alternative A: Client-authoritative boss HP, reconciled server-side
- **Pros**: Simpler client; no boss logic in the shared core.
- **Cons**: Win/loss becomes spoofable and is **not** bit-reproducible in Tier-2 replay — directly contradicts ADR-0007's server-authoritative, replay-verified model.
- **Rejection Reason**: The anti-cheat model requires win/loss to be a deterministic replayed outcome; boss HP must be in the sim.

### Alternative B: Boss damage scales with brick HP/value
- **Pros**: "Tougher bricks hit harder" intuition.
- **Cons**: `boss-ai-damage-model.md` explicitly **rejected** this (Decision 2026-07-09) in favor of the fixed 1-per-hit rule — decoupling keeps the "chip away" feel and avoids a balance coupling.
- **Rejection Reason**: Contradicts the owning GDD's deliberate decision.

## Consequences

### Positive
- Win/loss is deterministic and replayable — Tier-2 can verify boss defeats, not just scores.
- Reuses ADR-0002's event model and ADR-0004/0006/0007's authoritative reward/analytics — no new machinery.
- Clean visual/authoritative split: the boss bar is pure presentation.
- The decoupled 1-per-hit rule is enforced in one place (never reads brick HP).

### Negative
- Boss logic lives in SharedSimCore (a bit more in the shared library), gated by ADR-0002's determinism spike like the rest of the scored sim.
- The win-before-loss ordering is a subtle rule that must be identical client+server (mitigated: it's in the shared frame step, covered by replay test vectors).

### Risks
- **Risk**: Client renders a boss defeat that the server's replay doesn't reproduce (divergence).
  **Mitigation**: Same SharedSimCore code both sides (ADR-0002 bit-exact); a boss-defeat frame is part of the replay test vectors.
- **Risk**: A same-frame win+loss resolves inconsistently.
  **Mitigation**: Fixed win-before-loss order in the shared frame step; explicit test.
- **Risk**: Boss reward double-counts (client + server both emit).
  **Mitigation**: Server-authoritative emission only (ADR-0006/0007); `creditFlat` via ADR-0004.

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|------------|-------------|---------------------------|
| boss-ai-damage-model.md | Core Rule 1: every hit = 1 damage, decoupled from brick HP | `boss_hp -= hitCount`; never reads brick HP |
| boss-ai-damage-model.md | Simultaneous hits in one tick each count | Decrement by event **count** per frame |
| boss-ai-damage-model.md | Defeat at 0 HP triggers win immediately, even with airborne balls | In-frame win check right after damage apply |
| boss-ai-damage-model.md | Win takes priority over a same-frame loss | Win checked **before** danger-line loss |
| boss-ai-damage-model.md | Loss discards boss HP, no carryover | Loss path drops HP; nothing persisted |
| boss-ai-damage-model.md | Owns state machine, not boss visuals/name | Machine only; visuals/name owned elsewhere |
| boss-ai-damage-model.md | Per-hit trace + defeat sting (Visual/Audio) | Client renders from the same events (visual-only) |

## Performance Implications
- **CPU**: Trivial — an integer decrement + two comparisons per frame, inside the existing sim step.
- **Memory/Network**: None new.

## Migration Plan
Greenfield within SharedSimCore. The prototype's boss-defeat logic (`gameRules.ts`) informs the *rule* (1 hit = 1 damage), re-implemented deterministically.

## Validation Criteria
- Same seed + inputs reproduce the identical boss-defeat frame server-side (part of ADR-0002's replay test vectors).
- A double-hit tick applies exactly 2 damage; a normal hit applies 1; damage is independent of the hit brick's HP.
- A boss defeated on the same frame a brick crosses the danger line resolves as a **win** — including when the boss dies on an *earlier sub-step* than the brick crossing within the same frame (frame-boundary resolution, no per-sub-step loss preemption).
- `boss_defeated` credits the flat bonus via `creditFlat` (never multiplied) and emits the analytics event **server-side only**.
- A loss discards boss HP with no carryover to the next attempt.
- `boss_hp = 0` from config is rejected as invalid (must be ≥ 1); no instant frame-0 win.
- Boss surviving the 720-frame volley cap ends the turn (next turn), scored as neither win nor loss.

## Related Decisions
- ADR-0002 — HitEvents + deterministic frame model this builds on.
- ADR-0004 — `creditFlat` boss-defeat bonus (two-path rule).
- ADR-0006 — server-authoritative analytics emission.
- ADR-0007 — win/loss as a replayed, server-confirmed outcome.
- `boss-ai-damage-model.md`, `super-ricochet.md` — the designs implemented.
- `[[project_quack_ip_risk]]` (memory) — boss *names* (e.g. "Honktyson") are owned elsewhere and carry a known IP-risk note; not decided here.

## Open Questions
- None specific to this ADR — the boss-name IP question and `max_brick_hp` unbounded-growth (a `super-ricochet`/balance concern) are tracked elsewhere, not here.
