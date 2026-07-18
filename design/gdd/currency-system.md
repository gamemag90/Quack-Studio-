# Currency System

> **Status**: Revised
> **Author**: Abdulrahman Alenazan + Claude
> **Last Updated**: 2026-07-09
> **Implements Pillar**: Server-authoritative economy
> **Creative Director Review (CD-GDD-ALIGN)**: APPROVED 2026-07-09 (self-reviewed — no `creative-director` subagent registered in this environment)
> **Revision note (2026-07-09)**: `/review-all-gdds` found the Coin Value
> multiplier could double-apply to boss bonuses and that Anti-Cheat was
> missing from Dependencies. Both fixed — see Core Rule 4, Formulas, and
> Dependencies below.
> **`/design-review` (2026-07-14, lean mode — specialist subagents
> unavailable, weekly limit hit)**: NEEDS REVISION — 2 blocking items
> (Core Rule 2 contradicted ADR-0004's separate `mutateWallet` chokepoint;
> no idempotency contract stated despite ADR-0004 requiring one). Both
> folded in below, plus 3 recommended items; re-review pending.
> **Follow-up fix (2026-07-17)**: reviewing `anti-cheat-replay-verification.md`
> surfaced a related error this pass missed — Rule 5 said callers make "two
> separate calls" for the multiplied/flat split, contradicting ADR-0004's
> single atomic multi-leg `mutateWallet` call. Corrected in Rule 5 below;
> the Formulas section's own "two distinct credit paths" wording was already
> correct and needed no change.

## Overview

Currency System is the two-currency wallet (coins, gems) that every
reward-granting system credits into and every spend-granting system debits
from. It owns **balance integrity** — atomic mutation, never-negative
guarantees, and the generic point where the Coin Value upgrade multiplier
applies — but deliberately does **not** own the game-specific earning
formulas themselves (how much a Super Ricochet run or a login streak pays
out). Those formulas belong to their respective systems' own GDDs (not yet
authored) and simply call into this system's credit/debit API with an
already-computed, already-validated amount. This boundary keeps Currency
System narrow and reusable rather than becoming a dumping ground for every
system's reward math.

## Player Fantasy

Direct, but understated: the satisfying tick-up of coins after a run, the
rarer glint of gems after a boss kill. Currency System itself is invisible —
the fantasy lives in the *moment of crediting* (owned by whichever system
triggers it), not in the wallet's bookkeeping. What Currency System must
protect is trust: a balance that's always accurate and never mysteriously
changes without a visible cause undermines every other system's reward
loops.

## Detailed Design

### Core Rules

1. Two currencies: **coins** (common, most reward paths) and **gems**
   (scarce — boss kills, weekly streak bonus, bosses-type quests only, per
   `game-concept.md`).
2. Every credit/debit goes through a single mutate function — no system
   writes to a currency field directly. **[Corrected 2026-07-14]** This is
   **not** Save/Persistence's `updatePlayer` chokepoint — it is its own
   separate chokepoint, `mutateWallet` (per ADR-0004), store-level atomic
   (a conditional `UPDATE ... WHERE balance >= amt RETURNING`, not an
   app-side read-modify-write, and not in-process mutex serialization).
   Currency is the one explicit exception to `updatePlayer` — see
   save-persistence.md Rule 3. A prior version of this GDD described
   currency as reusing `updatePlayer`, written before ADR-0004 split the
   two chokepoints apart; this GDD had not been updated to match until now.
3. **Balance can never go negative.** A debit exceeding the current balance
   is rejected outright — never partially applied, never allowed to dip
   below zero.
