# ADR-0007: Server-Side Headless Replay Re-Simulation Architecture

## Status
Proposed

## Date
2026-07-10

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Server-side (.NET SharedSimCore workers + Node/TS backend + PostgreSQL). No on-device component. |
| **Domain** | Core / Anti-Cheat / Infrastructure (server) |
| **Knowledge Risk** | LOW for the service architecture itself; the *determinism* it depends on is owned by ADR-0002's spike gate (HIGH there, gated there) |
| **References Consulted** | `anti-cheat-replay-verification.md`, ADR-0001, ADR-0002, ADR-0004, ADR-0005, ADR-0006 |
| **Post-Cutoff APIs Used** | None |
| **Verification Required** | Load test: queue drains within the latency SLO at target DAU; capacity-degradation path sheds low-risk jobs and alerts without affecting rewards |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-0001 (SharedSimCore + CLI verifier + fail-open), ADR-0002 (the fixed-point sim the workers run — and its spike gate), ADR-0004 (reward grant via `mutateWallet`), ADR-0005 (Postgres = queue + landing) |
| **Enables** | Anti-Cheat Tier-2 at production scale; answers `anti-cheat-replay-verification.md` Open Q1 |
| **Blocks** | Nothing hard |
| **Ordering Note** | If ADR-0002's spike fails and its Alternative D (statistical anti-cheat) is taken, this service's *replay* role is replaced by statistical scoring — see Risks |

## Context

### Problem Statement
ADR-0001 established a `.NET` CLI verifier invoked by the Node backend, fail-open, with async re-verification; ADR-0002 built the deterministic fixed-point sim it runs. **Both explicitly deferred the perf/latency budget, concurrency model, and coverage policy to this ADR.** `anti-cheat-replay-verification.md` Open Q1 asks for the actual re-simulation CPU-cost budget and whether it scales across 5 mini-games. This ADR resolves those, and reconciles a reward-model discrepancy (below).

### Reward-model reconciliation (decided: flag-only)
`anti-cheat-replay-verification.md` (Rule 6 + Acceptance) is explicit: the reward is **always the Tier-1-clamped amount, credited at submission**; a **Tier-2 mismatch only raises a fraud flag** feeding the 3-flags/7-days review queue — it **never claws back** the reward. This deliberately prevents a float-drift/outdated-client false-positive from harming a legit player (Rule 6 minimizes misclassification harm). ADR-0001's prose implied *clawback on mismatch*, which is inconsistent with the GDD. **This ADR adopts the GDD's flag-only model** and flags ADR-0001's wording for a small reconciliation (see Open Questions / cross-ADR note) rather than silently diverging.

### Consequence of flag-only
Tier-1 (a cheap per-mini-game plausibility clamp) is the sole authority for the *reward amount*. Tier-2 (expensive deterministic replay) is purely a fraud-detection signal. So the reward path never blocks on Tier-2, and there is **no clawback machinery to build** — a materially simpler and safer design than a provisional-reward-with-clawback scheme.

## Decision

### 1. Synchronous Tier-1 governs reward; asynchronous Tier-2 only flags
- On run submission, **Tier-1 clamp runs synchronously** (per-mini-game ceiling formula, cheap) and the reward is credited immediately via ADR-0004 `mutateWallet`, idempotent on the client-generated **run ID** (anti-cheat GDD edge). The reward path does **not** wait for Tier-2.
- **The reward credit and the verification-job enqueue happen in ONE transaction** (a true outbox — both are ADR-0005 Postgres). If coverage (§2) selects this run for Tier-2, the `verification_job` row is inserted in the *same transaction* as the `mutateWallet` legs and commits atomically with them. This closes the gap where a rewarded run could be credited (committed) and then fail to enqueue — leaving a rewarded-but-never-verified run. Either both commit or neither does.
- The enqueued job `{runId, seed, inputSequence, clientReportedResult, miniGame, playerId}` is picked up by a worker, which re-simulates with ADR-0002's SharedSimCore and produces a **match/mismatch flag** (within `tolerance_units`) — never a reward change.

### 2. Risk-based coverage (answers GDD Open Q1)
A run is enqueued for Tier-2 if **any**: reward ≥ a value threshold; it is leaderboard-relevant; Tier-1 flagged it implausible-but-clamped; or the player is under escalation watch. Otherwise it is **sampled** at rate `p` (tunable), with `p` raised automatically for a player/client-version showing anomalies. Server CPU cost is therefore bounded by *(all high-value runs) + p·(low-value runs)*, not *100%·all* — the concrete answer to "does it scale across 5 mini-games": coverage is a tunable dial, not a fixed 100%.

