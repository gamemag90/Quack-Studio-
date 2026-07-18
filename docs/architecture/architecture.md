# Quack Studio — Master Architecture

## Document Status
- Version: 1.1
- Last Updated: 2026-07-18
- Engine: Unity 6.3 LTS
- GDDs Covered: account-auth, save-persistence, analytics-event-tracking,
  currency-system, anti-cheat-replay-verification, shared-hub,
  level-difficulty-config-ricochet, boss-ai-damage-model, super-ricochet,
  hub-ui, ricochet-hud (11 MVP-tier systems); plus the Vertical-Slice
  additions quack-runner, mascot-database, currency-ledger-transaction-log,
  leaderboard (15 systems total)
- ADRs Referenced: **14 ADRs now written** (ADR-0001…ADR-0014; see ADR
  Audit / Required ADRs sections below). **[Refreshed 2026-07-18 per
  /architecture-review]** System Layer Map and Module Ownership tables below
  now fold in the Vertical-Slice systems (Quack Runner, Mascot DB, Currency
  Ledger, Leaderboard) alongside the original 11 MVP-tier systems.
- Technical Director Sign-Off: 2026-07-09 — APPROVED WITH CONDITIONS (write
  the RNG-determinism and physics-API ADRs before any Super Ricochet code;
  self-reviewed, no `technical-director` subagent registered here)
- Lead Programmer Feasibility: FEASIBLE (self-reviewed, no `lead-programmer`
  subagent registered here)

## Engine Knowledge Gap Summary

Unity 6.3 LTS is beyond the LLM's training data (~May 2025 cutoff).

**HIGH RISK domains**:
- **Physics** — Unity 6.3 ships a new low-level Box2D v3 API
  (`UnityEngine.LowLevelPhysics2D`) alongside the existing high-level API.
  Super Ricochet's determinism-critical collision system must use the
  stable, well-documented **high-level** `Rigidbody2D`/`Collider2D` API —
  not the new low-level one, which is too new to trust for this
  determinism-critical use case.
- **Platform** — secure on-device token storage (Account/Auth) was never
  independently verified against current iOS Keychain/Android Keystore
  plugin APIs. `PlayerPrefs` is explicitly insecure and must not be used.

**MEDIUM RISK domains**:
- **UI** — UI Toolkit vs. UGUI choice for Hub UI/Ricochet HUD needs
  confirmation against current package versions before committing.
- Shared Hub's navigation implementation (`SceneManager` conventions may
  have shifted).

**LOW RISK**: UGUI itself (confirmed still fully supported, not
deprecated), Addressables core workflow, general C# patterns.

## System Layer Map

```
┌──────────────────────────────────────────────────────────────┐
│ PRESENTATION   Hub UI, Ricochet HUD, Leaderboard              │
├──────────────────────────────────────────────────────────────┤
│ FEATURE        Boss AI/Damage Model, Super Ricochet,          │
│                Quack Runner                                   │
│                [HIGH RISK: Physics domain]                   │
├──────────────────────────────────────────────────────────────┤
│ CORE           Shared Hub, Currency System,                  │
│                Currency Ledger/Transaction Log,                │
│                Anti-Cheat/Replay Verification,                │
│                Mascot Database + Rarity Logic,                 │
│                Level/Difficulty Config (Ricochet)             │
├──────────────────────────────────────────────────────────────┤
│ FOUNDATION     Account/Auth, Save/Persistence,                │
│                Analytics/Event Tracking                       │
│                [HIGH RISK: Platform domain — Account/Auth]    │
├──────────────────────────────────────────────────────────────┤
│ PLATFORM       Unity 6.3 LTS engine API surface (no GDD       │
│                owns this directly — governed by                │
│                docs/engine-reference/unity/)                  │
└──────────────────────────────────────────────────────────────┘
```

Layer assignment reuses the mapping already established (and user-approved)
during `/map-systems` — kept consistent with `systems-index.md` rather than
re-derived.

## Module Ownership

### Foundation Layer

