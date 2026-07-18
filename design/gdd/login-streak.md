# Login Streak

> **Status**: Designed
> **Author**: Abdulrahman Alenazan + Claude
> **Last Updated**: 2026-07-12
> **Implements Pillar**: Shared hub, shared economy
> **Creative Director Review (CD-GDD-ALIGN)**: APPROVED WITH CONDITIONS
> 2026-07-12 (2 conditions, both fixed same pass — C1: Open Question 5's
> UTC-midnight fairness concern was scoped as Login-Streak-specific when
> the exposure is actually shared with Daily Quests, which used the
> identical boundary with no equivalent open question; cross-referenced
> both ways — noted here and added to `daily-quests.md`'s own Open
> Questions as #6. C2: Core Rule 11/Tuning Knobs cited "(Formulas)" as
> the source of the "passive income stays below Daily Quests" design
> goal, but Formulas never stated it in prose — added an explicit
> sentence there so the citation is accurate.)
> **`/design-review` (2026-07-17)**: NEEDS REVISION — 1 blocking item
> (`streak_claimed`, proposed as new in Core Rule 12, was absent from
> analytics-event-tracking.md's own itemized server-emitted catalog —
> independent of the separate, still-open ADR-0004 formal-amendment
> question) plus 3 recommended and 1 nice-to-have item. All folded in
> below; re-review pending.

## Overview

Login Streak is a server-tracked daily counter that rewards a player for
returning on consecutive UTC calendar days: coins scale with streak length
up to a cap, a flat gem bonus fires every 7th day, and the whole counter
resets to zero if a day is missed. Unlike Daily Quests, progress here is
driven purely by login/session activity, not validated gameplay — the
system only cares that the player showed up, not what they did once
there. Like Daily Quests it shares a UTC-midnight boundary and a distinct
completion-vs-claim step, but the two loops are deliberately different in
character: Daily Quests resets clean every day with no memory of
yesterday, while Login Streak's entire premise is memory — it is carried
over from `game-concept.md` in spirit only (coins = 40 + min(streak,10)×15
capping at day 10/190 coins, +5 gems every 7th day, reset unless active
the exact previous UTC day), since no dedicated `streak.ts`-equivalent
prototype source exists to check the finer mechanics against. Several real
ambiguities in that one-line carryover — whether the streak counter itself
caps at day 10 or only the coin formula's scaling does, what "active"
means, and whether there's a manual claim step — are resolved explicitly
in this document's Core Rules, not silently assumed.

## Player Fantasy

Login Streak is the fantasy of a count that climbs the longer you show
up — not a bar filling toward a finish line, but a thread you don't want
to be the one to cut. The coins matter less than the number ticking
upward; the pull is not wanting to be the reason it resets to zero. Because
login alone advances it, regardless of which mini-game (or none) was
touched, it's the account itself — not any single game — that the player
stays loyal to, directly serving Pillar 1 (shared hub, shared economy) at
its loosest, most universal level. This is deliberately a different
register from Daily Quests: where Quests offer repeated, contained "tiny
win" closure moments, Login Streak trades on loss-aversion and memory — it
punishes absence rather than simply withholding a reward, and its
satisfaction arrives in spikes (the day-7 gem bonus) rather than
Quests' steady claim-and-done rhythm. The two loops share a UTC reset
boundary but are not redundant with each other.

## Detailed Design

### Core Rules

1. **The streak counter is uncapped.** `streakCount` increments by 1 per
   qualifying UTC day and never caps — it keeps counting past day 10
   (11, 14, 21, 28...). Only the coin formula's scaling caps at day 10
   (Rule 2); the counter itself doesn't, which is what lets the 7-day
   gem bonus (Rule 3) keep repeating indefinitely rather than silently
   stopping after day 10.
2. **Coin formula**: `coins = 40 + min(streakCount, 10) × 15` — reaches
   190 at day 10 and holds at 190 every day after.
3. **Gem bonus**: flat +2 gems whenever `streakCount % 7 == 0` (days 7,
   14, 21...), additive with that day's coins. **[Revised during design]**
   the prototype summary's carried-over value was 5 gems; economy-designer
   review found 5 would push zero-effort login income to nearly match
   Daily Quests' active-play gem average (~0.71 vs ~0.75 gems/day) and be
   5× Daily Quests' richest single reward (1 gem, its boss quest type) —
   revised to 2, which stays a real milestone payoff without threatening
   gem scarcity or undercutting the incentive to actually play.
