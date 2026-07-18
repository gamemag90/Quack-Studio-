# Currency Ledger/Transaction Log

> **Status**: Designed
> **Author**: Abdulrahman Alenazan + Claude
> **Last Updated**: 2026-07-12
> **Implements Pillar**: Server-authoritative economy
> **Creative Director Review (CD-GDD-ALIGN)**: APPROVED WITH CONDITIONS
> 2026-07-12 (2 conditions, both fixed same pass — C1: unified
> `getPlayerLedger`'s signature across Core Rule 5(a) to
> `(playerId, since, limit)`, matching Core Rule 2/Acceptance Criteria,
> since the Tuning Knobs' pagination only makes sense with an explicit
> `limit`; C2: clarified in Open Question 2 that this GDD's
> `(player_id, created_at)` index is a requirement for the future
> ADR-0004 addendum to satisfy, not a physical-schema spec to copy
> verbatim)
> **`/design-review` (2026-07-17)**: NEEDS REVISION — 2 blocking items
> (`getOperationLegs`, one of only three sanctioned query functions, had
> zero behavioral acceptance criteria and no edge-case coverage for a
> non-existent or already-anonymized operation) plus 2 recommended items.
> All folded in below; re-review pending. Confirmed still accurate:
> Open Question 2's claim that the ADR-0004 `player_id` addendum remains
> unwritten — verified directly against ADR-0004's current schema, no
> follow-up ADR has addressed it.

## Overview

