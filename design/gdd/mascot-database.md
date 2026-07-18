# Mascot Database + Rarity Logic

> **Status**: Designed
> **Author**: Abdulrahman Alenazan + Claude
> **Last Updated**: 2026-07-11
> **Implements Pillar**: Collectible mascots
> **Creative Director Review (CD-GDD-ALIGN)**: APPROVED WITH CONDITIONS
> 2026-07-11 (1 condition — Player Fantasy's "mascots will eventually be
> gem/real-money-priced" was ambiguous enough to read as contradicting
> Core Rule 3's deterministic-only acquisition; fixed same pass to say
> "mascot cosmetic skins," matching game-concept.md's actual Shop scope)
> **`/design-review` (2026-07-17)**: NEEDS REVISION — 2 blocking items
> (the milestone-category counts summed to exactly 16, the full MVP
> roster, leaving zero slots for Capstone despite Capstone being the sole
> Epic-tier source and required by this GDD's own Acceptance Criteria;
> no validation catches two *different* mascots accidentally claiming the
> same `unlockCondition`, only the narrower within-one-mascot case) plus
> 4 recommended items. All folded in below; re-review pending.
> **Confirmed clean**: the composed-transaction/shared-idempotency-key
> claim in Core Rule 5 was independently verified against ADR-0005 and
> found fully supported — not a gap.

## Overview

Mascot Database + Rarity Logic owns two things: the static roster of
collectible mascot characters (name, rarity tier, visual identity), and
each player's ownership record of which ones they've acquired. It is the
system behind Pillar 3, "Collectible mascots" — the master prompt's
headline new pillar, and a genuinely greenfield addition since the
prototype's data model has no character ID or cosmetic field anywhere in
it, just one hardcoded duck emoji.

On the data side, per-player ownership lives inside Save/Persistence's
JSONB `player_state` blob (per ADR-0005's existing scope, which already
names mascots alongside progress/quests) — this system doesn't own its own
database table for ownership, it owns the *shape* of that data and the
*logic* that mutates it. The static roster (which mascots exist, their
rarity, their assets) is content data, versioned alongside the game build,
not per-player state.

On the player side, this system is the payoff moment: every acquisition —
a level-up drop, a boss-kill reward, a shop pull — routes through this
system's rarity resolution before a player sees what they got.
`hub-ui.md` already committed to surfacing collection progress prominently
(right after the Games section); this GDD defines what actually powers
that.

## Player Fantasy

Direct engagement. Collecting mascots is making friends, not pulling loot
— this game deliberately avoids gacha language and mechanics (no spinning
wheel, no "3-2-1 reveal" tension, no "pull/roll/summon"). A new mascot's
reveal is a greeting beat: you meet them, they join your crew. The
collection screen itself is framed as a scrapbook — the payoff isn't any
single acquisition spiking hard, it's watching a page of blank, labeled
slots gradually fill in. That framing also does real design work: a new
player's empty gallery reads as an inviting checklist waiting to be
filled, not a void, which is exactly the "start collecting" state
`hub-ui.md` already committed to — the fantasy and the edge case solve
each other.

Rarity exists (per Pillar 3) but is explicitly **flair, not power** — a
Rare or Epic mascot is a bigger showstopper to look at and show off,
never a mechanical advantage in any mini-game. This is a hard line, not a
nuance: blurring it toward "rarity = stats" would directly threaten the
game's stated "no pay-to-win" pillar, since mascot **cosmetic skins**
(not mascots themselves — ownership stays 100% deterministic per Core
Rule 3, with no purchase-based acquisition path anywhere in this system)
will eventually be gem/real-money-priced per the Shop's planned
expansion.

## Detailed Design

### Core Rules

1. **Three rarity tiers**: Common, Rare, Epic — matching the art bible's
   existing 2-color visual language exactly (Fern Green = Common; Amethyst
   Purple = Rare and Epic, differentiated from each other by star-count
   and border-thickness, never by color alone, per the art bible's own
   colorblind-safety rule). No fourth tier — the art direction doesn't
   support one without a new color, and this system's MVP scope doesn't
   need the extra granularity yet.

2. **MVP roster: 16 mascots.** All ducks — outfit/prop silhouette
   variations on one cohesive cast, per the art bible, not distinct
   creature species. Decomposed across milestone categories below. This
   number is scoped to what one designed mini-game (Super Ricochet) can
   actually support; expanding it is explicitly tied to future mini-games
   shipping, not a number to inflate speculatively now.

3. **Acquisition is 100% deterministic — zero randomness anywhere.** Every
   mascot has one or more named `unlockCondition`s referencing stable
   content IDs (e.g. `bossId: 'boss_003'`), never mutable display strings
   or names, so a future rename never orphans a grant. There is no pull,
   roll, or weighted table of any kind, consistent with the Player
   Fantasy's explicit rejection of gacha mechanics.

4. **Milestone taxonomy** (5 categories, tier mapped to effort/horizon,
   never to luck):
   - **First Contact** (Common) — first-time defeat of each of the 6
     named Super Ricochet bosses. 6 mascots.
   - **Progression Gates** (Common→Rare) — level-number thresholds,
     pegged to `level-difficulty-config-ricochet.md`'s existing
     difficulty bands so Rare-tier ones require surviving past the harder
     curve, not just more taps. ~4 mascots.
   - **Mastery Repeats** (Rare) — defeating a specific boss again at a
     materially higher cycled-HP band. The only "hard" axis available
     without randomness. **[Corrected 2026-07-17]** ~2 mascots (was ~3) —
     see below.
   - **Consistency Streaks** (Common→Rare) — a full week of Daily Quest
     completion; Login Streak's existing day-10 cap and every-7th-day gem
     checkpoints double as natural unlock markers. ~3 mascots.
   - **Capstone** (Epic) — long-horizon completion only (e.g. "defeat all
     6 bosses at their hardest cycled HP," or "own every Common-tier
     mascot"). Reserved for genuine completionist payoff, never a single
     event. **[NEW 2026-07-17]** ~1 mascot — the sole Epic-tier source.

   **[Corrected 2026-07-17]** The original counts (6+4+3+3=16) summed to
   the *entire* MVP roster, leaving **zero** slots for Capstone — despite
   Capstone being the only category that produces an Epic-tier mascot, and
   this GDD's own Acceptance Criteria requiring at least one Epic mascot
   to exist (the Rare-vs-Epic color-consistency test). Mastery Repeats is
   reduced from ~3 to ~2 to make room: 6+4+2+3+1=16. This is still a
   placeholder distribution pending `/balance-check` (per Open Question
   4), but it must at minimum be internally consistent — summing to
   exactly the roster size *while* leaving Capstone non-empty — which the
   original numbers did not.

5. **Grants are server-authoritative, riding the existing reward
   pipeline — no new anti-cheat mechanism.** A milestone is evaluated
   against the same Tier-1-computed, trusted run outcome (ADR-0007) that
   already governs that run's currency reward — never against a
   client-reported claim. The grant mutation composes into the **same
   transaction** as that run's `mutateWallet` call (ADR-0004), gated on
   the same run-ID idempotency key, and uses Save/Persistence's locked
   `updatePlayer` JSONB mutator (ADR-0005) — mascots are not money, so
   they never touch `mutateWallet` itself. On a replayed/retried run
   submission, the idempotency guard short-circuits both the wallet legs
   and the mascot grant together, not separately. **[Verified 2026-07-17]**
   Independently checked against ADR-0005 §2's composed-operation
   mechanism — this claim is fully supported, not aspirational. Two
   nuances worth stating explicitly, since an implementer working from
   this GDD alone wouldn't otherwise see them: **(a)** any composed
   operation spanning both chokepoints must still acquire locks in
   ADR-0005's canonical order (`player_state` before `wallet`, per
   save-persistence.md Core Rule 4) to avoid the ABBA deadlock both ADRs
   warn about — this GDD's mascot grant is exactly such a composed
   operation, and previously never said so. **(b)** the shared
   idempotency anchor lives entirely in `currency_op` (written only by
   `mutateWallet`), so this mechanism implicitly assumes every
   mascot-granting run also produces a non-empty currency credit to anchor
   on — true for every milestone category today (all route through a run
   that credits at least some reward), but if a future grant path were
   ever added with zero currency reward, it would have no idempotency
   anchor at all. Flagged so this invariant isn't silently assumed forever.

6. **Ownership is one-way, monotonic set membership, with exactly one
   revocation path.** Once granted, a mascot is never revoked or
   re-validated for design/balance reasons — if a milestone's underlying
   condition is later rebalanced (e.g. a boss's HP curve changes via
   `/balance-check`), players who already own that mascot keep it, exactly
   matching Currency System's own posture of never retroactively
   re-debiting a past spend. The **sole exception** is a confirmed Tier-2
   fraud finding (the existing 3-flags/7-days human review outcome, per
   Anti-Cheat/Replay Verification) — never an automated or algorithmic
   revocation, only that specific human-adjudicated path.

7. **A mascot may have more than one `unlockCondition` (OR-semantics), by
   design** — multiple paths to the same mascot is legitimate given the
   "no gacha frustration" fantasy, and it's nearly free since grant logic
   already short-circuits on "already owned" before evaluating any
   condition. To catch *accidental* collisions (two unrelated milestones
   pointing at the same mascot by mistake), every multi-condition mascot
   must carry an explicit `intentional: true` flag; an unflagged
   duplicate is a data error, not a feature. **[NEW 2026-07-17]** Roster
   validation must also catch the inverse, cross-mascot case: **two
   different mascots must never claim the same `unlockCondition`** — this
   creates an ambiguous grant (which mascot does the trigger actually
   unlock?) that Rule 7's within-one-mascot check does not catch, since it
   only validates duplicates *inside* a single mascot's own condition list.
   Roster validation must scan the full condition set across all 16
   mascots for any `unlockCondition` value appearing under more than one
   mascot ID, with no `intentional`-style override — this specific
   collision has no legitimate use case, unlike the OR-semantics case
   above.

### States and Transitions

| State | Trigger | Result |
|---|---|---|
| Locked | Mascot exists in roster, condition not yet met | Shown in collection UI as a labeled silhouette (per `hub-ui.md`'s empty/locked-state pattern) — the *condition* is visible, the *mascot itself* is not, preserving discovery without touching acquisition odds |
| Locked → Unlocked | Server evaluates a Tier-1-trusted run outcome against the roster's unlock conditions and finds a match | Mascot added to `player_state.data.mascots` in the same transaction as that run's reward credit; `mascot_unlocked` event fires |
| Unlocked | (terminal — no reverse transition) | Permanent regardless of later balance changes to the triggering condition (Core Rule 6) |

### Interactions with Other Systems

- **Save/Persistence**: owns the actual storage — per-player ownership
  lives in `player_state.data.mascots` (JSONB), mutated only through the
  locked `updatePlayer` chokepoint. This system owns the *shape* of that
  data and the *unlock-condition evaluation logic*, not a database table
  of its own.
- **Anti-Cheat/Replay Verification**: mascot grants are evaluated against
  the same Tier-1 trusted outcome that governs currency for a run — no
  separate verification path. A Tier-2 replay mismatch on a
  mascot-granting run routes that mascot's legitimacy through the
  existing 3-flags/7-days human review queue (ADR-0007) rather than a
  bespoke mechanism — because even with zero mechanical effect, the
  collection screen is a reputational/profile-integrity surface (the same
  class of concern ADR-0007 already named for leaderboards), not just a
  competitive-balance one.
- **Currency System**: no direct dependency, but the grant mutation
  composes into the *same transaction* as that run's `mutateWallet` call,
  sharing its idempotency key (Core Rule 5).
- **Super Ricochet / Boss AI/Damage Model**: boss-defeat and
  level-threshold milestones read their trigger data from these systems'
  run-outcome fields — a soft read dependency, not a structural one.
- **Daily Quests, Login Streak** (neither has a GDD yet): Consistency
  Streak milestones will read completion/streak-day data from these once
  they're designed — flagged as a provisional dependency, not blocking,
  since the specific data shape isn't committed yet.
- **Hub UI**: consumes this system's roster + ownership data to render
  the collection preview and (future) Mascot Collection screen — already
  committed in `hub-ui.md`.

## Formulas

**No probability formula exists in this system, by design.** Unlike a
typical "Rarity Logic" system, acquisition has no weighted-roll math —
every check is a boolean condition (`met` / `not met`) against a trusted
run outcome. The formulas below are the only real calculations this
system performs.

**`collection_completion_percent`**:
`collection_completion_percent = (owned_count / total_roster_count) × 100`

| Variable | Type | Range | Description |
|---|---|---|---|
| owned_count | int | 0–16 | Player's current unlocked-mascot count |
| total_roster_count | int | 16 (MVP) | Total mascots in the live roster |

Output Range: 0–100%. Example: 4 owned of 16 → 25%.

**`progression_gate_level`** (lookup, not a continuous curve): Progression
Gate milestones fire at fixed level numbers, chosen to straddle
`level-difficulty-config-ricochet.md`'s existing difficulty bands rather
than invent new ones:

| Tier | Level Threshold | Rationale |
|---|---|---|
| Common | 3, 6 | Early, well before any difficulty cap — reachable in a first sitting or two |
| Rare | 15, 20 | Past the `spawnDensity` cap (level 11) but before the `bossHp` cap (level 30) — the genuinely harder, still-escalating band |

**`mastery_repeat_gap`**: a Mastery Repeat milestone requires defeating
the *same* boss again at least `+12` levels after the first defeat
(`repeat_level ≥ first_defeat_level + 12`). Since bosses cycle on
`level % 6` (per `level-difficulty-config-ricochet.md`), +12 guarantees
exactly 2 full cycles have passed, so `boss_hp` has grown substantially
(per that GDD's own formula) — not just "one level later, technically
true."

These specific numbers (3/6/15/20, +12) are placeholders pending
`/balance-check` telemetry once real play data exists — flagged as
tunable in the Tuning Knobs section below, not frozen here.

## Edge Cases

- **If a guest's device is lost or reinstalled before linking an
  account**: mascot ownership is wiped along with the rest of
  `player_state`, per `account-auth.md`'s existing guest-loss risk — this
  system adds no new persistence gap. Worth stating explicitly: the wiped
  state renders identically to a true new player's Locked-silhouette
  gallery (Player Fantasy's "inviting checklist" framing), so the loss is
  invisible to the player rather than surfaced. This is a known
  consequence of an already-flagged risk, not a new one.
- **If a new mascot is added to the live roster post-launch**:
  `total_roster_count` increases and a player previously at 100%
  completion drops below it with zero action taken on their part. This is
  intended (the roster is meant to grow), but must be surfaced in-product
  as "new mascot available," never silently — otherwise a completionist
  reads the drop as a bug.
- **If a mascot is deprecated/removed from the live roster**: it is
  **kept, grandfathered, in any owning player's collection** (Core Rule
  6's one-way ownership still applies), but is **excluded from
  `total_roster_count` and from the `collection_completion_percent`
  calculation entirely** — both numerator and denominator only count
  mascots currently in the live roster. This prevents the
  >100%-completion case a naive implementation would hit.
- **Deprecating a mascot always retires its `unlockCondition`(s) in the
  same content update.** A deprecated mascot must never remain acquirable
  by new players while invisible in the collection UI.
- **If a single run satisfies more than one `unlockCondition` at once**
  (e.g. a first boss kill and a level-threshold crossing in the same
  submission): **all matching conditions are evaluated, and all matching
  mascots are granted in the same transaction** — not first-match-only.
  The client queues multiple `mascot_unlocked` reveals rather than
  showing only one (a future Ricochet HUD/result-flow UX concern, not
  resolved here).
- **Capstone conditions are evaluated in a second pass, after all other
  grants from that run are applied within the same transaction.** This
  means a run that both completes a Capstone's prerequisite (e.g. the
  last Common-tier mascot) and would otherwise satisfy the Capstone
  condition grants *both* in one run, rather than requiring a follow-up
  run to notice the newly-completed prerequisite.
- **Consistency Streak milestones inherit Login Streak's exact
  day-boundary definition** ("active on the exact previous UTC calendar
  day") once that GDD exists — not a locally-defined "day." Stated now as
  a forward contract so Login Streak's author doesn't have to reconcile a
  second, possibly divergent definition later.
- **If a confirmed Tier-2 fraud finding implicates a run that granted a
  mascot**: that mascot is revoked via the human-adjudicated 3-flags/
  7-days review outcome — the sole exception to Core Rule 6's otherwise
  permanent ownership, stated there explicitly to resolve what was
  originally a real contradiction between that rule and this system's
  Anti-Cheat interaction.

## Dependencies

**Depends on (hard)**: Save/Persistence (owns the `player_state` storage
this system writes into via `updatePlayer`); Anti-Cheat/Replay
Verification (every grant rides Tier-1's trusted run outcome — this
system has no independent verification path).

**Depends on (soft — reads trigger data, doesn't require it to exist to
define the roster)**: Super Ricochet and Boss AI/Damage Model (First
Contact and Mastery Repeats milestones read boss-defeat/level data from
these); Currency System (no data dependency, but shares its
transaction/idempotency pattern per Core Rule 5).

**Depends on (soft, provisional — no GDD exists yet)**: Daily Quests and
Login Streak. Consistency Streak milestones need these systems'
completion/streak-day data, but the exact interface isn't committed since
neither system is designed. Flagged, not blocking — this GDD's milestone
taxonomy names the category without hard-wiring to an interface that
doesn't exist yet.

**Depended on by**: Hub UI (already consumes this system's roster +
ownership data for the collection preview, per `hub-ui.md`'s existing
Core Rule 2); the future Mascot Gallery/Equip UI screen (not yet
designed — `hub-ui.md`'s own UI Requirements already named it as needed).

## Tuning Knobs

| Knob | Value | Too Low | Too High |
|---|---|---|---|
| Progression Gate levels (Common) | 3, 6 | Unlocks feel trivial, no sense of reaching for them | Delays the first non-boss mascot too long, early-game gallery feels stuck |
| Progression Gate levels (Rare) | 15, 20 | Rare tier stops feeling rare | Pushes Rare mascots past where most players realistically reach, wasting the tier |
| Mastery Repeat gap | +12 levels (2 boss cycles) | Barely distinguishable from a fresh First Contact kill | Feels punishing/grindy given only one boss roster exists to repeat against |
| MVP roster size | 16 mascots | Collection pillar feels thin against the game's own headline pitch | Outpaces available milestone sources (only 1 mini-game), padding with trivial unlocks per the economy-designer's flagged retention risk |
| Milestone spacing | ~1 unlock per 3–5 levels (implied by the level numbers above) | Long dead stretches between acquisitions | Every level feels like a reward dump, cheapens each individual unlock |

All five are explicitly placeholder pending `/balance-check` telemetry
once real play data exists (stated already in Formulas) — none are frozen
design intent, they're the specific numbers this GDD commits to *for
now* so the system is buildable.

## Visual/Audio Requirements

**Reveal beat**: fires on **Hub return, sequenced after the run's
currency/reward tally resolves** — not mid-run, not simultaneous with it.
Mid-run states are tense/kinetic per the art bible's own Key Moments
table; the warm "greeting" beat belongs in the Hub, the mascot's home. A
chunky rounded card slides up from the bottom carrying the mascot's
full-detail illustration (temporarily promoted to hero-LOD for this one
beat) in a greeting/wave pose — fully visible on arrival, no unveiling
mechanic. Rarity border and star count are visible on the card
immediately. One Bill Gold highlight-glint accent behind the duck
(matching the existing Reward/Quest Claim treatment's "quick spike," not
a full-screen flash). Copy reads as introduction ("Barnaby joined the
Crew"), never acquisition ("You got Barnaby"). Audio: a short 2-note warm
chime plus an optional character quack vocalization — explicitly no
drumroll, no rising pitch, no pre-beat pause.

**Locked collection slot**: a readable, full silhouette (not blank) in a
muted warm-neutral fill — deliberately *not* the art bible's desaturated
loss-state treatment, since this is anticipation, not loss. The rarity
border shows through dimmed but present, correct star count — hiding
tier would fight the Scrapbook framing established in Player Fantasy.
Tapping a locked slot pops the unlock condition via the standard
spring-ease tooltip, with a soft "page-flip" sound — reinforces "sealed
scrapbook page," not "locked chest."

**Rarity feedback at unlock**: structurally identical beat across all
three tiers — no tier gets a longer runway, a delay, or a countdown,
since that's precisely where gacha-style tension would creep back in
despite the Player Fantasy's explicit rejection of it. Epic gets
*additive* polish only (denser border sparkle, a richer 3-note chime vs.
Common's 2-note), never a different *structure*.

**Production flag for `/asset-spec`**: 16 mascots fit within the art
bible's 2-material-slot budget (body + outfit) via a shared base rig with
a swappable outfit layer — not 16 separate character models. Open
question, deferred to `/asset-spec`: whether the reveal card's
temporarily-higher-resolution hero art is a separately authored asset or
a runtime upscale of the standard gallery asset.

📌 **Asset Spec** — once this section lands, run
`/asset-spec system:mascot-database` after the art bible's already-
approved status is confirmed current, to produce per-mascot visual specs
and generation prompts.

## UI Requirements

Full screen/surface inventory: **Mascot Collection screen** (new —
gallery/grid of all 16 roster slots, locked silhouettes and unlocked full
art per Visual/Audio Requirements, rarity border/star indicators on every
slot regardless of lock state); the **Hub's mascot preview** (already
specified in `hub-ui.md`'s Core Rule 2 — owned/total count + rarity
highlights, "View Collection" CTA); the **reveal card** (Visual/Audio
Requirements above). Detailed screen-by-screen UX spec belongs in
`/ux-design`, not this GDD — this section only defines what data and
states that spec needs to design against.

📌 **UX Flag — Mascot Database + Rarity Logic**: this system has real UI
requirements (the Mascot Collection screen is new, not yet spec'd). In
Pre-Production, run `/ux-design` for it before writing epics/stories —
stories referencing this UI should cite that future
`design/ux/mascot-collection.md`, not this GDD directly.

## Acceptance Criteria

- **GIVEN** a Rare and an Epic mascot rendered side-by-side, **WHEN**
  compared, **THEN** both show identical Amethyst Purple hex,
  distinguished only by star-count/border-thickness — no hue difference.
- **GIVEN** the live MVP roster, **WHEN** queried, **THEN** exactly 16
  entries exist, each with exactly one tier in {Common, Rare, Epic}.
- **GIVEN** a player meeting an `unlockCondition`, **WHEN** evaluated 100×
  against the same trusted run outcome, **THEN** the grant result is
  identical every time — no RNG call anywhere in the path.
- **GIVEN** the 16-mascot roster, **WHEN** grouped by milestone category,
  **THEN** exactly 6 are First Contact and the remaining 10 map to
  Progression Gates/Mastery Repeats/Consistency Streaks/Capstone with
  zero uncategorized mascots.
- **GIVEN** a run granting a mascot, **WHEN** the run-ID is resubmitted
  (retry), **THEN** the mascot grant is not duplicated and remains gated
  by the same idempotency key as the wallet credit.
- **GIVEN** an owned mascot whose `unlockCondition` is later rebalanced,
  **WHEN** the rebalance ships, **THEN** the player's ownership is
  unchanged.
- **GIVEN** a mascot with 2 `unlockCondition`s both `intentional:true`,
  **WHEN** either fires, **THEN** it grants once. **GIVEN** 2 conditions
  without the flag, **WHEN** roster validation runs, **THEN** it fails as
  a data error pre-ship.
- **[NEW 2026-07-17] GIVEN** two different mascots (e.g. mascot A and
  mascot B) both accidentally configured with the identical
  `unlockCondition` value, **WHEN** roster validation runs, **THEN** it
  fails as a data error pre-ship — regardless of any `intentional` flag,
  since this specific cross-mascot collision has no legitimate use case
  (distinct from the within-one-mascot OR-semantics case, which is
  legitimate when flagged).
- **[NEW 2026-07-17] GIVEN** boss_003 first defeated at level 3 (First
  Contact), **WHEN** boss_003 is defeated again at level 9 (the same boss
  identity, second `level % 6` cycle), **THEN** First Contact does not
  re-fire and no duplicate grant/event occurs — mirroring the same
  cycle-awareness already tested for Mastery Repeat.
- **[NEW 2026-07-17] GIVEN** a not-yet-owned Rare-tier mascot's locked
  collection slot, **WHEN** rendered, **THEN** it shows the correct
  dimmed rarity border and correct star count for that mascot's actual
  tier — verifying the locked-state visual is tier-accurate, not just the
  post-unlock reveal card, which was the only state previously tested.
- **[NEW 2026-07-17] GIVEN** a composed operation granting both a
  currency credit and a mascot in one run, **WHEN** the operation
  executes, **THEN** `player_state` is locked before `wallet`, per
  ADR-0005's canonical order — mirroring ADR-0005's own "composed-op
  deadlock test" for this specific caller.
- **GIVEN** owned_count=4, total_roster_count=16, **WHEN** computed,
  **THEN** `collection_completion_percent` == 25 exactly.
- **GIVEN** a player reaching level 15, **WHEN** evaluated, **THEN** the
  Rare level-15 gate fires and the Common gates (3, 6) are not
  re-triggered.
- **GIVEN** boss_003 first defeated at level 10, **WHEN** re-defeated at
  level 21, **THEN** Mastery Repeat does NOT fire (21 < 22). **WHEN**
  re-defeated at level 22, **THEN** it fires.
- **GIVEN** a granted mascot whose run is later confirmed Tier-2 fraud via
  the 3-flags/7-day human review, **WHEN** the review finalizes, **THEN**
  that mascot is revoked. **GIVEN** an unconfirmed/Tier-1-only flag,
  **WHEN** evaluated, **THEN** no revocation occurs.
- **GIVEN** Player A owns 5 mascots including deprecated mascot X, and the
  live roster is 15 post-deprecation, **WHEN** completion% is computed,
  **THEN** the numerator excludes X (→4) and the denominator excludes X
  (→15), yielding 4/15×100 ≈ 26.67% — while X remains visible/grandfathered
  in A's raw ownership record.
- **GIVEN** one run satisfying both a First Contact and a Progression Gate
  condition, **WHEN** processed, **THEN** both mascots grant in the same
  transaction and both `mascot_unlocked` events fire — not
  first-match-only.
- **GIVEN** a run whose first-pass grant completes a Capstone's
  prerequisite, **WHEN** the second pass evaluates within that same
  transaction, **THEN** the Capstone mascot also grants in that run, not
  a later one.
- **GIVEN** a run crediting coins via `mutateWallet` and granting a
  mascot under one shared run-ID idempotency key, **WHEN** the submission
  is sent twice, **THEN** the retry is rejected by the shared idempotency
  check, leaving the balance incremented exactly once and the mascot
  granted exactly once — no split state.
- **GIVEN** any two players, one owning only Common mascots and one
  owning several Epic mascots, **WHEN** both play the identical Super
  Ricochet run, **THEN** their mascot ownership produces zero difference
  in any mini-game mechanic, difficulty, or outcome — verifying "flair,
  not power" is actually true, not just stated.

## Open Questions

1. **Consistency Streak milestones have no testable acceptance criteria
   yet** (qa-lead's own flagged gap) — they depend on Daily Quests' and
   Login Streak's day-boundary definitions, and neither GDD exists. This
   GDD states the *intent* (inherit Login Streak's exact UTC-day rule,
   per Edge Cases) but cannot specify concrete criteria until that GDD is
   written. *Target: resolve when Daily Quests/Login Streak are designed.*
2. **"New mascot added, completion% drop surfaced not silently"** is only
   half-testable today — the data-layer behavior (denominator increases)
   is verifiable now, but "surfaced in-product" depends on the not-yet-
   written Mascot Collection UX spec. *Target: resolve during
   `/ux-design` for that screen.*
3. **Reveal-card art production**: separately authored hero-resolution
   asset vs. a runtime upscale of the standard gallery art — flagged by
   the art director as a real production-cost question, not resolved
   here. *Target: `/asset-spec system:mascot-database`.*
4. **All specific numbers in this GDD are placeholders**: the 16-mascot
   roster size, the level thresholds (3/6/15/20), and the +12 mastery gap
   are all explicitly provisional pending `/balance-check` telemetry once
   real play data exists (stated in Formulas and Tuning Knobs) —
   restated here as the single most consequential open item, since every
   other section builds on these numbers being roughly right.
   **[Corrected 2026-07-17]** The per-category breakdown must additionally
   remain internally consistent even as a placeholder — the original
   6+4+3+3 split summed to the entire roster with zero slots for Capstone,
   the sole Epic-tier source; corrected to 6+4+2+3+1 (Mastery Repeats
   reduced to ~2). Any future `/balance-check` retuning of these counts
   must preserve "sums to roster size AND leaves Capstone non-empty" as a
   hard constraint, not just get the total right.
5. **Roster-exhaustion retention risk is mitigated, not solved.** The
   economy-designer consult flagged that fully deterministic acquisition
   against only one designed mini-game means a completionist can clear
   all 16 mascots quickly, then hit a dead zone. Mitigations are already
   built into this design (silhouette-hidden specifics preserve some
   discovery; milestone spacing avoids clustering all "hard" unlocks at
   the tail), but the underlying gap — more mascots need more mini-games
   to hook into — isn't something this system alone can close. Tracked
   here, not hidden.