4. **[NEW 2026-07-14] Idempotency**: every `mutateWallet` call carries a
   single operation-level idempotency key, derived from the operation
   itself (a run-submission ID, a receipt ID, a shop-purchase ID) — **never
   a fresh UUID generated per retry attempt**, since a fresh-per-attempt key
   would defeat dedup entirely. A retried call with the same key is a
   no-op: the store rolls back the retry and returns a fresh full-wallet
   read (coins and gems), never a reconstruction from a single ledger row.
   This is what makes the system safe against a caller retrying after a
   network timeout (e.g., Anti-Cheat's validated credit call, or a queued
   offline action replaying per save-persistence.md's own idem-key
   mechanism) without double-crediting.
5. **The Coin Value upgrade multiplier applies generically, at the credit
   point, but ONLY to the "collected coins" term of a reward — never to
   flat bonuses (e.g. a boss-defeat bonus).** **[Corrected 2026-07-17]**
   Callers supply these as **two legs of one atomic `mutateWallet` call**,
   not two separate credit calls: a `creditMultiplied` leg for coins
   actually collected during play, and a `creditFlat` leg for flat bonuses
   — both committed in a single transaction (ADR-0004). A prior version of
   this GDD (and `anti-cheat-replay-verification.md`, now also corrected)
   described this as "two separate calls," which would let one leg commit
   while the other fails — exactly the partial-failure case ADR-0004's
   single multi-leg transaction exists to prevent. This resolves a bug
   found by `/review-all-gdds` (2026-07-09): the original wording ("applies
   generically to whatever `raw_amount` it's handed") let a caller
   accidentally pass a *combined* payout — collected coins plus the boss
   bonus — through a single multiplied call, inflating the boss bonus by up
   to 5× at max upgrade level, contrary to Super Ricochet's canonical
   `run_reward` formula (`coins = coinsCollected × (1+coinValueUpgrade) +
   (bossDefeated ? 50+min(level,30)×20 : 0)`, where the boss term is
   explicitly outside the multiplier). **[Corrected 2026-07-17]** This
   citation previously quoted an uncapped `50+level×20` sourced from
   `game-concept.md` — stale relative to the level-30 cap
   `super-ricochet.md` (the authoritative owner of this formula, per
   `/design-review`) now states explicitly; updated to match. This also
   resolves the `[OPEN]` question flagged in
   `game-concept.md` about whether Coin Value should affect Quack Runner
   too: yes, for Runner's collected-coin term, via the same `multiplied:
   true` path — automatically, because the multiplier lives in the shared
   credit path rather than being duplicated per mini-game.
6. Currency System **trusts its callers** to have already validated the
   amount (via Anti-Cheat/Replay Verification) — it does not re-derive
   reward amounts itself. Its job is atomic application, not validation.
7. Every credit/debit emits a `currency_earned`/`currency_spent` analytics
   event per Analytics/Event Tracking's schema (`source`, `amount`,
   `currency`). **[Clarified 2026-07-14]** Emission is via a **transactional
   outbox row written in the same `mutateWallet` transaction** as the
   balance change (ADR-0004 §4) — not a fire-and-forget call after commit,
   which would lose the event on a crash between commit and emit. A
   separate dispatcher publishes from the outbox at-least-once, keyed by
   `op_id`. This matches analytics-event-tracking.md's own (just-revised)
   requirement that these specific events are server-outbox-emitted, never
   client-buffered.

### States and Transitions

| State | Trigger | Result |
|---|---|---|
| Idle | Credit requested with a valid positive amount | → Applied: balance increases, event emitted |
| Idle | Credit requested with negative/NaN/malformed amount | → Rejected outright, flagged as an anomaly |
| Idle | Debit requested, amount ≤ balance | → Applied: balance decreases, event emitted |
| Idle | Debit requested, amount > balance | → Rejected, balance unchanged, no partial application |

### Interactions with Other Systems

- **Save/Persistence**: `mutateWallet` (Rule 2) is a sibling chokepoint to
  Save/Persistence's `updatePlayer`, not the same one — both share the same
  underlying Postgres transactional store (ADR-0005) and the same canonical
  lock order for any composed operation touching both, but currency's
  never-negative guarantee (Rule 3) is enforced by `mutateWallet`'s own
  store-level conditional update, not by `updatePlayer`.
- **Analytics/Event Tracking**: every mutation emits an event.
- **Account/Auth**: every mutation is scoped to the verified `playerId`.
- **Anti-Cheat/Replay Verification**: computes and validates the amount
  *before* calling Currency System — Currency System is downstream of
  validation, never performs it itself.
- **Future mini-game/quest/streak GDDs**: each owns its own earning formula
  and calls Currency System's credit API with the result. The prototype's
  proven formulas (`computeReward`, `computeRunnerReward`, `streakReward`,
  quest reward multipliers — all documented in `game-concept.md`) carry
  forward as those systems' own future GDD content, not this one's.

## Formulas

The `coin_credit` formula is defined as **two distinct credit paths**, not
one generic multiplier applied to an arbitrary amount (see Core Rule 4 for
why this distinction exists):

**Multiplied path** (collected-coins term only):
`coins_applied = collected_amount × (1 + coin_value_upgrade_level)`

**Flat path** (bonuses — e.g. boss-defeat bonus — never multiplied):
`coins_applied = flat_amount`

**Variables:**
| Variable | Symbol | Type | Range | Description |
|---|---|---|---|---|
| collected_amount | `collected_amount` | int | 0–∞ (pre-validated by caller) | Coins actually collected during play (e.g. `coinsCollected` in the canonical formula) |
| flat_amount | `flat_amount` | int | 0–∞ (pre-validated by caller) | A flat bonus amount that must NOT scale with Coin Value (e.g. the boss-defeat bonus) |
| coin_value_upgrade_level | `coin_value_upgrade_level` | int | 0–4 (per the Shop's cap — see Open Questions on cap consistency) | Player's current Coin Value upgrade tier |

**Output Range** (multiplied path): `collected_amount` to `5 ×
collected_amount` at max upgrade level. **Example**: collected_amount=10,
coin_value_upgrade_level=2 → 30 coins credited via the multiplied path. A
same-run boss bonus of 90 coins is credited via the flat path unchanged —
total 120 coins, matching `game-concept.md`'s formula exactly, not 5×270.

Gems are **never** multiplied — `gems_applied = raw_amount` always, since no
upgrade in the current shop scope affects gems (per `game-concept.md`).

## Edge Cases

- **If two simultaneous debit requests would each individually succeed but
  together exceed balance** (a double-spend race — e.g. two rapid shop
  purchase taps): Save/Persistence's atomic chokepoint serializes them — the
  second request re-checks balance against the *already-decremented*
  balance, not a stale read, so it correctly fails per Rule 3.
- **If a credit amount is negative or NaN** (a malformed call from a buggy
  caller, not a legitimate reward): reject outright and flag as an anomaly
  (analytics event + candidate Anti-Cheat signal) — never silently coerce to
  zero and proceed.
- **If a debit would push gems negative for a one-time item repurchase
  attempt** (e.g. Aim Assist already owned): rejected before mutation, same
  as any other insufficient-funds case — no special-casing needed since Rule
  3 already covers it generically.

## Dependencies

- **Depends on** (hard): Save/Persistence (atomic mutation), Analytics/Event
  Tracking (every mutation emits an event), Account/Auth (playerId scoping),
  **Anti-Cheat/Replay Verification (every credit's amount must already be
  Anti-Cheat-validated before it reaches this system — added 2026-07-09 per
  `/review-all-gdds`, previously missing despite being described in this
  document's own body text)**.
- **Depended on by** (hard): every mini-game, Shop/Cosmetics/Battle Pass,
  Daily Quests, Login Streak, Currency Ledger, IAP/Receipt Validation.
  **Depended on by (soft)**: Shared Hub, for its read-through currency
  display *(added 2026-07-09 — `/review-all-gdds` found Shared Hub declared
  this edge but this list omitted it)*.

**Consistency check**: Save/Persistence's GDD lists "Depended on by: ...
Currency System" — matches. Analytics' GDD lists "Depended on by: ... every
Core/Feature-layer system" — Currency System is one of them, consistent.
Anti-Cheat's GDD lists "Depended on by (hard): Currency System" — now
reciprocal following the fix above (previously a one-directional gap flagged
by `/review-all-gdds`, 2026-07-09).

## Tuning Knobs

| Knob | Suggested Value | Too Low | Too High |
|---|---|---|---|
| Starting balance (new player) | 0 coins, 0 gems (carried over from prototype) | N/A | A large starting grant would undercut the "earn your first reward" onboarding moment |

Coin Value's multiplier *range* (0–4, up to 5×) is owned by the Shop system,
not re-specified here — Currency System only consumes the current level.

## Visual/Audio Requirements

Deferred to whichever system triggers a credit/debit (a mini-game's
end-of-run screen, the Shop's purchase confirmation) — Currency System
itself has no dedicated feedback moment, only the shared HUD display below.

## UI Requirements

A shared currency-chip component (🪙 coins, 💎 gems) appears in the Hub
header and every mini-game's HUD, per the prototype's existing pattern
(`.chip` in `styles.css`). Detailed visual spec belongs in `/ux-design`;
this section just establishes that the display is a shared component, not
reimplemented per screen.

## Acceptance Criteria

- **[REVISED 2026-07-14] GIVEN** collected_amount=10 credited via the
  multiplied path at coin_value_upgrade_level=2, **AND** a same-run flat
  boss-defeat bonus of 90 coins credited via the flat path, **WHEN** both
  legs apply, **THEN** the total credited is exactly 30+90=120 coins — never
  `(10+90) × 3 = 300`, the exact double-dip bug this GDD's revision note
  already documents having found and fixed once (test exercises it
  concretely instead of only asserting the rule in prose).
- **[NEW 2026-07-14] GIVEN** a gem credit of any amount, **WHEN** applied at
  any coin_value_upgrade_level, **THEN** the credited amount is unchanged —
  gems are never multiplied, regardless of the coin-side upgrade level.
- **GIVEN** a debit request exceeding current balance, **WHEN** attempted,
  **THEN** it's rejected and balance is unchanged.
- **GIVEN** two concurrent debit requests that individually fit but not
  both, **WHEN** processed, **THEN** exactly one succeeds and the other is
  rejected — never both applied.
- **[REVISED 2026-07-14] GIVEN** a malformed (negative/NaN) credit amount,
  **WHEN** received, **THEN** it's rejected outright (not coerced to a
  valid value) AND produces both an analytics anomaly event and a
  candidate Anti-Cheat signal — not merely a silent rejection with no
  downstream record, per the Edge Cases section's own claim.
- **[NEW 2026-07-14] GIVEN** a `mutateWallet` call is retried with the same
  operation-level idempotency key (e.g. a network-timeout retry), **WHEN**
  the server processes the retry, **THEN** the balance is credited/debited
  exactly once, and the retry returns a fresh full-wallet read rather than
  applying a second time.

## Open Questions

1. **[RESOLVED elsewhere, 2026-07-14]** Should Currency System itself own a
   "starting balance" grant for new players (a welcome bonus), or is that
   onboarding/Shop's concern? This was left open here, but `player-
   journey.md` (authored 2026-07-11) already decided it: 0/0 is deliberate,
   not a placeholder — "so the first reward is EARNED not GIVEN," tied to
   the First Core Loop journey phase. This GDD's Open Questions hadn't been
   updated to reflect a decision already made elsewhere in the project.
2. IAP-sourced currency credits (real-money bundles) will call this same
   credit path — does that need an extra provenance field (receipt
   reference) for audit purposes, or is the existing `source` param on the
   analytics event sufficient? *Target: resolve during the
   IAP/Receipt Validation GDD.*
