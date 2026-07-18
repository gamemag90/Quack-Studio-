# Cross-GDD Review Report

**Date**: 2026-07-09
**GDDs Reviewed**: 11 system GDDs + game-concept.md + systems-index.md
**Systems Covered**: Account/Auth, Save/Persistence, Analytics/Event Tracking,
Currency System, Anti-Cheat/Replay Verification, Shared Hub, Level/Difficulty
Config (Ricochet), Boss AI/Damage Model, Super Ricochet, Hub UI, Ricochet HUD

Performed via three parallel independent passes (Cross-GDD Consistency,
Game Design Holism, Cross-System Scenario Walkthrough) per the
`review-all-gdds` skill. No dedicated director/specialist subagents exist in
this environment — each pass was run as a general-purpose agent given the
full GDD set, the entity registry, and its specific checklist.

Note: these GDDs use "## Overview" as their opening section rather than
"## Summary" — a template variant of the `design-system` skill used to
author them, not an error.

---

### Consistency Issues

#### Blocking (must resolve before architecture begins)

🔴 **Currency System's Dependencies section omits Anti-Cheat entirely, breaking
the core reward-validation contract's bidirectionality**

`anti-cheat-replay-verification.md` declares "Depended on by (hard): Currency
System, every mini-game's run-submission flow." For that to be reciprocal,
`currency-system.md` must list Anti-Cheat as a dependency — but its
Dependencies section lists only Save/Persistence, Analytics, Account/Auth.
Currency's own inline "Consistency check" audits its links to Save/Persistence
and Analytics and declares them consistent, but silently skips the Anti-Cheat
link — giving false confidence the dependency graph was fully verified.
**Fix**: add Anti-Cheat to Currency's "Depends on (hard)" list.

🔴 **Coin Value multiplier is double-applied and applied to the wrong terms**
*(independently confirmed by both the Consistency pass and the Scenario
Walkthrough — highest-confidence finding of this review)*