4. **"Active" means a server-recorded Account/Auth login — not
   gameplay.** A qualifying day is any UTC calendar day on which
   Account/Auth records a successful authenticated session-start for the
   player. No mini-game play, task completion, or Anti-Cheat validation
   is required — this deliberately does not reuse Daily Quests' own
   validated-gameplay-event definition (Daily Quests Rule 2); the two
   systems are driven by different upstream signals on purpose.
5. **One qualifying event per UTC day.** Extra session-starts on an
   already-credited day do nothing further.
6. **Eligibility auto-detects on login; the reward itself does not
   auto-credit.** On login, the server compares today's UTC date against
   the stored `lastQualifyingLoginDate`. If it's new, `streakCount`
   increments immediately and a claim affordance appears in the Hub —
   but the player must still tap claim. The client sends no amount; the
   server derives coins/gems from `streakCount` (Rules 2–3) and credits
   on a server-validated claim, mirroring Daily Quests' own
   completion-vs-claim distinction.
7. **Claims don't stack.** Only the current day's reward is claimable.
   If a day passes unclaimed while the player keeps logging in
   (extending the streak further), the prior day's specific reward is
   forfeited — there is no backlog of unclaimed days to work through.
   **[Clarified during Edge Cases review]**: this means streak *length*
   (Rule 1) and actual reward *income* can fully diverge — a player who
   logs in daily but never taps claim keeps growing `streakCount`
   indefinitely while forfeiting every day's reward. This is the
   intended, consistent reading of Rules 1/6/7 together, stated
   explicitly here rather than left as an implicit consequence.
8. **A missed day resets the counter to 0, then immediately to 1 on the
   triggering login — never a bare "0" state.** If the gap since
   `lastQualifyingLoginDate` exceeds one day, `streakCount` resets to 0;
   since the login that discovers this is itself a qualifying event, the
   counter advances to 1 in the same call. **[Amended during Edge Cases
   review]**: a brand-new account's very first-ever login has no prior
   `lastQualifyingLoginDate` to diff against — that case is treated as
   automatically qualifying (bypassing the gap comparison entirely, not
   evaluating it against a null value), with `streakCount` initializing
   to 1 the same way the post-reset branch above does.
9. **No make-up mechanic in the initial scope.** A missed day is simply
   gone — no grace period, no streak-freeze item. A streak-freeze
   consumable is a plausible future live-ops idea, not in scope here —
   see Open Questions.
10. **The server is the sole timestamp authority.** `lastQualifyingLoginDate`
    (UTC date only) is set at login time — not claim time — from the
    server-received session-start event. Client-reported timestamps are
    never trusted, matching Pillar 2 (server-authoritative economy).
11. **Rewards route via Currency System's `creditFlat` leg exclusively,
    never `creditMultiplied`** — the same choice Daily Quests made and
    for the same reason: routing through the multiplied path would let a
    maxed Coin Value account earn up to 950 coins/day for zero active
    play, breaking the design goal that passive login income stays below
    Daily Quests' active-play income (Formulas). **[Clarified 2026-07-17]**
    On a gem-bonus day, coins and gems credit as **two `creditFlat` legs
    (one per currency) inside a single atomic `mutateWallet` call** —
    never two sequential calls. This is the same distinction Currency
    System's own Rule 5 had to correct for the collected-coins-plus-
    boss-bonus case (two legs, one call, not two calls): two independent
    calls here would let a gem-bonus claim credit coins while separately
    failing (or being retried) on the gem leg, leaving a non-atomic,
    partially-credited claim. One call, two legs, one idempotency key.
12. **A new `streak_claimed` analytics event is proposed here** —
    unlike Daily Quests' `quest_claimed`, no existing ADR currently
    defines this event. It follows the same server-authoritative-outbox
    pattern (ADR-0004) on successful claim. Payload: `playerId`,
    `streakCount`, `coinsGranted`, `gemsGranted`, `isGemBonusDay`,
    `claimedAtUtc`. Flagged explicitly as new, not silently assumed to
    already exist in ADR-0004's catalog — see Open Questions.

### States and Transitions