**Sub-threshold blind spot (acknowledged)**: a cheater who knows only high-value runs are verified 100% could farm *just under* the value threshold. Two mitigations, stated so it isn't an unguarded hole: (a) set the threshold **at or below the meaningful-reward floor**, so anything worth cheating for is either verified or economically negligible; (b) floor the baseline sample rate `p` high enough that *repeated* sub-threshold cheating is caught in expectation within a few runs — and the **per-player anomaly-driven `p` bump** is the specific mechanism that escalates a suspected sub-threshold farmer to full coverage. A single sub-threshold cheat may slip; a *pattern* of it is caught by (b), which is the behavior that actually matters for a persistent exploiter.

### 3. Execution: warm .NET worker pool on a Postgres-backed queue
- A **pool of N warm `.NET` SharedSimCore worker processes** consumes a Postgres-backed job queue via `SELECT ... FOR UPDATE SKIP LOCKED`. This **refines ADR-0001's "short-lived child process per check"** to avoid per-check process cold-start cost at scale — a conscious, flagged evolution of that ADR.
- It is **not a persistent networked microservice** (ADR-0001's rejected pattern): workers *pull* jobs from the DB; there is no inbound network endpoint or auth boundary to secure. Reuses ADR-0005's Postgres — **no new infrastructure**.
- Job lifecycle: `pending → claimed(worker_id, lease_epoch, lease_expires) → done(match|mismatch) | dead_letter`.
- **Claim + reclaim in one atomic query** (fixes "who reclaims an expired lease"): the claim is a single `UPDATE verification_job SET status='claimed', worker_id=:w, lease_epoch=lease_epoch+1, lease_expires=now()+:lease WHERE id = (SELECT id FROM verification_job WHERE status='pending' OR (status='claimed' AND lease_expires < now()) ORDER BY ... FOR UPDATE SKIP LOCKED LIMIT 1) RETURNING *`. So an expired-lease job is reclaimed by the *same* claim query (no separate sweeper needed); `SKIP LOCKED` alone would never reclaim it. A short sweeper is optional belt-and-suspenders, not the primary mechanism.
- **Fencing token = `lease_epoch`** (fixes double-processing of a *slow* — not crashed — job whose lease expired and was reclaimed): a worker may write terminal state only if its held `lease_epoch` still matches the row's. The stale original worker's write is rejected (`WHERE lease_epoch = :heldEpoch` affects 0 rows). So at most one worker's result is ever recorded.
- **Terminal-state + side-effects are idempotent** regardless: a `UNIQUE(run_id)` constraint on the flag table (one flag per run, so a double-process can't inflate the 3-flags/7-days count), and the mismatch analytics emit carries a dedup key (`runId`) per ADR-0006's server-side dedup. Flag-only does **not** make double-processing harmless — without these, a reclaimed slow job would double-flag and double-emit.
- **Poison-job cap** (fixes infinite crash loop): each claim increments an `attempts` counter; at `max_attempts` the job goes to `dead_letter` and raises a `degraded-verification` flag + alert, rather than cycling through and crashing the whole worker pool forever. This covers a job that hard-crashes a worker *before* §4's graceful CPU/time cap can fire.

### 4. Resource caps per job (anti-DoS)
Reject/skip any replay whose input sequence exceeds the **bounded run size** — ADR-0002 caps a run at ≤ 720 sim frames × max balls, so a replay claiming more is malformed. Enforce a per-job CPU/wall-time cap. Over-cap or malformed ⇒ treat per the GDD edge: **Tier-1-only + a `degraded-verification` flag** (useful telemetry for forced-client-upgrade detection), never a hard reject of the whole submission.

### 5. Fail-open + capacity degradation (from ADR-0001 + GDD edge)
If the queue depth exceeds a threshold or workers are unavailable: **shed load by risk** — drop *sampled low-risk* jobs first (logged), retain high-value jobs; if fully saturated, degrade to **Tier-1-only and ALERT** (never silently disable verification — GDD edge). Because Tier-1 already governs the reward, degradation affects only *fraud-signal coverage*, never player rewards or correctness. The depth check reads a **cached/approximate queue-depth gauge** (updated periodically), not a per-enqueue `COUNT(*)` — a synchronous count on every submission would itself race and add load on the hot path. The **human review queue** likewise needs backpressure: if its backlog grows beyond a threshold, raise the escalation flag-count bar and alert, rather than letting an unbounded review backlog accumulate (owned with the review-UI decision, GDD Open Q2).

### 6. Idempotency end-to-end
The client-generated **run ID** dedups both the reward grant (ADR-0004 idem key = run ID) and the verification job (one job per run ID). A resubmitted run is a no-op on both paths (GDD edge).

### 7. Flag → escalation → analytics
A mismatch writes a flag row; **3 flags / 7 days** (GDD tuning) surfaces the player to a **human review queue — never auto-ban** (GDD). Mismatch/degraded events are emitted as **server-side analytics** (ADR-0006's server-authoritative emission), not from the client.

### Architecture Diagram
```
Run submission (client) ──► Backend /submit
    │  Tier-1 clamp (SYNC, cheap)  ──► reward = Tier-1 amount ──► ADR-0004 mutateWallet (idem = runId)
    │                                                              └─► result screen: success (always)
    │  enqueue if risk-based coverage says so
    ▼
 verification_job (Postgres)  pending
    │   SELECT ... FOR UPDATE SKIP LOCKED   (lease + visibility timeout)
    ▼
 warm .NET SharedSimCore worker pool (N)  ── re-simulate seed+inputs (ADR-0002 fixed-point)
    │   caps: ≤720 frames×balls, CPU/time cap; over-cap ⇒ Tier-1-only + degraded flag
    ▼
 match?  ── yes ─► done(match), no action
          └ no ─► done(mismatch) ─► flag row ─► 3/7d ─► human review queue
                                      └─► server-side analytics event (ADR-0006)

Capacity exceeded ⇒ shed sampled low-risk jobs first; saturated ⇒ Tier-1-only + ALERT.
Reward is NEVER affected by Tier-2 (flag-only).
```

## Alternatives Considered

### Alternative A: Synchronous Tier-2 on the request path
- **Cons**: Couples the reward response to an expensive re-sim (latency); ADR-0001 already chose async/fail-open.
- **Rejection Reason**: Blocks the player's result screen on server CPU; unnecessary since Tier-1 governs the reward.

### Alternative B: Verify 100% of all runs
- **Cons**: The CPU cost `ENHANCEMENTS.md` and GDD Open Q1 flag as the core risk; scales as DAU × runs × 5 mini-games.
- **Rejection Reason**: Wasteful for trivial low-reward runs; risk-based coverage gets the economically-meaningful coverage far cheaper.

### Alternative C: Clawback on mismatch (provisional reward)
- **Cons**: Contradicts the GDD's flag-only Rule 6; risks clawing back false-positives (bad UX on a casual game); needs a GDD amendment and clawback machinery.
- **Rejection Reason**: The GDD deliberately favors false-positive-safety; flag-only is simpler and matches it. (Retained as the option to revisit if economy-integrity is later prioritized over false-positive-safety — a GDD-owner call.)

### Alternative D: Dedicated queue (Redis/BullMQ)
- **Cons**: New infra component for throughput we don't yet need.
- **Rejection Reason**: Postgres `SKIP LOCKED` is sufficient at expected scale; revisit if queue depth SLO can't be met.

### Alternative E: Persistent networked verification microservice
- **Cons**: New deployment target + auth boundary — ADR-0001 rejected exactly this.
- **Rejection Reason**: Worker-pull-from-queue achieves scale without an inbound service surface.

## Consequences

### Positive
- Answers the GDD's open CPU-cost question with a tunable coverage dial, not a fixed 100%.
- No clawback machinery — simpler, and no false-positive can harm a legit player.
- Reward latency is unaffected by Tier-2; workers scale independently of the request path.
- No new infrastructure or network attack surface (Postgres queue, pull-based workers).

### Negative
- A confirmed-but-under-threshold cheater keeps a capped-but-nonzero reward until human review — the accepted cost of false-positive-safety (a deliberate GDD stance, restated here).
- **Leaderboard integrity lag**: because Tier-2 is flag-only and async, a flagged cheater **sits atop the leaderboard until human review** resolves it. Mitigation to consider (routed to the leaderboard/hub owner): mark a board entry *provisional/shadow* while it has an open mismatch flag, so it can be visually de-ranked or footnoted without an auto-ban. Called out rather than left implicit.
- Coverage thresholds/sample rate are tuning knobs needing real data to set well (Open Q).
- Worker pool + queue is operational surface (sizing, lease timeouts, monitoring).

### Risks
- **Risk**: ADR-0002's determinism spike fails → replay can't be bit-reliable.
  **Mitigation**: This service's replay role is then replaced by ADR-0002's Alternative D (statistical anti-cheat); the queue/worker/coverage/escalation scaffolding here is reused with a statistical scorer instead of a re-simulator. Explicit dependency.
- **Risk**: Queue backs up silently.
  **Mitigation**: Depth SLO + alert; risk-based shedding; capacity-exceeded degrades to Tier-1-only *with a trace* (GDD edge).
- **Risk**: A worker crashes mid-job and the run is never verified.
  **Mitigation**: Lease/visibility timeout re-queues; jobs are idempotent by run ID.

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|------------|-------------|---------------------------|
| anti-cheat-replay-verification.md | Rule 6 / Acceptance: Tier-1 governs reward; Tier-2 flags only | Synchronous Tier-1 credit; async flag-only Tier-2 |
| anti-cheat-replay-verification.md | Open Q1: re-sim CPU budget, scale across 5 games | Risk-based coverage dial + warm worker pool; cost bounded, not 100% |
| anti-cheat-replay-verification.md | Edge: too expensive under load ⇒ degrade + trace | Risk-based shedding; saturated ⇒ Tier-1-only + ALERT |
| anti-cheat-replay-verification.md | Edge: missing/malformed replay ⇒ Tier-1 + degraded flag | Caps + malformed handling do exactly this |
| anti-cheat-replay-verification.md | Edge: duplicate run ID idempotent | Run ID dedups reward grant + verification job |
| anti-cheat-replay-verification.md | Escalation to human review, never auto-ban | 3/7d flag threshold → review queue |

## Performance Implications
- **CPU**: Dominated by re-sim; bounded by coverage policy. Worker pool sized to meet a **latency SLO** (recommend: p99 run verified within 60s of submission at target DAU) — actual numbers pending a real Unity port + `/perf-profile` (GDD Open Q1).
- **Memory/Network**: Small per job; DB-polled queue, no inbound network surface.
- **Player-facing latency**: Zero added — Tier-2 is entirely off the request path.

## Migration Plan
Greenfield service. Reuses ADR-0005 Postgres and ADR-0001/0002 SharedSimCore. No data migration.

## Validation Criteria
- Reward is credited at the Tier-1 amount and the result screen succeeds *before* any Tier-2 work runs.
- A mismatch produces a flag + server-side analytics event and, at 3/7d, a review-queue entry — with **no** reward change.
- Load test: at target DAU the queue drains within the SLO; capacity-exceeded sheds sampled low-risk jobs first and alerts, rewards unaffected.
- A malformed/over-cap replay yields Tier-1-only + degraded flag, not a hard reject.
- Duplicate run ID: reward credited once, one verification job.
- **Transactional-enqueue test**: inject a failure between the wallet credit and job insert → *neither* commits (no rewarded-but-unenqueued run).
- **Lease-expiry double-process test**: force a slow job's lease to expire and be reclaimed while the original still runs → the stale worker's terminal write is rejected by the `lease_epoch` fence; exactly one flag row exists (`UNIQUE(run_id)`), one analytics event.
- **Poison-job test**: a job that hard-crashes a worker reaches `dead_letter` at `max_attempts` and raises a degraded flag — it does not cycle the pool indefinitely.

## Related Decisions
- ADR-0001 — CLI verifier + fail-open (this ADR refines its execution model to a warm pool and adopts the GDD's flag-only over ADR-0001's clawback wording).
- ADR-0002 — the fixed-point sim the workers run; its spike gates this service's replay validity.
- ADR-0004 — reward grant + idempotency (run ID).
- ADR-0005 — Postgres queue + flag/landing tables.
- ADR-0006 — server-side emission of mismatch/degraded analytics.

## Open Questions
- **ADR-0001 wording reconciliation**: ADR-0001 says "clawback"; the GDD + this ADR are flag-only. Recommend a one-line correction to ADR-0001 (or an explicit GDD change if clawback is later wanted). Surfaced as a registry open-item, not silently edited.
- Concrete coverage threshold + sample rate `p` + worker count — set after a real mini-game port + `/perf-profile` (GDD Open Q1).
- Human review-queue UI vs. a lightweight admin list (GDD Open Q2).
- `tolerance_units` for the replay comparison: 0 for Super Ricochet (ADR-0002 bit-exact) vs. a small value for any future non-fixed-point mini-game — ties to the existing `tolerance_units_reconciliation` open-item.
