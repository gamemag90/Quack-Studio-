# Cross-GDD Review Report — Fresh Full Re-run

**Date**: 2026-07-09
**GDDs Reviewed**: 11 system GDDs + game-concept.md + systems-index.md
**This supersedes**: the addendum's informal summary — this is a genuine
independent full re-run (2 fresh parallel agents, no prior-finding framing),
not a targeted re-check.

---

### Consistency Issues

#### Blocking
None. The level-30 `boss_hp` hard cap was independently re-derived by both
review passes and confirmed arithmetically correct and internally
consistent with every reference to it across the document set.

#### Warnings — all found and fixed same-session
- `game-concept.md` still carried the pre-fix unbounded `boss_hp` formula
  and the reversed "endless scaling is fine" decision — **fixed**.
- `boss-ai-damage-model.md`'s dependency-list correction over-tightened and
  dropped a real edge to Ricochet HUD — **fixed**.
- `shared-hub.md` → `currency-system.md` soft dependency was one-directional
  — **fixed**.
- `level-difficulty-config-ricochet.md` claimed a direct Anti-Cheat
  dependency Anti-Cheat's own GDD didn't reciprocate (Anti-Cheat models it
  as transitive via Super Ricochet) — **fixed**, downgraded to transitive.
- `super-ricochet.md` emits analytics events but didn't list Analytics as a
  dependency — **fixed**.

### Game Design Issues

#### Blocking
None.

#### Warnings
- **"Structurally resolved / guaranteed winnable" overstated the boss-HP
  cap's guarantee** — it stops the boss-HP-vs-board divergence from
  widening, but `maxBrickHp` is a separate, still-fully-unbounded lever
  driving the *other* loss condition (danger-line overflow), never
  addressed by this fix. **Language corrected same-session** in
  `level-difficulty-config-ricochet.md` and `systems-index.md`; a new Open
  Question added asking whether `maxBrickHp` should get the same hard-cap
  treatment.
- **The post-level-30 easing does not count as a catch-up mechanic** — it's
  a win-more effect for players who already cleared levels 1–29, not relief
  for a stuck player. **Language corrected same-session**; the original
  "no catch-up mechanic" warning remains open, not resolved.
- **[NOT YET ADDRESSED — genuine design decision, not a doc fix]**
  Unbounded boss rewards (`coins bonus = 50 + level×20`, `gems = 5 +
  floor(level/2)`) combined with decreasing difficulty past level 30 create
  a risk-free, ever-increasing-payout farming loop at the highest reachable
  level. See discussion below.
- **[NOT YET ADDRESSED — genuine design decision, not a doc fix]** Coins
  have no sink once both upgrades (Extra Balls, Coin Value) are maxed — the
  concept doc only flagged this gap for gems, not coins. See discussion
  below.

### Verdict: PASS (with 2 open design-decision warnings, both non-blocking for architecture)

No contradiction, stale reference, or design-theory blocker remains
unaddressed. The 5 mechanical consistency gaps found by this pass were
fixed immediately. The 2 remaining warnings are genuine economy-design
questions that call for a human decision, not something to silently resolve
— see below.
