# Control Manifest

> **Engine**: Unity 6.3 LTS (build `6000.3.0f1`)
> **Last Updated**: 2026-07-18
> **Manifest Version**: 2026-07-18
> **ADRs Covered**: ADR-0001, ADR-0002, ADR-0003, ADR-0004, ADR-0005, ADR-0006, ADR-0007, ADR-0008, ADR-0009, ADR-0010, ADR-0011, ADR-0012, ADR-0013, ADR-0014
> **Status**: ⚠️ **PROVISIONAL** — all source ADRs are `Status: Proposed`, not `Accepted`. `/architecture-review` ran 2026-07-18 (verdict: CONCERNS — no blocking conflicts; every designed system covered; all ADRs still Proposed pending ADR-0002's determinism spike). Safe as a working reference now — treat as a strong working reference, not a final ratified contract, until `/architecture-review` passes and the ADR set moves to Accepted.

This manifest is a programmer's quick-reference extracted from all 14 ADRs
(all reviewed by an independent subagent and revised before this extraction —
see each ADR's own review history), `technical-preferences.md`, and
`docs/engine-reference/unity/deprecated-apis.md`. For the reasoning behind
each rule, see the referenced ADR. **No rule below was invented for this
document — every one traces to an ADR, the registry, or an engine-reference
doc.**

---

## Foundation Layer Rules

*Applies to: scene management, save/load, engine init, on-device secure storage, client durable storage, leaderboard/ledger schema*

### Required Patterns
- **Session tokens go through `ISecureTokenStore`** (`Save`/`TryLoad`/`Clear`) — iOS Keychain (Swift) / Android Keystore-backed (Kotlin) native impls; editor mock is Editor-only and un-shippable — source: ADR-0003
- **iOS token storage**: Keychain with `kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly` + `ThisDeviceOnly` (or tighter `WhenUnlocked` if no locked-state network call needed) — source: ADR-0003
- **Android token storage**: Keystore-wrapped AES-GCM key (or Tink), app-private encrypted blob — source: ADR-0003
- **`player_state` (non-money per-player state — progress, quests, mascots, upgrades)** is owned by Save/Persistence's `updatePlayer(id, mutate)` chokepoint, stored as a JSONB column with `schema_version` — source: ADR-0005
- **Generic `updatePlayer` is a LOCKED read-modify-write**: `SELECT ... FOR UPDATE` + mutate + `UPDATE`, all in one transaction — source: ADR-0005
- **Server persistence schema is hybrid**: relational for money + cross-player queries, JSONB (`player_state.data`) for evolving per-player-only state — source: ADR-0005. *(NOTE: ADR-0005's original singular `player.leaderboard_score` column was superseded by ADR-0012's per-game `leaderboard_scores(player_id, game_id, score, achieved_at, display_stats)` table — Ricochet and Runner have structurally incomparable score formulas. Cross-player leaderboard sort keys remain relational/indexed, never JSONB.)*
- **`leaderboard_scores(player_id, game_id, score, achieved_at, display_stats)` is current-best-only** (one row per player+game, never a history table); upsert via `ON CONFLICT (player_id, game_id) DO UPDATE ... WHERE EXCLUDED.score > leaderboard_scores.score` — a worse run is a silent no-op; `score`/`achieved_at`/`display_stats` always commit together, never partially — source: ADR-0012
- **A leaderboard write's `achieved_at` is captured server-side (`now()` at commit), never a client-supplied timestamp** — Core Rule 6 tie-breaks on earliest `achieved_at`, so a client-controlled value could unfairly win a tie — source: ADR-0012
- **`currency_ledger` gains a denormalized `player_id` column + `(player_id, created_at DESC)` index**, written inside the same `mutateWallet` transaction as the rest of the ledger row (no new write path, no new lock) — `getPlayerLedger(playerId, since, limit)` reads directly off that index, never joining through `currency_op` — source: ADR-0012
- **Data-access layer is a query builder / raw parameterized SQL** (Knex/pg-style), not a heavy ORM — source: ADR-0005
- **Schema changes via versioned, ordered, code-reviewed SQL migrations** — never ad-hoc `ALTER`s — source: ADR-0005
- **`HubNavigator`** is the single client-side authority for current-screen state (Hub / mini-game / modal, exactly one active), living on a **never-deactivated persistent root**, separate from the deactivatable Hub-view root — source: ADR-0009
- **Hub navigation**: persistent Hub scene + Addressables **additive** mini-game scene load/unload, one mini-game scene resident at a time, released on exit — source: ADR-0009
- **Navigation debounce**: 300ms, last-tap-wins pre-commit, nav-lock (ignore further taps) post-commit — source: ADR-0009
- **Client durable store is a single embedded SQLite DB** (WAL mode, one owned connection behind a serialized access layer) shared by `cached_state`, `analytics_buffer` (ADR-0006), and `offline_queue` — source: ADR-0010
- **On SQLite open, run `PRAGMA integrity_check`** — recreate the DB on corruption rather than limping on a broken file — source: ADR-0010
- **Client cache is last-known-good, read-first, server-wins full overwrite, display-only** (never authoritative for rewards) — source: ADR-0010
- **Offline queue is idempotent-or-block, remove-after-server-ack, server-revalidated on replay, 24h + size cap, `playerId`-scoped** — source: ADR-0010

### Forbidden Approaches
- **Never use `PlayerPrefs` (or any plaintext on-disk store) for a secret, credential, or auth token** — plaintext is readable on rooted/jailbroken devices or via local backup — source: ADR-0003
- **Never let a shipped player build select an in-memory/mock `ISecureTokenStore`** — the mock must be physically un-shippable (Editor-only assembly), and the runtime selector must fail closed (throw) on an unknown platform, never silently default to plaintext/in-memory — source: ADR-0003
- **Never log or crash-report the `Authorization` header or raw token value** — source: ADR-0003
- **Never mutate currency through the generic `updatePlayer(id, mutate)` chokepoint** — money uses ADR-0004's `mutateWallet` column-level path only; wallet columns live outside `player_state.data` so currency isn't reachable from the generic mutator — source: ADR-0005
- **Never perform network/file I/O inside an `updatePlayer` mutator function** — the mutator runs while a row lock and pooled DB connection are held; I/O there throttles concurrency — source: ADR-0005
- **Never store a cross-player/leaderboard sort key inside JSONB `player_state`** — promote it to an indexed relational column, or top-N becomes a full scan — source: ADR-0005
- **Never acquire a composed transaction's legs out of the fixed order** — canonical order is `player_state FOR UPDATE` → guarded `wallet` update → `leaderboard_scores` upsert, always in that sequence, for **any** transaction touching 2+ of these three legs (not only the reward-credit chokepoint — an admin score-correction tool, a season-reset batch job, an account-merge routine must follow it too), or you get ABBA deadlocks — source: ADR-0005, extended to a third leg by ADR-0012
- **Never add a `NOT NULL` column via a single `ALTER TABLE ... ADD COLUMN ... NOT NULL` statement against a table with existing rows** — Postgres requires every row to already satisfy the constraint the instant it's added, and a fresh column with no default starts every row at `NULL`. Sequence: add nullable → backfill → `SET NOT NULL` → index — source: ADR-0012
- **Never place `HubNavigator` / aggregation coroutines under a `GameObject` root that gets `SetActive(false)`** — deactivating a root stops its coroutines and `Update` — source: ADR-0009
- **Never leave the Hub's Camera / EventSystem / AudioListener enabled while a mini-game scene is active** — additive scenes produce dual EventSystem (flaky input), dual AudioListener (broken audio), dual camera (overdraw/wrong output). Disable Hub's on mini-game activate + `SetActiveScene(minigame)`; reverse on return — source: ADR-0009
- **Never unload an Addressables-loaded scene with `SceneManager.UnloadScene`** — use `Addressables.UnloadSceneAsync` on the stored `SceneInstance` handle, plus release asset handles + `Resources.UnloadUnusedAssets` — source: ADR-0009
- **Never hold more than one mini-game scene resident at a time** (mobile memory budget) — source: ADR-0009
- **Never store the session token in the client cache/SQLite DB** — tokens live in Keychain/Keystore (ADR-0003) only; the cache DB holds display/game state + queued actions, never credentials — source: ADR-0010
- **Never apply a reward optimistically to the client cache before server confirmation** — cache is display-only; offline reward actions are queued (never shown as already-granted) until the server confirms — source: ADR-0010
- **Never remove an offline-queue entry before a confirmed server ack** — remove-after-ack gives exactly-once; removing before sending loses the action on send failure — source: ADR-0010
- **Never let subsystems open their own separate SQLite connections, and never disable WAL** — off-thread analytics flush + main-thread cache/queue on separate connections causes `SQLITE_BUSY` — source: ADR-0010
- **Never use `androidx.security:security-crypto` / `EncryptedSharedPreferences` for Android token storage** — Google has deprecated it. This is called out explicitly because it's the exact API a coding agent's stale training data would reach for by default (ADR-0003's own Risks section names this failure mode directly) — use the Keystore-wrapped AES-GCM/Tink pattern above instead — source: ADR-0003
- **Never discard the client cache/queue on a guest→account link** — linking preserves the same `playerId` + progress; cache/queue carry over. Discard applies only to logout or a true account switch — source: ADR-0010

---

## Core Layer Rules

*Applies to: deterministic RNG, deterministic physics (Ricochet + Runner), spawn-roll determinism, boss damage/combat state — SharedSimCore*

### Required Patterns
- **RNG algorithm: PCG32** (`algorithm_version = 1`), canonical reference implementation — source: ADR-0001
- **`SharedSimCore` targets .NET Standard 2.1** — no `Span<T>`/`unsafe`/heavy reflection, to avoid AOT-compilation divergence between Unity client (iOS IL2CPP) and the standalone CLI runtime — source: ADR-0001
- **All PRNG arithmetic wrapped in explicit `unchecked` blocks** — guarantees identical integer-overflow wraparound regardless of either side's overflow-checking project setting — source: ADR-0001
- **`IDeterministicRng`**: `Seed(ulong seed, int algorithmVersion)`, `NextUInt32()`, `NextFloat01()` — implemented by `Pcg32Rng` — source: ADR-0001
- **Super Ricochet scored physics uses Q16.16 fixed-point (`Fix32`)**, never float — bit-exact cross-platform, so `tolerance_units` can be 0 on this path — source: ADR-0002
- **Sub-stepping is driven by REMAINING DISTANCE** (`advance min(0.5×ball_radius, remaining)`), never a per-frame sub-step count — preserves the half-ball-radius tunnelling invariant even if a bounce/nudge changes speed mid-frame — source: ADR-0002
- **Fixed 60Hz simulation timestep with a fixed-timestep accumulator** (spiral-of-death clamped via `MaxCatchUp`) + display-only render interpolation — source: ADR-0002
- **`Rigidbody2D` in Kinematic mode (interpolation off) drives ball rendering — visual only**, never gameplay-outcome authority — source: ADR-0002
- **`IDeterministicPhysics2D`**: `AdvanceFrame()`, `Balls`, `ConsumeHitEvents()` (count-based, order not scored) — sole authority for scored ball physics — source: ADR-0002
- **⚠️ BLOCKING GATE**: ADR-0002 requires a spike proving ARM64-IL2CPP == x86-CLI byte-identical output (incl. signed-`unchecked` overflow + `IntSqrt`) before status can move to Accepted — fallback if it fails is Alternative D (statistical anti-cheat) — source: ADR-0002. **Do not begin Super Ricochet gameplay implementation before this spike passes.**
- **`BossDamageModel` lives inside `SharedSimCore`** (not a `MonoBehaviour`) — deterministic, bit-reproducible boss HP/defeat state machine; win at `hp≤0` checked at frame boundary BEFORE the danger-line loss check — source: ADR-0011
- **Quack Runner's duck/obstacle state and collision test live in `Fix32`, inside `SharedSimCore`** — reuses ADR-0002's Q16.16 type verbatim; no new numeric type for position/dimensions — source: ADR-0013
- **Runner's AABB overlap test is a plain `Fix32.Raw` (`Int32`) comparison, once per sim frame, no distance sub-stepping** — bit-identical on every platform by construction, since comparison carries zero cross-platform risk once operands are already `Fix32` (Runner has no tunnelling risk, unlike Ricochet) — source: ADR-0013
- **The coordinate-space conversion is `Fix32.FromRatio(rawSpeed, 600)`, and per-frame displacement always multiplies by `FRAME_DT`** (`y += obstacleSpeedFix32PerSecond(t) * FRAME_DT`) — never advance by the bare per-second rate — source: ADR-0013
- **`obstacleSpeedFix32PerSecond(t)` is recomputed fresh every frame, never cached** — a cache would need its own deterministic invalidation trigger, for no measurable benefit over recomputing — source: ADR-0013
- **Runner's engineering safety net**: if a frame's Y-displacement would exceed the configured maximum-safe-displacement constant, clamp it for collision-testing purposes and emit `mode=degraded` (per `anti-cheat-replay-verification.md` Rule 9) — never allow an unchecked skip-through — source: ADR-0013
- **`Fix32.FromRatio` (division) needs its own supplementary CI test-vector proof** — it is not covered by ADR-0002's own spike scope (`+`/`-`/`*`/`IntSqrt` only); `Overlaps()` comparison needs no spike, `FromRatio` does — source: ADR-0013 §4
- **Any probability-branch decision in a scored/replayed path is an integer cumulative threshold out of `RESOLUTION = 10000`, compared against `NextUInt32() % RESOLUTION`** — never a float comparison against `NextFloat01()` or any floating probability value, in any scored path, for any mini-game (project-wide pattern; precedented by Runner's own obstacle-type roll, formally registered by this ADR) — source: ADR-0014
- **Ricochet's per-turn RNG roll order is pinned**: row-spawn roll first; only if a row spawned, 7× brick-HP rolls (column 0→6 ascending), then coin-spawn, then power-up-spawn, in that exact order, every turn, both sides — a no-spawn turn consumes exactly 1 `NextUInt32()` draw, a spawn turn consumes exactly 10 — source: ADR-0014 §5
- **`brick_hp_roll` uses an offline-precomputed cumulative-weight table (largest-remainder apportionment, `Σ == 10000` build-time invariant), checked in as generated C# source and compiled directly into `SharedSimCore.dll`** — same compiled-binary guarantee `Pcg32Rng` itself relies on; zero runtime float, zero `pow()` — source: ADR-0014 §4
- **`SpawnDensityThreshold`'s arithmetic is wrapped in `unchecked`**, matching ADR-0001's existing PRNG-arithmetic convention (cross-platform-identical wraparound at extreme `level`) — source: ADR-0014 §2

### Forbidden Approaches
- **Never use `UnityEngine.Random` or `System.Random` anywhere in gameplay-affecting or RNG-critical code** — neither is guaranteed bit-identical across platforms/.NET versions. **Compile-time enforced**: `SharedSimCore.asmdef` sets `"noEngineReferences": true`, so `using UnityEngine;` fails to compile inside that assembly — `UnityEngine.Random` is structurally unreachable, not just discouraged. `System.Random` is still only a review-time convention (no compiler guard yet) — source: ADR-0001
- **Never stand up a persistent networked microservice for Tier-2 replay verification** — rejected in favor of a stateless CLI child process, to avoid a new deployment target/health-check surface/network auth boundary — source: ADR-0001
- **Never use `float`/`double` anywhere in the scored simulation path** — IEEE-754 precision is not guaranteed bit-identical across iOS-ARM64 and x86; a discrete hit/miss disagreement forks the trajectory unboundedly, which `tolerance_units` cannot absorb. Float is allowed only for display/rendering, never fed back into scored state — source: ADR-0002
- **Never let Unity physics (`Rigidbody2D` dynamic simulation, or `UnityEngine.LowLevelPhysics2D`) be the gameplay-outcome authority** — neither can run in the server-side verification CLI; Unity physics is permitted only in a kinematic, visual-only role driven from `SharedSimCore` — source: ADR-0002
- **Never use wall-clock/elapsed-seconds timing in scored gameplay logic** — all time-bounded rules (e.g. the 12s volley cap = 720 sim frames @60Hz) are counted in integer sim frames, never wall-clock, or a client stutter/focus-loss desyncs the server replay — source: ADR-0002
- **Never let boss damage read a brick's HP/value** — every hit deals exactly 1 boss damage, decoupled from brick HP (deliberate, `boss-ai-damage-model.md` Core Rule 1) — decrement by hit COUNT only — source: ADR-0011
- **Never let a per-sub-step loss check preempt the frame-end boss-win check** — win must take priority over a same-frame loss; latch the danger-line condition as a frame flag and resolve loss AFTER boss-damage apply/win check at the frame boundary, never mid-frame — source: ADR-0011
- **Never make boss HP/defeat state client-authoritative** — win/loss is a replayed scored outcome (ADR-0007); it lives in `SharedSimCore` so it's bit-reproducible and unspoofable — source: ADR-0011
- **Never let the client emit `boss_defeated`/boss-reward events** — server-authoritative only (ADR-0006/0007); client emission would double-count or be spoofable — source: ADR-0011
- **Never treat an AABB/collision comparison as "safe" independent of its operand type** — Runner's comparison is bit-identical *because* both operands are already `Fix32`, not because AABB tests are inherently simple in any numeric type — source: ADR-0013
- **Never leave the coin-spawn/power-up-spawn/brick-HP rolls unconditional on the row-spawn outcome** — all three are gated on "a row spawned this turn"; rolling-and-discarding on a no-spawn turn consumes a different number of `NextUInt32()` draws than skipping entirely, desyncing every subsequent roll for the rest of the run — source: ADR-0014 §5
- **Never regenerate or independently recompute a `BrickHpTable` per consumer** — client and server must link the same compiled `SharedSimCore.dll`; a CI golden-file check confirms the checked-in table matches what the generator currently produces — source: ADR-0014 §4

---

## Feature Layer Rules

*Applies to: currency/economy, analytics, anti-cheat replay verification*

### Required Patterns
- **`mutateWallet(playerId, legs[], idemKey, source)`**: a single multi-leg chokepoint applying N legs in ONE transaction. Leg kinds: `creditMultiplied` (coins, ×(1+coinValueLevel)), `creditFlat` (coins|gems, raw), `debit` (coins|gems, never-negative) — source: ADR-0004
- **Currency never-negative enforced by a store-level conditional `UPDATE ... WHERE coins >= amount RETURNING`** — READ COMMITTED isolation is sufficient, no `SERIALIZABLE` needed — source: ADR-0004
- **Operation-level idempotency**: `INSERT currency_op ON CONFLICT(idem_key) DO NOTHING`; a conflict means replay → rollback + fresh full-wallet `SELECT` — source: ADR-0004
- **New-player wallet uses upsert**: `INSERT wallet ... ON CONFLICT(player_id) DO UPDATE` — a plain `UPDATE` silently drops the credit if the row doesn't exist yet — source: ADR-0004
- **Currency writes append to `currency_ledger`** (one row/leg) **and `currency_op`** (one row/operation) — balance column + append-only ledger, not full event-sourcing — source: ADR-0004
- **Currency analytics via a transactional outbox**, dispatched at-least-once keyed by `op_id` — fire-and-forget after commit loses events on a crash between COMMIT and emit — source: ADR-0004
- **Analytics client API**: `Analytics.Emit(name, params)` — cheap, non-blocking, enqueues to an in-memory queue then a durable buffer — source: ADR-0006
- **Analytics transport is first-party**: durable client buffer → our `/events` batch endpoint; optional server-side fan-out to third-party tools later, no vendor SDK on device — source: ADR-0006
- **Analytics delivery is at-least-once, single-flight flush, remove-after-2xx-ack by `eventId`**; server dedups by `eventId` — source: ADR-0006
- **Analytics durability boundary = synchronous persist to the durable log on `OnApplicationPause`** — on-background is persist + next-launch-reflush, NOT a guaranteed network send (mobile OS suspends the player loop too fast) — source: ADR-0006
- **Analytics buffer: cap 500, FIFO-drop-oldest by `eventId`** (excluding the in-flight batch); batch size 20 / flush interval 30s — source: ADR-0006
- **`sessionId` is a client UUID minted at foreground; `session_end` is best-effort only** — the server closes sessions by inactivity/heartbeat, since a hard kill means the client `session_end` event may never fire. Session-length KPIs are computed server-side from timestamps + `received_at`, never solely from a client `session_end` — source: ADR-0006
- **Anti-cheat reward model is FLAG-ONLY**: Tier-1 clamp governs the reward synchronously at submission; Tier-2 replay is async and only raises a fraud flag → human review. **No clawback.** (This supersedes ADR-0001's original "clawback" wording — see ADR-0001's own superseded-note.) — source: ADR-0007
- **Tier-2 coverage is risk-based**: 100% of high-value/leaderboard/flagged runs + a sampled remainder with anomaly-driven sample-rate escalation — never 100% of all runs — source: ADR-0007
- **Replay verification runs on a warm `.NET SharedSimCore` worker pool consuming a Postgres-backed queue** (`SELECT ... FOR UPDATE SKIP LOCKED`), with atomic claim+reclaim and `lease_epoch` fencing on terminal writes — source: ADR-0007
- **Reward credit + verification-job enqueue commit in ONE transaction** (outbox pattern) — source: ADR-0007
- **`getPlayerLedger(playerId, since, limit)` reads off `idx_currency_ledger_player_created` (an index scan), never a join through `currency_op`** — source: ADR-0012

### Forbidden Approaches
- **Never combine collected-coins (multiplied) and flat-bonus amounts into a single credit call** — the Coin Value multiplier must touch ONLY the collected-coins term, or you reintroduce the reward double-dip (e.g. 5×270 instead of 120). Use separate `creditMultiplied`/`creditFlat` legs — source: ADR-0004
- **Never use application-level read-modify-write, or in-process-mutex-only serialization, for currency balance atomicity** — neither serializes across multiple server instances — source: ADR-0004
- **Never generate a fresh idempotency key per retry attempt for currency mutations** — keys must derive from the operation (run-submission id, IAP receipt id) so a retry reuses the same key — source: ADR-0004
- **Never let the client emit server-authoritative events** (`currency_earned`/`spent`, `purchase_completed`, `quest_claimed`, `mascot_acquired`, `streak_claimed`, `mascot_equipped`) — these come from the server's transactional outbox, never the client. `streak_claimed` rides `mutateWallet`'s outbox (a streak claim credits currency via a `creditFlat` leg — ADR-0004 §4); `mascot_equipped` rides `updatePlayer`'s outbox (equipping is a non-money `player_state` change — ADR-0005 §2) — same shared `analytics_outbox` table, different writing transaction depending on which chokepoint made the change. Client emission double-counts KPIs and is spoofable. Client emits only client-observable events (`app_open`, `session_start`, level/run start+complete, `purchase_initiated`, screen views) — source: ADR-0006 §5, ADR-0004 §4, ADR-0005 §2
- **Never send analytics per-event over HTTP** — batch (20 events / 30s / on-background), never one call per event — source: ADR-0006
- **Never trust the client-supplied timestamp for KPI cohorting** (D1/D7/D30) — device clocks are skewable; cohort on server `received_at`, keep client timestamp only for intra-session ordering — source: ADR-0006
- **Never embed a third-party analytics SDK in the client** — first-party transport only; data leaves the device to our `/events` endpoint alone — source: ADR-0006
- **Never remove/evict buffered analytics events by list position/index** — always key by `eventId`; the buffer shifts between assembling and acking a batch, so index-based removal drops the wrong event — source: ADR-0006
- **Never let Tier-2 replay verification claw back or adjust a granted reward** — anti-cheat GDD Rule 6 is flag-only; Tier-2 has no write path to the wallet — source: ADR-0007
- **Never credit a reward and enqueue its verification job in separate transactions** — a post-commit enqueue failure leaves a rewarded-but-never-verified run; both must be in the same transaction — source: ADR-0007
- **Never write a verification job's terminal state without checking the `lease_epoch` fence** — a slow (not crashed) job whose lease expired and was reclaimed would otherwise double-write terminal state (double flag/analytics). Gate the terminal `UPDATE` on `WHERE lease_epoch = :held`, plus `UNIQUE(run_id)` — source: ADR-0007
- **Never use a per-enqueue `COUNT(*)` to measure queue depth for load-shedding decisions** — a synchronous count on every submission races and adds hot-path load; use a cached/approximate depth gauge — source: ADR-0007

---

## Presentation Layer Rules

*Applies to: UGUI, HUD rendering, mobile touch UI*

### Required Patterns
- **UGUI (Canvas) is the primary UI system** for both the Hub and in-game HUDs — UI Toolkit is deferred (allowed later only for one whole data-heavy screen, via an explicit gate) — source: ADR-0008
- **EventSystem uses `InputSystemUIInputModule` (+ `EnhancedTouch`)** — Unity 6's default new Input System — source: ADR-0008
- **Canvas Scaler: Scale-With-Screen-Size**, with a `SafeArea` component that **recomputes on change** (not once at `Start`/`OnEnable`) — source: ADR-0008
- **TextMeshPro (TMP) is the default text component** — source: ADR-0008
- **UGUI perf discipline for the ≤150 draw-call budget**: static/dynamic Canvas split (don't over-split), `RectMask2D` not `Mask`, no Layout Groups/`ContentSizeFitter` on dynamic content, atlases, event-driven HUD updates (not polled) — source: ADR-0008

### Forbidden Approaches
- **Never use the legacy `StandaloneInputModule`** on the UGUI EventSystem — Unity 6 defaults to the new Input System, with which `StandaloneInputModule` is incompatible (UI receives **zero** touches) — source: ADR-0008
- **Never leave `Raycast Target` enabled on non-interactive UGUI Graphics** (decorative images, text) — the raycaster walks every raycastable graphic per touch; this is the single highest-value UGUI mobile CPU waste — source: ADR-0008
- **Never use stencil `Mask`, or Layout Groups/`ContentSizeFitter` on dynamic content, in UGUI** — `Mask` breaks batching (extra draw calls, undercuts the ≤150 budget); Layout Groups/`ContentSizeFitter` force a rebuild on every change, worst on frequently-updating HUDs/lists. Use `RectMask2D`; position dynamic content manually or pool it — source: ADR-0008
- **Never compute `Screen.safeArea` only in `Start`/`OnEnable`** — safe area/orientation/resolution can change at runtime; a Start-only `SafeArea` component is the classic shipped notch bug. Recompute on change (cache+compare each frame) — source: ADR-0008
- **Never mix UI Toolkit widgets inside a UGUI screen** — if UI Toolkit is introduced later, it must be a WHOLE data-heavy screen via the ADR-0008 gate, never widgets mixed within an existing UGUI screen — source: ADR-0008
- **Never leave the Hub's Camera / EventSystem / AudioListener enabled while a mini-game scene is active** (duplicate of the Foundation-layer rule — this is also a Presentation concern since it directly causes render/audio bugs) — source: ADR-0009

---

## Global Rules (All Layers)

### Naming Conventions
| Element | Convention | Example |
|---------|-----------|---------|
| Classes | PascalCase | `PlayerController` |
| Public fields/properties | PascalCase | `MoveSpeed` |
| Private fields | `_camelCase` | `_moveSpeed` |
| Methods | PascalCase | `TakeDamage()` |
| Files | PascalCase matching class | `PlayerController.cs` |
| Constants | PascalCase or `UPPER_SNAKE_CASE` | — |

### Performance Budgets
| Target | Value |
|--------|-------|
| Frame rate | 60fps target on mid-range devices, 30fps fallback on low-end with dynamic quality scaling |
| Frame budget | 16.6ms |
| Draw calls | ≤150 per frame on mid-range mobile (dynamic batching enabled) |
| Memory | <1.3GB high-end, <700MB mid-range |
| Initial download | <150MB app size before asset-bundle streaming |

### Approved Libraries / Addons
⚠️ **Gap, not silently filled**: `technical-preferences.md`'s "Allowed Libraries" section is still `[TO BE CONFIGURED]`. No libraries are formally approved yet beyond what the ADRs individually name (Addressables per ADR-0009, TextMeshPro per ADR-0008). Populate this before it blocks a real dependency decision.

### Forbidden APIs (Unity 6.3 LTS)
Deprecated/removed as of Unity 6.3 — source: `docs/engine-reference/unity/deprecated-apis.md`:
- `Object.FindObjectOfType<T>()` → use `Object.FindFirstObjectByType<T>()` (deprecated since 2023.1)
- `Object.FindObjectsOfType<T>()` → use `Object.FindObjectsByType<T>(FindObjectsSortMode.None)`
- `[SerializeField]` on a property/method/type → `[field: SerializeField]` on an auto-property, or plain `[SerializeField]` on a backing field only (compile-time error as of 6.3, not a no-op)
- `RenderGraphSettings.enableRenderCompatibilityMode` → rewrite against the Render Graph API (removed in 6.3)
- Custom `ScriptableRenderPass.Execute(...)` as the primary override → `ScriptableRenderPass.RecordRenderGraph(...)`
- `AccessibilityNode.selected` → `AccessibilityNode.invoked`
- `PlayerSettings.Android.androidIsGame` → Android App Category Player Setting
- Round/legacy Android launcher icon slots → adaptive icons only
- `UPM_NPM_CACHE_PATH` env var → `UPM_CACHE_ROOT`
- Experimental lightmapping `AdditionalBakedProbes`/`CustomBake` → removed/obsolete, use `LightTransport.IProbeIntegrator`

**Not deprecated, contrary to common assumption** (don't "fix" these): legacy `Input.*` (still functional, new Input System just preferred), UGUI (`UnityEngine.UI`) itself (maintenance mode, not deprecated — this project uses it as primary per ADR-0008), raw `AssetDatabase`/`AssetBundle` APIs.

### Cross-Cutting Constraints
- **No dedicated Unity specialist subagents exist in this environment.** Route Unity-specific work to `general-purpose`/`Plan` agents using `technical-preferences.md`'s routing table, or use the Agent tool with an explicit brief naming the relevant Unity subsystem.
- **Target platforms**: iOS 14+, Android API 25+ (Android 7.1 minimum, per Unity 6.3 LTS's own floor — raised from the master prompt's original "API 21+" ask).
- **Input**: Touch only, no gamepad support. All UI must be thumb-reachable on one-handed portrait play; no hover-only interactions.
- **This manifest is PROVISIONAL** (see header) — every rule above is sourced from a Proposed, not yet Accepted, ADR. Treat as a strong working reference, not a final ratified contract, until `/architecture-review` passes.

### Open Cross-ADR Items (not resolved by this manifest — see `docs/registry/architecture.yaml`'s `open_items`)
- `tolerance_units` reconciliation for Super Ricochet's now-bit-exact physics path (ADR-0002 vs. ADR-0001/anti-cheat GDD)
- Auth token revocation/TTL (ADR-0003) — no server-side revocation endpoint exists; secure storage is the only defense against a stolen 7-day token
- Currency starting-grant ownership + ledger retention policy (ADR-0004)
- UI Toolkit gate trigger screen — which future screen (if any) justifies the gate (ADR-0008)
- `obstacleSpeed(t)`'s unbounded growth (Runner) — confirmed-structural exploit (guaranteed survival + coin farming past a large-but-reachable `t`), routed to `obstacle-spawn-difficulty-ramp-runner.md` as a required cap; ADR-0013's engineering safety net prevents a silent break but does not restore the intended difficulty curve (ADR-0013 §2)