| Module | Owns | Exposes | Consumes | Engine APIs (risk) |
|---|---|---|---|---|
| Account/Auth | Player identity, JWT session | `Login()`, `Register()`, `LinkGuestAccount()`, `CurrentPlayerId` | none | `UnityWebRequest` (LOW); secure token storage — **HIGH RISK, unverified**, needs platform Keychain/Keystore plugin, not `PlayerPrefs` |
| Save/Persistence | Player record (server), local cache | `GetPlayer()`, `MutatePlayer(fn)`, cache read API | Account/Auth (playerId) | Standard C# file I/O for local cache (LOW); server is Node/Postgres, outside Unity engine risk |
| Analytics/Event Tracking | Event buffer | `TrackEvent(name, params)` | Account/Auth (playerId scoping) | Shares Save/Persistence's local-storage mechanism (deliberate, not duplicated) |

### Core Layer

| Module | Owns | Exposes | Consumes | Engine APIs (risk) |
|---|---|---|---|---|
| Currency System | Coin/gem balances | `CreditMultiplied()`, `CreditFlat()`, `Debit()`, `Balance` | Save/Persistence (atomic mutation), Anti-Cheat (validated amounts only) | None engine-specific |
| Currency Ledger/Transaction Log | Player-scoped append-only transaction log (`currency_ledger.player_id`) | `getPlayerLedger(playerId, since, limit)` | Currency System (`mutateWallet` writes the ledger row in its own transaction — same insert, no separate leg) | None engine-specific — Postgres schema/index only (ADR-0012) |
| Anti-Cheat/Replay Verification | Run validation state | `SubmitRun(runResult)` → validated reward | Account/Auth, Analytics | Determinism requirement touches every mini-game's physics (see Feature layer) |
| Mascot Database + Rarity Logic | Static mascot roster + shape/logic of per-player ownership (`player_state.data.mascots`) | Milestone grant evaluation, `collection_completion_percent` | Save/Persistence (`updatePlayer` chokepoint), Anti-Cheat (Tier-1 trusted run outcome — no independent verification path) | None engine-specific — deterministic C#, zero RNG by design |
| Level/Difficulty Config | Difficulty formulas | `GetLevelConfig(level, upgrades)` | Save/Persistence (upgrade level read) | None — pure C# functions |
| Shared Hub | Navigation state | `NavigateTo(screen)`, `CurrentScreen` | Currency, Mascot DB (see row above), Daily Quests* (*soft, undesigned) | `SceneManager` or custom screen-stack — **MEDIUM RISK** |

### Feature Layer — HIGH RISK domain (Physics)

