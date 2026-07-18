# Boss AI/Damage Model

> **Status**: Revised
> **Author**: Abdulrahman Alenazan + Claude
> **Last Updated**: 2026-07-09
> **Implements Pillar**: Super Ricochet's core fantasy ("chip away the boss")
> **Creative Director Review (CD-GDD-ALIGN)**: APPROVED 2026-07-09 (self-reviewed — no `creative-director` subagent registered in this environment)
> **Revision note (2026-07-09)**: `/review-all-gdds` found this GDD's
> `bossDefeated` "authoritative source" claim conflicted with Anti-Cheat's
> server-side re-derivation, and that "depended on by Currency/Anti-Cheat"
> was a phantom direct edge (the real path routes through Super Ricochet).
> Both fixed below — see Interactions with Other Systems.
> **`/design-review` (2026-07-17)**: NEEDS REVISION — 3 blocking items
> (frame-boundary win-before-loss ordering and the accumulate-then-decrement
> mechanic, both determinism-critical per ADR-0011, were undocumented in
> this GDD's own Core Rules/Edge Cases; the "boss HP going negative is not
> possible" Edge Case claim is imprecise given how ADR-0011 actually
> resolved the multi-hit case; a third non-loss outcome — surviving the
> volley cap — was missing from States/Transitions) plus 2 recommended
> items. All folded in below; re-review pending.
> **Follow-up fix (2026-07-17, found reviewing `leaderboard.md`)**:
> Interactions' claim that Anti-Cheat "re-derives via replay simulation"
> and that a "server-replay-derived `bossDefeated`" gates gem rewards
> contradicted Anti-Cheat's own Rule 6 (reward is Tier-1-clamped,
> synchronous; Tier-2 replay is async and flag-only, per ADR-0007) —
> corrected to say Tier-1's synchronous clamp gates the reward, not a
> replay-derived recomputation. Same class of error found and fixed the
> same pass in `anti-cheat-replay-verification.md` Rule 5 (the root
> cause) and `super-ricochet.md`'s `run_reward` formula.

## Overview

Boss AI/Damage Model is the server-validated damage and defeat state
machine for Super Ricochet's boss fights. Every brick hit deals exactly 1
damage to the current boss, **deliberately decoupled** from the brick's own
HP or toughness — a proven, already-shipped design carried over from the
prototype rather than a placeholder awaiting a fix. This decision was
flagged as open three separate times earlier in this design process (in
`game-concept.md`, then again in Level/Difficulty Config's Core Rules) and
is resolved here: **keep it decoupled** (see Core Rule 2 for the reasoning).

## Player Fantasy

Direct. This is the tactile core of Super Ricochet: fire a volley, watch
bricks shatter, watch the boss HP bar visibly drain with every hit. The
"chip away the boss" feeling — rapid, satisfying, cumulative damage from a
chaotic volley — *is* this system's central design goal, not a side effect
of it.

## Detailed Design

### Core Rules

1. Every brick hit — from any ball, on any brick, **regardless of that
   brick's own remaining or starting HP** — deals exactly 1 damage to the
   boss's current HP, simultaneously with the brick's own HP decrementing
   by 1.
2. **Decision (2026-07-09)**: boss damage stays decoupled from brick
   HP/value — 1 hit = 1 boss HP, always. Chosen over an untested "tougher
   bricks deal more boss damage" alternative because the current model is
   proven and already shipped, is simple for players to reason about
   (no hidden multiplier math), and keeps brick-HP scaling and boss-HP
   scaling as two independently tunable levers rather than coupling them in
   a way that hasn't been playtested.
3. The boss is defeated the **instant** its HP reaches 0, independent of how
   many bricks remain on the board — a level can end mid-volley the moment
   the threshold is crossed. **[Clarified 2026-07-17, per ADR-0011]** Since
   win/loss is a server-replayed, deterministic outcome (Anti-Cheat Tier 2),
   this "instant" resolution is defined precisely at the **frame boundary**,
   not per physics sub-step: a game frame runs all of its physics sub-steps
   first, accumulating every brick-hit event as a count (never applying
   damage mid-frame); only once the frame's sub-steps are done does the
   model subtract the accumulated hit count from boss HP **once**, then
   check for defeat. **Win is checked before a same-frame loss (a brick
   crossing the danger line)** — a boss that reaches 0 HP on an earlier
   sub-step of a frame still wins even if a brick crosses the danger line
   on a later sub-step of that *same* frame. This ordering is
   determinism-critical (client and server must resolve it identically) and
   was previously undocumented in this GDD, living only in ADR-0011 — a
   reader of this GDD alone could not have implemented it correctly.
4. **[NEW 2026-07-17, per ADR-0011 §2a]** If the boss is **not** defeated
   when a turn's volley time/frame cap is reached (owned by Super
   Ricochet's own turn-timing rules), the turn simply ends and the next
   turn begins with boss HP carried forward unchanged — **this is neither a
   win nor a loss**, a third outcome distinct from both terminal states in
   the States and Transitions table below. Naming this explicitly prevents
   an implementer from treating "volley ended, boss still alive" as an
   ambiguous loss.
5. Boss HP total for a level comes from Level/Difficulty Config's `boss_hp`
   formula — this system consumes that number, never computes its own.
   **[NEW 2026-07-17]** A configured `boss_hp` of 0 is treated as an
   **invalid config**, rejected rather than accepted — 0 would trigger an
   instant, degenerate win on frame 1 with zero hits landed, which is never
   a legitimate outcome. Level/Difficulty Config's formula floor (800 at
   level 1) makes this unreachable in practice, but the guard exists so a
   future formula change can't silently reintroduce it.
