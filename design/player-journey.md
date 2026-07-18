---
status: draft
author: Abdulrahman Alenazan + Claude
date: 2026-07-11
---

# Quack Studio — Player Journey Map

> **Note on provenance**: `shared-hub.md`'s Open Questions referenced a
> template at `.claude/docs/templates/player-journey.md`. That file does
> not exist — checked via Glob before writing this, not assumed. This
> document is authored from established mobile-game UX practice instead,
> grounded directly in the four already-approved UX specs' own entry/exit
> points and the GDDs' explicit onboarding signals, rather than a template
> that was never actually created.
>
> **Scope limit, stated honestly**: only Super Ricochet has a complete
> GDD among the five planned mini-games; Mascot Database, Shop/Cosmetics,
> and mini-games 3–5 don't exist as designed systems yet. This map covers
> the journey through what's actually designed (MVP tier: Hub, Account/
> Auth, Currency, Super Ricochet, Daily Quests, Login Streak) and marks
> everything downstream as explicitly blocked, not invented.

## Purpose

Every one of the four approved UX specs had to *assume* what emotional
state and context a player arrives in — `shared-hub.md` said so directly
in its own Open Questions. This document exists to make those assumptions
explicit, check them against each other for consistency, and give future
specs (Mascot Gallery, Shop, Runner) a real reference instead of another
set of guesses.

## Journey Phases

### Phase 1 — First Launch (0–60 seconds)

**Player goal**: figure out what this game is and get into it fast, with
minimal friction.

**Emotional state on arrival**: curious, low commitment, low patience for
setup friction — this is the standard mobile-F2P assumption and matches
`account-auth.md`'s own design decision to make **Guest the primary CTA**,
not an equal-weight option buried under Login/Register.

**Flow**: App opens → `account-auth.md`'s main entry screen renders →
player taps Guest (the emphasized path) → account created silently,
no form to fill → **warmer transition** into Shared Hub (`account-auth.md`
already specifies this: first-time entry gets "a slightly warmer
transition... matches the art bible's 'character-first moments' principle
— perhaps the duck mascot reacting," distinct from the quick fade/cut a
returning Login gets).

**Hub UI on first arrival**: `hub-ui.md`'s Edge Cases already specify this
exact state — 0 mascots, 0 coins, 0 gems (`currency-system.md`'s explicit
new-player baseline), no quest history. Must read as **inviting, not
empty** — silhouette placeholders + clear CTAs on the mascot preview,
never a blank gallery strip. This is not a hypothetical; it's the literal
first thing every single player sees, so `hub-ui.md`'s "inviting empty
state" requirement is this journey's single highest-leverage screen.

**Validates `shared-hub.md`'s "Player Context on Arrival" assumption**:
first-time arrival = warm/celebratory (matches account-auth.md's warmer
transition), not neutral. **Confirmed consistent** between the two specs
— no contradiction found.

### Phase 2 — First Core Loop (60 seconds – ~5 minutes)

**Player goal**: understand what "playing" actually means in this game —
the first real gameplay moment.

