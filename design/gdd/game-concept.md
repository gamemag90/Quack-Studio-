---
status: reverse-documented
source: ../../../quack-blaster/ (React+Canvas2D+Express web prototype)
date: 2026-07-09
verified-by: Abdulrahman Alenazan
---

# Quack Studio — Game Concept

> **Note**: This document was reverse-engineered from the working `quack-blaster`
> web prototype, then extended to close the gap against an external studio brief
> ("master prompt") calling for a premium 5-mini-game mobile collection. Sections
> marked **[NEW — not in prototype]** are net-new scope for the native pivot, not
> carried over from the web version. Design-intent questions were clarified with
> the product owner on 2026-07-09; unresolved balance nuances are flagged inline
> as **[OPEN]**.

## Pitch

A duck-themed mobile mini-game collection: a shared hub where players collect
mascot ducks, spend earned currency on upgrades and cosmetics, and jump between
distinct mini-games united by one economy, one set of daily quests, and one
login-streak system. Family-friendly, energetic, high-polish arcade feel —
short sessions, frequent reward loops, no pay-to-win.

## Pillars

1. **Shared hub, shared economy** — one account, one currency system (coins +
   gems), one quest/streak loop, feeding every mini-game equally.
2. **Server-authoritative economy** — the client never dictates rewards; the
   server independently recomputes and validates every payout. **[Elevated to a
   hardened pillar per the master prompt's anti-fraud requirements — the
   prototype's clamp-only approach was flagged as MVP-grade in its own
   `ENHANCEMENTS.md`; the native version should budget real effort for
   replay/verification, not just clamping.]**
3. **Collectible mascots** **[NEW — not in prototype]** — the prototype has
   exactly one hardcoded duck (an emoji, no character ID or cosmetic field
   anywhere in its data model). The native version pivots this into a real
   mascot roster with rarity tiers, per the master prompt's explicit call for
   "collectible mascots."
4. **Every mini-game is a real pillar, not a side loop** **[Elevated]** — in
   the prototype, "Quack Runner" is a lightweight coin-farming side activity:
   no progression, no boss, no gems, capped at 100 coins/run, and absent from
   the leaderboard entirely. The master prompt wants 5 roughly equal-weight
   mini-games, so Runner (and the 3 net-new games) should be designed as full
   pillars with their own progression — not clamped to the prototype's
   "side activity" shape.

## Mini-Games

### 1. Super Ricochet (ball/brick physics blaster) — carried over
The prototype's proven core loop, reverse-documented in full:
- 7-column grid; aimable multi-ball volley launcher; sub-stepped, tunnelling-proof
  collision at step size = half a ball radius.
- Board descends one row/turn; game over if a brick crosses the danger line.
- **Boss HP model**: every brick hit drains exactly 1 boss HP, regardless of
  the brick's own HP or the ball's power. **[RESOLVED 2026-07-09, see
  `boss-ai-damage-model.md`]**: kept decoupled from brick toughness —
  proven, already-shipped, simpler for players to reason about, and keeps
  brick-HP and boss-HP as independently tunable levers rather than coupling
  them in an untested way.