6. Boss identity (name) also comes from Level/Difficulty Config's
   `boss_name` — this system owns only the damage/defeat state machine, not
   boss identity itself.

### States and Transitions

| State | Trigger | Result |
|---|---|---|
| Active | Level starts | Boss HP initialized to the level's configured `boss_hp` (rejected if 0 — Rule 5) |
| Active | Any brick(s) take a hit within a frame | At frame end, boss HP −= accumulated hit count for that frame (once, not per individual hit — Rule 3) |
| Active → Defeated | Accumulated frame-end boss HP ≤ 0 | Level win state triggers immediately, even with balls still airborne; checked **before** a same-frame loss (Rule 3) |
| Active → (turn ends, boss survives) **[NEW 2026-07-17]** | Turn's volley time/frame cap reached with boss HP > 0 | Turn ends, next turn begins; boss HP carries forward unchanged — **neither a win nor a loss** (Rule 4) |
| Active → (level ends, boss survives) | A brick crosses the danger line, with no same-frame boss defeat | Level loss state triggers; boss HP is discarded — no partial-progress carryover to the next attempt |

### Interactions with Other Systems

- **Level/Difficulty Config (Ricochet)**: supplies `boss_hp` and
  `boss_name` as inputs — hard dependency.
- **Super Ricochet → Anti-Cheat → Currency System** [corrected 2026-07-09]:
  this system's Defeated state is authoritative only for the **client-side
  engine's own in-session logic** (ending the run locally, triggering the
  win presentation). It is **not** a direct dependency of Currency System or
  Anti-Cheat — there is no direct Boss-AI-to-Currency edge. The actual path
  is: this system's client-side `bossDefeated` flows into Super Ricochet's
  run-result submission, which Anti-Cheat uses **[Corrected 2026-07-17]**
  Tier-1's synchronous, plausibility-clamped view of (per Anti-Cheat's
  Run-Result Interface, Rule 5) to gate gem rewards at submission time —
  not this system's client-side value used directly, but also not a
  Tier-2 replay-derived recomputation, since Tier-2 is asynchronous and
  never gates the reward (only flags a later mismatch for human review,
  per Anti-Cheat Rule 6). This resolves a conflict `/review-all-gdds`
  found: this GDD previously claimed to be "the authoritative source"
  without qualification, which contradicted
  Anti-Cheat's server-authoritative design. **[Clarified 2026-07-17]** This
  client-side `bossDefeated` (and the boss HP it derives from) lands in
  Anti-Cheat's `clientReportedFields` specifically, per that GDD's named
  Run-Result Interface. Because boss HP is always an integer decremented by
  a whole hit count (never a float), the Tier-2 comparison against the
  server-replayed value is an **exact** match, not a `tolerance_units`-style
  approximate comparison — that tolerance exists for float-drift-prone
  values like physics-derived score, which doesn't apply here.