**Flow**: Hub UI's Games section (only Super Ricochet unlocked at this
point — `hub-ui.md`'s Edge Cases: "onboarding always unlocks at least
Super Ricochet") → tap the hero card → Ricochet HUD renders → level 1
config (`level-difficulty-config-ricochet.md`: bossHp=800, initialRows=4,
startingBalls=3 — the easiest configuration in the whole difficulty
curve) → aim, fire, watch bricks break and the boss bar drain
(`boss-ai-damage-model.md`'s "chip away the boss" fantasy, `super-ricochet
.md`'s "Ready, Aim, Fire!" tagline) → win or lose → return to Hub.

**Deliberate design signal this phase must honor**:
`currency-system.md`'s Tuning Knobs section states the new-player starting
balance is 0/0 specifically *because* "a large starting grant would
undercut the 'earn your first reward' onboarding moment." The first coin
reward a player receives must come from **playing**, not from a welcome
gift — this is an explicit, already-made design decision this journey map
inherits rather than re-litigates.

**Emotional arc**: curiosity (Phase 1) → focused anticipation while
aiming → "chaotic delight" on volley release (art bible's own Key Moments
table names both of these states for this exact sequence) → either
triumph (first boss kill) or gentle, non-punishing setback (art bible:
"loss state is gentle disappointment, never grim, matching the
family-friendly pillar").

**Return to Hub**: reward is now visible (first coins earned, boss-kill
gems if won) — Hub UI's "return from mini-game" entry point
(`hub-ui.md`'s Entry & Exit Points table) specifies the currency delta
must be immediately reflected, confirming the reward "landed."

### Phase 3 — Early Retention (Session 2 through ~Day 7)

**Player goal**: has a reason to come back tomorrow, not just today.

**Mechanics already designed to drive this**:
- **Daily Quests** (`game-concept.md`): 3/day, drawn from 4 types
  (bricks/coins/bosses/runs), boss quests richest (100 coins + 1 gem/target).
- **Login Streak** (`game-concept.md`): `coins = 40 + min(streak,10)×15`,
  caps at day 10 (190 coins), flat +5 gems every 7th day. Resets unless
  active on the exact previous UTC calendar day.

**Friction point, flagged not resolved here**: `game-concept.md`'s own
Economy section flags that once both permanent Shop upgrades (Extra
Balls, Coin Value) are maxed, coins have zero further sink — and the
mirror gap on gems (one gem-cost item total). This journey phase is
exactly where a player who plays consistently would first *feel* that
gap (nothing left worth saving currency for). Deliberately deferred to
the Shop GDD per `game-concept.md`'s own note — restated here because a
journey map is precisely the tool that should catch a phase-specific
symptom of a system-level gap, not because this document resolves it.

**Level progression through this phase**: `level-difficulty-config-
ricochet.md`'s curve is gentle early (boss HP +650/level through level
11) — by day 7 of daily play a player is plausibly around level
5–10, still in the escalating-but-not-yet-capped region of the curve.

### Phase 4 — Building the Collection (BLOCKED — no GDD yet)

Mascot Database + Rarity Logic has no GDD. `game-concept.md` names
"collectible mascots" as a **pillar**, and `hub-ui.md`'s own layout puts
the mascot preview prominently (right after the Games section — an
explicit early-prominence decision made during that spec's authoring).
This journey phase cannot be mapped in any real detail without that GDD
— flagged as a real, current gap in the journey rather than filled with
invented content. **Recommend**: `/design-system mascot-database` before
any Mascot Gallery UX spec is attempted, since `hub-ui.md` already
depends on this system existing conceptually.

### Phase 5 — Habitual Engagement (Week 2+)

**Player goal**: the game is now a normal part of a routine, not a novelty.

At this point the player has: multiple mini-games (if Runner and the 3
undesigned games existed — they don't yet, see below), an established
daily-quest/login-streak rhythm, and a currency balance increasingly
constrained by the sink gap flagged in Phase 3.

**BLOCKED sub-scope**: Quack Runner has no completed native GDD yet
(Level/Difficulty Config exists conceptually per the systems index, but
Runner itself is "Not Started" per `systems-index.md`'s Progress
Tracker), and mini-games 3–5 have no mechanics defined at all
(`game-concept.md`'s own Explicit Non-Goals). A genuine "collection of 5
mini-games" habitual-engagement phase — the master prompt's core pitch —
**cannot be fully mapped until those exist**. This is the single largest
gap this journey map surfaces: the habitual-engagement phase of a
"5-game collection" pillar currently has real design behind exactly one
of those five games.

### Phase 6 — Churn Risk Points (cross-phase, not a single phase)

Points in the journey with an identified, real risk of losing the player
— surfaced here because a journey map is the right place to look across
phases for these, not because each is newly discovered:

1. **Guest-never-links data loss** (`account-auth.md`'s own flagged gap):
   a Guest account has no recovery path if the device is lost/app is
   uninstalled before linking. Highest risk immediately after Phase 1,
   before any real investment — but the *cost* of losing progress scales
   with every phase that follows, making this a compounding risk the
   longer a player stays Guest-only.
2. **Currency sink exhaustion** (Phase 3/5): once both permanent upgrades
   are maxed and no cosmetics/battle-pass exist yet to spend on, coins
   stop mattering — a classic F2P retention killer if it lands before the
   Shop expansion ships.
3. **Single-mini-game ceiling** (Phase 5): a "collection" pillar with one
   real game risks feeling like false advertising to an engaged player
   who's exhausted Super Ricochet's escalating-but-eventually-flat
   (bossHp caps at level 30) difficulty curve.
4. **No password-reset flow** (`account-auth.md`'s own flagged gap): a
   Login-path player locked out has no self-service recovery — a hard
   churn point for anyone who converted from Guest and then loses
   credentials.

None of these are resolved by this document — they're collected here
because seeing them side-by-side, across phases, is exactly what a
journey map is for, and no single system-level GDD would have surfaced
the *pattern* of "every major churn risk clusters around incomplete
downstream systems" on its own.

## Cross-Spec Consistency Check

Checked this map's phase assumptions against all 4 approved UX specs'
own "Player Context on Arrival" / entry-point tables:

- `account-auth.md` (first-time = warmer transition) ↔ `shared-hub.md`
  (assumed first-time arrival is a distinct emotional state from
  returning) — **consistent**.
- `hub-ui.md` (first-time = inviting empty state, 0 mascots/currency) ↔
  `currency-system.md` (0/0 starting balance is deliberate, not a gap) —
  **consistent**, and this map makes the connection between them explicit
  for the first time (neither spec cross-referenced the other on this
  point).
- `ricochet-hud.md` (no assumptions about journey phase — its own header
  states "Journey Phase(s): unknown," consistent with this document not
  having existed yet when it was written) — this map now supplies that:
  Ricochet HUD's primary journey phase is **Phase 2 (First Core Loop)**,
  recurring through **Phase 3/5** at higher levels.

No contradictions found between any approved spec and this map. Where a
spec made an assumption, this map either confirms it (Phase 1's emotional
arc) or explicitly defers it to a system that doesn't exist yet (Phases
4–5) rather than inventing a resolution.

## Open Items

1. **Phases 4 and 5 cannot be fully designed until Mascot Database, Shop/
   Cosmetics, and mini-games 3–5 have GDDs.** This is the most consequential
   gap this document surfaces — not a documentation gap, a *content* gap.
2. **Currency sink timing**: exactly when in Phase 3/5 a player hits the
   "nothing left to spend on" wall depends on real play-rate data this
   project doesn't have yet — a `/balance-check` question once telemetry
   exists, not resolved here.
3. **Guest-to-account conversion rate** is currently unmeasured (the two
   proposed analytics events, `auth_completed`/`account_linked`, are still
   unapproved per `account-auth.md`'s own Open Questions) — without them,
   Churn Risk #1 above can't be quantified, only reasoned about.