| State | Description | Enter when | Exit when |
|---|---|---|---|
| Claimed (Today) | Today's reward already collected | Successful server-validated claim | UTC rollover → Unclaimed (Eligible) |
| Unclaimed (Eligible) | A new qualifying login was detected; `streakCount` already incremented; reward waiting | Login on a new UTC day (Rule 6) | Claim tap → Claimed; or UTC rollover while still unclaimed → forfeited, re-enters Unclaimed (Eligible) at the next qualifying login |
| Broken/Reset | `streakCount` was zeroed due to a >1-day gap | Login after one or more missed days | Immediately re-enters Unclaimed (Eligible) at `streakCount = 1`, same call (Rule 8) |

### Interactions with Other Systems

- **Account/Auth** (reads) — the sole source of the session-start event
  driving streak eligibility (Rule 4); Login Streak never owns or
  duplicates auth state, only reads its login timestamps.
- **Currency System** (writes) — reward delivery via the `creditFlat`
  leg only, on a server-validated claim (Rule 6, Rule 11).
- **Coin Value upgrade** (explicitly bypassed) — Login Streak rewards
  never enter the multiplied path, preventing multiplier stacking
  (Rule 11).
- **Daily Quests** (reference only, no shared state) — shares the same
  UTC-midnight reset boundary and completion-vs-claim pattern, but no
  shared UI, event, or counter.
- **Analytics/Event Tracking** (writes) — the new `streak_claimed`
  event, via ADR-0004's outbox pattern (Rule 12).
- **Hub UI** (read by) — current streak count, Claimed/Unclaimed state,
  and the claim action; see UI Requirements.

## Formulas

The `streakReward(streakCount)` formula is defined as:

`streakReward(streakCount) = { coins: 40 + min(streakCount, 10) × 15, gems: streakCount % 7 == 0 ? 2 : 0 }`

**Variables:**
| Variable | Symbol | Type | Range | Description |
|---|---|---|---|---|
| Streak Count | `streakCount` | int | 1–∞ | UTC-day streak length at claim time (Rule 1); uncapped even though the coin term saturates |
| Coins Granted | `coins` | int | 55–190 | Flat coin credit for the claim, capped at day 10 (Rule 2) |
| Gems Granted | `gems` | int | {0, 2} | Flat gem credit, fires only on multiples of 7 (Rule 3) |

**Output Range:** Coins range 55 (day 1) to 190 (day 10+), non-decreasing
and flat past day 10. Gems are 0 on non-multiple-of-7 days, 2 on
multiple-of-7 days.

**Example:**
- `streakCount = 1`: coins = 40 + min(1,10)×15 = 55, gems = 0 (1 % 7 ≠ 0)
- `streakCount = 10`: coins = 40 + min(10,10)×15 = 190, gems = 0 (10 % 7
  ≠ 0) — cap reached
- `streakCount = 14`: coins = 40 + min(14,10)×15 = 190 (cap holds), gems
  = 2 (14 % 7 == 0) — the periodic bonus stacks atop the held cap

Both components credit via Currency System's `creditFlat` leg exclusively
(Rule 11) — never `creditMultiplied` — so the Coin Value upgrade never
applies to either term, identical to Daily Quests' own `questReward`
treatment. **[Added during CD-GDD-ALIGN review]**: this formula's 190-coin
cap is a deliberate design constraint, not an incidental number — it
keeps passive login income below Daily Quests' active-play income
(~206 coins/day average), so that requiring actual play still feels
worth more than simply opening the app (see Rule 3's revision rationale
for the equivalent gems-side reasoning). **[Added 2026-07-17]** The
accepted gem value's own ratio, pinned numerically to match the coin
side's treatment: 2 gems every 7 days averages **~0.286 gems/day**,
comfortably below Daily Quests' ~0.75 gems/day active-play average —
Rule 3's revision rationale pinned only the *rejected* 5-gem option's
ratio (~0.71/day) in prose; the accepted value's own number was never
stated until now.

## Edge Cases

- **If a login occurs on UTC calendar day N and the next occurs on day
  N+1, regardless of elapsed real time** (e.g. 23:59 UTC then 00:01 UTC,
  two minutes apart): it counts as a valid consecutive-day login and
  extends the streak. Qualification is defined by UTC calendar-date
  change alone (Rules 4, 10), not elapsed hours — a 2-minute
  midnight-crossing gap is indistinguishable from a 24-hour gap under
  this model. This is the model's intended behavior, not a loophole to
  patch.
