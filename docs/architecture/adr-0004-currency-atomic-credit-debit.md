# ADR-0004: Currency System Atomic Credit/Debit

## Status
Proposed

## Date
2026-07-10

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Unity 6.3 LTS client, but this decision is **server-side** (Node.js/TypeScript backend per the master prompt) |
| **Domain** | Core / Economy / Persistence (server) |
| **Knowledge Risk** | LOW — this is server-side transaction design, independent of the post-cutoff Unity APIs; no engine-version risk |
| **References Consulted** | `design/gdd/currency-system.md`, `design/gdd/save-persistence.md` |
| **Post-Cutoff APIs Used** | None |
| **Verification Required** | Concurrency test: N parallel debits that individually fit but jointly exceed balance must leave balance ≥ 0 with exactly the fitting subset applied; retry test: the same idempotency key applied twice credits once |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | The **persistence-migration ADR** (not yet written) must supply a data store with real multi-statement transactions / atomic conditional updates. This ADR defines the *contract*; that ADR chooses the engine that satisfies it. |
| **Enables** | IAP purchase-credit ADR (real-money currency uses this same path + idempotency + provenance) |
| **Blocks** | Any reward-granting or shop-spend implementation that mutates currency |
| **Ordering Note** | The in-process-atomic-file model inherited from `quack-blaster` satisfies this contract **only for a single server instance**; multi-instance deployment requires the persistence migration first (see Consequences) |

## Context

### Problem Statement
`currency-system.md` makes Currency System the sole owner of coin/gem **balance integrity**: atomic mutation (Rule 2), never-negative (Rule 3), the two-path Coin Value multiplier (Rule 4, the fix for the reward double-dip bug found in `/review-all-gdds`), and an analytics event per mutation (Rule 6). The GDD specifies *what* must hold; this ADR fixes *how* — the server-side transaction contract — and closes two gaps the GDD leaves open that a real-money economy cannot ship without:
- **Retry-safety (idempotency)**: a network retry of a run-reward credit, or an IAP credit, must not double-apply. The GDD does not define this.
- **Concurrency across instances**: the prototype's atomicity was an in-process atomic file write, which does not serialize two *separate* server processes mutating the same wallet. Real atomicity must be enforced by the data store.

### Requirements
- Atomic read-modify-write of a wallet, correct under concurrency **and** across multiple server instances.
- Never-negative, enforced at the store, with no partial application on a failed debit.
- Exactly-once application per idempotency key (safe retries).
- The two-path credit API is the only way to credit coins; the multiplier can never touch a flat bonus.
- An audit trail sufficient for IAP dispute/provenance and reconciliation.

## Decision