Currency Ledger/Transaction Log is the append-only audit trail behind every
Currency System mutation — the system of record that answers "what happened
to this player's balance, and why" for disputes, anti-cheat
cross-referencing, and IAP reconciliation. It owns no mutation logic of its
own: every row it contains is a byproduct of Currency System's
`mutateWallet` chokepoint, written in the same transaction as the balance
change (ADR-0004). This GDD does not re-derive that schema —
`currency_ledger` (one row per credit/debit leg: `op_id`, `currency`,
`delta`, `resulting_balance`, `multiplier_applied`, `created_at`) and
`currency_op` (one row per operation, holding the idempotency key) are
already locked by ADR-0004 and confirmed as relational Postgres tables by
ADR-0005. What this GDD defines is the product/design layer on top: who can
read the ledger and for what purpose, how long entries are retained, what
queries it must support (a specific player's history, a specific
operation's legs, cross-referencing a flagged run), and how it interacts
with Anti-Cheat's escalation review — none of which the ADRs specify, since
they're design decisions, not persistence mechanics.

## Player Fantasy

Players never see or touch the Ledger — there is no screen, no icon, no
moment that belongs to them here. What it protects is verifiability: every
credit and debit the server issues leaves a permanent, tamper-evident
record, so if a balance is ever questioned, the answer is provable, not
argued. It is the enforcement arm beneath "server-authoritative economy" —
that pillar isn't just that the server decides, it's that the server can
prove what it decided. A future player-facing transaction history is not
designed anywhere in this project yet, but the audit trail already exists
to support one cheaply if a later system (support tooling, a trust/dispute
screen) needs it — see Open Questions.

## Detailed Design

### Core Rules

1. **Readers are enumerated, not open-ended — three classes, all read-only.**
   (a) Anti-Cheat/Replay Verification's escalation review (Rule 2); (b)
   internal support/ops tooling, for dispute and refund lookups; (c)
   IAP/Receipt Validation (not yet designed) and finance/reconciliation
   tooling, for credit-vs-debit sums by `source`. No other system reads this
   table. **Players never get direct access.** The only path is indirect —
   a support agent querying on the player's behalf during a dispute — never
   a self-serve endpoint or player-facing API, consistent with Player
   Fantasy: this system has no screen and no moment that belongs to the
   player.
2. **Anti-Cheat/Replay Verification gains a formal hard read-dependency on
   this system**, resolving the aspirational note in `systems-index.md`
   ("pairs with anti-cheat hardening"). Contract:
   `getPlayerLedger(playerId, since, limit) → currency_ledger rows, most
   recent first`, scoped to one player. Called only when a player crosses
   the mismatch-escalation threshold (Anti-Cheat's Tuning Knob: 3 flags/7
   days) and a human reviewer opens the review queue — **not** on every
   Tier 2 mismatch, since most mismatches are false positives (drift) and
   don't warrant a ledger pull. **[Corrected 2026-07-17]** "Outdated
   client" was removed from this example — per
   `anti-cheat-replay-verification.md`'s Rule 8, an outdated client
   producing missing replay data is a `mode=degraded` flag, not a
   `mode=mismatch` one; it never reaches Tier 2 comparison at all and
   never counts toward this escalation threshold, so it doesn't belong in
   an example about *mismatch* false positives specifically. This is
   read-only;
   Currency Ledger exposes no write or callback surface to Anti-Cheat.
   *Anti-Cheat's own Dependencies section should be updated to list this
   reciprocally, matching the project's convention for fixing
   one-directional edges.*
   **Required schema addendum (not yet in ADR-0004):** `currency_ledger`
   as currently specified has no `player_id` column — only `op_id`, which
   requires a join through `currency_op` to scope by player. Every reader
   in Rule 1 needs player-scoped queries, so `player_id` must be
   denormalized directly onto `currency_ledger`, written in the same
   `mutateWallet` transaction (the value is already in scope there),
   backed by a composite `(player_id, created_at)` index. This GDD cannot
   silently assume that column exists — it's a real gap in the locked
   schema, surfaced here rather than assumed, and needs an ADR-0004
   addendum or a small follow-up ADR before implementation.
3. **Retention: forever — no pruning, no default archival window.** This is
   the audit trail behind a server-authoritative, real-money economy; a
   `currency_ledger` row is the only proof of what the server decided on a
   given credit or debit (Player Fantasy). Pruning removes exactly the
   evidence an IAP dispute or chargeback needs, often months after the
   fact — unacceptable for an audit trail whose entire purpose is
   provenance. If storage cost ever becomes a real constraint, archive
   older rows to cold storage (never delete) as a future ADR; that is not
   today's default and is not a launch requirement.
4. **Account deletion: anonymize the player link on BOTH tables, never
   delete the rows.** On account deletion, `currency_op.player_id` AND the
   Rule 2 denormalized `currency_ledger.player_id` are both replaced with
   the same tombstone/anonymized reference in one operation; every other
   `currency_ledger` column (`delta`, `resulting_balance`,
   `multiplier_applied`, `created_at`) is retained unchanged. **[Corrected
   during Edge Cases review]**: an earlier draft of this rule anonymized
   only `currency_op.player_id` — written before Rule 2's denormalization
   existed — which would have left an un-anonymized, queryable player link
   sitting directly on `currency_ledger`, silently defeating the GDPR
   erasure this rule exists to satisfy. Both columns must tombstone
   together, in the same operation, or the guarantee is false. This
   satisfies a GDPR erasure request (the identifying link is gone) while
   preserving the audit and reconciliation trail, on the same
   legal-obligation/legitimate-interest basis that lets financial records
   survive erasure requests. Ledger rows are never hard-deleted on account
   deletion — that would silently break reconciliation and any dispute
   still open against that player's `op_id`s. Reconciliation against a
   deleted account's rows happens via the shared tombstone value, never the
   original `player_id`.
5. **Supported query patterns are scoped to Rule 1's readers — nothing
   speculative — and every reader queries through a narrow API, never raw
   table access.** No system (including Anti-Cheat and support tooling)
   is granted direct SQL access to `currency_ledger`/`currency_op`; each
   pattern below is its own named function, not an open query surface —
   this keeps every future schema change (the Rule 2 denormalization, any
   future partitioning) a one-call-site change instead of a breaking
   change across every consumer, and **provides** a single enforcement
   point where logging *who* pulled a player's financial history could be
   added cheaply. **[Clarified 2026-07-17]** This is stated as an
   architectural affordance the narrow-API pattern enables, not a
   commitment this GDD is making — no access-logging behavior is actually
   specified or required here; if that logging becomes a real requirement
   (e.g. for a compliance need), it belongs in its own Core Rule with its
   own Acceptance Criteria, not implied by this one. Patterns: (a)
   `getPlayerLedger(playerId, since, limit)`
   — a player's rows since a given time, most-recent-first, bounded by
   `limit` (Tuning Knobs: default 50, clamped at 500) — backed by the
   `(player_id, created_at)` index from Rule 2 — readers (a) and (b);
   **[Corrected during CD-GDD-ALIGN review]** an earlier draft of this
   entry used a `(playerId, dateRange)` signature inconsistent with Rule
   2 and Acceptance Criteria's `(playerId, since, limit)` — unified to
   the latter, since the Tuning Knobs' pagination behavior only makes
   sense with an explicit `limit` parameter. (b)
   `getOperationLegs(opId)` — all legs for a given operation, resolving
   one operation's full multi-leg detail (e.g. a reward with both a
   `creditMultiplied` and a `creditFlat` leg) — readers (a) and (b). (c)
   `getReconciliationSummary(source, dateRange)` — sum of credits vs.
   debits grouped by `source` — reader (c). No ad-hoc query surface, no
   full-table scans, no cross-player aggregation beyond (c).