| Module | Owns | Exposes | Consumes | Engine APIs (risk) |
|---|---|---|---|---|
| Super Ricochet | Ball physics, board state | `StartRun(config)`, `Fire(aimVector)`, run-end callback | Level/Difficulty Config, Boss AI | **HIGH RISK**: must use high-level `Rigidbody2D`/`Collider2D`, NOT `UnityEngine.LowLevelPhysics2D`. Fixed timestep via `Time.fixedDeltaTime`. Seeded RNG must be a **custom deterministic PRNG**, NOT `UnityEngine.Random` (not guaranteed bit-identical across platforms/versions — a genuine cross-platform determinism risk Anti-Cheat's whole design depends on) |
| Boss AI/Damage Model | Boss HP state | `ApplyDamage()`, `IsDefeated` | Level/Difficulty Config, Super Ricochet (hit events) | None engine-specific |
| Quack Runner | Duck/obstacle `Fix32` state, collision outcome | `Overlaps(duck, obstacle)`, `ObstacleSpeedFix32PerSecond(rawSpeed)`, run-end callback | Obstacle Spawn/Difficulty Ramp (Runner) formulas, `SharedSimCore` (`Fix32`/`FRAME_DT` from ADR-0002, `Pcg32Rng` spawn rolls from ADR-0001), Currency System (`creditMultiplied`) | **HIGH RISK**: reuses ADR-0002's `Fix32`/`SharedSimCore` (comparison-only, no sub-stepping needed) but adds one new operation, `Fix32.FromRatio` — gated on both ADR-0002's determinism spike and its own supplementary spike (ADR-0013 §4) |

### Presentation Layer

| Module | Owns | Exposes | Consumes |
|---|---|---|---|
| Hub UI | none (pure presentation) | Screen render | Shared Hub |
| Ricochet HUD | none (pure presentation) | HUD render | Super Ricochet stats, Boss AI |
| Leaderboard | none (pure presentation — score data owned by `leaderboard_scores`, ADR-0012) | Top-N board render, rank display | Anti-Cheat (Tier-1-clamped score, same transaction as reward credit), Super Ricochet (`ricochetBestScore`), Quack Runner (`runnerLeaderboardScore`), Account/Auth (username display) |

## Data Flow

**Initialization order**: Account/Auth resolves (Authenticated) →
Save/Persistence loads player record (cache-first) → Currency
System/Level-Difficulty-Config become valid → Shared Hub renders. Analytics
buffers independently from boot (fire-and-forget by design).

**Frame update path** (active Super Ricochet volley): Aim input → fixed-
timestep physics substep loop → collision detected → `Boss AI.ApplyDamage()`
(same frame) → Ricochet HUD reads dirty-checked stats → HP bar tweens
(0.12s, decoupled from the triggering frame rate).

**Event/signal path**: no generic pub/sub event bus exists as a separate
Foundation module — Analytics/Event Tracking is already the event sink
every system calls into directly. This is a deliberate **non-decision**:
the reward pipeline is a direct call chain, not a broadcast pattern: adding
a generic bus would be undirected complexity for this game's scope.

**The victorious-run reward chain** (highest-risk data flow — two full GDD
review passes were spent getting this contract right):

```
Player defeats boss (client)
  │
  ▼
Super Ricochet: run ends, packages { runId, level, seed, inputSequence,
                clientReportedFields } per Anti-Cheat's Run-Result Interface
  │
  ▼
Anti-Cheat: Tier-1 clamp → Tier-2 headless replay re-simulation (SERVER)
            re-derives bossDefeated, coinsCollected, bricksDestroyed, score
            independently — client's fields used ONLY for mismatch comparison
  │
  ▼
Anti-Cheat → Currency System: TWO separate calls
            CreditMultiplied(coinsCollected)
            CreditFlat(bossBonus)                  ← never combined into one call
  │
  ▼
Save/Persistence: atomic mutation commits both credits + progress in one transaction
  │
  ▼
Analytics: run_complete event emitted (fire-and-forget, doesn't block the above)
  │
  ▼
Shared Hub: re-aggregates on return (Currency + Daily Quests refresh)
```

**Save/load path**: owned entirely by Save/Persistence; Account/Auth
supplies the key (`playerId`), no other module touches storage directly.

## API Boundaries

```csharp
// Currency System — the two-path credit API is THE architectural artifact
// that fixes the reward-double-dip bug found during GDD review.
public interface ICurrencySystem
{
    Task<CreditResult> CreditMultiplied(int amount, CurrencyType type);
    Task<CreditResult> CreditFlat(int amount, CurrencyType type);
    Task<DebitResult> Debit(int amount, CurrencyType type);
}
// INVARIANT: never combine a multiplied and a flat amount into one
// CreditMultiplied() call. GUARANTEE: balance never negative under any
// concurrent-call interleaving.

// Anti-Cheat — sole entry point for any mini-game reporting a result.
public interface IAntiCheat
{
    Task<ValidatedReward> SubmitRun(RunResult clientReported, string seed, IReadOnlyList<InputEvent> inputSequence);
}
public record RunResult(string RunId, int Level, bool ClientReportedBossDefeated, int ClientReportedCoins, int ClientReportedBricks, int ClientReportedScore);
public record ValidatedReward(bool BossDefeated, int CoinsCollected, int BricksDestroyed, int Score, bool WasFlagged);
// INVARIANT: RunId unique per attempt — duplicate is a no-op, never
// re-processed. GUARANTEE: every ValidatedReward field is server-derived
// from replay, never copied from clientReported.

// Save/Persistence — sole write path to durable player state.
public interface IPlayerStore
{
    Task<PlayerRecord> GetPlayer(string playerId);
    Task<PlayerRecord> MutatePlayer(string playerId, Action<PlayerRecord> mutation);
}
// INVARIANT: no module outside this interface writes PlayerRecord fields
// directly. GUARANTEE: concurrent MutatePlayer calls for the same playerId
// serialize; no lost updates.
```

None of these interfaces use Unity engine types directly — intentionally
plain C#, testable without the Unity runtime, and swappable into
Anti-Cheat's headless server-side re-simulation context.

## ADR Audit

**[Updated by /architecture-review 2026-07-18]** All 11 originally-required ADRs
are written, plus 3 Vertical-Slice additions and 1 rejected/retained draft —
**14 active ADRs total**. Every *designed* system has architectural coverage.
All ADRs are still `Status: Proposed` (none `Accepted`) — the whole
scored-gameplay chain is gated on **ADR-0002's determinism spike**, which is
BLOCKING for Accepted status and transitively gates ADR-0007/0011/0013/0014.

## Required ADRs

All 11 originally-required ADRs below are now **written** (✅). The fulfilling
ADR file is noted per line.

**Foundation Layer (write before any coding):**
1. Secure on-device token storage strategy — ✅ **ADR-0003**
2. Server-side persistence: JSON store → PostgreSQL migration — ✅ **ADR-0005**
3. Client-side local cache and offline action queue — ✅ **ADR-0010**
4. Analytics event buffer and flush strategy — ✅ **ADR-0006**

**Core Layer:**
5. Deterministic RNG strategy for Anti-Cheat replay — ✅ **ADR-0001** (the linchpin)
6. Currency System atomic credit/debit implementation — ✅ **ADR-0004**
7. Server-side headless replay re-simulation architecture — ✅ **ADR-0007**
8. Shared Hub navigation architecture — ✅ **ADR-0009**

**Feature Layer:**
9. Super Ricochet physics — ✅ **ADR-0002** (`adr-0002-deterministic-fixedpoint-physics.md`;
   reframed from "which Unity API" to a deterministic fixed-point sim — supersedes the
   rejected `adr-0002-super-ricochet-physics-api.md`, retained as a record)
10. Boss AI damage event architecture — ✅ **ADR-0011**

**Presentation Layer:**
11. UI Toolkit vs UGUI for Hub UI and Ricochet HUD — ✅ **ADR-0008** (UGUI primary)

**Vertical-Slice additions (written after this doc's original ADR list):**
12. Persistence schema addendum — per-game leaderboard table + ledger `player_id` — ✅ **ADR-0012**
13. Quack Runner deterministic collision — ✅ **ADR-0013**
14. Super Ricochet deterministic spawn-probability / brick-HP rolls — ✅ **ADR-0014**

**Still required when their GDDs are designed (Not-Started, Alpha tier):**
IAP/receipt-credit ADR (ADR-0004 reserves the seam), Shop/coin-sink ADR (QQ-04),
Live-Ops feature-flag/remote-config ADR.

**Deferred to implementation**: texture-atlas tooling, animation rig
approach — already scoped at the asset-standards level in the art bible.

## Architecture Principles

1. Server is the sole source of truth for anything reward-granting — client
   state is always provisional.
2. Every mutation goes through exactly one atomic chokepoint per system —
   no scattered writes.
3. Determinism is a hard constraint on gameplay code, not an optimization —
   Anti-Cheat depends on it structurally.
4. New systems default to plain, engine-agnostic C# interfaces; Unity-
   specific types stay at the presentation/physics edges only.

## Open Questions

| ID | Summary | Priority | Resolution Path |
|---|---|---|---|
| QQ-01 | Secure token storage plugin unverified | High | ADR 1, verify against current platform plugin docs |
| QQ-02 | UI Toolkit vs UGUI split | Medium | ADR 11 |
| QQ-03 | `max_brick_hp` remains unbounded (from GDD review) | Medium | `/balance-check` once Unity build exists |
| QQ-04 | Coin-sink gap (deferred to Shop GDD) | Low | Shop GDD, not yet designed |