### 1. One multi-leg atomic chokepoint; the two-path API builds legs
All currency writes go through one internal `mutateWallet` that applies **N legs in a single transaction** — so a reward that credits collected coins *and* a flat boss bonus is one atomic operation, never two that can partially fail (reviewer B4). The two-path credit API constructs *legs*, it does not open its own transactions:
```ts
// Server-side (Node/TS). Amounts are pre-validated positive integers.
type Leg =
  | { kind: 'creditMultiplied'; amount: number }          // coins only; ×(1+coinValueLevel) applied here
  | { kind: 'creditFlat'; currency: 'coins'|'gems'; amount: number }  // raw, never multiplied
  | { kind: 'debit'; currency: 'coins'|'gems'; amount: number };      // never-negative

mutateWallet(playerId, legs: Leg[], idemKey, source): WalletResult   // ONE transaction for all legs
```
- The `creditMultiplied` leg is the **only** place `× (1 + coin_value_upgrade_level)` is applied; `creditFlat` is raw. They are never merged — this locks `currency-system.md` Rule 4 and prevents the `5×270`-instead-of-`120` double-dip. Gems only ever use `creditFlat`/`debit` (never multiplied).
- One **operation-level** `idemKey` covers the whole multi-leg call (e.g. a run-submission id). Amount validation (reject negative/NaN/non-integer) happens before the store is touched (GDD Rule 5 — structural validity only; reward-formula correctness is Anti-Cheat's job upstream).

### 2. Correct transaction flow (fixes reviewer B1/B2/B3)
```sql
BEGIN;  -- READ COMMITTED is sufficient (see note)

-- (a) Idempotency guard, operation-level. ON CONFLICT DO NOTHING — a bare unique
--     violation would abort the whole txn, so we must use ON CONFLICT, not catch-and-continue.
INSERT INTO currency_op (idem_key, player_id, source, created_at)
  VALUES (:idemKey, :playerId, :source, now())
  ON CONFLICT (idem_key) DO NOTHING
  RETURNING op_id;
--   → 0 rows returned ⇒ this is a REPLAY: ROLLBACK, then serve a fresh full-wallet
--     SELECT (coins AND gems) as the result. (Do not reconstruct from a single ledger row.)

-- (b) For each leg, apply to the balance FIRST and read the result via RETURNING
--     (never an app-side re-read — that read is racy). Ordering matters: the guarded
--     UPDATE must run before the ledger insert, because resulting_balance and the
--     insufficient-funds outcome are only known after it.

-- credit leg (also fixes the new-player row-missing gap via upsert):
INSERT INTO wallet (player_id, coins, gems) VALUES (:playerId, :dCoins, :dGems)
  ON CONFLICT (player_id) DO UPDATE
    SET coins = wallet.coins + :dCoins, gems = wallet.gems + :dGems
  RETURNING coins, gems;

-- debit leg (never-negative enforced at the store):
UPDATE wallet SET coins = coins - :amt
  WHERE player_id = :playerId AND coins >= :amt
  RETURNING coins, gems;
--   → 0 rows affected ⇒ insufficient ⇒ ROLLBACK the whole operation, reject, balance untouched.

-- (c) Append one ledger row per leg, using the RETURNING balance:
-- ⚠️ AMENDED by ADR-0012: currency_ledger gains a denormalized player_id
-- column (currency-ledger-transaction-log.md Core Rule 2 needs player-scoped
-- queries without a join through currency_op). Insert below updated
-- accordingly; see ADR-0012 for the column, its index, and the migration
-- sequence for backfilling existing rows.
INSERT INTO currency_ledger (op_id, currency, delta, resulting_balance, multiplier_applied, created_at, player_id)
  VALUES (:opId, :currency, :delta, :resultingBalance, :multiplierApplied, now(), :playerId);

COMMIT;
```
- **Isolation note**: the single-row guarded `UPDATE` is never-negative-safe under **READ COMMITTED** (row lock + re-evaluation of the `WHERE` on the locked row). It does **not** require `SERIALIZABLE` or an explicit `SELECT ... FOR UPDATE` — stated so nobody "fixes" it by over-locking.
- **Explicitly rejected**: app-level read-modify-write, and in-process-mutex-only serialization — both wrong the moment a second instance exists.

### 3. Balance column + append-only ledger
- Authoritative `wallet(player_id, coins, gems)` for O(1) reads.
- Append-only `currency_ledger` (one row per leg) + `currency_op` (one row per operation, holding the `UNIQUE idem_key`). Together they give DB-enforced idempotency, an IAP-grade audit/provenance trail, and a reconciliation source — **without** rebuilding balance from events per read (full event-sourcing rejected as heavier than needed).

### 4. Analytics via a transactional outbox (fixes the crash-after-commit loss)
`currency_earned`/`currency_spent` (GDD Rule 6) must not be lost if the process crashes after `COMMIT` but before emitting. The event is written to an **outbox table inside the same transaction** as the balance change; a separate dispatcher publishes from the outbox and marks it sent. This gives at-least-once delivery keyed by `op_id`/`tx_id`, rather than fire-and-forget after commit.

> **[ADDED 2026-07-18] Outbox catalog additions — `streak_claimed`, and a note on `mascot_equipped`.** `login-streak.md` Rule 12 proposes a `streak_claimed` analytics event. Because a streak claim credits currency via a `creditFlat` leg (Rule 11), it is already a `mutateWallet` operation — so `streak_claimed` is written to the outbox **in this same transaction**, exactly-once keyed by `op_id`, exactly like `quest_claimed`. No mechanism change here; it's another event name on the existing currency-outbox path. **`mascot_equipped` (mascot-gallery-equip-ui.md Rule 7) is NOT on this path** — equipping changes only `player_state.data.equippedMascotId` with no currency mutation, so it never calls `mutateWallet`. Both GDDs' Open Questions loosely routed both events "to ADR-0004's outbox"; that is correct for `streak_claimed` but `mascot_equipped` rides ADR-0005's `updatePlayer` transaction writing the **same** shared `analytics_outbox` table. See ADR-0005 §2 and the ADR-0006 §5 ownership split (the authoritative event catalog).

### Architecture Diagram
```
Reward chain / Shop spend / IAP credit
        │  legs[] + one operation-level idemKey
        ▼
 creditMultiplied / creditFlat / debit legs   ← two-path API builds legs (multiplier only in creditMultiplied)
        │
        ▼
   mutateWallet ───────────  BEGIN (READ COMMITTED)
        │                     (a) INSERT currency_op ON CONFLICT(idem_key) DO NOTHING RETURNING op_id
        │                         └ 0 rows ⇒ REPLAY: ROLLBACK, return fresh full-wallet SELECT
        │                     (b) per leg: guarded UPDATE/upsert wallet ... RETURNING balance
        │                         └ debit 0 rows ⇒ insufficient ⇒ ROLLBACK, reject
        │                     (c) per leg: INSERT currency_ledger(resulting_balance from RETURNING)
        │                     (d) INSERT analytics outbox row (same txn)
        ▼                    COMMIT
   WalletResult ──► outbox dispatcher publishes currency_earned/spent (at-least-once, keyed by op_id)
```

## Alternatives Considered

### Alternative A: In-process single-writer mutex/queue (the prototype's model)
- **Pros**: Simplest; matches `quack-blaster`'s atomic-file chokepoint.
- **Cons**: Serializes only within one process; two server instances mutating the same wallet race and can drive balance negative or lose writes.
- **Rejection Reason**: A live-ops economy will scale past one instance; correctness must not depend on single-process deployment.

### Alternative B: Balance column only, no ledger
- **Pros**: Lightest schema.
- **Cons**: No audit trail (bad for IAP disputes/provenance), idempotency needs a separate dedupe store anyway, no reconciliation source.
- **Rejection Reason**: The ledger's cost is small and it solves idempotency + audit + reconciliation together.

### Alternative C: Full event-sourced ledger (no stored balance)
- **Pros**: Maximum auditability; balance always derivable.
- **Cons**: Requires snapshotting and replay; higher read cost and operational complexity than this economy warrants.
- **Rejection Reason**: The balance-column-plus-log hybrid captures the audit benefit at a fraction of the complexity.

## Consequences

### Positive
- Correct under concurrency and horizontal scaling — the never-negative and double-spend guarantees live in the store.
- Retries are exactly-once; safe for flaky mobile networks and IAP.
- Full audit/provenance trail; the two-path API structurally prevents the multiplier double-dip.

### Negative
- **Hard coupling to the persistence-migration ADR**: this contract needs real store transactions. The single-instance JSON-file prototype satisfies it only until a second instance is deployed — so multi-instance rollout is *gated* on the persistence migration. Recorded explicitly rather than discovered in production.
- More schema (wallet + ledger) and caller discipline (every mutation must supply a stable idempotency key).

### Risks
- **Risk**: A caller passes a non-stable idempotency key (e.g., a fresh UUID per retry), defeating dedupe.
  **Mitigation**: Keys must derive from the *operation* (run-submission id, receipt id), not be freshly generated per attempt — documented as an API contract; review checks.
- **Risk**: Ledger growth unbounded.
  **Mitigation**: Retention/archival policy (Open Questions) — not a launch blocker but must be owned.
- **Risk (cross-ADR)**: Someone deploys multiple instances on the JSON-file backend before the migration.
  **Mitigation**: The persistence ADR must gate multi-instance on a transactional store; flagged in both places.

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|------------|-------------|---------------------------|
| currency-system.md | Rule 2: single atomic mutate chokepoint | `mutateWallet`, store-level transaction |
| currency-system.md | Rule 3: never negative, no partial debit | Conditional atomic UPDATE guarded by `coins >= amt` |
| currency-system.md | Rule 4: two-path multiplier, never combined | `creditMultiplied` vs `creditFlat`; multiplier only in the former |
| currency-system.md | Rule 6: analytics event per mutation | Written to a transactional outbox in the same txn; dispatched at-least-once, keyed by `op_id` |
| currency-system.md | Edge: concurrent double-spend race | Store serializes; second debit re-checks decremented balance |
| currency-system.md | Edge: malformed (negative/NaN) amount rejected | Entrypoint validation before store access |
| currency-system.md | Open Q2: IAP provenance | Ledger `source` + `idempotency_key` (= receipt id) give provenance; full IAP receipt validation is its own ADR |

## Performance Implications
- **CPU/Memory**: Negligible per mutation. One transaction, one conditional update, one insert.
- **Storage**: Ledger grows one row per mutation — bounded by activity; retention policy owns long-term size.
- **Network**: None new (server-internal).

## Migration Plan
The `quack-blaster` prototype's atomic-file `updatePlayer` chokepoint maps to this contract for single-instance operation. Moving to the transactional store (persistence ADR) is where the wallet + ledger tables and the conditional-update/idempotency mechanics are implemented. No player-facing balance changes.

## Validation Criteria
- Parallel-debit concurrency test: balance never negative, exactly the fitting subset applied.
- Idempotency/replay test: same operation `idemKey` twice ⇒ credited once; the second call rolls back and returns a fresh full-wallet read (coins AND gems), not a reconstruction from one ledger row.
- New-player test: first-ever credit to a player with no `wallet` row succeeds via upsert (no silently-dropped credit).
- Multi-leg atomicity test: a reward with a `creditMultiplied` leg + a `creditFlat` leg where the second leg is forced to fail leaves **neither** applied (all-or-nothing, no partial reward).
- Double-dip test: a run crediting collected coins + a boss flat bonus applies `collected × (1+level) + flat`, never `(collected+flat) × (1+level)`.
- Outbox test: a crash injected after `COMMIT` but before dispatch still results in the analytics event being published (at-least-once), and a rolled-back operation publishes nothing.

## Related Decisions
- `currency-system.md` — the economy design this implements.
- ADR-0005 — supplies the transactional store this contract requires (Postgres).
- ADR-0012 — amends `currency_ledger` with a denormalized `player_id` column + index (currency-ledger-transaction-log.md's required addendum).
- Future: **IAP purchase-credit ADR** — reuses this path with receipt-derived idempotency keys.

## Open Questions
- **Starting-balance grant** (`currency-system.md` Open Q1): does Currency System own a new-player grant, or does onboarding? Routed to the GDD owner.
- **IAP receipt validation depth**: this ADR gives provenance via the ledger; full store-receipt verification is the IAP ADR's job.
- **Ledger retention/archival policy**: define before long-term live-ops, not a launch blocker.