- **Super Ricochet** (`design/gdd/super-ricochet.md`, Designed) **[stale
  "not yet designed" reference corrected 2026-07-17]**: this system is a
  sub-component of Super Ricochet's overall engine, owning only the
  boss-HP-tracking slice — ball physics/collision is a separate concern
  living in that system's own GDD. **[NEW 2026-07-17, per ADR-0011]** Super
  Ricochet's own danger-line loss check must be compatible with Core Rule
  3's frame-boundary ordering: it must latch "a brick crossed the
  danger line" as a frame flag and defer the loss decision to the frame
  boundary, never terminate mid-frame — otherwise a boss defeated on an
  earlier sub-step could be wrongly overridden by a same-frame danger-line
  crossing on a later sub-step, violating "win takes priority over a
  same-frame loss." This constraint belongs to Super Ricochet's own
  implementation and should be verified when that GDD is reviewed.

## Formulas

None new — `boss_hp` is already owned by Level/Difficulty Config. This
system's only rule is a fixed per-hit decrement (see Core Rule 1), not a
formula with independent variables.

## Edge Cases

- **If multiple bricks are hit in the same frame** (e.g. a ball clips two
  bricks across the frame's sub-steps, or two balls each land a hit): each
  hit counts individually toward that frame's accumulated total — a
  double-hit frame deals 2 boss damage, applied as a single decrement at
  frame end (Rule 3), not two sequential −1 operations.
- **[CORRECTED 2026-07-17]** Boss HP **can** go below 0 internally in a
  multi-hit frame (e.g. HP=1 with a same-frame double-hit produces an
  internal value of −1) — the prior claim that "negative HP is not possible
  by construction" was imprecise once ADR-0011 defined the actual
  accumulate-then-decrement-once mechanic. What's actually guaranteed:
  defeat is checked as `boss_hp ≤ 0`, not `== 0`, so an internal negative
  value still resolves to Defeated correctly; and any HP value is **clamped
  at 0 for display purposes** (the boss HP bar never visually shows
  negative). There is no overkill *reward* mechanic reading the negative
  value — it has no gameplay meaning beyond triggering defeat.
- **If the boss reaches 0 HP mid-volley while balls are still airborne**:
  the win state triggers immediately; airborne balls are not required to
  finish their trajectory — matches the prototype's existing behavior.

## Dependencies

- **Depends on** (hard): Level/Difficulty Config (Ricochet).
- **Depended on by**: Super Ricochet *(stale "future GDD" reference
  corrected 2026-07-17 — now designed)*, Ricochet HUD (source of
  boss name/HP for its display — see that GDD's Dependencies and
  Interactions). **[Corrected 2026-07-09, twice]** — this list previously
  also named Currency System and Anti-Cheat directly, contradicting this
  file's own revised Interactions section ("there is no direct
  Boss-AI-to-Currency edge"); fixed by removing that phantom edge. A second
  `/review-all-gdds` pass then caught that the fix over-tightened to
  "Super Ricochet only," incorrectly dropping the real Ricochet HUD edge —
  restored here.

## Tuning Knobs

None beyond what Level/Difficulty Config already owns (`boss_hp` base and
growth rate). The per-hit damage amount is deliberately fixed at 1, not
exposed as a tunable multiplier — that fixed simplicity is the point of
Core Rule 2's decision.

## Visual/Audio Requirements

**[Self-review — art-director consult, performed directly]**: this is a
Combat/damage/health category system, where Visual/Audio is required, not
optional.

- **Boss HP bar**: smooth tween on depletion (not an instant snap) — the
  prototype's existing `.boss-hp-fill` transition (0.12s linear) is a good
  baseline to carry forward.
- **Per-hit feedback**: a subtle visual trace (particle/beam) from the hit
  brick toward the boss portrait on each hit — reinforces that *this
  specific hit* mattered to the boss, which matters more here than in a
  typical damage system precisely because damage is decoupled from brick
  difficulty (Core Rule 2) and could otherwise feel arbitrary without a
  clear causal link.
- **Boss-defeat beat**: a distinct celebratory flourish (screen shake +
  audio sting) bigger than a routine hit — the prototype's existing rising
  arpeggio "win" sound plus particle burst is proven and should carry
  forward as the baseline.

📌 **Asset Spec** — once the art bible is approved, run
`/asset-spec system:boss-ai-damage-model` to produce per-asset visual specs
from this section.

## UI Requirements

The boss name + HP bar/text readout is part of Super Ricochet's HUD
(Presentation layer, not this system) — this GDD only specifies *what data*
that HUD needs (current HP, max HP, boss name, defeated boolean), not the
HUD's own layout.

## Acceptance Criteria

- **GIVEN** a level starts, **WHEN** boss state initializes, **THEN** boss
  HP equals the level's configured `boss_hp` exactly.
- **GIVEN** a brick is hit, **WHEN** the hit registers, **THEN** boss HP
  decrements by exactly 1, regardless of the brick's own HP or type.
- **GIVEN** boss HP reaches 0, **WHEN** detected, **THEN** the level's win
  state triggers immediately, even with balls still in flight.
- **GIVEN** a level ends in a loss before boss HP reaches 0, **WHEN** the
  level restarts, **THEN** boss HP resets to the full configured value — no
  partial-progress carryover between attempts.
- **GIVEN** two bricks are hit in the same frame, **WHEN** both register,
  **THEN** boss HP decrements by 2 in a single frame-end operation, not two
  sequential −1s.
- **[NEW 2026-07-17] GIVEN** boss HP=1 and two bricks are hit within the
  same frame, **WHEN** the frame-end decrement applies, **THEN** the
  internal HP value is −1 (not clamped mid-calculation), defeat triggers
  (checked as `≤ 0`), and the displayed boss HP bar shows 0, never a
  negative number.
- **[NEW 2026-07-17] GIVEN** a boss reaches 0 HP on an earlier physics
  sub-step of a frame and a brick crosses the danger line on a later
  sub-step of that **same** frame, **WHEN** the frame boundary resolves,
  **THEN** the outcome is a win, never a loss — verifying win-before-loss
  ordering holds even when both conditions occur in the same frame.
- **[NEW 2026-07-17] GIVEN** a turn's volley time/frame cap is reached with
  the boss still above 0 HP, **WHEN** the turn ends, **THEN** the next turn
  begins with boss HP carried forward unchanged, and this outcome is
  recorded as neither a win nor a loss.
- **[NEW 2026-07-17] GIVEN** a level's configured `boss_hp` is 0, **WHEN**
  the boss state attempts to initialize, **THEN** the config is rejected as
  invalid rather than triggering a degenerate frame-1 win with zero hits
  landed.

## Open Questions

1. Should the boss-defeat celebration have its own tunable hold-duration
   before transitioning to the result screen, or is that purely a UX
   timing decision with no gameplay implication? *Target: resolve during
   `/ux-design` for the Ricochet HUD/result flow.*
2. **[NEW 2026-07-17]** Core Rule 2's fixed 1-hit=1-damage rule was proven
   at prototype-scale boss HP — but Level/Difficulty Config's `boss_hp` now
   hard-caps at 11,100 from level 30 onward, meaning defeating a level-30+
   boss requires roughly 11,100 individual hits total, across as many turns
   as it takes at this fixed no-multiplier rate. Whether that's plausibly
   achievable within a reasonable number of turns (each capped at 12s/720
   frames per ADR-0011 §2a) is exactly the kind of question Level/Difficulty
   Config's own Open Question 1 already flags as needing real
   average-hits-per-turn telemetry from the Unity port before
   `/balance-check` can validate either axis — cross-referenced here since
   Core Rule 2's fixed-damage decision is the other half of that same
   equation, not resolved independently by either GDD alone.