`game-concept.md`'s canonical formula: `coins = coinsCollected × (1 +
coinValueUpgrade) + (bossDefeated ? 50 + level×20 : 0)` — the multiplier
applies **only** to `coinsCollected`; the boss bonus is added outside it.

`anti-cheat-replay-verification.md` Core Rule 4 states Anti-Cheat is "the
only system permitted to compute final reward amounts from raw run data" —
implying it computes the whole formula, multiplier included, before handing
Currency an already-computed amount.

`currency-system.md` Core Rule 4 + Formulas state the multiplier "applies
generically, at the credit point" to whatever `raw_amount` it receives.

**Net effect**: if Anti-Cheat computes the full formula and passes the result
as `raw_amount`, Currency multiplies it *again*, and the boss bonus — which
the source formula deliberately leaves unmultiplied — gets multiplied too. At
max upgrade level, boss-kill payouts inflate ~5×.

**Critical caveat**: at Level 1 with no upgrade owned, the multiplier is ×1,
so this bug is invisible in the exact scenario most likely to be played
first (cold-start/first playtest) and would ship undetected without this
review.

**Fix**: pick one owner. Recommended: Anti-Cheat passes raw, un-multiplied
`coinsCollected` plus a separately-flagged boss bonus; Currency multiplies
only the coins term. Write this explicitly into both GDDs.

#### Warnings (should resolve, but won't block)

⚠️ **Level/Difficulty Config's dependency on Save/Persistence is
one-directional** — `level-difficulty-config-ricochet.md` declares it (soft),
but `save-persistence.md`'s enumerated "Depended on by" list omits it.

⚠️ **Boss AI's "depended on by Currency/Anti-Cheat" has no reciprocal entry
in either GDD** — the real data path is almost certainly Boss AI → Super
Ricochet → Anti-Cheat, not a direct Boss-AI-to-Currency link. Reconcile the
graph to reflect the actual flow.

⚠️ **Super Ricochet emits `run_complete` but `level_complete` (which carries
`rewards`/`bossDefeated`) fits a leveled boss game better** — as written,
Super Ricochet's telemetry would omit the fields needed for the balance-
tuning KPIs Analytics exists to serve.

---

### Game Design Issues

#### Blocking

🔴 **Boss HP scaling diverges from the capped board substrate — runs become
mathematically unwinnable past some level**

Boss HP grows linearly and unbounded (`800 + (L-1)×650`). The board you hit
to deal damage is hard-capped from level 11 on (~42 brick cells max, 7
columns × 8-row cap × 0.75 density cap), refilled by only ~5 bricks/turn.
Ball count grows only +1 per 3 levels (+0–5 from upgrade). Since boss damage
is purely hit-count-based (1 hit = 1 boss HP, by design — see
`boss-ai-damage-model.md`), turns-to-kill grows without bound while the
danger-line pressure per turn stays constant. Past an as-yet-undetermined
level, no player skill can kill the boss before the board overflows. Both
`level-difficulty-config-ricochet.md` and `boss-ai-damage-model.md`'s own
Tuning Knobs name the symptom ("grindy," "damage sponges") without connecting
the cause.

**Recommendation**: instrument average boss-damage-per-turn once the Unity
port exists, find the practical wall level, then either cap/soft-reset boss
HP growth (a prestige curve) before that wall, or couple one of the
currently-capped levers to level so damage output keeps pace. Resolve before
`/balance-check` signs off — the three "endless scaling is fine for now"
decisions made earlier in this session had no supporting math behind them.

#### Warnings

⚠️ **Gems have no recurring sink** *(confirms the open question already
flagged in `currency-system.md` and `game-concept.md` — now verified true
across all 11 GDDs, not hypothetical)*. Every gem source (boss kills, weekly
streak, bosses-quest) is recurring; the only sink anywhere in the MVP slice
is the one-time 15-gem Aim Assist purchase. Severity is moderate for MVP
(dead currency = soft retention problem) but hardens into a real
compliance/consumer-protection risk the moment gem bundles become real-money
IAP. Keep the concept doc's own rule as a hard gate: no gem IAP ships before
a recurring gem sink exists.

⚠️ **No catch-up mechanic for a player stuck below the boss-scaling wall** —
same root cause as the blocking issue above. Coins/gems are overwhelmingly
boss-kill-gated, and a loss discards all boss progress with no partial
carryover — a player who hits the wall is cut off from both currency *and*
the upgrades that could help them push through. Recommend a guaranteed
per-run coin floor, a farmable earlier-level option, or partial boss-damage
carryover, decided alongside the scaling fix.

---

### Cross-System Scenario Issues

Scenarios walked: 4
1. Victorious Super Ricochet run → reward crediting chain
2. Session expiry mid-volley / mid-modal
3. Tier-2 mismatch on a boss-defeating run
4. Cold-start: brand-new player's very first run

#### Blockers

🔴 **Coin Value multiplier double-dip** — see Consistency section above; this
walkthrough found the identical bug independently while tracing Scenario 1.

🔴 **No agreed contract for "the run result" data shape, especially who owns
`bossDefeated`** — `boss-ai-damage-model.md` calls itself "the authoritative
source" of `bossDefeated`, but it's a client-side engine slice. Anti-Cheat's
entire premise is server-side re-derivation from replay that doesn't trust
client claims. Two systems claim authority over the single boolean gating
every gem payout in the game, and no GDD defines what happens if they
disagree. `systems-index.md` explicitly predicted this gap during
`/map-systems` ("recommend defining a shared mini-game-result interface
during `/create-architecture`") — this walkthrough confirms it's load-bearing,
not a hypothetical nice-to-have.

**Fix**: author the shared mini-game-result interface now (don't defer to
architecture), stating explicitly that `bossDefeated`, `coinsCollected`,
`bricksDestroyed`, `level`, and `score` are all server-derived from replay,
with Boss AI's "authoritative" claim scoped to the client-side engine only.

#### Warnings

⚠️ **Tier-1 coin/score clamp ceiling cannot be derived from Level/Difficulty
Config alone** — the coin ceiling actually depends on Super Ricochet's own
spawn constants (`coin_spawn_chance`, power-up rate) too. State that the
Tier-1 clamp draws from both GDDs, and put the coin-ceiling formula
explicitly in Super Ricochet's own future content.

⚠️ **Upgrade-level ranges are inconsistent across 3 documents** —
`currency-system.md` says 0–4 for Coin Value; `level-difficulty-config-
ricochet.md` says 0–5 for Extra Balls; `game-concept.md`'s formula states no
cap at all. Combined with the double-dip blocker, true max payout is
currently indeterminate. Lock tier caps in one place before the Shop GDD.

⚠️ **Session expiry mid-volley leaves a winning run's fate undefined** — the
JWT has no refresh token and rejects with no grace period. Shared Hub covers
expiry-while-a-modal-is-open, but no GDD covers expiry landing mid-run in an
active mini-game. A queued resubmit after re-auth would collide with Anti-
Cheat's run-ID idempotency in an unclear way. Recommend: let the run finish
locally, hold the submission, prompt re-auth, then submit under the fresh
token using the same run ID.

#### Info

ℹ️ **Tier-2 "resolve silently" vs. the win-state UI: verified NOT a
contradiction.** Checked specifically since it looked like a candidate
issue — Boss AI's win trigger and Ricochet HUD's transition are both
purely client-side and don't depend on the server's Tier-2 verdict, so the
"never alarm the player" requirement and the win-state UI don't actually
fight each other. One cosmetic note for `/ux-design`: if the result screen
ever shows a reward number before the server confirms the (possibly
Tier-1-clamped) amount, that number could later shrink — worth a UX decision,
not a contradiction.

ℹ️ **Cold-start path is internally consistent end to end.** One notable
seam: at Level 1 with 0 Extra Balls upgrade, the Coin Value multiplier is ×1
— which is exactly why the double-dip blocker is invisible in first
playtests. Flagging this connection explicitly so whoever fixes the bug
knows not to trust "it worked in my test run" as evidence it's fixed.

ℹ️ **Analytics-emission ordering vs. Currency crediting is unspecified but
non-blocking** — Currency's atomic chokepoint (Save/Persistence) makes this a
non-issue in practice even though neither GDD states pre- vs. post-commit
emission explicitly.

---

### GDDs Flagged for Revision

| GDD | Reason | Type | Priority |
|---|---|---|---|
| currency-system.md | Coin Value multiplier double-application; missing Anti-Cheat dependency | Consistency | Blocking |
| anti-cheat-replay-verification.md | Undefined `bossDefeated` authority contract vs. Boss AI; reward-payload contract underspecified | Consistency / Scenario | Blocking |
| boss-ai-damage-model.md | Overreaching "depended on by Currency/Anti-Cheat" claim; conflicts with Anti-Cheat over `bossDefeated` authority | Consistency / Scenario | Blocking |
| level-difficulty-config-ricochet.md | Boss HP scaling diverges from capped board substrate (unwinnable late-game); missing dependent listing | Design Theory / Consistency | Blocking |
| super-ricochet.md | Ambiguous analytics event choice; Tier-1 coin-clamp ceiling incomplete without its own spawn constants | Consistency / Scenario | Warning |
| account-auth.md | Session-expiry-mid-active-run path undefined | Scenario | Warning |
| game-concept.md | Gems have no recurring sink; no catch-up mechanic decided | Design Theory | Warning |

---

### Verdict: FAIL

Three blocking issues, all concentrated at the highest-fan-in point in the
system (the reward-crediting pipeline through Anti-Cheat → Currency), plus
one blocking game-design math error (boss scaling). This is exactly where a
holistic review is most valuable — a per-GDD review could not have caught
either issue, since each GDD is internally consistent on its own.

### Required actions before re-running

1. Resolve Coin Value multiplier ownership — decide exactly what
   `raw_amount` contains when Currency's credit function is called; update
   `currency-system.md` and `anti-cheat-replay-verification.md` to agree.
2. Author the shared mini-game-result interface (already anticipated in
   `systems-index.md`) — explicitly scope `bossDefeated`'s authority to
   server-side replay derivation, not the client-side Boss AI engine.
3. Add Anti-Cheat to Currency System's Dependencies section.
4. Resolve the boss-HP-vs-board-substrate scaling divergence — needs a
   math-based cap/prestige curve or a coupled lever, decided before
   `/balance-check` sign-off.
5. (Recommended, non-blocking) Decide gem-sink timing and a catch-up
   mechanic before the Shop GDD locks.