### States and Transitions

Stateless, append-only — no states table for this system. A row is written
exactly once, inside Currency System's `mutateWallet` transaction
(ADR-0004 §2c), and is never subsequently updated, transitioned, or
soft-deleted. There is no row lifecycle to define, and inventing one (e.g.
"pending / reviewed / archived") would contradict ADR-0004's append-only
design and imply mutation logic this system explicitly does not own.

### Interactions with Other Systems

- **Currency System** (writes) — `mutateWallet` inserts the `currency_op`
  row and one `currency_ledger` row per leg inside its own transaction
  (ADR-0004). Currency Ledger issues no calls back into Currency System; it
  is a passive consumer of rows Currency System writes, per
  `currency-system.md`'s "Depended on by (hard): ... Currency Ledger."
- **Anti-Cheat/Replay Verification** (reads) — escalation review queries a
  flagged player's recent rows via `getPlayerLedger` (Rule 2); new hard
  dependency, resolving the `systems-index.md` note.
- **Save/Persistence** (infrastructure) — supplies the durable Postgres
  store per ADR-0005; already lists Currency Ledger as a hard dependent.
- **Support/ops tooling** (reads) — dispute and refund lookups, staff-
  initiated; not yet a formal system in `systems-index.md`.
- **IAP/Receipt Validation** (reads, once designed) — reconciliation sums
  by `source`, per ADR-0004's "IAP-grade audit/provenance trail."

## Formulas

None. This system computes nothing — every `currency_ledger`/`currency_op`
row is a byproduct of Currency System's already-computed, already-validated
mutation (ADR-0004). Currency Ledger's only outputs are the read-only query
functions in Core Rule 5 (`getPlayerLedger`, `getOperationLegs`,
`getReconciliationSummary`), which return stored data, not derived values —
there is no reward curve, cost formula, or tunable output range for this
system to own.

## Edge Cases

- **If a date-range query (`getPlayerLedger`, `getReconciliationSummary`)
  filters by time**: it filters exclusively on `currency_ledger.created_at`
  — the column backed by Rule 2's `(player_id, created_at)` index — never
  `currency_op.created_at`. Both columns are populated from a single
  timestamp value captured once per `mutateWallet` call and written to
  both rows, not two independent clock reads, so they cannot drift; any
  future query path must still key off `currency_ledger.created_at`
  specifically, since that's the indexed column.
- **If `getPlayerLedger` is called for a player with zero
  `currency_ledger` rows** (a new account, or one that has never had a
  wallet mutation): return an empty array, not an error or null. A valid
  player with no history isn't a fault condition — callers must treat
  "checked, found nothing" identically to "found rows."
