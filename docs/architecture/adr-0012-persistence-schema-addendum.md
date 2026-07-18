# ADR-0012: Persistence Schema Addendum — Currency Ledger Player-ID + Per-Game Leaderboard Table

## Status
Proposed

## Date
2026-07-17

> **Independently reviewed (2026-07-17, general-purpose agent standing in
> for a backend/distributed-systems specialist)**: 1 blocking issue found
> and fixed (the `currency_ledger.player_id` migration SQL as originally
> drafted would fail on execution — adding a `NOT NULL` column to a table
> with existing rows and no `DEFAULT` violates the constraint the same
> statement adds), plus 2 recommended clarifications folded in (the fixed
> lock order's scope stated explicitly as covering any transaction
> touching 2+ of the three tables, not just the reward-credit chokepoint;
> the leg-3 idempotency-gating boundary condition — automatic only while
> the write stays inside the transaction — made explicit) and 1
> nice-to-have (`achieved_at` provenance). The leaderboard upsert itself
> was independently verified as correct Postgres with no issues found.

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Server-side (Node.js/TypeScript backend, PostgreSQL). No client/Unity surface. |
| **Domain** | Core / Persistence / Economy (server) |
| **Knowledge Risk** | LOW — standard PostgreSQL schema/index/transaction work, no post-cutoff dependency |
| **References Consulted** | `currency-ledger-transaction-log.md`, `leaderboard.md`, ADR-0004, ADR-0005 |
| **Post-Cutoff APIs Used** | None |
| **Verification Required** | Composed-op deadlock test extended to the three-leg case; `getPlayerLedger` index-scan test; leaderboard upsert-with-display-stats atomicity test |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-0004 (`currency_ledger`/`currency_op` schema this amends), ADR-0005 (Postgres backend, `player.leaderboard_score` column this replaces, canonical lock order this extends) |
| **Enables** | Anti-Cheat's `getPlayerLedger(playerId, since, limit)` escalation-review query (currency-ledger-transaction-log.md Rule 2); Leaderboard's per-game top-N reads and writes (leaderboard.md Rules 3/9) |
| **Blocks** | Nothing hard — both consuming GDDs already ship other content; this ADR unblocks their *persistence layer* specifically |
| **Ordering Note** | Both amendments here are schema-only — no new architectural pattern, no new engine risk. They are grouped in one ADR because both are gap-filling addenda to ADR-0004/0005 flagged by GDDs written *after* those ADRs, and both touch the same composed-transaction machinery ADR-0005 already established. |

## Context

### Problem Statement
Two GDDs, both written and CD-GDD-ALIGN-approved after ADR-0004/ADR-0005 were finalized, each flagged a required schema addendum that neither GDD could itself author:

1. **`currency-ledger-transaction-log.md` Core Rule 2**: ADR-0004's `currency_ledger` table has no `player_id` column — only `op_id`, which requires a join through `currency_op` to scope by player. Every reader this GDD defines (Anti-Cheat's escalation review, support/ops tooling, IAP/finance reconciliation) needs player-scoped queries. Without a direct column, `getPlayerLedger(playerId, since, limit)` cannot be a cheap indexed query.

2. **`leaderboard.md` Core Rule 9**: ADR-0005 locked a **singular** `player.leaderboard_score` column, written before `quack-runner.md` established that Ricochet and Runner need **separate per-game boards** (their score formulas are structurally incomparable). A singular column cannot hold two independent scores per player. The fix also surfaced a second gap during that GDD's own CD-GDD-ALIGN review: the per-game display columns (Ricochet's `level`/`bossesDefeated`, Runner's `coinsCollected`/survival time) aren't derivable from `score` alone — Runner's `runnerLeaderboardScore = coinsCollected × 25 + Σ dodgeBonus(t_i)` is a non-invertible sum. And because Leaderboard's score write happens in the *same transaction* as reward crediting (Rule 4), this table's upsert is a **third leg** alongside ADR-0005's existing two (`player_state` `FOR UPDATE`, then the guarded `wallet` update) — the canonical lock order and composed-op idempotency gating ADR-0005 established must extend to cover it, or the same ABBA-deadlock and double-apply risks ADR-0005 closed for two legs reopen for three.