- **If a brand-new account's very first-ever login has no prior
  `lastQualifyingLoginDate` to diff against**: treated as automatically
  qualifying, bypassing the gap comparison entirely rather than
  evaluating it against a null value (Rule 8's amendment).
- **If a guest account links to a permanent account** (same `playerId`,
  per Account/Auth's GDD): streak state carries over unchanged. It's
  keyed to `playerId`, not auth method, and account-linking is not
  itself a session-start event (Rule 4), so linking neither increments
  nor resets the streak.
- **If the same player logs in from multiple devices on one UTC day**:
  only the first session-start that day is qualifying; every later
  session-start that day, from any device, no-ops against `streakCount`
  and `lastQualifyingLoginDate` (Rule 5).
- **If a player logs in daily but never taps claim**: see Rule 7's
  clarification — `streakCount` keeps incrementing indefinitely (only a
  missed UTC day resets it, Rule 8) while every unclaimed day's specific
  reward is permanently forfeited, with no backlog. Streak length and
  actual reward income can fully diverge by design.
- **If a claim is double-tapped or double-submitted**: the claim is
  atomic per (`playerId`, current qualifying day) — the losing
  concurrent request finds the day already claimed and is rejected as a
  no-op, never double-credited. Same resolution pattern Daily Quests
  uses for its own claim atomicity.
- **If `streakCount` resets (Rule 8) while a prior day's reward was
  still `Unclaimed (Eligible)`**: the reset clears the dangling
  unclaimed state along with it — the pre-break reward is not carried
  forward as claimable; only the new post-reset day-1 reward becomes
  claimable.

## Dependencies

- **Depends on** (hard): Account/Auth (session-start events are the sole
  eligibility signal, Rule 4); Currency System (reward credit on claim
  via `creditFlat`, Rule 6/11); Analytics/Event Tracking (`streak_claimed`
  via ADR-0004's server-authoritative outbox, Rule 12); Save/Persistence
  (durable streak state).
- **Depended on by**: none yet formal.
- **Reference only, no shared state**: Daily Quests (same UTC-midnight
  reset cadence, for consistency).

**Consistency check**: Account/Auth's GDD says "Depended on by (hard, all
of them): ... Login Streak" — matches. Save/Persistence's GDD says
"Depended on by (hard, all of them): ... Login Streak" — matches.
Currency System's GDD says "Depended on by (hard): ... Login Streak" —
matches. Unlike Currency Ledger and Daily Quests, no one-directional
dependency gap was found this pass — every reciprocal link already
existed correctly in the dependency GDDs.

## Tuning Knobs

| Knob | Suggested Value | Too Low | Too High |
|---|---|---|---|
| Coin formula (base + per-day, cap day) | base 40, +15/day, caps at day 10 = 190 | Return incentive feels negligible, doesn't motivate a daily check-in | Passive login income rivals or exceeds Daily Quests' active-play income, undermining the "streak income ≤ Daily Quests" effort hierarchy (Formulas) |
| Gem bonus (amount + interval) | 2 gems every 7th day | The 7-day milestone feels unrewarded, undercutting the "spike" satisfaction (Player Fantasy) | Rivals Daily Quests' active-play gem income for zero effort — the exact concern that revised this value from the carried-over 5 down to 2 |
| Cap day | 10 | Ramp-up feels rushed; early streak growth outpaces any meaningful reward increase | New players wait too long to reach the "full" daily reward, weakening the early-retention hook |

## Visual/Audio Requirements

**Claim, non-bonus day**: the streak *number* is the hero, not the
reward. On claim, the counter digit does a quick spring-scale pop with a
warm Bill Gold glint — smaller and quieter than a Daily Quests reward
burst. Coins/gems slide into their HUD chips with minimal fanfare; no
character illustration.

**Gem-bonus day (7/14/21...)**: same counter-pop, but escalated — a
full-screen warm light sweep (Amethyst Purple gem-glint accent), a
distinct milestone chime, and this is where a character reaction earns
its place (mascot celebratory pose), marking it as the "spike" Player
Fantasy calls for.

**Streak ticking up**: its own beat, separate from claim — fires on login
the instant eligibility is detected (Rule 6), before any tap. Treated
like a thread/flame visually extending by one link/lick, distinct from
the claim's reward-delivery animation.

**Broken/Reset**: the hard case. No shatter, snap, or Brick Red flash —
that reads grim, violating the art bible's "warm, never grim" principle.
Instead: the old number embers down (warm Marquee Orange dim-and-fade,
not gray/desaturated) before bouncing right back up to "1" with the same
spring-pop as a normal tick — framing it as "the thread starts again,"
not "you broke it."

**Differentiation from Daily Quests**: Quests is character-first and
closure-framed (mascot reacts, burst, done). Streak is number-first and
continuity-framed (counter/thread motif, character mostly reserved for
bonus spikes) — a different focal object, not just a different palette,
so the two loops don't read as reskins of each other.

**Art bible fit**: the tactility/pressability, warm/saturated-not-grim,
and animation-feel principles all apply cleanly; the colorblind-safety
rule applies if any red appears in the Reset treatment (must pair with a
non-color icon).

*Honestly flagged*: the art bible's mood table has no row for Streak
Claim or Broken/Reset, and its three character-first VFX triggers
(mini-game entry, quest complete, mascot unlock) don't list streak
events — the bonus-day character-reaction call above is this document's
own precedent, not yet bible-sanctioned.

## UI Requirements

Login Streak renders as a compact component in the Hub — hub-ui.md's own
Interactions section already lists Login Streak as data rendered via
Shared Hub's aggregation, but does not yet name a dedicated component
(matching Daily Quests' own, since-corrected UI Requirements wording, not
an "already-reserved slot"). It shows: the current streak-day number, a
claim button when `Unclaimed (Eligible)`, a distinct visual indicator on
gem-bonus days (every 7th day, Core Rule 3), and a distinct
reset-acknowledgment treatment after `Broken/Reset` (Visual/Audio
Requirements). Detailed layout, spacing, and interaction map belong in
`/ux-design`, not this GDD — this section only establishes the
component's required content and states.

## Acceptance Criteria

- **GIVEN** `streakCount`=10, **WHEN** the player logs in the next
  consecutive UTC day, **THEN** `streakCount`=11 (the counter keeps
  counting, never caps).
- **GIVEN** `streakCount`=10 and, separately, `streakCount`=15, **WHEN**
  each is claimed, **THEN** `coinsGranted`=190 in both cases.
- **GIVEN** `streakCount`=14, **WHEN** claimed, **THEN** `gemsGranted`=2
  and `coinsGranted`=190 (the bonus stacks on the held cap). **GIVEN**
  `streakCount`=8, **WHEN** claimed, **THEN** `gemsGranted`=0.
- **GIVEN** the player has completed zero mini-game runs today, **WHEN**
  Account/Auth records a successful login, **THEN** `streakCount`
  increments — no gameplay or Anti-Cheat event is required.
- **GIVEN** today's login already incremented `streakCount`, **WHEN**
  the player logs in again the same UTC day (any device), **THEN**
  `streakCount` and `lastQualifyingLoginDate` are unchanged.
- **GIVEN** a new qualifying login, **WHEN** the server processes it,
  **THEN** `streakCount` increments and a claim affordance appears
  immediately, but coins/gems are not credited until the player taps
  claim.
- **GIVEN** an `Unclaimed (Eligible)` day-3 reward, **WHEN** the player
  logs in on day 4 without claiming day 3, **THEN** the day-3 reward is
  permanently forfeited and only day 4's reward (`streakCount`=4) is
  claimable.
- **GIVEN** `lastQualifyingLoginDate` is 2+ UTC days ago, **WHEN** the
  player logs in, **THEN** `streakCount` resets to 0 then immediately to
  1 in the same call. **GIVEN** a brand-new account with no
  `lastQualifyingLoginDate`, **WHEN** the first login occurs, **THEN**
  it auto-qualifies (the gap check is bypassed entirely) with
  `streakCount`=1.
- **GIVEN** a streak day reaches `Unclaimed (Eligible)` and is never
  claimed before UTC rollover, **WHEN** rollover occurs, **THEN** that
  day's reward is forfeited permanently
  (no grace period or make-up path exists), and the state re-enters
  `Unclaimed (Eligible)` fresh only at the player's next qualifying
  login — never automatically.
- **GIVEN** a login request with a client-reported timestamp that
  disagrees with server time, **WHEN** the server processes it, **THEN**
  `lastQualifyingLoginDate` is set from server-received time only.
- **GIVEN** a player with a maxed Coin Value upgrade, **WHEN** claiming
  at `streakCount`≥10, **THEN** `coinsGranted`=190 unmultiplied — never
  scaled by the upgrade.
- **GIVEN** a successful claim, **WHEN** it completes, **THEN** a
  `streak_claimed` event fires with `playerId`, `streakCount`,
  `coinsGranted`, `gemsGranted`, `isGemBonusDay`, `claimedAtUtc`.
- **GIVEN** a login at 23:59 UTC on day N, **WHEN** the next login
  occurs at 00:01 UTC on day N+1, **THEN** it counts as a valid
  consecutive-day login regardless of the 2-minute real-time gap.
- **GIVEN** a guest account with `streakCount`=5, **WHEN** it links to a
  permanent account (same `playerId`), **THEN** `streakCount` remains 5.
- **GIVEN** 5 consecutive daily logins with no claims tapped, **WHEN**
  checked after day 5, **THEN** `streakCount`=5 but total credited
  coins/gems across those days = 0.
- **GIVEN** two concurrent claim requests for the same `playerId` and
  day, **WHEN** both submit, **THEN** exactly one succeeds and the other
  is rejected as already-claimed, with zero additional credit.
- **GIVEN** an `Unclaimed (Eligible)` day-6 reward, **WHEN** a missed
  day resets `streakCount` to 1, **THEN** the day-6 reward is no longer
  claimable under any circumstance.
- **[NEW 2026-07-17] GIVEN** a streak was building toward a day-7
  gem-bonus milestone but a gap is detected first, **WHEN** the
  triggering login resets `streakCount` to 1 (Rule 8), **THEN**
  `gemsGranted`=0 on claim — the gem check evaluates purely against the
  post-reset `streakCount` value (`1 % 7 ≠ 0`), never against any
  separate "days since last bonus" state that could accidentally survive
  the reset.
- **[NEW 2026-07-17] GIVEN** a qualifying login today that the player
  does not immediately claim, **WHEN** the app is closed and reopened
  later the same UTC day, **THEN** the claim affordance still shows as
  `Unclaimed (Eligible)` with the same `streakCount` — reopening the app
  does not re-trigger a second qualifying event or alter state.
- **[NEW 2026-07-17] GIVEN** a gem-bonus-day claim (`streakCount` a
  multiple of 7), **WHEN** the claim transaction executes, **THEN**
  coins and gems credit as two legs of one atomic `mutateWallet` call —
  verified by injecting a failure on one leg and confirming neither
  currency is credited (all-or-nothing), never a partial credit from two
  independent calls.

## Open Questions

1. **No streak-freeze or make-up mechanic exists in the initial scope**
   (Rule 9) — a missed day is simply gone. A streak-freeze consumable
   (spend a currency or IAP item to protect one missed day) is a
   plausible future live-ops idea, deliberately not designed here. Given
   the project's "no pay-to-win" pillar, any future version would need
   scoping as convenience-only. *Target: revisit post-launch if
   retention data shows streak breaks are a significant churn driver.*
2. **Hub UI does not yet name a dedicated component for Login Streak
   (or Daily Quests)** — both currently render only via Shared Hub's
   generic aggregation per hub-ui.md's Interactions section, not a named
   slot. *Target: resolve during `/ux-design` for the Hub screen.*
3. **The `streakReward` formula's values (base 40, +15/day, cap day 10,
   2 gems/7 days) are a fresh balance decision, not a playtested one** —
   same caveat as Daily Quests' target counts, since no verified
   prototype source exists. Needs `/balance-check` once real
   retention/session telemetry exists.
4. **[RESOLVED 2026-07-18, see ADR-0006 §5 event-ownership split + ADR-0004 §4
   annotation]** `streak_claimed` (Core Rule 12) is now formally in the
   server-authoritative outbox catalog. Because the claim credits currency via a
   `creditFlat` leg (Rule 11), the event rides the **same `mutateWallet`
   transaction** (ADR-0004), written to the shared `analytics_outbox` table
   exactly-once keyed by `op_id` — identical mechanism to `quest_claimed`, no new
   machinery. Confirmed server-emitted, never client-emitted (registry
   client-emission-ban updated to include it).
5. **UTC midnight is a fixed, timezone-agnostic reset boundary that may
   fall at an inconvenient local hour for some players** (e.g., mid-sleep
   in some regions), making "did you log in yesterday" feel arbitrarily
   harder to satisfy depending on where a player lives. Not resolved
   here — flagged rather than silently accepted, since it's a genuine
   fairness question a global mobile audience will eventually surface.
   **[Noted during CD-GDD-ALIGN review]**: this is not a Login-Streak-
   specific risk — Daily Quests uses the identical UTC-midnight boundary
   and carries the exact same exposure; that GDD's own Open Questions
   has been cross-referenced to point back here rather than duplicating
   the analysis. *Target: revisit if regional retention data shows a
   pattern, for both systems together.*