- **If `getPlayerLedger` is called for a player already anonymized under
  Rule 4**: both the operation-level and ledger-level player links were
  tombstoned together (Rule 4, corrected during this review), so the
  query resolves via the shared tombstone value — it never exposes the
  original `player_id`-keyed history to a caller who doesn't already hold
  the tombstone.
- **If `getReconciliationSummary(source, dateRange)` spans a period
  before the Rule 2 `player_id` denormalization backfill completes**: no
  impact on this query — it groups by `source` and filters by
  `created_at`, neither dependent on the new column.
- **If `getPlayerLedger` is queried for a range spanning an incomplete
  backfill window**: rows not yet backfilled would be silently excluded
  from an index-only scan, understating a player's history. Anti-Cheat's
  Core Rule 2 read dependency must not go live until the `player_id`
  backfill is verified complete — defined concretely as: `COUNT(*) FROM
  currency_ledger WHERE player_id IS NULL` returns 0, checked and
  recorded once, before Anti-Cheat's dependency is enabled — not rolled
  out incrementally alongside live escalation-review traffic.
- **If a `source` has legs in more than one currency** (e.g. a bundle
  granting both coins and gems in one operation): `getReconciliationSummary`
  groups by `(source, currency)`, never `source` alone — summing unlike
  currencies into one total would be meaningless, not just imprecise.
- **If `mutateWallet` is retried with the same idempotency key** (client
  resubmission, timeout-and-retry): the ledger shows the original legs
  exactly once, never a duplicate set, since ADR-0004's `currency_op`
  idempotency check rolls back the retry before any new `currency_ledger`
  rows are inserted. Anti-Cheat and reconciliation both trust row counts
  and summed deltas as ground truth — an appended retry would silently
  double-count one real transaction.

## Dependencies

- **Depends on** (hard): Currency System (every `currency_ledger`/
  `currency_op` row is written by its `mutateWallet` transaction, ADR-0004
  — matches Currency System's own "Depended on by (hard): ... Currency
  Ledger"); Save/Persistence (durable Postgres store, ADR-0005 — already
  lists Currency Ledger as a hard dependent).
- **Depended on by** (hard): Anti-Cheat/Replay Verification (escalation
  review reads via `getPlayerLedger`, Core Rule 2 — a new reciprocal
  dependency this GDD establishes).
- **Depended on by** (soft, not yet formal systems in `systems-index.md`):
  internal support/ops tooling (dispute/refund lookups, Core Rule 1b);
  IAP/Receipt Validation (reconciliation reads, Core Rule 1c — not yet
  designed).

