# Leaderboard

> **Status**: Designed
> **Author**: Abdulrahman Alenazan + Claude
> **Last Updated**: 2026-07-12
> **Implements Pillar**: Every mini-game is a real pillar, not a side loop
> **Creative Director Review (CD-GDD-ALIGN)**: APPROVED WITH CONDITIONS
> 2026-07-12 (4 conditions, all fixed same pass — C1: `leaderboard_scores`
> schema had no column for Rule 3's per-game display stats
> [level/`bossesDefeated`, `coinsCollected`/survival time], unrecoverable
> from `score` alone for Runner's non-invertible sum — added a
> `display_stats` JSONB column, updated atomically with `score` in the
> same upsert; C2: `quack-runner.md`'s Interactions section still read
> "Leaderboard (not yet designed)" with obsolete field names
> [`runnerBestScore`, Ricochet's `bestScore`] — fixed to match its own
> already-correct Dependencies section; C3: `ricochetBestScore` was
> authored in this GDD rather than owned by `super-ricochet.md`, breaking
> this GDD's own Rule 2 ownership precedent for Runner — backported the
> full formula/proof/examples to that GDD, reduced here to a pure
> Rule-1 consumer; C4: the required ADR-0005 addendum named only the
> table shape, not that Core Rule 4 makes this table's write a third
> transaction leg alongside `player_state`/`wallet` — extended Open
> Question 2 and Core Rule 9 to require the addendum also cover
> ADR-0005's canonical lock order and idempotency gating for this leg)
> **`/design-review` (2026-07-17)**: APPROVED with 2 minor additions
> (ADR-0007's Consequences section explicitly routes a "mark a flagged
> entry provisional/shadow" mitigation to "the leaderboard/hub owner" for
> consideration — this GDD's Edge Cases independently reaches the
> opposite, well-reasoned conclusion, but never cited ADR-0007, so the
> rejection read as made in a vacuum rather than considered-and-declined;
> `bossDefeated` (this-run boolean, Super Ricochet's new `run_reward`
> formula) and `bossesDefeated` (lifetime-cumulative int, this GDD's own
> `ricochetBestScore`) are a close naming collision worth an explicit
> disambiguation note). Both folded in below. This is the most mature GDD
> in the queue — 4 CD-GDD-ALIGN conditions already resolved, extensive
> formula-ownership backporting, self-flagged QA-harness gaps — no
> blocking items found. A separate, more significant issue surfaced
> during this review (a stale "server-replay-derived `bossDefeated`"
> claim contradicting Anti-Cheat's own Rule 6 and ADR-0007) was traced to
> its root cause in `anti-cheat-replay-verification.md` Rule 5 and fixed
> there, in `super-ricochet.md`, and in `boss-ai-damage-model.md` — not a
> defect in this file itself, which already correctly said "Tier-1-clamped
> value only... never a separate async step" (Core Rule 4).

## Overview

Leaderboard is the per-game competitive ranking system, formalizing a
decision Quack Runner's own GDD already made when it resolved
`systems-index.md`'s open question ("unify across games?"): Runner and
Super Ricochet get **separate leaderboards**, not one unified score,
since their scoring formulas are structurally incomparable
(`runnerLeaderboardScore` mixes coins-collected and dodge-bonus
time-decay; Ricochet has no formalized score at all yet). This GDD
confirms that decision explicitly rather than silently inheriting it,
and resolves two real gaps it surfaces: (1) ADR-0005 already locked a
**singular** `player.leaderboard_score` indexed column, written before
the per-game-separate decision existed — that schema needs to become
per-game, a required ADR-0005 addendum this GDD flags but cannot itself
author; (2) Super Ricochet's own GDD never defined what its leaderboard
score actually *is* — no formula exists anywhere, only the prototype's
raw `bestScore`/`level`/`bossesDefeated` fields — resolved explicitly in
`super-ricochet.md`'s own Formulas section. **[Amended during CD-GDD-ALIGN
review]**: this formula was originally drafted here, but ownership was
backported to `super-ricochet.md` to match Core Rule 2's precedent (a
mini-game's own GDD owns its leaderboard-facing score; this GDD only
consumes it) — see Core Rule 1. Carried over from the prototype: a top-20
public leaderboard, visible without authentication (shown even on the
login screen).

## Player Fantasy

Leaderboard turns each mini-game into its own proving ground: a player
doesn't just want a high score, they want to be *the* Ricochet player
or *the* Runner player — a title that only means something because it
isn't split against a different skillset. Pillar 4 says every game
deserves to matter on its own terms; a unified board would let
whichever game has the most forgiving curve quietly eclipse the others,
making Runner's leaderboard presence decorative rather than real.
Separate boards mean climbing Runner's ranks is its own accomplishment,
not a footnote to someone's Ricochet score.

## Detailed Design

### Core Rules

1. **Super Ricochet's leaderboard score is consumed as-is, never
   recomputed — mirroring Rule 2's treatment of Runner's score.**
   `ricochetBestScore = (levelReached × 100,000) + min(bossesDefeated,
   99,999)`. **[Disambiguated 2026-07-17]** `bossesDefeated` here is the
   lifetime-cumulative **int** (total boss kills ever, tie-break input to
   this formula) — a different field from `bossDefeated`, the this-run
   **boolean** (did this specific run kill a boss) that Super Ricochet's
   `run_reward` formula and Anti-Cheat's Run-Result Interface use for
   reward-gating. The two names are close enough to collide at a glance;
   this GDD only ever consumes the cumulative int. Now formalized in
   `super-ricochet.md`'s own Formulas section
   **[Amended during CD-GDD-ALIGN review — originally authored here,
   backported to that GDD so it owns its own leaderboard-facing score,
   the same ownership pattern Rule 2 already established for Runner]**,
   resolving the gap this GDD's Overview named. Level dominates the
   sort — it's the legible, infinite-progression milestone this game's
   core loop naturally produces — while `bossesDefeated` breaks ties
   *within* a level, which matters because level hard-caps at 30
   (`level-difficulty-config-ricochet.md`): once boss HP plateaus at
   11,100, skilled players cluster at the cap, and repeat boss kills at
   flat difficulty become the only remaining skill signal this formula
   can still surface. Leaderboard has no authority to redefine or adjust
   it.
2. **Quack Runner's score is consumed as-is, never recomputed.**
   `runnerLeaderboardScore = coinsCollected × 25 + Σ dodgeBonus(t_i)`,
   already locked by `quack-runner.md`. Leaderboard has no authority to
   redefine or adjust it.
3. **Per-game display columns**: Ricochet shows rank, username, score
   (Rule 1's composite), level, `bossesDefeated` — the same shape the
   prototype used, with "score" now formally defined instead of an
   opaque `bestScore`. Runner shows rank, username,
   `runnerLeaderboardScore`, `coinsCollected`, survival time —
   mirroring Ricochet's level/`bossesDefeated` pair so Runner players
   get the same two-column "how" behind their score.
4. **Eligibility is gated on Anti-Cheat's Tier-1-clamped value only,
   in the same transaction as reward crediting** — never the raw
   client-reported value, never a separate async step. A later Tier-2
   mismatch does not retroactively remove a posted score, mirroring
   Anti-Cheat's own "flag, don't block" philosophy (a mismatch flags
   the run for review; it doesn't zero the reward, so it doesn't zero
   leaderboard eligibility either). No separate "pending" leaderboard
   state exists.
5. **Top-20 is retained from the prototype** — a cheap indexed query,
   no pagination, glanceable on a mobile HUD.
6. **Tie-breaking**: identical scores rank by earliest `achieved_at`
   server timestamp, then `player_id` as a final deterministic
   tiebreak if timestamps somehow coincide — first to the milestone
   outranks a later duplicate, and the ranking is never ambiguous.
7. **Update cadence is live, with no artificial delay.** A validated
   run immediately overwrites the player's stored best-score row — no
   cache layer, no batch window — so the very next leaderboard read
   reflects it. **[Amended during Acceptance Criteria review]**: "live"
   is defined concretely as no server-side caching or batching stage
   between the write and the next read; the actual read latency is
   whatever the indexed top-N query costs (sub-second at top-20 scale
   per ADR-0005's schema), not a separately tuned delay this system
   introduces. At top-20 scale this is cheap, and instant feedback
   matches Player Fantasy's "being *the* Ricochet/Runner player"
   framing.
8. **Public, no-auth visibility carries forward from the prototype.**
   Both boards are readable without a session, including from the
   login screen. Username exposure is not a new decision — this
   matches the prototype's existing pattern and introduces no new
   player data beyond what Account/Auth already treats as displayable.
9. **A required ADR-0005 schema addendum is flagged, not authored
   here.** ADR-0005 locked a *singular* `player.leaderboard_score`
   column, written before Quack Runner's GDD established the
   per-game-separate decision this Overview formalizes — that schema
   is now stale. The needed fix is a generic
   `leaderboard_scores(player_id, game_id, score, achieved_at)` table
   with a composite `(game_id, score DESC, achieved_at ASC, player_id
   ASC)` index, current-best-only via an `ON CONFLICT ... DO UPDATE
   WHERE EXCLUDED.score > leaderboard_scores.score` upsert — not
   dedicated per-game columns, since 3 more mini-games are scoped to
   arrive eventually (`game-concept.md`) and a table means a new game
   is a data-only event, never a schema migration. This GDD specifies
   the requirement; the actual ADR-0005 amendment is a follow-up step,
   same pattern as Currency Ledger's own required ADR-0004 addendum.
   **A worse run never overwrites a better one**: the `WHERE
   EXCLUDED.score > leaderboard_scores.score` clause makes the upsert a
   silent no-op whenever a validated run's score is lower than the
   player's current best — nothing is written, the existing best row is
   untouched. See Edge Cases for the concurrent-submission case this
   same clause also resolves.
   **[Amended during CD-GDD-ALIGN review]**: the table also needs a
   `display_stats` JSONB column. Rule 3's per-game supporting columns —
   Ricochet's `level`/`bossesDefeated`, Runner's
   `coinsCollected`/survival time — aren't derivable from `score` alone
   at read time; this is unambiguous for Runner, where
   `runnerLeaderboardScore = coinsCollected × 25 + Σ dodgeBonus(t_i)` is
   a non-invertible sum (no way to recover `coinsCollected` or survival
   time back out of the total). Schema becomes `leaderboard_scores(
   player_id, game_id, score, achieved_at, display_stats)`, and the
   upsert sets `score`, `achieved_at`, and `display_stats` together in
   one `SET` clause whenever `EXCLUDED.score > leaderboard_scores.score`,
   so the display columns can never drift out of sync with the score
   they describe. **[Amended]**: Core Rule 4 puts this table's write in
   the same transaction as reward crediting, making it a *third* leg
   alongside ADR-0005's existing two (`player_state` FOR UPDATE, then
   the guarded `wallet` update). The required addendum must extend that
   ADR's canonical lock order to include this table's upsert (acquiring
   it in the same fixed position every time, not left to each call
   site), and extend its whole-operation idempotency gating — the shared
   `idem_key` check already dedupes the `player_state` mutator on
   replay; the `leaderboard_scores` upsert must be skipped on the same
   replay too, or a retried commit-ack could re-evaluate the upsert
   against a since-changed row. Leaving this table's lock position
   unspecified risks the same ABBA-deadlock class ADR-0005 already
   solved for its other two legs.
10. **Account deletion removes the leaderboard row entirely — it does
    not anonymize it.** This deliberately diverges from Currency
    Ledger's own precedent (anonymize, never delete): that pattern
    exists because financial records must survive account deletion for
    audit/legal/tax reconciliation, an obligation a leaderboard entry
    carries none of. A tombstoned "Deleted Player" sitting in a
    *public* top-20 is confusing broken UX with zero compensating
    audit value, unlike an internal ledger nobody browses casually.
    Removal also correctly lets the next-ranked player advance into the
    vacated spot.

### States and Transitions

Stateless query layer. Leaderboard has no lifecycle of its own — it's
a read view over per-game "current best" rows that other systems write
to, post-Anti-Cheat-clamp (Rule 4). Any notion of a "flagged" state
belongs to Anti-Cheat, not Leaderboard.

### Interactions with Other Systems

- **Anti-Cheat/Replay Verification** (reads from): the sole valid
  source for score writes — always the Tier-1-clamped value, in the
  same transaction as reward crediting (Rule 4).
- **Super Ricochet** (reads from): writes `ricochetBestScore`, level,
  and `bossesDefeated` per validated run.
- **Quack Runner** (reads from): writes `runnerLeaderboardScore`,
  `coinsCollected`, and survival time per validated run.
- **Account/Auth** (reads from): supplies the username for display;
  read access needs no session, write access needs a player ID (guest
  or full account).
- **Currency System**: explicitly no interaction — leaderboard scores
  never feed reward calculation, and reward calculation never reads
  from the leaderboard.
- **ADR-0005**: requires the per-game schema addendum named in Rule 9.

## Formulas

Neither leaderboard score is re-derived here — both are owned by their
respective mini-game's own GDD and consumed unchanged, per Core Rules 1
and 2:

- **`ricochetBestScore`** — fully formalized in `super-ricochet.md`'s own
  Formulas section (`(levelReached × 100,000) + min(bossesDefeated,
  99,999)`, including its overflow-safety proof and worked examples).
  **[Amended during CD-GDD-ALIGN review]**: originally derived here, then
  backported to `super-ricochet.md` so ownership matches Rule 2's
  precedent for Runner — see that GDD for the full derivation.
- **`runnerLeaderboardScore`** — fully formalized in `quack-runner.md`'s
  own Formulas section (`coinsCollected × 25 + Σ dodgeBonus(t_i)`).

**Currency System**: neither formula interacts with it whatsoever — both
are pure ranking values, never read by, written to, or converted into any
currency calculation.

## Edge Cases

- **If a guest account links to a permanent account** (same
  `playerId`): the leaderboard entry carries over unchanged, matching
  Mascot Gallery/Equip UI's and Login Streak's own precedent. Since
  `leaderboard_scores` keys on `player_id` (stable across linking),
  this isn't actually a Leaderboard state transition at all — nothing
  to implement or migrate.
- **If two devices submit a validated run for the same player at
  nearly the same time**: safe by construction, no idempotency key
  needed. `ON CONFLICT DO UPDATE` row-locks `(game_id, player_id)`,
  serializing the two writes; whichever runs second re-evaluates
  `EXCLUDED.score >` against whatever the first write just committed,
  so the final state is always "highest score wins" regardless of
  arrival order. This genuinely differs from Currency System's
  `mutateWallet`, which is additive and vulnerable to double-counting
  on replay — this upsert is a MAX operation, idempotent by
  construction, not merely "probably fine." **[Amended during
  CD-GDD-ALIGN review]**: `display_stats` (Core Rule 9) rides in the same
  `SET` clause as `score`/`achieved_at`, so the loser of the row-lock race
  can never leave a winning score paired with the loser's stale
  `display_stats` — all three columns commit or none do.
- **If a posted score is later Tier-2-flagged by Anti-Cheat**: fully
  silent to other players — no badge, asterisk, or visible marker on
  the leaderboard entry. A visible marker would function as a public
  pre-verdict accusation, contradicting Anti-Cheat's own "flag, don't
  block" philosophy (Core Rule 4), and would be actively harmful on
  the subset of flags Tier-2 later clears as false positives —
  reputational damage that can't be undone even if the score is
  vindicated. **[Cross-referenced 2026-07-17]** ADR-0007's Consequences
  section names exactly this scenario ("Leaderboard integrity lag: a
  flagged cheater sits atop the leaderboard until human review resolves
  it") and routes a specific mitigation — mark the entry
  provisional/shadow while flagged — to "the leaderboard/hub owner" for
  consideration. This is that consideration: the mitigation is
  considered and **declined**, for the false-positive-harm reasoning
  above, not overlooked. A flagged entry that survives human review
  (confirmed legitimate) suffers no visible stigma either way under this
  choice.

## Dependencies

- **Depends on** (hard): Anti-Cheat/Replay Verification (every score is
  the Tier-1-clamped value only, Core Rule 4); Super Ricochet
  (`ricochetBestScore`/level/`bossesDefeated` source); Quack Runner
  (`runnerLeaderboardScore`/`coinsCollected`/survival-time source);
  Account/Auth (username display, `playerId` scoping); Save/Persistence
  (the underlying `leaderboard_scores` table, Core Rule 9).
- **Depended on by** (hard): Hub UI (the leaderboard preview its own
  Core Rule 1 layout already names).

**Consistency check**: Super Ricochet's GDD already says "Depended on
by: Ricochet HUD (Presentation), Leaderboard (score feed)" — matches.
Account/Auth's GDD already says "Depended on by (hard, all of them):
... Leaderboard ..." — matches. Quack Runner's GDD said "Leaderboard
(not yet designed — consumes `runnerLeaderboardScore` ...)" — now
stale, fixed in the same pass. Save/Persistence and Anti-Cheat/Replay
Verification did not yet list Leaderboard — one-directional gaps this
GDD's own Dependencies section creates, fixed in the same pass (see
those files), following the project's established convention.

## Tuning Knobs

| Knob | Suggested Value | Too Low | Too High |
|---|---|---|---|
| Top-N size | 20 (carried from the prototype) | The board feels exclusive/unreachable for most players, undercutting the "being the Ricochet/Runner player" aspiration (Player Fantasy) | The list stops being glanceable on a mobile HUD and dilutes the prestige of a top-ranked spot |

## Visual/Audio Requirements

**Top-3 differentiation**: warranted, not optional. Player Fantasy
frames this as a title ("*the* Ricochet/Runner player"), not a
browsing list, so #1 needs to read as won. Rank 1 gets a small
crown/medal glyph in Bill Gold (the art bible's currency-and-reward
color, treasure/achievement) plus a marginally more saturated row per
the art bible's "hero shapes vs. supporting shapes" distinction; ranks
2–3 get the medal glyph only, no color escalation. Ranks 4–20 stay
plain numbered rows on the standard chunky-tactile panel — nothing
more. No character illustration: rank position isn't among the art
bible's three character-first triggers, so this stays icon-first, the
same boundary Mascot Gallery's equip badge follows.

**Own row**: reuses Mascot Gallery/Equip UI's established
"equipped-slot" language directly — a thick rounded Marquee Orange
border (the art bible's UI shape grammar), no new vocabulary invented.
Kept visually distinct from the rank-1 gold medal in both color and
shape so the two don't collide if the player holds #1.

**Rank-change feedback**: informational, not celebratory. Rule 7 makes
score writes live, but the player only meets their new rank on next
visit to a static list — there's no in-play trigger moment for a
Boss-Victory-scale beat. A small delta badge (e.g. "▲7") in Bill Gold
on the player's own row, a one-shot spring-pop on first render after
the change, is sufficient.

**Art bible fit**: tactility/pressability, Bill Gold/Marquee Orange
color roles, the hero-vs-supporting-shapes distinction, spring-based
animation feel.

*Honestly flagged*: the art bible's mood table has no row for
Leaderboard, and rank-change isn't among its three character-first
triggers — this document's own call, the same category of gap
`login-streak.md` flagged for its own claim beats.

## UI Requirements

Leaderboard renders as two full screens (one per mini-game, per Core
Rule 3's per-game column split) plus a compact preview embedded in Hub
UI (that GDD's own Core Rule 1 layout already names a leaderboard
slot). Each full screen shows the top-20 list with rank, username,
score, and the two supporting stat columns for that mini-game, the
player's own row highlighted per Visual/Audio Requirements, and top-3
medal treatment. Detailed layout, spacing, and interaction map belong
in `/ux-design`, not this GDD — this section only establishes required
content and states.

## Acceptance Criteria

- **GIVEN** a Ricochet run validated with `levelReached`=10,
  `bossesDefeated`=50, **WHEN** the score is computed, **THEN**
  `ricochetBestScore` stored equals 1,000,050.
- **GIVEN** `levelReached`=30, `bossesDefeated`=150,000, **WHEN**
  scored, **THEN** the value equals 3,099,999 (clamped at 99,999) and
  ranks strictly below a level-31/0-boss score of 3,100,000.
- **GIVEN** a validated Runner run whose `runnerLeaderboardScore` (per
  `quack-runner.md`'s formula) equals 4,250, **WHEN** submitted to
  Leaderboard, **THEN** the stored value is exactly 4,250, unaltered.
- **GIVEN** the Ricochet leaderboard screen, **WHEN** rendered, **THEN**
  each row shows rank, username, score, level, `bossesDefeated`.
  **GIVEN** the Runner screen, **WHEN** rendered, **THEN** rank,
  username, `runnerLeaderboardScore`, `coinsCollected`, survival time.
- **[Backend/integration tier, not black-box QA]** **GIVEN** a run's
  Tier-1-clamped value differs from the raw client-reported value,
  **WHEN** reward crediting commits, **THEN** only the clamped value is
  written to `leaderboard_scores` — not observable from the client UI
  alone, requires inspecting the write payload or the persisted row
  directly.
- **GIVEN** a posted score is later Tier-2-flagged, **WHEN** checked,
  **THEN** the row remains on the leaderboard, unremoved, and shows no
  badge, asterisk, or visual marker to other players.
- **GIVEN** 25+ scored players for a game, **WHEN** the leaderboard is
  queried, **THEN** exactly 20 rows return with no pagination control
  present.
- **[Requires seeded test fixtures with contrived timestamps, not a
  design gap]** **GIVEN** two players share an identical score, **WHEN**
  ranked, **THEN** the earlier `achieved_at` timestamp ranks higher.
  **GIVEN** identical score and identical `achieved_at`, **THEN** the
  lower `player_id` ranks higher.
- **GIVEN** a validated run improves a player's stored best, **WHEN**
  the write commits, **THEN** the immediately following leaderboard
  read reflects the new value with no cache or batch delay (Rule 7's
  amended definition of "live").
- **GIVEN** an unauthenticated client on the login screen, **WHEN** it
  requests either leaderboard, **THEN** the top-20 returns successfully
  without a session token.
- **[Database-level tier, not client-observable]** **GIVEN** the
  persisted schema, **WHEN** inspected directly, **THEN** a single
  generic `leaderboard_scores(player_id, game_id, score, achieved_at,
  display_stats)` table exists with the composite `(game_id, score DESC,
  achieved_at ASC, player_id ASC)` index — no per-game dedicated columns;
  this cannot be verified through the app UI, only via schema inspection.
- **[Database-level tier, not client-observable]** **GIVEN** a validated
  run improves a player's stored best, **WHEN** the write commits,
  **THEN** `score`, `achieved_at`, and `display_stats` are all updated in
  the same `SET` clause — no state where the score reflects the new run
  but `display_stats` still shows the previous run's level/`bossesDefeated`
  or `coinsCollected`/survival time.
- **GIVEN** a player's stored score is 500, **WHEN** a new validated
  run scores 300, **THEN** the row is untouched — still 500, original
  `achieved_at` unchanged.
- **GIVEN** a player with a leaderboard row deletes their account,
  **WHEN** deletion completes, **THEN** their row is deleted (not
  anonymized) and the next-ranked player advances one position.
- **GIVEN** a guest's leaderboard entry under `player_id` P, **WHEN** P
  links to a permanent account, **THEN** the entry is unchanged and
  still queryable under P.
- **GIVEN** two devices submit validated runs for the same
  `(player_id, game_id)` near-simultaneously, **WHEN** both writes
  process, **THEN** the final row equals the higher score regardless of
  arrival order, with no duplicate row or error.

**QA harness note (flagged by qa-lead review, not a design gap in this
GDD):** two criteria above require tooling beyond standard manual
QA — direct inspection of the write payload/persisted row for the
Tier-1-clamping criterion, and schema-level inspection for the table-
shape criterion. Neither is client-observable through the app UI alone.
These are `/qa-plan` / test-harness scope, matching the same pattern
this project's other Presentation-layer GDDs used for their equivalent
gaps — carried to Open Questions so it isn't silently dropped.

## Open Questions

1. **QA test-harness gaps** (qa-lead review, Acceptance Criteria):
   direct inspection of the write payload/persisted row to verify
   Tier-1-clamping, and schema-level inspection to verify the table
   shape. Neither is client-observable through the app UI alone — scope
   for a future `/qa-plan` pass, not this GDD.
2. **[RESOLVED 2026-07-17, see `adr-0012-persistence-schema-addendum.md`]**
   The required ADR-0005 schema addendum (Core Rule 9) is now authored:
   the generic `leaderboard_scores(player_id, game_id, score, achieved_at,
   display_stats)` table, the composite index, the upsert pattern, and the
   canonical composed-op lock order/idempotency gating extended to cover
   this table as a third transaction leg alongside `player_state` and
   `wallet` — including a corrected rule that the fixed lock order applies
   to *any* transaction touching 2+ of the three tables, not only the
   run-submission reward-credit path this GDD itself describes.
3. **The art bible's mood table has no Leaderboard row, and rank
   position isn't among its three character-first VFX triggers**
   (Visual/Audio Requirements) — this document's own extrapolation, not
   yet bible-sanctioned.
4. **Each of the 3 undesigned future mini-games will need its own
   leaderboard score formula defined with the same rigor this GDD gave
   Ricochet's** — that work belongs to each mini-game's own future GDD
   (matching how `quack-runner.md` formalized `runnerLeaderboardScore`
   itself), not this one. Flagged as a recurring process note so a
   future mini-game GDD doesn't silently skip formalizing its own
   leaderboard-facing score the way Super Ricochet's originally did.
5. **No content-moderation policy exists for usernames displayed on a
   public, no-auth leaderboard.** Account/Auth's own username
   constraints (3–20 characters, alphanumeric + underscore) don't
   prevent an offensive-but-charset-valid username from appearing
   publicly, unlike an internal display surface a player must be logged
   in to see. Not resolved here — flagged as a real, not-yet-addressed
   gap for a family-friendly game, since this is genuinely different
   exposure than any other username-display surface in the project so
   far. *Target: resolve alongside Account/Auth or a future
   trust-and-safety pass.*