Both are narrow, additive schema changes — no new transactional pattern, no new consistency model. This ADR authors them together since both GDDs explicitly deferred authorship to "a future ADR-0004/0005 addendum" and both are otherwise unrelated GDDs' persistence needs converging on the same underlying schema.

## Decision

### 1. `currency_ledger` gains a denormalized `player_id` column (amends ADR-0004 §3)
**[Corrected 2026-07-17 per independent review]** A single `ADD COLUMN ... NOT NULL` statement fails outright the moment it runs against a table with existing rows and no `DEFAULT` — Postgres requires every row to already satisfy `NOT NULL` at the instant the constraint is added, and a fresh column with no default starts every existing row at `NULL`. The migration must run as four ordered steps, not one:
```sql
-- Step 1: add the column NULLABLE (existing rows get NULL, no constraint violated yet)
ALTER TABLE currency_ledger ADD COLUMN player_id BIGINT REFERENCES player(id);

-- Step 2: backfill every existing row via the currency_op join (dev/test data only,
-- no production players per ADR-0005's own migration stance)
UPDATE currency_ledger cl
  SET player_id = co.player_id
  FROM currency_op co
  WHERE cl.op_id = co.op_id AND cl.player_id IS NULL;

-- Step 3: only NOW enforce NOT NULL, once every row is verified populated
ALTER TABLE currency_ledger ALTER COLUMN player_id SET NOT NULL;

-- Step 4: index after the column is fully populated and constrained
CREATE INDEX idx_currency_ledger_player_created ON currency_ledger (player_id, created_at DESC);
```
- `player_id` is written in the **same `mutateWallet` transaction** as the rest of the ledger row (§2c of ADR-0004's transaction flow) — the value is already in scope there (it's the transaction's own `playerId` parameter), so this is a same-insert column addition, not a new write path or a new lock. The four-step sequence above is the one-time migration only; every row inserted *after* this migration runs already has `player_id` supplied directly by the insert, with no nullable window.
- This does **not** change ADR-0004's transaction shape, lock order, or idempotency mechanics in any way — it is a passive column populated for free inside an insert that already happens. No new leg, no new deadlock surface.
- `getPlayerLedger(playerId, since, limit)` (currency-ledger-transaction-log.md Core Rule 5a) now reads directly off `idx_currency_ledger_player_created`, never joining through `currency_op` to scope by player.
- **Backfill**: existing `currency_ledger` rows (dev/test data only — no production players per ADR-0005's own migration stance) get `player_id` populated via a one-shot backfill joining through `currency_op.player_id` once, at migration time. Anti-Cheat's read-dependency on `getPlayerLedger` must not go live until backfill is verified complete (`COUNT(*) FROM currency_ledger WHERE player_id IS NULL` returns 0) — already stated as a concrete gate in currency-ledger-transaction-log.md's own Edge Cases; this ADR is what makes that column, and that gate, real.
- **Account deletion** (currency-ledger-transaction-log.md Core Rule 4): the anonymization tombstone now applies to **both** `currency_op.player_id` and this new `currency_ledger.player_id` in the same operation — the GDD already specifies this; this ADR is what makes the second column exist to anonymize.

### 2. `player.leaderboard_score` is replaced by a generic `leaderboard_scores` table (amends ADR-0005 §1)
```sql
DROP INDEX IF EXISTS idx_player_leaderboard_score;  -- ADR-0005's original singular-column index
ALTER TABLE player DROP COLUMN IF EXISTS leaderboard_score;  -- dev/test only, no production players (ADR-0005's own migration stance)

CREATE TABLE leaderboard_scores (
  player_id      BIGINT NOT NULL REFERENCES player(id),
  game_id        TEXT   NOT NULL,               -- 'super_ricochet' | 'quack_runner' | future mini-games
  score          BIGINT NOT NULL,
  achieved_at    TIMESTAMPTZ NOT NULL,
  display_stats  JSONB  NOT NULL,               -- Ricochet: {level, bossesDefeated}; Runner: {coinsCollected, survivalTime}
  PRIMARY KEY (player_id, game_id)
);
CREATE INDEX idx_leaderboard_scores_rank
  ON leaderboard_scores (game_id, score DESC, achieved_at ASC, player_id ASC);
```
- One row per `(player_id, game_id)` — "current best only," never a history table. A new mini-game is a **data-only event** (a new `game_id` value), never a schema migration — the entire reason a generic table was chosen over per-game dedicated columns (leaderboard.md Core Rule 9's own stated rationale, confirmed here as the correct call).
- The composite index `(game_id, score DESC, achieved_at ASC, player_id ASC)` directly serves both required query shapes: top-N per game (`WHERE game_id = :g ORDER BY score DESC, achieved_at ASC, player_id ASC LIMIT 20`) and the tie-break order leaderboard.md Core Rule 6 specifies (earliest `achieved_at`, then lowest `player_id`) — the index *is* the tie-break order, not a separate sort step.
- **Upsert, current-best-only** (leaderboard.md Core Rule 9's own spec, now given a concrete statement):
  ```sql
  INSERT INTO leaderboard_scores (player_id, game_id, score, achieved_at, display_stats)
    VALUES (:playerId, :gameId, :score, :achievedAt, :displayStats)
  ON CONFLICT (player_id, game_id) DO UPDATE
    SET score = EXCLUDED.score, achieved_at = EXCLUDED.achieved_at, display_stats = EXCLUDED.display_stats
    WHERE EXCLUDED.score > leaderboard_scores.score;
  ```
  A worse run is a silent no-op (the `WHERE` clause fails, `DO UPDATE` skips, existing row untouched) — no separate "compare then maybe write" round trip, and no window where `score` reflects a new run while `display_stats` still shows the old one, since all three columns commit in the same `SET` clause or none do. **[Added 2026-07-17 per independent review]** `achieved_at` is captured **server-side** (`now()` at commit time), never a client-supplied timestamp — matching the "Tier-1-clamped value only, never client-trusted" treatment `score` itself already requires (leaderboard.md Core Rule 4). Since Core Rule 6 breaks ties on earliest `achieved_at`, a client-controlled timestamp would let clock skew or a manipulated value unfairly win a tie.
- **Third-leg canonical lock order (extends ADR-0005 §2's two-leg rule to three)**: leaderboard.md's Core Rule 4 puts this table's write in the **same transaction** as reward crediting whenever a run is both scoreable and reward-granting. ADR-0005's existing rule (`player_state` `FOR UPDATE` before the guarded `wallet` update) is extended to a **fixed three-position order, acquired identically on every composed path that touches more than one leg**:
  1. `player_state` row lock (`SELECT ... FOR UPDATE`), if the operation touches non-money state (e.g. a mascot grant, per ADR-0012's sibling composed-op precedent) — **first**.
  2. Guarded `wallet` update (ADR-0004's conditional `UPDATE ... WHERE coins >= :amt`) — **second**.
  3. `leaderboard_scores` upsert — **third, always**.
  This table's upsert is a row-level `ON CONFLICT` write, not a whole-table lock, so it does not introduce a new lock *class* — but its position in the sequence must be fixed and last, never interleaved before the wallet or player_state legs, or two composed operations acquiring legs in different orders can still ABBA-deadlock even though this specific leg's own lock footprint is small. "Third leg, fixed position" is the enforceable rule, not "safe because it's cheap."
  **[Clarified 2026-07-17 per independent review]** This rule covers **any transaction that touches two or more of `player_state`, `wallet`, and `leaderboard_scores`** — not only the run-submission reward-credit chokepoint this ADR walks through as its worked example. A future writer outside that chokepoint (an admin score-correction tool, a season-reset batch job, an account-merge routine) that composes any two of these three legs in one transaction must acquire them in this same fixed order, or the same ABBA-deadlock class reopens outside the path this ADR happened to describe. The registry forbidden-pattern entry (below) states this generically for exactly this reason — it is a database-agnostic process rule, not something Postgres enforces on its own, so any new composed-write code path must be checked against it at review time, not assumed to inherit it automatically.
- **Whole-operation idempotency extended to the third leg**: the same operation-level `idem_key` gating ADR-0005 §2 already requires (check/insert `currency_op` first; a replay rolls back the *entire* transaction before any leg runs, not just the money one) now also covers the `leaderboard_scores` upsert for the same reason — it lives inside the same transaction. **This ADR does not weaken that rule to accommodate a third leg — it explicitly restates it as covering however many legs a composed operation has, not "the two ADR-0005 originally described."**
  **[Clarified 2026-07-17 per independent review]** As specified, this protection is automatic and free: because leg 3 sits inside the same transaction as ADR-0004's `currency_op` replay check, a retried submission never reaches leg 3 at all — the whole transaction rolls back at step (a) before any leg executes. Separately, even a same-score resubmission that somehow did reach the upsert would be a harmless no-op via the `WHERE EXCLUDED.score > leaderboard_scores.score` guard itself. Explicit idem_key gating for this leg only becomes **load-bearing**, not merely restated-for-clarity, if a future implementation ever pulls the leaderboard write out of this transaction (e.g., into a post-commit side-effect call) — at that point the automatic in-transaction protection no longer applies, and standalone idem_key checking at that call site becomes mandatory, not optional. Flagged now so a future refactor doesn't silently assume the protection travels with the code once it leaves the transaction boundary.

### Architecture Diagram
```
Run submission → Anti-Cheat Tier-1 clamp → composed reward-credit operation
                                              │
                    (fixed lock order, EVERY composed op, idem_key-gated)
                                              │
        1. player_state FOR UPDATE (if non-money state changes — e.g. quest/mascot)
        2. wallet guarded UPDATE (ADR-0004 mutateWallet: legs, currency_op, currency_ledger
           — now ALSO writes currency_ledger.player_id, same insert, no new leg)
        3. leaderboard_scores upsert (ON CONFLICT ... WHERE EXCLUDED.score > current)
                                              │
                                          COMMIT (all or none)

currency_ledger(op_id, currency, delta, resulting_balance, multiplier_applied,
                created_at, player_id ← NEW)
  index: (player_id, created_at DESC) ← NEW

leaderboard_scores(player_id, game_id, score, achieved_at, display_stats) ← NEW TABLE
  replaces: player.leaderboard_score (singular column, DROPPED)
  index: (game_id, score DESC, achieved_at ASC, player_id ASC)
```

## Alternatives Considered

### Currency Ledger — Alternative: join through `currency_op` at query time, no denormalized column
- **Pros**: No schema change, no backfill.
- **Cons**: Every `getPlayerLedger` call becomes a join instead of a single-index range scan; at escalation-review or reconciliation scale (support tooling, IAP audits) this is a real cost for a query pattern used repeatedly per player. The composite index this ADR adds is the entire reason `getPlayerLedger` can be a cheap indexed query at all.
- **Rejection Reason**: currency-ledger-transaction-log.md's own Core Rule 5 already specifies the `(player_id, created_at)` index as a requirement; a join-based alternative contradicts the GDD directly, not just a style preference.

### Leaderboard — Alternative A: dedicated per-game columns (`ricochet_score`, `runner_score`, ...)
- **Pros**: Simple, no JSONB, direct column access.
- **Cons**: A new mini-game (3 more scoped in `game-concept.md`) requires a schema migration per game, forever. This was one of the two specialist recommendations during the original GDD authoring session and was explicitly not chosen.
- **Rejection Reason**: `leaderboard.md` Core Rule 9 already settled this — a generic table makes a new game a data-only event; per-game columns reopen exactly the migration-per-field cost ADR-0005's own hybrid-schema rationale (§1) rejected for evolving JSONB state, applied here to a different axis (game count, not per-player-field count) but the same underlying argument.

### Leaderboard — Alternative B: keep `player.leaderboard_score` singular, store per-game scores inside `player_state.data` JSONB
- **Pros**: No new table.
- **Cons**: Directly violates ADR-0005's own forbidden pattern ("storing a cross-player/leaderboard sort key inside JSONB player_state") — a JSONB scan for top-N defeats the entire reason ADR-0005 promoted the original column out of JSONB in the first place.
- **Rejection Reason**: Reintroduces the exact anti-pattern ADR-0005 already rejected; a relational table with a proper composite index is strictly better for a cross-player top-N query regardless of per-game vs. singular.

### Leaderboard — Alternative C: separate `leaderboard_scores` table per game (`ricochet_scores`, `runner_scores`, ...)
- **Pros**: Avoids a `game_id` discriminator column; each table can have game-specific typed columns instead of a generic `display_stats` JSONB.
- **Cons**: Same migration-per-new-game cost as per-game player columns (Alternative A), just moved to the table level instead of the column level.
- **Rejection Reason**: Doesn't solve the actual problem (schema changes for future games); the generic table + `display_stats` JSONB gets typed-enough display data without paying that cost.

## Consequences

### Positive
- `getPlayerLedger` and `getReconciliationSummary` become genuinely cheap indexed queries, matching what currency-ledger-transaction-log.md already assumed but couldn't have without this column.
- Leaderboard scales to N future mini-games as a data-only event, never a migration — directly serving `game-concept.md`'s "5-game collection, more later" scope.
- The three-leg canonical lock order closes an ABBA-deadlock class before any code is written against it, rather than discovering it under load.
- No new consistency model, no new engine/client risk — this is schema-and-transaction-shape work only.

### Negative
- `currency_ledger` denormalizes a value (`player_id`) that's technically derivable via `currency_op` — a small, deliberate normalization trade for query performance, consistent with ADR-0005's own stated hybrid-schema philosophy (real columns where query performance matters).
- The composed-operation transaction now potentially spans three legs instead of two, each with its own lock-acquisition step — a marginally larger transaction body, though each leg's own cost is small (an indexed insert, a conditional update, an upsert).
- Contributors must learn a three-position lock order instead of two — the same "must be taught/enforced" cost ADR-0005 already flagged for its own two-leg rule, now extended by one more position.

### Risks
- **Risk**: A future composed operation acquires the `leaderboard_scores` leg before `wallet` or `player_state`, reintroducing an ABBA deadlock this ADR's fixed ordering exists to prevent.
  **Mitigation**: Registry forbidden-pattern entry (see below) + a composed-op deadlock test extended to the three-leg case, mirroring ADR-0005's own two-leg validation criterion.
- **Risk [corrected 2026-07-17 per independent review]**: this risk does not actually manifest under the design as specified — a retried transaction rolls back entirely at ADR-0004's replay check before leg 3 ever runs, and even a same-score resubmission that somehow reached the upsert is a no-op via its own `WHERE` guard. **Real risk**: a future refactor moves the `leaderboard_scores` write out of this transaction (e.g., a post-commit call), silently losing the automatic in-transaction protection.
  **Mitigation**: Explicit note in Decision §2 that standalone `idem_key` checking becomes mandatory the moment this write ever leaves the transaction boundary — not assumed to travel with the code automatically.
- **Risk**: The `currency_ledger.player_id` backfill is treated as complete before it actually finishes, and Anti-Cheat's escalation review silently misses rows.
  **Mitigation**: Explicit backfill-complete gate (`COUNT(*) WHERE player_id IS NULL = 0`), already specified in currency-ledger-transaction-log.md's Edge Cases and restated here as this ADR's own validation criterion.

## Performance Implications
- **CPU/Memory**: Negligible — one additional indexed column write per ledger row; one upsert per scoreable run, gated by an index already required for top-N.
- **Storage**: `currency_ledger` gains one `BIGINT` column + one composite index per row (small, linear in ledger growth — already an accepted cost per currency-ledger-transaction-log.md's forever-retention Core Rule 3). `leaderboard_scores` is bounded by `player_count × game_count` rows (current-best-only, not a history table) — trivially small relative to the ledger.
- **Network**: None new — same server-internal transaction.

## Migration Plan
Both changes are schema-only, applied via versioned migrations per ADR-0005's own migration discipline (§4): (1) `currency_ledger` — add `player_id` **nullable**, backfill via a one-shot join through `currency_op`, only then `SET NOT NULL`, only then add the composite index (four ordered steps, per Decision §1's corrected SQL — a single `ADD COLUMN ... NOT NULL` statement is invalid against a table with existing rows); (2) create `leaderboard_scores`, drop `player.leaderboard_score` and its index (dev/test only, no production players to migrate, per ADR-0005's own no-live-cutover stance). No application code currently depends on the old singular column (Leaderboard was never implemented against it) or queries `currency_ledger` without `player_id` (Currency Ledger was never implemented at all) — this addendum lands before either consumer's first line of code, not as a breaking change to running code.

## Validation Criteria
- `getPlayerLedger(playerId, since, limit)` query plan uses `idx_currency_ledger_player_created` (index scan, not a join + filter) — verify via `EXPLAIN`.
- Backfill-complete gate: `COUNT(*) FROM currency_ledger WHERE player_id IS NULL` returns 0, checked once before Anti-Cheat's `getPlayerLedger` dependency goes live (already a stated GDD requirement; this is its concrete verification).
- **Three-leg composed-op deadlock test**: many concurrent operations touching `player_state` + `wallet` + `leaderboard_scores` in the same transaction run without deadlock, confirming the fixed lock order holds under load — extends ADR-0005's own two-leg deadlock test.
- **Three-leg composed-op retry test**: a retried operation (same `idem_key`) applies the wallet leg once, the player_state mutator once, and the leaderboard upsert once — no double-increment, no double-upsert-evaluation.
- Leaderboard upsert test: a lower-scoring run against an existing best is a silent no-op (row unchanged, including `achieved_at`); a higher-scoring run updates `score`, `achieved_at`, and `display_stats` together in one commit.
- Leaderboard top-N query plan uses `idx_leaderboard_scores_rank` (index scan) for `WHERE game_id = :g ORDER BY score DESC, achieved_at ASC, player_id ASC LIMIT 20` — verify via `EXPLAIN`.
- `achieved_at` is populated from server `now()` inside the transaction, never a client-supplied field on the submission payload — verify no code path plumbs a client timestamp into this column.
- **Migration-sequencing test**: running the four-step `currency_ledger.player_id` migration (nullable add → backfill → `SET NOT NULL` → index) against a table with pre-existing rows completes without error, confirming the corrected sequence (not the invalid single-statement form) actually works end-to-end.

## Related Decisions
- ADR-0004 — original `currency_ledger`/`currency_op` schema this amends (adds `player_id`, no other change).
- ADR-0005 — original Postgres backend, `player.leaderboard_score` column this replaces, and the two-leg canonical lock order/idempotency rule this extends to three.
- `currency-ledger-transaction-log.md` — Core Rule 2's required addendum, now authored.
- `leaderboard.md` — Core Rule 9's required addendum, now authored.

## Open Questions
- Exact `game_id` value format (string enum vs. a small lookup table) — an implementation-time choice, not load-bearing; either satisfies the schema above.
- Whether `leaderboard_scores` ever needs a *history* table (not just current-best) for a future "score over time" feature — not requested by any current GDD; deferred until a real product need surfaces.
- Ledger retention/archival (currency-ledger-transaction-log.md Open Question 1) is unaffected by this addendum and remains its own separate open item.