**Consistency check**: Currency System's GDD says "Depended on by (hard):
... Currency Ledger" — matches. Save/Persistence's GDD says "Depended on by
(hard, all of them): ... Currency Ledger" — matches. Anti-Cheat/Replay
Verification's GDD did not yet list Currency Ledger — the one-directional
gap Core Rule 2 creates, fixed in the same pass as this GDD (see that
file's Dependencies section), following the project's established
convention of fixing rather than just flagging reciprocal gaps.

## Tuning Knobs

| Knob | Suggested Value | Too Low | Too High |
|---|---|---|---|
| `getPlayerLedger` default page size | 50 rows | Anti-Cheat/support callers need multiple round-trips for a single review, slowing escalation response | A single query returns an unbounded amount of history, defeating the pagination the narrow-API pattern (Core Rule 5) exists to enforce |
| `getPlayerLedger` max page size (hard cap) | 500 rows | N/A | A caller requesting the max repeatedly approximates an unbounded scan, reintroducing the direct-table-access risk Core Rule 5 was written to prevent |
| Reconciliation query date-range cap | 1 year per call | Legitimate multi-year audits need many calls | A single call can scan and aggregate a very large row range against an append-only, ever-growing table (see Open Questions on partitioning) |

**Enforcement behavior (not a knob itself, but load-bearing for Acceptance
Criteria)**: `getPlayerLedger` requests above the 500-row hard cap are
**clamped** to 500, not rejected — a caller just gets fewer rows back,
still correct, no error handling burden for a plain safety limit.
`getReconciliationSummary` requests spanning more than 1 year are
**rejected** with an explicit error, never silently clamped — a clamped
date range would still return a `sum`, but a wrong one that looks
correct, which is unacceptable for a financial reconciliation function.

## Visual/Audio Requirements

N/A — pure server-side infrastructure with no client-visible surface
(Player Fantasy: "no screen, no icon, no moment that belongs to the
player"). Any future player-facing transaction history (see Open
Questions) would own its own Visual/Audio requirements in a separate GDD
or UX spec, not here.

## UI Requirements

N/A — same reasoning as Visual/Audio. All access is programmatic
(Anti-Cheat's escalation review, internal support/ops tooling), per Core
Rule 1's narrow-API pattern (Core Rule 5); there is no screen for this
system to specify.

## Acceptance Criteria

- **GIVEN** a data request against `currency_ledger`/`currency_op`, **WHEN**
  the requester is not Anti-Cheat, support/ops tooling, or IAP/finance
  reconciliation, **THEN** the request is rejected — no player-facing
  endpoint or screen exposes ledger data directly to any player.
- **GIVEN** a player has crossed Anti-Cheat's current mismatch-escalation
  threshold (owned by that system's own Tuning Knob, not hardcoded here)
  **AND** a reviewer opens the review queue, **WHEN**
  `getPlayerLedger(playerId, since, limit)` is called, **THEN** it returns
  that player's rows, most recent first. **GIVEN** either condition is
  unmet, **WHEN** a Tier 2 mismatch occurs, **THEN** `getPlayerLedger` is
  NOT invoked.
- **GIVEN** a `mutateWallet` transaction commits, **WHEN** the resulting
  `currency_ledger` row is inspected, **THEN** `player_id` is populated
  and a `(player_id, created_at)` composite index covers it.
- **GIVEN** any `currency_ledger` row of any age, **WHEN** scheduled
  jobs/processes are audited, **THEN** none delete or archive it, and it
  remains retrievable.
- **GIVEN** an account-deletion request for player X, **WHEN** deletion
  completes, **THEN** `currency_op.player_id` and `currency_ledger.player_id`
  hold the identical tombstone value on every one of that player's rows,
  and `delta`/`resulting_balance`/`multiplier_applied`/`created_at` are
  unchanged.
- **GIVEN** any authorized reader needing ledger data, **WHEN** it
  queries, **THEN** it calls only `getPlayerLedger`, `getOperationLegs`,
  or `getReconciliationSummary` — no reader has direct SQL/table access.
- **GIVEN** a date-range query, **WHEN** executed, **THEN** it filters
  exclusively on `currency_ledger.created_at`, never `currency_op.created_at`.
- **GIVEN** `getPlayerLedger` is called for a player with zero rows,
  **WHEN** executed, **THEN** it returns an empty array, never null or an
  error.
- **GIVEN** `getPlayerLedger` is called for an already-anonymized player,
  **WHEN** executed, **THEN** it resolves via the shared tombstone value
  and never exposes the original `player_id`-keyed history.
- **GIVEN** `getReconciliationSummary` spans a period before the
  `player_id` backfill completed, **WHEN** executed, **THEN** results are
  still correct (it filters on `source`/`created_at`, not `player_id`).
- **GIVEN** `COUNT(*) FROM currency_ledger WHERE player_id IS NULL` is
  greater than 0, **WHEN** checked, **THEN** Anti-Cheat's `getPlayerLedger`
  read-dependency is not yet enabled — the backfill-complete signal from
  Edge Cases, made independently testable.
- **GIVEN** a `source` has legs in more than one currency in the same
  period, **WHEN** `getReconciliationSummary` is called, **THEN** results
  are grouped by `(source, currency)`, never `source` alone.
- **GIVEN** `mutateWallet` is retried with the same idempotency key,
  **WHEN** the ledger is inspected afterward, **THEN** the original legs
  appear exactly once — no duplicate rows from the retry.
- **GIVEN** a `getPlayerLedger` request for more than 500 rows, **WHEN**
  executed, **THEN** the result is clamped to 500 rows, not rejected.
- **GIVEN** a `getReconciliationSummary` request spanning more than 1
  year, **WHEN** executed, **THEN** it is rejected with an explicit
  error, never silently clamped to a shorter (and therefore wrong) sum.
- **[NEW 2026-07-17] GIVEN** an operation with two legs (e.g. a boss-defeat
  reward's `creditMultiplied` and `creditFlat` legs), **WHEN**
  `getOperationLegs(opId)` is called, **THEN** both legs are returned —
  previously only asserted in prose (Formulas' worked example), never
  exercised as its own acceptance criterion despite being one of only
  three sanctioned query functions.
- **[NEW 2026-07-17] GIVEN** `getOperationLegs` is called for a
  non-existent `opId`, **WHEN** executed, **THEN** it returns an empty
  array, never null or an error — mirroring `getPlayerLedger`'s own
  zero-rows handling, which this function previously lacked entirely.
- **[NEW 2026-07-17] GIVEN** `getOperationLegs` is called for an operation
  belonging to an already-anonymized (tombstoned) player, **WHEN**
  executed, **THEN** the legs are still returned (operation-level data,
  `delta`/`resulting_balance`/etc., is retained per Rule 4) without
  exposing the original `player_id` — mirroring `getPlayerLedger`'s
  already-anonymized-player handling, which this function previously
  lacked.
- **[NICE-TO-HAVE 2026-07-17] GIVEN** a `getPlayerLedger` request for
  exactly 500 rows, **WHEN** executed, **THEN** all 500 are returned
  unclamped. **GIVEN** a `getReconciliationSummary` request spanning
  exactly 1 year, **WHEN** executed, **THEN** it succeeds unrejected —
  both existing ACs previously tested only the "exceeds the limit" side
  of each boundary, not the exactly-at-the-limit side.

## Open Questions

1. **Partitioning/archival for the ever-growing `currency_ledger` table**:
   Core Rule 3 commits to forever retention with no default pruning, but
   an append-only table with no bound is a real long-term operational
   risk (index/vacuum bloat, backup growth, eventual point-query
   degradation) per independent backend review. Not solved here — needs
   a future ADR (monthly range partitioning on `created_at` plus a
   cold-storage archival plan) once a concrete trigger (row count or
   storage size threshold) is defined. *Target: revisit once real
   production volume data exists; premature to pick a threshold now.*
2. **[RESOLVED 2026-07-17, see `adr-0012-persistence-schema-addendum.md`]**
   The Core Rule 2 `player_id` denormalization is a required ADR-0004
   schema addendum this GDD cannot itself author. ADR-0012 now authors it:
   the column + composite `(player_id, created_at)` index, written in the
   same `mutateWallet` transaction, with a corrected migration sequence
   (nullable → backfill → `SET NOT NULL` → index) and an explicit
   backfill-complete gate before Anti-Cheat's read-dependency goes live.
3. **Should internal support/ops tooling become its own designed
   system** (with a GDD, or at least an entry in `systems-index.md`), or
   does it stay permanently out of this project's GDD scope as
   pure-internal tooling? Currently referenced only as a "soft" dependent
   here (Dependencies) with no home of its own.
4. **Should a player-facing "transaction history" surface ever be
   built?** Player Fantasy notes the audit trail could support one
   cheaply since the data already exists, but no such UI is designed or
   planned anywhere in the project today. Not assumed either way —
   *Target: revisit only if a real player-support pain point surfaces
   post-launch.*
5. **IAP/Receipt Validation's exact reconciliation needs are
   unconfirmed**, since that system isn't designed yet. Core Rule 1c and
   `getReconciliationSummary`'s `(source, currency)` grouping are a
   best-guess interface based on ADR-0004's "IAP-grade audit/provenance
   trail" framing — *Target: confirm or revise when
   `/design-system IAP/Receipt Validation` runs.*