- Difficulty scaling (level `L`, prototype baseline — **`bossHp` superseded,
  see note above**): `bossHp = 800 + (L-1)×650` *(prototype/original value;
  the native version hard-caps this at level 30 — see
  `level-difficulty-config-ricochet.md`)*,
  `initialRows = min(4 + floor((L-1)/2), 8)`, `maxBrickHp = 6 + (L-1)×4`
  *(still unbounded in the native version too — flagged as an open risk,
  see `level-difficulty-config-ricochet.md`'s Open Questions)*,
  `spawnDensity = min(0.45 + (L-1)×0.03, 0.75)`,
  `startingBalls = 3 + floor((L-1)/3) + extraBallsUpgrade`.
- **Boss content**: 6 named bosses cycling forever (`level % 6`). `startingBalls`
  scales linearly without end (fine — see below). `bossHp` **no longer scales
  without end** — **[SUPERSEDED 2026-07-09]**: the original decision here
  ("endless scaling is fine for now") caused a real, mathematically-provable
  unwinnable-late-game bug, found by `/review-all-gdds` and fixed in
  `level-difficulty-config-ricochet.md`. `bossHp` now hard-caps at 11,100
  from level 30 onward — do not port the old unbounded formula
  (`800 + (L-1)×650` forever) from this document; treat
  `level-difficulty-config-ricochet.md` as the authoritative source for this
  formula going forward.
- Reward formula: `coins = coinsCollected × (1 + coinValueUpgrade) + (bossDefeated ? 50 + min(level,30)×20 : 0)`;
  `gems = bossDefeated ? 5 + floor(min(level,30)/2) : 0` (gems are
  boss-kill-gated). **[Capped 2026-07-09]**: the `level` term in both bonus
  formulas is now clamped at 30, matching `boss_hp`'s hard cap — a second
  `/review-all-gdds` pass found the original uncapped version created a
  risk-free, ever-increasing-payout farming loop once boss difficulty
  itself stopped scaling at level 30 (reward kept climbing while risk
  flattened). Beyond level 30, the boss bonus is fixed at `50+600=650` coins
  and `5+15=20` gems per kill — still generous, but no longer strictly
  improving forever.
  `score = bricksDestroyed×10 + coinsCollected×5 + (bossDefeated ? bossHp : 0)`
  is unaffected (already implicitly capped, since `bossHp` itself caps at
  11,100 from level 30).

### 2. Quack Runner (endless dodge/collect) — carried over, elevated in scope
- Vertical obstacle-dodge, horizontal-only duck movement (drag/keyboard in the
  prototype → touch-drag on mobile).
- Health = 1 (one-hit run-ending), obstacle mix 18% coin / 24% bomb / 26% bird
  / 32% cloud, continuous difficulty ramp (speed +50 every 5s, spawn interval
  1.5s → 0.6s floor).
- **Decision (2026-07-09): keep the one-hit-punishing design** — intentional
  contrast against the blaster's more forgiving multi-turn loss, short
  high-tension arcade sessions are the point, not a gap to fix.
- **[OPEN]**: Runner currently earns no gems and isn't affected by the Coin
  Value upgrade (`computeReward`'s coin multiplier is scoped only to the
  blaster's `computeRunnerReward` path, per the prototype's code comments).
  Since Runner is being elevated toward a full pillar, decide during
  `/design-system runner` whether it should get its own progression/currency
  interactions or continue sharing the blaster's upgrade economy as-is.
- **[OPEN]**: Runner is entirely absent from the leaderboard (`leaderboard.ts`
  only tracks blaster `bestScore`). Decide whether Runner needs its own
  leaderboard or a unified cross-game score once it's a real pillar.

### 3–5. Three additional mini-games **[NEW — not in prototype]**
The master prompt names "Dice Board" and "Rocket Shooter" as reference
examples (Super Ricochet already covers rocket/projectile-shooter territory
conceptually) plus a 5th unspecified slot. Concepts, mechanics, and formulas
for these are **not yet designed** — this is the largest single scope gap
against the master prompt and should be the subject of a dedicated
`/brainstorm` or `/design-system` pass per mini-game, not squeezed into this
reverse-documentation pass.

## Economy

- **Currencies**: Coins (common, earned via most activities) and Gems (scarce,
  boss-kill/weekly-streak/boss-quest-gated only). **[OPEN]**: once a player
  buys the one gem-cost item (Aim Assist, 15 gems, one-time), gems currently
  have zero further sink. **Decision needed before final economy lock**: the
  master prompt's cosmetics/battle-pass expansion (see Shop below) should
  give gems a permanent sink — don't ship the dual-currency split without
  one. **[OPEN, added 2026-07-09]**: a second `/review-all-gdds` pass found
  the identical gap on the **coins** side — once both permanent upgrades
  (Extra Balls, Coin Value) are maxed, coins also have zero further sink.
  **Deliberately deferred to the Shop GDD** (consciously, not forgotten) —
  the Shop needs at least one coin-priced recurring item (consumables, a
  coin-priced cosmetic track, or similar) alongside the gem sink above,
  before the economy can be considered locked.
- **Daily quests**: 3/day, randomly drawn without repeats from 4 types
  (bricks/coins/bosses/runs). Reward scale: bricks 1:1 coins, coins ×4 coins,
  runs ×15 coins, bosses ×100 coins + 1 gem/target (the single richest quest
  type).
- **Login streak**: `coins = 40 + min(streak,10)×15` (caps at day 10, 190
  coins), flat +5 gems every 7th day. Streak resets unless the player was
  active on the exact previous UTC calendar day.

## Shop & Monetization

- **Prototype scope**: 3 permanent, coin/gem-priced upgrades only (Extra
  Balls, Coin Value, Aim Assist) — no consumables, no cosmetics, no IAP.
- **Decision (2026-07-09): expand significantly.** The master prompt calls
  for cosmetic skins, currency bundles, and a premium battle pass, explicitly
  "no pay-to-win." Native version scope:
  - Cosmetic mascot skins (gem or real-money priced, cosmetic-only — no
    stat effect, preserving the no-pay-to-win pillar)
  - Currency bundles (real-money IAP — requires App Store/Play Store
    developer accounts and server-side receipt validation; not yet started)
  - Seasonal battle pass (free + premium track, per master prompt)
  - The 3 existing permanent upgrades carry over as the "power" track;
    cosmetics/battle-pass are a parallel, separate track so power progression
    stays earnable through play alone

## Target Platform & Feel

- Mobile-first (iOS 14+, Android API 21+), 60fps target on mid-range devices
  with 30fps/dynamic-quality fallback on low-end (per master prompt).
- Touch-only input — single tap/hold/drag, no gamepad/mouse assumptions (see
  `technical-preferences.md`).
- Family-friendly, energetic tone; readable shapes and strong color contrast
  per the master prompt's Creative/UX direction.

## Explicit Non-Goals (for this pass)

- Mini-games 3–5 are **not designed yet** — see above.
- Real-money IAP integration is **not started** — no store developer accounts,
  no receipt validation server-side yet.
- Full replay-based anti-cheat (vs. the current clamp-only server validation)
  is **scoped as a pillar but not implemented**.

## Follow-Up Recommended

1. `/map-systems` — decompose this concept into individual systems (mascot
   collection, shop/cosmetics, the 3 new mini-games, anti-cheat hardening)
2. `/design-system` — author a full GDD per system, starting with whichever
   system is highest-priority for the next milestone
3. `/balance-check` — once formulas are ported to Unity, re-validate the boss
   HP/brick-HP decoupling and the gems-sink gap flagged above as **[OPEN]**
4. `/art-bible` — the prototype has zero authored art (100% procedural Canvas2D
   shapes + emoji); the native version needs a real visual identity spec
   before any mascot/asset production begins
