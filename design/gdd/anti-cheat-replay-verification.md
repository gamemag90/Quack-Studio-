# Anti-Cheat/Replay Verification

> **Status**: Revised
> **Author**: Abdulrahman Alenazan + Claude
> **Last Updated**: 2026-07-17
> **Implements Pillar**: Server-authoritative economy (hardened)
> **Creative Director Review (CD-GDD-ALIGN)**: APPROVED 2026-07-09 (self-reviewed — no `creative-director` subagent registered in this environment)
> **Revision note (2026-07-09)**: `/review-all-gdds` found no agreed contract
> for the run-result data shape, and specifically that this GDD and Boss
> AI/Damage Model both claimed authority over `bossDefeated`. Fixed by
> adding a Run-Result Interface (new Core Rule 5) and rewriting Rule 4 to
> match Currency System's corresponding fix (two separate credit terms,
> not one combined amount).
> **`/design-review` (2026-07-17)**: NEEDS REVISION — 3 blocking items
> (Rule 4's "two separate calls" contradicted ADR-0004's single atomic
> `mutateWallet` multi-leg call — same error found and now fixed in
> `currency-system.md` too; Run-Result Interface missing `miniGame`;
> `anti_cheat_mismatch`/degraded-verification events absent from
> `analytics-event-tracking.md`'s catalog) plus 4 recommended items. All
> folded in below; re-review pending.
> **Follow-up fix (2026-07-17, found reviewing `leaderboard.md`)**: Rule
> 5 claimed Anti-Cheat "re-derives `bossDefeated`... independently from
> replay simulation" and that a Tier-2 mismatch makes "the reward use the
> server-derived value" — directly contradicting this document's own Rule
> 6 (reward is the Tier-1-clamped, synchronous amount; a mismatch never
> changes it) and ADR-0007 (Tier-2 is async and flag-only, never
> reward-gating). Corrected Rule 5 to match Rule 6/ADR-0007: reward and
> quest-progress values come from Tier-1's synchronous clamp on
> `clientReportedFields`, never a replay-derived recomputation. Matching
> fixes applied to `super-ricochet.md`'s `run_reward` formula and
> `boss-ai-damage-model.md`'s Interactions section, both of which had
> inherited this same stale framing.

## Overview

Anti-Cheat/Replay Verification is the sole system permitted to compute final
reward amounts from a raw mini-game run — every other system (Currency
System included, per its own GDD) trusts this system's output rather than
re-deriving anything itself. It runs two tiers of validation: **plausibility
clamping** (carried over directly from the prototype's proven
`computeReward`/`computeRunnerReward` ceiling logic) and **deterministic
replay verification** (new — the hardening the master prompt's anti-fraud
requirements call for, and the exact plan the prototype's own
`ENHANCEMENTS.md` already scoped as its P2 #5 item but never implemented:
"make the engine deterministic... client submits the seed + input sequence...
server re-simulates headlessly... reject on mismatch"). This GDD formalizes
that plan for the native pivot rather than inventing a new approach.

## Player Fantasy

Indirect, but not nothing: players never see Anti-Cheat directly, but they
feel its failure modes acutely — a false-positive rejection of a legitimate
run is a trust-breaking moment, and a leaderboard everyone knows is gameable
is a motivation-breaking one. The design goal is for this system to be
strict enough that leaderboards and rewards stay meaningful, while erring
toward *never punishing a legitimate player over a technical false
positive* (see Core Rule 6).

## Detailed Design

### Core Rules

1. **Tier 1 — Plausibility clamping** (carried over, proven): every reported
   run result is clamped to a ceiling derived from what the level/time
   actually played could plausibly produce, before any reward is computed —
   the exact philosophy already implemented in the prototype's
   `computeReward`/`computeRunnerReward`. This tier's specific formulas
   belong to each mini-game's own future GDD (Super Ricochet,
   Quack Runner, etc.), not this one — Anti-Cheat's job is to *require and
   enforce* that every mini-game has this clamp, not to own each game's
   specific ceiling math.
2. **Tier 2 — Deterministic replay verification** [NEW]: the client submits
   not just the final result but a deterministic replay input (a seed for
   the run's RNG, plus the ordered sequence of player inputs — aim vectors
   for Super Ricochet, movement/timing for Quack Runner). The server
   headlessly re-simulates the *same* game logic using that seed+input
   sequence and derives the true result independently. A mismatch beyond
   tolerance means the client-reported result is not trusted as-is.
3. **Every mini-game's engine must be deterministic by construction**
   (fixed timestep, seeded RNG) specifically to make server-side replay
   possible. This is a hard engineering constraint on every future mini-game
   GDD, not an afterthought bolted on top. **[Clarified 2026-07-17]** This
   determinism is a genuine engineering risk, not a settled given —
   ADR-0002 gates bit-exact fixed-point physics determinism behind a HIGH-risk
   spike, with a named fallback (statistical/behavioral anti-cheat, no
   replay) if the spike fails. Open Question 1 below should be read together
   with that gate: the CPU-budget question only matters if the spike
   succeeds; if it doesn't, Tier 2 as described here is replaced entirely,
   not merely re-tuned.
4. **[Corrected 2026-07-17]** Anti-Cheat is the **only** system permitted to
   compute final reward amounts from raw run data. Its output is passed to
   Currency System as **one `mutateWallet` call carrying two legs in a
   single transaction** — a `creditMultiplied` leg for the "collected"
   amount and a `creditFlat` leg for any flat bonuses (per ADR-0004) — never
   two separate calls, and never pre-combined into one number before the
   split. A prior version of both this GDD and `currency-system.md`
   described "two separate calls," written before ADR-0004 settled on one
   atomic multi-leg call specifically to prevent a partial-failure case
   (one leg committing while the other fails) that two independent calls
   would reintroduce; both GDDs have now been corrected to match. This
   distinction matters specifically because flat bonuses (e.g. the
   boss-defeat bonus) must not receive the Coin Value multiplier —
   combining them into a single `raw_amount` before handing off to Currency
   was the root cause of a reward-inflation bug found by `/review-all-gdds`
   (2026-07-09) and fixed here and in `currency-system.md` together. **The
   `mutateWallet` call's operation-level idempotency key is the run's own
   `runId`** (per ADR-0007) — this is the same identifier Rule 5's replay
   dedup uses, not a separately-minted key, so "this run was already
   processed" and "this credit was already applied" can never disagree.
5. **Run-Result Interface** [NEW, added 2026-07-09 per `/review-all-gdds`;
   renumbered into sequence 2026-07-09 after an independent re-verification
   pass found it was appended out of order]: every mini-game submits a run
   result in one shared shape: `{ runId, miniGame, level, seed,
   inputSequence, clientReportedFields }`. **[Corrected 2026-07-17]**
   `miniGame` was missing from this shape despite being required by
   Currency Ledger's `getReconciliationSummary(source, ...)` and every
   analytics event that groups by `source` — ADR-0007's own verification-job
   payload already had to carry it independently
   (`{runId, seed, inputSequence, clientReportedResult, miniGame,
   playerId}`); the canonical shared interface should be the one place this
   is defined, not something each consumer re-adds separately.
   **[Corrected 2026-07-17]** For reward and quest-progress purposes,
   Anti-Cheat uses **Tier-1's synchronous, plausibility-clamped view of
   `clientReportedFields`** — `bossDefeated`, `bricksDestroyed`,
   `coinsCollected`, `score`, and the gem reward (`gems = bossDefeated ?
   5 + floor(min(level, 30) / 2) : 0`, per `super-ricochet.md`'s
   `run_reward` formula — **[Corrected 2026-07-17]** this GDD had
   previously dropped the `min(level, 30)` cap when re-stating the
   formula, silently resurrecting an ever-increasing-payout farming-loop
   exploit that `/review-all-gdds` had already closed elsewhere) — never
   the raw client-reported values unclamped. A prior version of this rule
   claimed these values are "re-derived independently from replay
   simulation," which is wrong: per ADR-0007, replay (Tier-2) is
   **asynchronous** and produces only a match/mismatch flag; the reward
   is computed and credited **synchronously at submission time**, before
   Tier-2 ever runs, so it cannot be gated by a replay-derived value —
   only by Tier-1's cheap, synchronous clamp. (For a boolean field like
   `bossDefeated`, Tier-1 has no numeric ceiling to clamp against, so it
   passes the client-reported value through as-is for reward purposes;
   Tier-2's later, async replay is what actually verifies it, per Rule 6
   below — never retroactively.) This directly contradicted Rule 6's own
   text in the same document ("the player still receives the
   Tier-1-clamped amount... not... a server-derived recomputation") — Rule
   6 already had the correct model; Rule 5's framing was the stale one and
   is now aligned to match it, and to match ADR-0007. `clientReportedFields`
   remain untrusted **only** in the sense that Tier-1 must clamp/validate
   them before use — they are still the actual source of the credited
   values, not replaced by a separately-computed replay result.
   **This still resolves the conflict found by `/review-all-gdds`**: Boss
   AI/Damage Model's GDD calls itself "the authoritative source" of
   `bossDefeated" — correct only for the *client-side engine's own
   in-session logic* (deciding when a run ends locally). For reward/quest
   purposes, Anti-Cheat's Tier-1-clamped view of the submitted
   `bossDefeated` is authoritative, not Boss AI's raw client value used
   directly. **If Tier-2 later (asynchronously) disagrees**: per Rule 6,
   the already-credited reward is never changed — only a fraud flag is
   raised for human review.
6. **A Tier-2 mismatch does not zero the reward.** The player still receives
   the Tier-1-clamped amount (not the client-reported amount), and the
   submission is flagged for review — this avoids punishing a legitimate
   player caught by a false positive (e.g. genuine floating-point drift)
   while still capping any actual exploit's upside and surfacing the
   anomaly.
7. Repeated mismatches from the same player are an escalation signal (rate
   review, not instant punishment) — see Tuning Knobs.
8. **[NEW 2026-07-17] Degraded-verification flags are a separate category
   from mismatch flags and do NOT count toward the same escalation
   counter.** A run flagged `mode=degraded` (missing/malformed replay data,
   capacity-exceeded fallback, or a poison-job hitting `max_attempts`) is
   evidence of an infrastructure or client-version condition, not evidence
   of cheating — counting it toward the 3-flags/7-days mismatch threshold
   would risk escalating a player merely running an outdated client to
   human review, directly contradicting this GDD's own "never punish a
   legitimate player" goal (Player Fantasy, Rule 6). Only `mode=mismatch`
   flags increment the escalation counter.

### States and Transitions

| State | Trigger | Result |
|---|---|---|
| Run submitted | Client posts result + replay data (seed + input sequence) | → Tier 1 clamp applied first |
| Tier 1 applied | Clamped result computed | → Tier 2 replay verification runs |
| Tier 2 match | Server-simulated result matches client-reported, within tolerance | → Full Tier-1-clamped reward credited, no flag |
| Tier 2 mismatch | Results diverge beyond tolerance | → Tier-1-clamped reward still credited (not the client-reported amount); submission flagged; mismatch count incremented |
| Mismatch count exceeds threshold | Repeated flags for the same player | → Escalation: submissions rate-reviewed, surfaced to a review queue — **not** auto-ban |

### Interactions with Other Systems

- **Currency System**: Anti-Cheat computes/validates before Currency System
  is ever invoked; Currency System never re-derives reward amounts. The
  reward credit (`mutateWallet`, two legs, idempotent on `runId`) and the
  Tier-2 verification-job enqueue commit in **one transaction** (ADR-0007)
  — either both happen or neither does, so a run is never credited without
  also being queued for whatever verification coverage it qualifies for.
- **Every mini-game engine** (Super Ricochet, Quack Runner, 3 undesigned
  ones): must be built deterministic (fixed timestep, seeded RNG) — a
  binding constraint their own future GDDs must satisfy for replay
  verification to be possible at all.
- **Account/Auth**: verifies `playerId` before any run submission is
  accepted.
- **Analytics/Event Tracking**: mismatches emit a dedicated
  `anti_cheat_mismatch` event (and a separate `degraded_verification` event
  for Rule 8's degraded-mode flags) so the studio can monitor false-positive
  rates over time, not just catch cheaters. **[Clarified 2026-07-17]** Both
  are detected and emitted **server-side, via the transactional outbox**
  (ADR-0006/ADR-0007) — never client-buffered, consistent with
  `analytics-event-tracking.md`'s split between client-buffered and
  server-outbox-emitted events. Both event names must be added to that
  GDD's Core Rule 3 catalog, which does not yet list either.

## Formulas

The `replay_match` formula is defined as:

`is_match = abs(server_simulated_value − client_reported_value) ≤ tolerance_units`

**Variables:**
| Variable | Symbol | Type | Range | Description |
|---|---|---|---|---|
| server_simulated_value | `server_simulated_value` | int | 0–∞ | The result the server derives from headlessly re-running the submitted seed + input sequence |
| client_reported_value | `client_reported_value` | int | 0–∞ | The result the client claims |
| tolerance_units | `tolerance_units` | int | 0–5 | Small integer slack absorbing legitimate cross-platform float-to-int rounding differences in an otherwise-identical deterministic simulation |

**Output**: boolean match/mismatch. **Example**: server derives score=482,
client reports 484, tolerance=2 → match (within tolerance). Client reports
520 → mismatch (exceeds tolerance), Tier-2 flag raised.

Tier 1's specific per-mini-game clamp formulas (e.g. `maxScore = 50 +
time×40` for Quack Runner) remain owned by those systems' own future GDDs —
not duplicated here, per the Overview's stated boundary.

## Edge Cases

- **If legitimate cross-device floating-point drift causes a small,
  genuine divergence** between client and server simulation of identical
  seeded logic: `tolerance_units` absorbs small legitimate drift. Anything
  beyond tolerance is still flagged even if it's a false positive — but per
  Rule 6, this only clamps the reward, it doesn't punish, minimizing harm
  from misclassification.
- **If a player's connection drops mid-submission and they resubmit the
  same run**: must be idempotent. Every run carries a unique client-generated
  run ID; a duplicate run ID is rejected/no-op'd on the second submission,
  never re-processed or double-credited.
- **If replay data is missing or malformed** (e.g. an outdated client that
  predates Tier 2 support): fall back to Tier-1-clamp-only rather than
  hard-rejecting the whole submission — but flag the submission as running
  in degraded verification mode, which is itself useful telemetry for
  detecting when a client version needs a forced upgrade.
- **If server-side replay simulation is too expensive under load** (the
  prototype's own `ENHANCEMENTS.md` explicitly flags "server CPU per run" as
  the trade-off of this approach): if capacity is exceeded, degrade to
  Tier-1-only for that submission and log/alert that capacity was exceeded —
  never silently disable verification without a trace.

## Dependencies

- **Depends on** (hard): Account/Auth (playerId verification), Analytics/Event
  Tracking (mismatch telemetry). **Currency Ledger/Transaction Log** —
  added 2026-07-12, reciprocal fix — escalation review reads a flagged
  player's recent mutation history via `getPlayerLedger(playerId, since,
  limit)` when the mismatch-escalation threshold is crossed (Tuning
  Knobs, below) and a human reviewer opens the queue; read-only, not
  called on every Tier 2 mismatch since most are false positives.
  **Currency System** — added 2026-07-17, reciprocal fix: Rule 4 has this
  system directly invoking `mutateWallet`, which currency-system.md's own
  Dependencies section already lists as a hard dependent of Currency
  System ("Anti-Cheat/Replay Verification... every credit's amount must
  already be Anti-Cheat-validated"); this file's own Depends-on list had
  never listed the reverse edge.
- **Depended on by** (hard): Currency System, every mini-game's
  run-submission flow. **Daily Quests** — added 2026-07-12, reciprocal
  fix — quest progress increments consume this system's already-validated
  run results exclusively (never raw client-reported events); read-only,
  no callback surface to Anti-Cheat. **Leaderboard** — added 2026-07-12,
  reciprocal fix — every posted score is this system's Tier-1-clamped
  value only, never a raw client-reported one; read-only, no callback
  surface to Anti-Cheat.

**Consistency check**: Currency System's GDD states "Anti-Cheat/Replay
Verification: computes and validates the amount *before* calling Currency
System" — matches this GDD's Core Rule 4 exactly. ✅ Currency Ledger/
Transaction Log's GDD states "Depended on by (hard): Anti-Cheat/Replay
Verification (escalation review reads via `getPlayerLedger`, Core Rule
2)" — now bidirectionally consistent *(added 2026-07-12 — the ledger GDD's
own Core Rule 2 surfaced this as a one-directional gap in this file,
fixed same pass)*. Daily Quests' GDD states "Depends on (hard): Anti-Cheat/
Replay Verification (validated run results are the only source of
progress increments, Rule 2)" — now bidirectionally consistent *(added
2026-07-12 — same fix pattern)*.

## Tuning Knobs

| Knob | Suggested Value | Too Low | Too High |
|---|---|---|---|
| `tolerance_units` | 2 (integer units on final score-like outputs) | Legitimate float-drift false-positives spike, eroding trust in flagged players | Real exploited-score gaps slip through as "matches" |
| Mismatch escalation threshold | 3 flags within 7 days | Legitimate one-off false positives trigger review too eagerly | Repeat exploiters accumulate many flagged (but still rewarded) runs before any review happens |

## Visual/Audio Requirements

N/A — invisible to legitimate play by design. A flagged/degraded submission
must **not** surface an alarming "you might be cheating" message to the
player (see UI Requirements) — false positives shouldn't feel punitive.

## UI Requirements

No dedicated screen. The one UX principle worth stating: a Tier-2 mismatch
or degraded-mode submission must resolve silently from the player's
perspective (their run result screen shows the same success state) — the
flag is backend-only telemetry, not a player-facing warning, since most
flags will turn out to be false positives (drift, outdated client) rather
than actual cheating.

## Acceptance Criteria

- **GIVEN** a legitimate run with matching client-reported and
  server-simulated results, **WHEN** submitted, **THEN** the full
  Tier-1-clamped reward is credited with no flag.
- **GIVEN** a run where the client-reported score is implausible for the
  time/level played, **WHEN** Tier 1 clamp is applied, **THEN** the reward
  is capped at the plausible ceiling regardless of what Tier 2 finds.
- **[REVISED 2026-07-17] GIVEN** server-simulated score=482 and
  client-reported score=520 at `tolerance_units=2`, **WHEN** compared,
  **THEN** `|520−482|=38>2` → mismatch; the reward is credited at the
  Tier-1-clamped amount only (never 520's implied amount) and the
  submission is flagged `mode=mismatch`.
- **GIVEN** the same run ID submitted twice, **WHEN** the second submission
  arrives, **THEN** it's rejected/no-op'd, never double-credited — this
  holds whether the duplicate arrives after the first fully completes or
  while the first is still being processed (concurrent resubmission).
- **[REVISED 2026-07-17] GIVEN** a player accumulates `mode=mismatch` flags
  beyond the escalation threshold (3 within 7 days), **WHEN** the 3rd lands,
  **THEN** they're surfaced to a review queue, **AND** their next login and
  next run submission both still succeed and are scored/rewarded
  identically to a non-flagged player — never auto-banned, never
  blocked from play.
- **[NEW 2026-07-17] GIVEN** a run submitted with no `inputSequence` (e.g.,
  a pre-Tier-2 client), **WHEN** processed, **THEN** no replay simulation is
  attempted, the reward is credited at Tier-1-clamp only, and the
  submission is flagged `mode=degraded` — a distinct category from
  `mode=mismatch` (Rule 8) that never increments the escalation counter.
- **[NEW 2026-07-17] GIVEN** server replay capacity is exceeded at
  submission time, **WHEN** the submission falls back to Tier-1-only,
  **THEN** a capacity-exceeded alert/log entry is emitted in addition to
  the per-submission `mode=degraded` flag — never a silent degradation.
- **[NEW 2026-07-17] GIVEN** a run is flagged `mode=mismatch` or
  `mode=degraded`, **WHEN** the run-result response is returned to the
  client, **THEN** it is structurally identical to the clean-success
  response (same success flag, same reward-credited fields, no
  cheat/degraded indicator visible to the player).

## Open Questions

1. **[RESOLVED elsewhere, 2026-07-17]** What's the actual re-simulation
   CPU-cost budget per run, and does it scale across 5 mini-games' worth of
   engines needing headless replay support? `adr-0007-replay-resimulation-
   service.md` answers this: risk-based coverage (100% of high-value/
   leaderboard-relevant/already-flagged/under-escalation runs, sampled at a
   tunable rate `p` otherwise), run on a warm `.NET` SharedSimCore worker
   pool pulling from a Postgres-backed queue — cost is bounded by coverage
   policy, not a fixed 100% of all runs. Concrete threshold/`p`/worker-count
   values still need real data (a genuine remaining open item, owned by that
   ADR, not this GDD).
2. Does the escalation review queue need a human-moderation UI, or can it be
   fully automated (auto-throttle without human review)? The master prompt
   implies human audit ("bot detection heuristics") but doesn't fully
   specify. Still open per ADR-0007's own Open Questions. *Target: resolve
   before live-ops tooling is built.*
