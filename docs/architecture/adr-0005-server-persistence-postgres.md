# ADR-0005: Server-Side Persistence Migration to PostgreSQL

## Status
Proposed

## Date
2026-07-10

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Unity 6.3 LTS client, but this decision is **server-side** (Node.js/TypeScript backend) |
| **Domain** | Core / Persistence (server) |
| **Knowledge Risk** | LOW — standard PostgreSQL/Node data-layer design, no post-cutoff engine dependency |
| **References Consulted** | `design/gdd/save-persistence.md`, `design/gdd/currency-system.md`, ADR-0004 |
| **Post-Cutoff APIs Used** | None |
| **Verification Required** | Concurrency test (two writers to one player record lose no data); ADR-0004's currency tests pass on the real backend |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | None |
| **Enables** | **ADR-0004** (supplies the transactional store its atomicity contract requires — unblocks multi-instance currency); Account/Auth, Daily Quests, Mascot DB, and every system needing durable player state |
| **Blocks** | Multi-instance deployment of anything writing currency (see ADR-0004 coupling) |
| **Ordering Note** | Client-side local cache / offline-queue is a **separate ADR** (per architecture.md's ADR list) and is explicitly out of scope here |

## Context

### Problem Statement
The `quack-blaster` prototype persists each player as an atomic-JSON-file record behind a narrow interface (`findById`, `findByUsername`, `insert`, `updatePlayer(id, mutate)`, top-N query). `save-persistence.md` Rule 2 mandates migrating that store to **PostgreSQL** while preserving the same narrow interface so calling systems don't change. ADR-0004 further requires a store with real multi-statement transactions and column-level conditional updates — which the JSON-file store cannot provide across instances. This ADR defines the PostgreSQL backend, its schema shape, and — the non-obvious part — how the GDD's generic `updatePlayer(id, mutate)` chokepoint coexists with ADR-0004's currency path, which explicitly **forbids** app-level read-modify-write.

### Scope boundary
**In scope**: server-side durable storage, schema, transaction model, data-access layer, migration mechanics. **Out of scope** (own ADRs): the client-side local cache, offline action queue, and reconnect/reconcile logic (`save-persistence.md` Rules 5–8) — routed to a "client cache / offline queue" ADR.

### The read-modify-write reconciliation (the crux)
`save-persistence.md` Rule 3 wants one `updatePlayer(id, mutate)` that loads a player, applies a mutator, and persists atomically. The precise distinction (not "RMW is unsafe"): an **unlocked/optimistic** read-modify-write can lose updates — that is what ADR-0004 rejected for money — whereas a **locked** read-modify-write (`SELECT ... FOR UPDATE` → mutate → `UPDATE`, all in one transaction) is safe against lost updates, because a second writer blocks until the first commits and then re-reads the post-commit row. So the generic mutator uses the *locked* form and is legitimate for whole-record, non-money state. Money still goes through ADR-0004's column path for concrete reasons a locked whole-record mutator doesn't give: idempotency on retry, a cheap never-negative conditional that doesn't hold a row lock, and the ledger/outbox. This ADR makes that split explicit rather than letting the two GDD rules look contradictory.

## Decision

### 1. PostgreSQL, hybrid schema — relational money, JSONB flexible state
- **Relational tables (real columns)** for anything contended, money-critical, or *queried across players*, so ADR-0004's guarded updates and the top-N query work: `player(id PK, username UNIQUE, created_at, ...)`, `wallet(player_id PK/FK, coins, gems)`, `currency_ledger`, `currency_op`, `analytics_outbox`.
- **Any value the top-N (leaderboard) query sorts on is a real indexed column**, not a JSONB field — e.g. `player.leaderboard_score` with a descending index (or an expression index). A leaderboard sort key buried in `player_state.data` would force a full table scan + sort on every call; promoting it to an indexed column keeps top-N cheap. (This satisfies the narrow interface's top-N query, which the prototype served trivially from memory.)
  > ⚠️ **AMENDED by ADR-0012**: this singular `player.leaderboard_score` column was written before `quack-runner.md` established that Ricochet and Runner need **separate per-game leaderboards** (structurally incomparable score formulas). ADR-0012 replaces it with a generic `leaderboard_scores(player_id, game_id, score, achieved_at, display_stats)` table — same underlying principle (indexed relational column, never JSONB), now per-game. The column-vs-JSONB *reasoning* on this line stands; the specific singular-column *implementation* is superseded. See ADR-0012.
- **JSONB column** `player_state.data` for the evolving, whole-record-mutated *per-player-only* game state (progress, daily/quest state, mascot ownership, upgrades) that is never queried across players — schema-flexible as new systems ship, without a migration per field, and closest to the prototype's per-player shape for an easy import. It carries a `schema_version` (own column or reserved key) and is validated app-side on read/write with a concrete validator (e.g. **zod** / JSON-schema), since Postgres does not type JSONB contents.
- Rationale: the money paths need real columns for `WHERE coins >= :amt` / `ON CONFLICT`; the rest is malleable display/progress state only ever read-modified-written as a unit. Fully-normalizing that evolving state now would mean a migration per new mascot/quest field for no concurrency benefit; a pure JSONB blob couldn't give the wallet its column-level guards. The hybrid takes each where it's strongest.

### 2. Two write paths on one transactional store (reconciles the GDD crux)
- **Generic `updatePlayer(id, mutate)`** — for JSONB state and non-money player fields. Implemented as, inside one transaction: `SELECT ... FOR UPDATE` (row lock) → apply the mutator in app → `UPDATE`. The row lock makes concurrent whole-record mutations safe (no last-write-wins clobber, satisfying GDD Rule 4/Acceptance "neither is lost"). The interface signature is preserved, so callers don't change. **Mutators must be pure and fast — no network/file I/O inside `mutate`**, because the row lock (and a pooled DB connection) is held for the mutator's whole execution; blocking on I/O there would hold the lock across a network wait and throttle concurrency.
- **Currency mutations do NOT use `updatePlayer`** — they go through ADR-0004's `mutateWallet` (column-level conditional `UPDATE ... WHERE coins >= :amt`, idempotency, ledger). Registered as the exception: money is never mutated via the generic read-modify-write mutator.
- **[ADDED 2026-07-18] `updatePlayer` also writes the shared `analytics_outbox`** for server-authoritative **non-money** state events, the same at-least-once transactional-outbox mechanism ADR-0004 §4 established for currency events — generalized here to the player-state chokepoint. The first such event is `mascot_equipped` (`mascot-gallery-equip-ui.md` Rule 7): equipping mutates `player_state.data.equippedMascotId` via `updatePlayer` with **no currency change**, so its analytics event is written to the outbox **inside the `updatePlayer` transaction** (keyed by the same operation id used to dedupe the write), never `mutateWallet` and never the client buffer. This is the mechanically-correct home the two GDDs' Open Questions approximated as "ADR-0004's outbox"; the outbox *table* is shared (owned by this ADR's store), the *writing transaction* is whichever chokepoint made the change. See ADR-0006 §5 for the full ownership split.
- Both paths share the same connection/transaction machinery and can compose in a single transaction when an operation touches both (e.g. a run that updates quest JSONB state *and* credits coins — one transaction, wallet via the guarded path, state via the locked mutator). Two **mandatory** rules for composed operations, or they are subtly broken:
  - **Canonical lock order (prevents deadlock)**: always acquire the `player_state` row lock (`SELECT ... FOR UPDATE`) **before** the guarded `wallet` update, in *every* composed path. Two paths acquiring these in opposite orders would ABBA-deadlock under concurrency (Postgres aborts one, producing intermittent op failures). One order, enforced everywhere.
    > ⚠️ **EXTENDED by ADR-0012**: `leaderboard.md`'s scoring writes share a transaction with reward crediting, adding a **third** leg. ADR-0012 extends this to a fixed three-position order (`player_state` → `wallet` → `leaderboard_scores`) for any transaction touching 2+ of the three — not just this two-leg case. See ADR-0012.
  - **Whole-operation idempotency (prevents partial double-apply)**: on a retried composed op (e.g. a lost commit-ack), the money leg self-dedupes via `currency_op ON CONFLICT`, but the JSONB mutator would **re-apply** and double-increment state. So a composed op MUST be gated on the same operation `idem_key`: check/insert `currency_op` first and, if it's a replay, skip the state mutator too (return the prior result). Equivalently, state mutators in composed ops must be idempotent. Never let the two legs have different retry semantics.

### 3. Data-access layer: query builder + raw parameterized SQL (no heavy ORM)
Use a thin query builder (e.g. Knex) and/or `pg` with hand-written parameterized SQL. ADR-0004's `UPDATE ... WHERE ... RETURNING`, `INSERT ... ON CONFLICT DO NOTHING`, and multi-leg transactions are written exactly as specified, with no ORM abstraction to fight. All SQL is parameterized (no string interpolation) — SQL-injection-safe by construction. JSONB CRUD uses the same layer with Postgres JSONB operators.

### 4. Migration mechanics — versioned migrations, fresh schema, optional dev import
- **Versioned, ordered SQL migrations** (e.g. `node-pg-migrate` / Knex migrations) in the repo; schema changes are code-reviewed migration files, never ad-hoc `ALTER`s. Each migration is forward-only with a documented rollback where feasible.
- quack-studio is a **new native product with no production players** — the prototype's JSON data is dev/test only. So this is *not* a live zero-downtime migration: stand up the Postgres schema fresh. A **one-shot JSON→Postgres import script** is provided only for dev continuity (map each JSON player file: money fields → `wallet`, everything else → `player_state.data`), not as a production cutover.
- **Connection pooling** via `pg.Pool` (and PgBouncer if instance count grows) — standard; sized per deployment.

### Architecture Diagram
```
Calling systems (Auth, Quests, Mascots, run-reward chain, Shop)
     │                                   │
     │ non-money / JSONB state           │ money
     ▼                                   ▼
 updatePlayer(id, mutate)          mutateWallet (ADR-0004)
     │  BEGIN                            │  BEGIN
     │  SELECT player_state FOR UPDATE   │  currency_op ON CONFLICT ...
     │  mutate() in app                  │  guarded UPDATE wallet ... RETURNING
     │  UPDATE player_state.data         │  INSERT currency_ledger + outbox
     │  INSERT analytics_outbox          │  COMMIT
     │   (server-authoritative state
     │    events, e.g. mascot_equipped)
     │  COMMIT
     └──────────────┬────────────────────┘
                    ▼
         PostgreSQL (single transactional store; pg.Pool)
   player · wallet · currency_ledger · currency_op · analytics_outbox · player_state(JSONB)
```

## Alternatives Considered

### Alternative A: Fully normalized relational schema (everything in columns/tables)
- **Pros**: Best for ad-hoc querying/analytics; strong typing at the DB.
- **Cons**: A schema migration for every new per-player field (mascots, quests still evolving); heavy upfront modelling of state that changes shape often.
- **Rejection Reason**: The hybrid gets relational guarantees exactly where needed (money) without imposing per-field migrations on fast-evolving game state.

### Alternative B: Player-as-JSONB-blob only
- **Pros**: Minimal migration from the JSON prototype; one document per player.
- **Cons**: Wallet can't get efficient column-level `WHERE coins >= :amt` guards; ADR-0004's atomicity would be awkward and slow (JSONB field updates under contention).
- **Rejection Reason**: Undermines the currency atomicity just designed.

### Alternative C: Stay on JSON files / adopt a document DB (e.g. Mongo)
- **Pros**: Least immediate change.
- **Cons**: JSON files give no cross-instance ACID; the master prompt and GDD Rule 2 specify PostgreSQL; a document DB reopens a settled decision without cause.
- **Rejection Reason**: Fails the core requirement (real transactions) and contradicts the specified stack.

### Alternative D: Full ORM (Prisma/TypeORM)
- **Pros**: Ergonomic typed models, easy JSONB/CRUD.
- **Cons**: ADR-0004's precise conditional-update/idempotency SQL needs the raw-query escape hatch anyway; the ORM adds weight for partial benefit.
- **Rejection Reason**: The money paths — the whole reason for this migration — read cleanest as explicit SQL.

## Consequences

### Positive
- Real ACID transactions unblock ADR-0004 and multi-instance deployment.
- Narrow interface preserved (`findById`/`findByUsername`/`insert`/`updatePlayer`/top-N) — calling systems largely unchanged.
- Hybrid schema keeps game-state iteration fast (JSONB) while money is strongly consistent (relational).
- Explicit two-path split ends the latent GDD Rule 3 vs ADR-0004 contradiction.

### Negative
- Operational surface: a database to provision, back up, monitor, and run migrations against (vs. copying a JSON file).
- Two write paths to teach/enforce — contributors must know money never goes through `updatePlayer`.
- JSONB is weakly typed at the DB — app-side validation of `player_state.data` shape becomes the app's responsibility.

### Risks
- **Risk**: A developer routes a currency change through the generic `updatePlayer` mutator (read-modify-write), reintroducing the lost-update/never-negative bug.
  **Mitigation**: Registry forbidden pattern (money via `updatePlayer`); `wallet` columns live outside `player_state.data` so currency simply isn't reachable from the generic mutator; code review.
- **Risk**: Unvalidated/inconsistent JSONB shape drifts over time.
  **Mitigation**: App-side schema/version field on `player_state.data`; validate on read/write.
- **Risk**: The one-shot import script is mistaken for a production migration path.
  **Mitigation**: Documented as dev-only; no production players exist yet.

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|------------|-------------|---------------------------|
| save-persistence.md | Rule 2: migrate atomic-JSON store to PostgreSQL, preserve narrow interface | Hybrid Postgres schema; same `findById/findByUsername/insert/updatePlayer/top-N` interface |
| save-persistence.md | Rule 3: single atomic mutate chokepoint | `updatePlayer` = `SELECT FOR UPDATE` + mutate + `UPDATE` in one txn (money excepted → ADR-0004) |
| save-persistence.md | Rule 4 / Acceptance: concurrent writes lose no data | Row lock (`FOR UPDATE`) for generic state; conditional update for money |
| currency-system.md | Atomic mutation depends on Save/Persistence's chokepoint | Provides the transactional store ADR-0004's `mutateWallet` requires |

## Performance Implications
- **CPU/Memory**: Standard DB server cost; per-op work is small (indexed by `player_id`).
- **Load Time**: `findById` by PK is fast; JSONB read is a single row.
- **Network**: App↔DB round-trips replace file I/O; pooled connections.

## Migration Plan
Stand up the Postgres schema via versioned migrations; no production cutover (no live players). Optional dev import maps prototype JSON → `wallet` + `player_state.data`. Calling code changes only where it must stop routing money through the generic mutator.

## Validation Criteria
- Two concurrent `updatePlayer` calls on the same player both apply (no lost update).
- ADR-0004's currency concurrency/idempotency/multi-leg tests pass against the real Postgres backend.
- A single operation crediting coins *and* updating quest JSONB state commits as one transaction (both or neither).
- **Composed-op deadlock test**: many concurrent composed (wallet + player_state) operations run without deadlock, confirming the canonical lock order (player_state `FOR UPDATE` before wallet update) holds everywhere.
- **Composed-op retry test**: a retried composed op (same `idem_key`) applies the money leg once AND the JSONB state mutator once — no double-increment of state.
- **Leaderboard test**: the top-N query uses the `leaderboard_score` index (verify via `EXPLAIN` — index scan, not a full table scan).
- Migrations apply cleanly from empty; dev import reproduces a prototype player faithfully.

## Related Decisions
- **ADR-0004** — the currency atomicity contract this store satisfies.
- `save-persistence.md` — the persistence design this implements (server half).
- **ADR-0010** — the client cache / offline-queue ADR (Rules 5–8), the client half explicitly out of scope here.
- **ADR-0012** — replaces the singular `player.leaderboard_score` column with a per-game table, and extends the canonical lock order/idempotency rule (Decision §2) to a third leg.

## Open Questions
- Exact migration tooling (`node-pg-migrate` vs Knex migrations vs other) — a small implementation choice, not load-bearing; pick at setup.
- Backup/PITR and connection-pool sizing (PgBouncer threshold) — deployment-time, owned by the future infra/hosting decision.
- Whether any prototype dev data actually needs importing, or a clean start suffices — confirm at implementation.
