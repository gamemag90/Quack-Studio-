# Consistency Check Report
Date: 2026-07-12
Registry entries checked: 43 (0 entities, 0 items, 19 formulas, 24 constants)
GDDs scanned (14, excludes game-concept.md/systems-index.md/cross-review reports):
account-auth.md, analytics-event-tracking.md, shared-hub.md, hub-ui.md,
ricochet-hud.md, save-persistence.md, anti-cheat-replay-verification.md,
boss-ai-damage-model.md, currency-system.md, super-ricochet.md,
level-difficulty-config-ricochet.md, mascot-database.md,
obstacle-spawn-difficulty-ramp-runner.md, quack-runner.md

Method: grep-first. Each of the 43 registered entity/formula/constant names was
searched across all in-scope GDDs (batched by domain into 5 alternation
patterns), with ±2 lines of context. Hits were compared against the registry's
recorded value/output_range. Full targeted reads were done only where a
discrepancy needed confirming.

Note on registry count: session memory/state carried a running total of "42"
entries; a direct line count of `entities.yaml` during this check found 43.
Corrected here — no entries are missing, this was a prior tally error, not a
data-loss issue.

---

### Conflicts Found (must resolve before architecture begins)

None. Zero 🔴 CONFLICT entries detected.

---

### Stale Registry Entries (registry behind the GDD)

None. Every checked formula/constant's registry `value`/`output_range` matches
its source GDD's current text exactly, including the two most recently revised
areas (`boss_hp`'s level-30 hard cap at 11,100, and all three Quack Runner
entries added this session).

---

### Cross-Reference Verification (entries with `referenced_by`, spot-checked in full)

✅ `reconnect_backoff` — save-persistence.md defines it
(`min(max_backoff, base_backoff × 2^attempt)`); analytics-event-tracking.md
explicitly reuses the same formula by reference rather than restating it, 3
separate places (States/Transitions, Formulas, Tuning Knobs, Acceptance
Criteria). No duplicate/divergent definition anywhere.

✅ `coin_credit` — currency-system.md's two-leg definition
(`creditMultiplied` 1×–5×, `creditFlat` unmultiplied) is consumed identically
by quack-runner.md's `runner_coin_credit` (coinsCollected → creditMultiplied,
1:1 base, dodge bonus never credited) and by anti-cheat-replay-verification.md
(gates every credit, no direct-write path). No conflicting multiplier or path
choice found.

✅ `maxPlausibleScore` — obstacle-spawn-difficulty-ramp-runner.md's formula
(`25 × N(t)` for t<75, closed-form switch above) is called out in
anti-cheat-replay-verification.md as the Tier-1 ceiling source, and
quack-runner.md's own Acceptance Criteria reference it as the sole per-run
ceiling (Open Question 6 explicitly names it as replacing the prototype's
100-coin cap). Same formula everywhere, no restatement drift.

✅ `runner_health` — obstacle-spawn-difficulty-ramp-runner.md's `1 (any
single non-coin collision ends the run)` is consumed by quack-runner.md's
Core Rules (coin vs. non-coin collision handling) without modification.

✅ `runner_coin_credit` — quack-runner.md's definition is what
currency-system.md's `coin_credit` formula generically supports; no
second, conflicting Runner-specific currency path exists anywhere (this was
the exact risk CD-GDD-ALIGN condition C1 originally worried about, and it's
confirmed not to have happened).

### Value Spot-Checks (Ricochet difficulty formulas, arithmetic re-verified)

✅ `boss_hp = 800 + min(level−1,10)×650 + min(max(0,level−11),19)×200` — at
level 30: 800 + 10×650 + 19×200 = 11,100, matching the registry's stated cap
exactly. Referenced identically (same cap value, same rationale) in
boss-ai-damage-model.md, game-concept.md, and all 3 cross-review reports.

✅ `initial_rows`, `max_brick_hp`, `spawn_density`, `starting_balls`,
`boss_roster` — all match registry `output_range` exactly, single source
(level-difficulty-config-ricochet.md), no other GDD restates their formulas.

✅ `obstacleSpeed(t)`, `spawnInterval(t)`, `dodge_bonus`,
`runner_leaderboard_score` — all match registry exactly across both the ramp
GDD and quack-runner.md, including the newly-added
`normalizedObstacleSpeed(t) = obstacleSpeed(t) / 600` unit-reconciliation,
which correctly cites "Core Rules 1 and 2" (confirms the CD-GDD-ALIGN
condition C2 fix actually landed in the file, not just in the session log).

✅ `collection_completion_percent`, `progression_gate_level`,
`mastery_repeat_gap` — mascot-database.md's stated formulas/lookup tables
match the registry exactly; no other GDD references mascot entities yet
(expected — Mascot Gallery/Equip UI isn't designed).

---

### Unverifiable / Informational (no conflict, no action required)

ℹ️ 24 constants with `referenced_by: []` (jwt_token_ttl, bcrypt_cost_factor,
username_constraints, password_min_length, analytics_batch_size,
analytics_flush_interval, analytics_buffer_cap, starting_balance,
navigation_debounce, boss_roster, coin_spawn_chance, powerup_spawn_chance,
boss_hp_bar_tween_duration, local_cache_staleness_threshold,
queued_action_max_age, mvp_mascot_roster_size, mascot_rarity_tiers,
mascot_milestone_taxonomy, runner_obstacle_type_mix) appear only in their
source GDD, as their registry entry predicts. Nothing to reconcile.

⚠️ **Stale prose reference (not a registry/value conflict — a wording
staleness in running text)**: `obstacle-spawn-difficulty-ramp-runner.md`
still describes Quack Runner as **"(future GDD)"** in two places —
"Interactions with Other Systems" (line ~138) and "Depended on by" (line
~255) — even though `quack-runner.md` is now fully designed and CD-approved.
This doesn't affect any registered value (both places correctly name what
Quack Runner consumes), so it isn't a 🔴 conflict by this skill's formal
definition, but it's worth a quick two-line text fix so a future reader
doesn't assume Quack Runner is still unscoped.

---

### Clean Entries

✅ 43 of 43 registry entries verified across all 14 in-scope GDDs with no
conflicts and no stale values.

---

Verdict: **PASS**
