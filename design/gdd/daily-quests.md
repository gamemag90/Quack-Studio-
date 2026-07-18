# Daily Quests

> **Status**: Designed
> **Author**: Abdulrahman Alenazan + Claude
> **Last Updated**: 2026-07-12
> **Implements Pillar**: Shared hub, shared economy
> **Creative Director Review (CD-GDD-ALIGN)**: APPROVED WITH CONDITIONS
> 2026-07-12 (2 conditions — C1 fixed same pass: Core Rule 4 and
> Dependencies misattributed the `quest_claimed` server-authoritative
> outbox to ADR-0006; corrected to ADR-0004, per ADR-0006's own text
> confirming server-authoritative events "come from ADR-0004's outbox,
> NOT this client path." C2 investigated, not a real defect: the
> reviewer flagged `creditFlat`/`creditMultiplied` as GDD-invented API
> names since `currency-system.md`'s own prose only says "flat
> path"/"multiplied path" — but these ARE real ADR-0004/
> `control-manifest.md`-sourced leg names (`mutateWallet`'s leg kinds),
> and `quack-runner.md` already uses this exact terminology as
> established precedent — no fix needed.)
> **`/design-review` (2026-07-17)**: NEEDS REVISION — 2 blocking items
> (this GDD's absolute "never... claws back a claimed reward" statement
> contradicts mascot-database.md's Core Rule 6, which treats a
> human-confirmed Tier-2 fraud finding as the sole exception that DOES
> revoke a grant — the two GDDs describe mutually exclusive downstream
> consequences of the same upstream escalation-review outcome, an
> unresolved product question, not just wording; Rule 6's one-free-reroll
> limit had no server-side counter or enforcement mechanism specified at
> all, let alone rejection behavior for a second attempt) plus 2
> recommended and 2 nice-to-have items. All folded in below; re-review
> pending.

## Overview

Daily Quests is the recurring 3-per-day reward loop that pulls players back
into the Hub and across mini-games: each day the server draws 3 quests
without repeat from 4 types (bricks destroyed, coins collected, bosses
defeated, runs completed), tracks progress toward each, and credits a
reward through Currency System's existing paths on claim. The system
resolves a real structural gap flagged in `systems-index.md`: only 2 of 5
mini-games currently define a result shape (Ricochet's `RunResult`,
Runner's `RunnerRunPayload`), and they differ. Rather than binding quest
logic to either shape, or blocking on a formal cross-cutting result-schema
ADR, Daily Quests defines its own narrow abstracted event set
(`bricksDestroyed`, `coinsCollected`, `bossDefeated`, `runCompleted`) that
any mini-game's result maps into — the mapping is each mini-game's
responsibility, quest tracking never inspects a mini-game-specific shape
directly. This GDD is carried over from the prototype in spirit only:
`game-concept.md` preserved the per-target reward multipliers (bricks 1:1,
coins ×4, runs ×15, bosses ×100+1 gem) but not the actual target counts per
quest (no `quests.ts`-equivalent source exists to check against) — those
are a fresh balance decision made explicitly in this document, not
silently invented.

## Player Fantasy

Daily Quests is the fantasy of a short list that resets — three small
promises the hub makes each morning, each one an excuse to open a specific
mini-game rather than just "the app." The pull isn't loot, it's closure:
watching a bar fill and tapping claim is a complete, tiny win, distinct
from a run's own win/loss. Because the three draws span bricks, coins,
bosses, and runs, no single mini-game's session fulfills the day alone —
the quest board is what makes Ricochet, Runner, and every future mini-game
read as one shared campaign instead of separate apps sharing a wallet,
directly serving Pillar 1 (shared hub, shared economy). *A tension worth
naming rather than assuming away: a player who dislikes Ricochet can still
draw a bricks or bosses quest, forcing play in a disliked mini-game — is
that cross-game exposure a feature, or a resented obligation? See Open
Questions.*

## Detailed Design

### Core Rules

1. **Generation is server-authoritative, resets at UTC midnight** — the
   same cadence Login Streak uses, for one consistent "new day" boundary
   across both systems (referenced for consistency, no shared state). The
   server draws 3 of the 4 quest types without repeat (one type omitted
   each day), using a deterministic seed (`playerId + UTC date + server
   salt`) — reproducible for support/audit lookups, never client-
   predictable since the salt never reaches the client. Quest state
   (type, target, progress, status) lives entirely server-side; the
   client receives read-only state on session start and after each
   validated progress event.
2. **Progress increments ONLY from Anti-Cheat-validated results — never
   from raw client-reported mid-run events.** This mirrors Currency
   System's own "trusts already-validated amounts" pattern. Pipeline
   order: a mini-game submits its run → Anti-Cheat/Replay Verification
   validates it → the validated result is mapped through the abstracted
   event set (`bricksDestroyed`, `coinsCollected`, `bossDefeated`,
   `runCompleted` — Overview) → in the same server-side transaction as
   Currency System's run-reward credit, any active quest whose type
   matches has its counter incremented. This progress-increment
   transaction is distinct from a quest's own reward credit, which only
   happens on claim (Rule 4) — a completed quest does not auto-credit.
3. **"Runs completed" means any validated run submission from any of the
   5 mini-games, win or loss.** Cross-game, not scoped to one mini-game —
   this quest type rewards session engagement and breadth across the
   collection, not mastery of one game, consistent with Pillar 1 (shared
   hub, shared economy).
4. **Completion and claim are distinct states**, matching Player
   Fantasy's explicit claim beat. When progress reaches target, the quest
   becomes `Complete-Unclaimed` — no reward is credited yet. The player
   taps claim; the client sends only the quest ID, never an amount. The
   server re-verifies the quest is genuinely `Complete-Unclaimed` for
   that player, then credits the reward via Currency System using the
   server-known amount (Rule 7), and transitions the quest to `Claimed`.
   The client never supplies or influences the credited amount. **The
   same claim transaction emits `quest_claimed`** via ADR-0004's
   server-authoritative analytics outbox (the same pattern
   `currency_earned`/`currency_spent` already use — ADR-0006 documents
   this event-ownership split but the outbox mechanism itself is
   ADR-0004's) — this GDD aligns with, rather than duplicates or
   contradicts, that existing decision; Daily Quests does not define a
   new client-side event for claiming. **[Corrected during
   CD-GDD-ALIGN review]**: an earlier draft misattributed this outbox to
   ADR-0006; ADR-0006's own text is explicit that server-authoritative
   events including `quest_claimed` "come from ADR-0004's outbox, NOT
   this client path."
5. **Unclaimed quests are forfeited at rollover, softened by a UI nudge,
   not a grace period.** At UTC midnight, any quest not in `Claimed`
   (including `Complete-Unclaimed`) transitions to `Expired` and its
   reward is lost; a fresh slate of 3 generates per Rule 1. This keeps
   the "three promises each morning" fantasy clean — no cross-day reward
   bookkeeping, no stockpiling exploits, one clear daily rhythm. The only
   softening is a client-side reminder/badge when a quest is claimable
   and reset is near (Tuning Knobs) — never a mechanical grace period.
6. **One free reroll per day mitigates the cross-game-obligation
   tension** named in Player Fantasy (a player who dislikes Ricochet can
   still draw a bricks/bosses quest). Server-side; the rerolled slot's
   type is swapped for the single type omitted from that day's original
   3-of-4 draw (Rule 1) — the only substitution that preserves the
   no-repeat invariant, since redrawing from all 3 remaining types could
   duplicate a type already occupying another active slot. **[Corrected
   during Edge Cases review]**: an earlier draft of this rule said
   reroll "re-runs the same deterministic draw excluding the current
   type," which is underspecified and could violate Rule 1's no-repeat
   guarantee — fixed to the single-omitted-type swap above. Cheap,
   requires no new play-history tracking. A more adaptive mitigation
   (weighted draw favoring a player's recently-played mini-games) is
   deliberately deferred — see Open Questions — since no play-history
   system exists yet to feed it. **[NEW 2026-07-17]** Enforcement: a
   per-day `rerollsUsed` counter (server-side, part of the day's quest
   state, reset at UTC rollover alongside the slate itself) increments on
   each successful reroll. A reroll request when `rerollsUsed ≥ 1` is
   **rejected server-side as a no-op** — the client never gains a way to
   force a second reroll, regardless of which slot or how many times it
   retries. This was previously unstated: Rule 6 named the one-per-day
   limit but never specified the counter or the rejection behavior that
   actually enforces it.
7. **Target counts and reward routing, per quest type — all rewards
   credit via Currency System's `creditFlat` leg, never
   `creditMultiplied`.** This is a deliberate choice: routing the
   "coins collected" quest type's reward through the multiplied path
   would stack with the Coin Value upgrade's own up-to-5× multiplier
   (Currency System GDD), pushing a single quest reward toward ~400
   coins for maxed-upgrade players and badly breaking the income-parity
   target below — so every quest type is a flat completion bonus, not a
   scaled one, regardless of type.

   | Type | Target | Reward |
   |---|---|---|
   | Bricks destroyed | 50 | 50 coins (flat) |
   | Coins collected | 20 | 80 coins (flat) |
   | Runs completed | 3 | 45 coins (flat) |
   | Bosses defeated | 1 | 100 coins + 1 gem (flat) |

   Daily total (3 of 4 types drawn) ranges ~175 coins (boss excluded) to
   ~230 coins + 1 gem (runs excluded), averaging ~206 coins + ~0.75 gems
   — sitting modestly above Login Streak's own 40–190 coins + ~0.71
   gems/7-day-cycle average, which is intentional rather than budget
   creep: quests require active play, streak doesn't.

### States and Transitions

| State | Enter when | Exit when |
|---|---|---|
| Assigned | Daily generation (Rule 1) | First validated progress event |
| InProgress | progress > 0, < target | progress ≥ target, or rollover |
| Complete-Unclaimed | progress ≥ target | Claim tap (server-validated), or rollover |
| Claimed | Server-validated claim (Rule 4) | — (terminal for the day) |
| Expired | UTC rollover while not Claimed (Rule 5) | — (terminal) |

### Interactions with Other Systems

- **Anti-Cheat/Replay Verification** (reads) — every quest progress
  increment consumes an already-validated run result, never a raw
  client-reported one (Rule 2).
- **Every mini-game's own result mapper** (reads, indirect) — each
  mini-game owns the responsibility of mapping its own result shape
  (`RunResult`, `RunnerRunPayload`, or a future mini-game's own type)
  into the abstracted event set this GDD defines (Overview); Daily
  Quests never inspects a mini-game-specific shape directly.
- **Currency System** (writes) — exactly once per quest, only on a
  server-validated claim, via the `creditFlat` leg (Rule 4, Rule 7).
- **Login Streak** (reference only, no shared state) — same UTC-midnight
  reset cadence, for consistency (Rule 1).
- **Hub UI** (read by) — quest slate, per-quest progress, claim and
  reroll actions; see UI Requirements.
- **[NEW 2026-07-17] Mascot Database + Rarity Logic** (soft, currently
  unfulfilled) — that GDD's "Consistency Streaks" milestone category
  needs "a full week of Daily Quest completion" as an unlock condition
  input, flagged there as a provisional dependency since neither GDD was
  designed yet. Now that this GDD is fully designed, the gap is no longer
  provisional: this GDD's only persisted state is the **current** UTC
  day's 3-quest slate (Rule 1, States table) — there is no per-day
  "were all 3 claimed" boolean, no rolling streak counter, and no
  historical log surviving rollover (Rule 5 forfeits and replaces the
  slate outright). Mascot Database's milestone category has no actual
  interface to consume. See Open Questions.

## Formulas

The `questReward` formula is defined as a lookup over the 4 discrete quest
types (Core Rule 7), not a continuous curve:

```
questReward(type) =
  { 50 coins            if type = BRICKS  (target: BRICKS_TARGET = 50)
  { 80 coins            if type = COINS   (target: COINS_TARGET  = 20)
  { 45 coins            if type = RUNS    (target: RUNS_TARGET   = 3)
  { 100 coins + 1 gem   if type = BOSSES  (target: BOSSES_TARGET = 1)
```

**Variables:**
| Variable | Symbol | Type | Range | Description |
|---|---|---|---|---|
| Quest type | `type` | enum | {BRICKS, COINS, RUNS, BOSSES} | Type drawn per Core Rule 1 |
| Target constant | `BRICKS_TARGET`, `COINS_TARGET`, `RUNS_TARGET`, `BOSSES_TARGET` | int (named constant) | 1–50, fixed per type | Progress required to enter `Complete-Unclaimed` (Rule 7 table) |
| Reward | return value | int coins, or int coins + int gems | 45–100 coins, +1 gem on Bosses only | Flat amount credited on claim via Currency System's `creditFlat` leg (Rule 4) |

**Output Range:** 45–100 coins per claim, plus a 1-gem bonus only on the
Bosses branch. Exactly one reward per claimed quest — not cumulative
across types.

**Example:** A player draws a Coins quest, collects 23 coins (target 20,
so it completes at 20 with 3 unconsumed toward this quest).
`questReward(COINS)` returns 80 coins flat via `creditFlat` — the 3 extra
collected coins do not scale the reward.

**Relationship to Currency System:** `questReward` is never routed through
`coin_credit`'s multiplied path (Currency System Formulas) — only through
the flat path, per Rule 7's rationale (avoiding stacking with Coin Value's
up-to-5× multiplier). Consequently, if Currency System's Coin Value
upgrade formula (`coins_applied = collected_amount × (1 +
coin_value_upgrade_level)`) changes in the future, `questReward`'s output
is unaffected — it never enters that multiplied term.

## Edge Cases

- **If a single validated event pushes progress past target** (e.g. one
  run destroys 80 bricks against `BRICKS_TARGET`=50): progress is capped
  at target on transition to `Complete-Unclaimed`; the excess (30 bricks)
  is discarded — no carryover to another quest, slot, or day. Matches the
  Formulas section's own worked example (23 coins collected vs. target
  20) and Rule 5's ban on cross-day reward bookkeeping.
- **If one validated submission produces multiple matching event types**
  (e.g. a Ricochet run yields `bricksDestroyed`, `coinsCollected`, AND
  `bossDefeated` in one submission, with 2–3 of those types active as
  quests that day): all active quests whose type matches ANY produced
  event type are incremented in that same transaction — one submission
  can advance multiple quests simultaneously. Rule 2's "any active quest
  whose type matches" applies per-event-type independently, never
  exclusively to one quest.
- **If reroll is attempted on a quest with progress > 0** (`Assigned` or
  `InProgress`): allowed — this is the mechanism's intended use, letting
  a player escape a disliked mini-game even after reluctant partial
  progress; existing progress is discarded along with the swapped type.
  **If reroll is attempted on a `Complete-Unclaimed` quest**: rejected
  server-side — there is nothing to gain and an earned-but-unclaimed
  reward to lose by rerolling it. **[NEW 2026-07-17] If a reroll is
  attempted after the day's one free reroll is already used** (per the
  `rerollsUsed` counter, Rule 6): rejected server-side as a no-op,
  regardless of which slot is targeted or whether that slot itself would
  otherwise be eligible.
- **If claim is double-tapped** (rapid concurrent requests): the claim
  transaction is an atomic compare-and-transition (a conditional update
  scoped to `WHERE status = 'Complete-Unclaimed'`); the losing concurrent
  request finds status already `Claimed` and is rejected as a no-op —
  never double-credited.
- **[CORRECTED 2026-07-17] If Anti-Cheat's async Tier-2 review flags a
  run after its Tier-1-validated quest progress/reward was already
  credited**: the automatic flag itself never reverses progress,
  un-completes a quest, or claws back a claimed reward — it only raises
  a fraud flag for human review, matching Anti-Cheat/Replay
  Verification's flag-only, no-clawback model for the automatic Tier-2
  response. This GDD previously stated that guarantee as unconditional
  ("never... exactly"), which contradicts mascot-database.md's Core Rule
  6: a **human-confirmed** Tier-2 fraud finding (the 3-flags/7-days
  escalation outcome, after review) *is* the sole exception that revokes
  a mascot grant. Whether a confirmed fraud finding should likewise claw
  back an already-claimed quest reward (matching the mascot precedent) or
  whether currency is deliberately treated differently (e.g., because a
  credited amount may already be spent, unlike a static ownership flag)
  is an open product question this GDD does not resolve — see Open
  Questions. What's certain: the *automatic*, pre-human-review flag never
  reverses anything; only the *confirmed, human-adjudicated* case is
  ambiguous across documents.
- **If a run spans UTC rollover** (started before midnight, submitted
  after): the validated result applies to the NEW slate only. By the
  time it arrives, yesterday's quests are already `Expired` (terminal,
  non-active per Rule 5), and Rule 2 only credits active quests — the
  pre-rollover play investment earns nothing toward the old slate. This
  is an intentional consequence of Rule 1's deterministic UTC boundary,
  not a bug, and is stated explicitly here to block any future ad hoc
  grace-period logic.

## Dependencies

- **Depends on** (hard): Anti-Cheat/Replay Verification (validated run
  results are the only source of progress increments, Rule 2); Currency
  System (reward credit on claim via `creditFlat`, Rule 4/7);
  Analytics/Event Tracking (`quest_claimed` via ADR-0004's
  server-authoritative outbox, Rule 4); Save/Persistence (durable quest
  state — already lists Daily Quests as a hard dependent); Account/Auth
  (playerId scoping for quest state and claims).
- **Depended on by**: none yet formal. Hub UI's own Interactions section
  lists Daily Quests as data rendered via Shared Hub's read-through
  aggregation, but that's a UX consumer, not a named component slot or a
  system-level dependency — **[Corrected 2026-07-12]** an earlier draft
  overstated this as an "already-reserved 'quests-card' slot"; hub-ui.md
  does not actually name a dedicated component — see UI Requirements.
- **Reference only, no shared state**: Login Streak (same UTC-midnight
  reset cadence, for consistency — Rule 1).

**Consistency check**: Currency System's GDD says "Depended on by (hard):
... Daily Quests" — matches. Save/Persistence's GDD says "Depended on by
(hard, all of them): ... Daily Quests" — matches. Anti-Cheat/Replay
Verification's GDD did not yet list Daily Quests among its dependents —
the one-directional gap this GDD's Rule 2 creates, fixed in the same pass
(see that file's Dependencies section), following the project's
established convention of fixing rather than just flagging reciprocal
gaps.

## Tuning Knobs

| Knob | Suggested Value | Too Low | Too High |
|---|---|---|---|
| Quests per day | 3 | The daily loop feels thin, not worth a check-in | Rollover forfeiture (Rule 5) feels punishing more often; claim-fatigue sets in |
| Free rerolls per day | 1 | The cross-game-obligation tension (Player Fantasy) goes unmitigated | Trivializes the no-repeat 3-of-4 variety the loop is built around |
| Per-type targets/rewards | see Core Rule 7 table (bricks 50→50 coins, coins 20→80 coins, runs 3→45 coins, bosses 1→100 coins+1 gem) | Quests clear trivially in a fraction of a session, cheapening the claim moment (Player Fantasy) | A quest routinely spans many sessions, breaking the "three promises each morning" daily-reset fantasy |
| "Claimable soon" UI nudge lead time | 2 hours before UTC rollover | Player has no realistic time to act on the nudge before forfeiture | Nudge fatigue — an always-on badge stops meaning anything |

## Visual/Audio Requirements

**VFX/Visual Feedback**

- **Assigned / rerolled**: not covered by an explicit art-bible entry —
  treated as a lightweight, icon-first card refresh (a quick spring-based
  flip/shuffle per the art bible's animation-feel section) rather than a
  character moment, since assignment/reroll isn't one of the art bible's
  listed character-first triggers (mini-game entry, quest complete,
  mascot unlock).
- **Progress increment**: tweens, never snaps — the art bible mandates
  spring-based easing with slight overshoot for all animation, so the bar
  eases to its new value on hub return rather than jumping.
- **Complete-Unclaimed**: the claim button takes the art bible's
  "pressable object" treatment (thick border, layered shadow) plus a
  subtle idle bounce/glint in Bill Gold — the same saturated, "wanted"
  treatment specified for reward pickups.
- **Claim moment**: the art bible has a mood-table row built for exactly
  this — a punchy Bill Gold highlight burst on the reward itself while
  the rest of the screen stays warm-neutral: "sparkly, satisfying,
  bite-sized," a quick energy spike, not a takeover. Quest-complete is
  listed as a character-first trigger, so the burst pairs with a brief
  duck reaction — smaller and faster than Boss Victory's radial-burst
  treatment, matching "tiny win" scale rather than "triumphant" scale
  (Player Fantasy).
- **Expiration / 2-hour nudge**: not specified in the art bible —
  inferred from its Brick-Red-must-pair-with-a-non-color-icon rule and
  its "warm, never grim" principle: the nudge reads as a gentle
  badge/icon cue, never an alarming red flash.

**Animation/style constraints**: bouncy spring easing throughout; no
linear/mechanical tweens. Claim VFX stays contained to the quests-card
slot — no full-screen takeover, preserving the "quick spike" distinct
from Boss Victory.

**Art bible principles applied**: tactility/pressability, warm/saturated
treatment even in loss-adjacent states, the Reward/Quest Claim mood row,
the animation-feel and character-first-trigger boundary, the
colorblind-safety color-pairing rule.

*Honestly flagged*: the expiration-nudge and assign/reroll treatments
have no direct art-bible precedent and are extrapolated from adjacent
rules rather than lifted from a written spec — worth a design pass or an
art-bible addendum if this system's polish bar rises.

## UI Requirements

Daily Quests renders as 3 quest cards inside the Hub (hub-ui.md's own
Interactions section already lists Daily Quests as data rendered via
Shared Hub's aggregation, but does not yet name a dedicated component —
**[Corrected 2026-07-12]**, matching the Dependencies section fix above).
Each card shows:
quest-type icon, a progress bar (current/target), the reward preview
(coins and/or gem icon per Core Rule 7), and a state-dependent action —
no button while `InProgress`, a Claim button once `Complete-Unclaimed`,
and a Reroll icon-button while a free reroll remains for the day (Tuning
Knobs). Detailed layout, spacing, and interaction map belong in
`/ux-design`, not this GDD — this section only establishes the
component's required content and states.

## Acceptance Criteria

- **GIVEN** no quest slate exists for the current UTC day, **WHEN** the
  server generates today's quests, **THEN** exactly 3 of 4 types are
  assigned with no repeats, and re-fetching later the same day returns
  the identical 3 types and targets.
- **GIVEN** an active quest of type X, **WHEN** a run result fails
  Anti-Cheat validation, **THEN** quest X's progress does not increment.
- **GIVEN** an active Runs-completed quest, **WHEN** a validated run from
  any of the 5 mini-games ends in a loss, **THEN** progress increments by
  1 (win/loss is irrelevant to this quest type).
- **GIVEN** a quest in `Complete-Unclaimed`, **WHEN** the player taps
  Claim, **THEN** the credited amount matches the server-side table
  regardless of any client-supplied amount, the quest transitions to
  `Claimed`, and `quest_claimed` fires in the same transaction.
- **GIVEN** a quest in `Complete-Unclaimed` at UTC rollover, **WHEN**
  rollover occurs, **THEN** it transitions to `Expired` with no reward
  credited, and a fresh 3-quest slate generates.
- **GIVEN** daily type Y was the one omitted from the 3-of-4 draw,
  **WHEN** the free reroll is used on any active slot, **THEN** that
  slot's type becomes Y and no duplicate type exists among the 3 active
  quests.
- **GIVEN** each quest type reaches target while the player has a maxed
  Coin Value upgrade, **WHEN** claimed, **THEN** the credited amount
  equals the flat table value only (e.g. Coins = 80, never ~400).
- **GIVEN** a Bricks quest with target 50, **WHEN** one submission
  reports 80 `bricksDestroyed`, **THEN** progress caps at 50 with no
  carryover of the excess 30.
- **GIVEN** active Bricks, Coins, and Bosses quests, **WHEN** one
  submission yields all three matching events, **THEN** all three
  increment in the same transaction.
- **GIVEN** an `InProgress` quest, **WHEN** reroll is used, **THEN**
  progress is discarded and its type swaps to the omitted type. **GIVEN**
  a `Complete-Unclaimed` quest, **WHEN** reroll is attempted, **THEN** it
  is rejected server-side and the quest is unchanged.
- **GIVEN** a `Complete-Unclaimed` quest, **WHEN** two claim requests
  fire concurrently, **THEN** exactly one succeeds and the other is
  rejected as a no-op.
- **GIVEN** a Tier-1-validated run already credited, **WHEN** async
  Tier-2 later flags it fraudulent, **THEN** quest state is unchanged —
  only a fraud flag is raised, never a reversal.
- **GIVEN** a run started pre-rollover and submitted post-rollover,
  **WHEN** the validated result arrives, **THEN** it applies only to the
  new slate; the `Expired` old slate receives nothing.
- **[NEW 2026-07-17] GIVEN** a day where Bosses is the omitted type,
  **WHEN** all 3 remaining quests (Bricks, Coins, Runs) are claimed,
  **THEN** the total credited is exactly 50+80+45=175 coins. **GIVEN** a
  day where Runs is omitted instead and Bricks/Coins/Bosses are all
  claimed, **THEN** the total is exactly 50+80+100=230 coins + 1 gem —
  pinning Core Rule 7's own worked range as executable tests, not prose
  alone.
- **[NEW 2026-07-17] GIVEN** a `Complete-Unclaimed` quest with rollover
  exactly 2 hours away, **WHEN** the client checks nudge state, **THEN**
  the claimable-soon badge is shown; **GIVEN** rollover is 3 hours away,
  **THEN** it is not yet shown — pinning the Tuning Knob's stated lead
  time to a testable boundary.
- **[NEW 2026-07-17] GIVEN** a player has already used their one free
  reroll for the day, **WHEN** a second reroll is attempted (on any
  slot, including one that would otherwise be individually eligible),
  **THEN** it is rejected server-side as a no-op and no slot's type
  changes.
- **[NICE-TO-HAVE 2026-07-17] GIVEN** a brand-new player's very first UTC
  day, **WHEN** the deterministic quest draw runs, **THEN** it behaves
  identically to any other day (including a possible Bosses-defeated
  quest before the player has necessarily fought a boss) — no special
  day-1 case exists or is implied; stated explicitly rather than left to
  assumption, flagging the onboarding-UX question (a first-day Bosses
  quest may be unreachable for a brand-new player) as a UX consideration,
  not a mechanical bug.

**QA harness note (flagged by qa-lead review, not a design gap in this
GDD):** several criteria above are only exercisable with test-tooling
support that doesn't exist yet — a way to force an Anti-Cheat rejection
on demand, visibility into the analytics outbox to confirm
`quest_claimed` fired, and a way to simulate an async Tier-2 fraud flag.
These are `/qa-plan` / test-harness scope, not something this GDD can or
should resolve — carried to Open Questions so it isn't silently dropped.

## Open Questions

1. **The cross-game-obligation tension named in Player Fantasy is only
   mitigated, not resolved.** One free reroll/day (Rule 6) softens but
   doesn't eliminate the case where a player who dislikes Ricochet still
   draws a bricks/bosses quest. Is a single daily reroll enough, or does
   this need real playtest/retention data before judging it a feature
   (cross-game exposure) vs. friction (resented obligation)? *Target:
   revisit once telemetry from actual play exists.*
2. **A more adaptive mitigation (weighted draw favoring a player's
   recently-played mini-games) is deliberately deferred**, since no
   play-history system exists yet to feed it (Rule 6). Not assumed
   necessary — only worth building if Open Question 1's data shows the
   flat reroll mitigation is insufficient.
3. **Target counts and rewards (Core Rule 7 table) are a fresh balance
   decision, not a playtested one** — no prototype source existed to
   check against (Overview). Needs `/balance-check` validation once real
   session-length and per-mini-game-output telemetry exists, the same
   way `level-difficulty-config-ricochet.md`'s boss-HP cap was flagged
   for validation rather than assumed correct on first pass.
4. **QA test-harness gaps** (qa-lead review, Acceptance Criteria): a way
   to force an Anti-Cheat validation rejection on demand, visibility into
   the analytics outbox to confirm `quest_claimed` fired, and a way to
   simulate an async Tier-2 fraud flag. None of these tools currently
   exist — scope for a future `/qa-plan` pass, not this GDD.
5. **No monetization hook for Daily Quests is currently scoped** (extra
   rerolls, quest skips, etc.) even though Shop/Cosmetics/Battle Pass
   (not yet designed) will eventually exist. Given the project's explicit
   "no pay-to-win" pillar, any future quest-related IAP would need
   careful scoping (e.g. convenience-only, never a power/reward
   advantage) — not assumed either way here, flagged so it isn't
   silently introduced later without a deliberate decision.
6. **[NEW 2026-07-17] Confirmed-fraud clawback asymmetry between mascots
   and quest rewards is unresolved.** mascot-database.md's Core Rule 6
   revokes a mascot grant on a human-confirmed Tier-2 fraud finding
   (3-flags/7-days escalation outcome). This GDD's Edge Cases previously
   claimed an unconditional no-clawback guarantee for quest rewards,
   which is now corrected to acknowledge the same confirmed-fraud
   scenario is genuinely ambiguous here — not yet decided whether
   claimed quest coins/gems should also be clawed back to match, or
   whether currency is deliberately exempt (e.g., because it may already
   be spent, unlike a boolean ownership flag). This is a cross-system
   product decision (also touching Currency System and Anti-Cheat
   directly), not something this GDD alone should resolve unilaterally.
   *Target: resolve alongside Anti-Cheat's own escalation-review tooling
   design, as a single cross-system decision rather than per-GDD.*
7. **[NEW 2026-07-17] Consistency Streak data shape is undefined.**
   Mascot Database's "Consistency Streaks" milestone category needs a
   way to check "a full week of Daily Quest completion," but this GDD
   exposes no per-day completion flag or streak counter — only the
   current day's live slate. At minimum, this likely needs a persisted
   per-day boolean ("all 3 quests claimed that UTC day") plus a rolling
   count of consecutive such days, surviving rollover independently of
   the slate itself being replaced — but the exact shape (does "complete"
   mean all 3 claimed, or just reaching `Complete-Unclaimed`? does a
   missed day reset the streak to 0 or just pause it?) is a real design
   decision this GDD should make deliberately in a future revision, not
   something invented in passing during this review. *Target: resolve
   before Login Streak and Mascot Database's Consistency Streak milestones
   can be implemented.*
8. **The UTC-midnight reset boundary is a fixed, timezone-agnostic
   cutoff that may fall at an inconvenient local hour for some players**
   — the same fairness question `login-streak.md`'s own Open Questions
   raises (that GDD's CD-GDD-ALIGN review flagged that this exposure is
   shared, not Login-Streak-specific, and asked for a cross-reference
   here rather than a duplicated analysis). *Target: revisit alongside
   Login Streak's own Open Question, together, if regional retention
   data shows a pattern.*
