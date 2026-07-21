---
status: draft
source: design/gdd/game-concept.md
date: 2026-07-09
---

# Quack Studio — Systems Index

24 systems decomposed from `game-concept.md`. See that doc for pillars and
per-mini-game design intent; this index owns dependency ordering, priority
tiers, and GDD authoring progress.

## Enumeration

| System | Category | Explicit/Implicit | Notes |
|---|---|---|---|
| Account/Auth | Foundation | Explicit (carried over) | Prototype's JWT+bcrypt implementation is proven, port directly |
| Save/Persistence | Foundation | Implicit | Every other system needs durable state |
| Analytics/Event Tracking | Foundation | Implicit (NEW) | Zero instrumentation exists in the prototype — flagged gap |
| Live-Ops Event/Flag Config | Foundation | Implicit (NEW) | Master prompt requirement, nothing in prototype |
| Shared Hub | Core | Explicit | Single navigation surface across all mini-games |
| Currency System (coins/gems) | Core | Explicit | Two-currency wallet |
| Currency Ledger/Transaction Log | Core | Implicit | Audit trail, pairs with anti-cheat hardening |
| Anti-Cheat/Replay Verification | Core | Explicit (hardened) | Elevated beyond prototype's clamp-only approach per master prompt |
| Mascot Database + Rarity Logic | Core | Explicit (NEW) | Prototype has one hardcoded duck, no data model at all |
| Level/Difficulty Config (Ricochet) | Core | Explicit (carried over) | Prototype's `gameRules.ts` formulas, proven |
| Obstacle Spawn/Difficulty Ramp (Runner) | Core | Explicit (carried over) | Prototype's `runner.ts` formulas, proven (post-bugfix) |
| Boss AI/Damage Model | Feature | Implicit | The 1-hit-=-1-boss-HP formula, decoupled from brick HP — flagged **[OPEN]** in concept doc |
| Super Ricochet | Feature | Explicit (carried over) | Proven core loop |
| Quack Runner | Feature | Explicit (carried over, elevated) | Being elevated from side-activity to full pillar |
| 3 Undesigned Mini-Games | Feature | Explicit (gap) | No mechanics defined yet — needs own `/brainstorm` pass |
| Daily Quests | Feature | Explicit (carried over) | 3/day rotation, needs a shared result-schema across all mini-games |
| Login Streak | Feature | Explicit (carried over) | Simpler than quests, separate claim |
| IAP/Receipt Validation | Feature | Implicit (NEW) | Real-money currency bundles — not started anywhere |
| Shop/Cosmetics/Battle Pass | Feature | Explicit (expanded) | Prototype's 3-item shop → cosmetics + IAP + season pass |
| Hub UI | Presentation | Implicit | Wraps Currency, Mascot Gallery, Quests, Streak, Shop |
| Mascot Gallery/Equip UI | Presentation | Implicit | View/select collected mascots |
| Per-Mini-Game HUD | Presentation | Implicit | Each mini-game needs its own in-play UI layer |
| Leaderboard | Presentation | Explicit (resolved) | Designed 2026-07-12 — separate per-game boards, not unified (Quack Runner's own GDD implicitly decided this; leaderboard.md formalizes it) |
| *(Full Vision tier — empty)* | Polish | — | Expected this early; no polish/meta systems scoped yet |

## Dependency Map

**Layer 1 — Foundation** (zero dependencies *on layers above Foundation* —
intra-layer dependencies within this list are allowed and do occur, e.g.
Account/Auth's hard dependency on Save/Persistence for durable player
records; see `account-auth.md` Dependencies. **[Clarified 2026-07-14]**:
prior wording read as "zero dependencies" unqualified, which contradicted
account-auth.md's own stated dependency — resolved in favor of the
dependency being real, not retracted): Account/Auth, Save/Persistence,
Analytics/Event Tracking, Live-Ops Event/Flag Config

**Layer 2 — Core** (→ Foundation only): Shared Hub, Currency System, Currency
Ledger, Anti-Cheat/Replay Verification, Mascot Database + Rarity Logic,
Level/Difficulty Config (Ricochet), Obstacle Spawn/Difficulty Ramp (Runner)

**Layer 3 — Feature** (→ Core): Boss AI/Damage Model, Super Ricochet, Quack
Runner, 3 Undesigned Mini-Games, Daily Quests, Login Streak,
IAP/Receipt Validation, Shop/Cosmetics/Battle Pass

**Layer 4 — Presentation** (wraps Feature): Hub UI, Mascot Gallery/Equip UI,
Per-Mini-Game HUD, Leaderboard

**Layer 5 — Polish**: none yet

### Bottleneck systems (high dependency fan-in)
**Currency System, Account/Auth, Save/Persistence** — nearly everything
depends on these three. Get them right first; mistakes cascade everywhere.

### Resolved near-circularity
Currency System and Anti-Cheat looked mutually dependent at first glance.
Resolved via a one-directional contract matching the prototype's proven
`gameRules.ts` pattern: **raw mini-game result → Anti-Cheat computes + clamps
the reward → Currency System only ever credits already-validated rewards.**
No actual cycle.

**[Self-review — TD-SYSTEM-BOUNDARY gate, performed directly; no
`technical-director` subagent or `director-gates.md` criteria doc exists in
this environment]**: Boundary is sound, matches proven code. Real risk: Daily
Quests depends on every mini-game reporting results in a common shape, but
the 3 undesigned mini-games have no result schema yet. **Recommend defining a
shared mini-game-result interface during `/create-architecture`**, not
per-game, so quest integration doesn't fragment across 5 different shapes.

## Priority Tiers & Design Order

### MVP (prove the core loop: hub + economy + one mini-game)
1. Account/Auth
2. Save/Persistence
3. Analytics/Event Tracking
4. Currency System
5. Anti-Cheat/Replay Verification
6. Shared Hub
7. Level/Difficulty Config (Ricochet)
8. Boss AI/Damage Model
9. Super Ricochet
10. Hub UI
11. Ricochet HUD (Per-Mini-Game HUD, Ricochet instance)

### Vertical Slice (prove the "collection" concept + mascots)
12. Obstacle Spawn/Difficulty Ramp (Runner)
13. Mascot Database + Rarity Logic
14. Currency Ledger/Transaction Log
15. Quack Runner
16. Daily Quests
17. Login Streak
18. Mascot Gallery/Equip UI
19. Runner HUD (Per-Mini-Game HUD, Runner instance)
20. Leaderboard

### Alpha (full content + monetization depth)
21. Live-Ops Event/Flag Config
22. 3 Undesigned Mini-Games *(needs its own `/brainstorm` pass first — no mechanics exist to sequence yet)*
23. IAP/Receipt Validation
24. Shop/Cosmetics/Battle Pass

### Full Vision
*(empty — expected this early in the project)*

**[Self-review — PR-SCOPE gate, performed directly; no `producer` subagent
available]**: MVP tier (11 systems) is a realistic solo/small-team slice —
deliberately excludes new mini-games and mascots from MVP, proving the loop
before expanding content. Anti-Cheat + Analytics landing in MVP is more
upfront investment than a typical MVP tier, but justified: it was explicitly
hardened as a decision earlier this session, and retrofitting trust/telemetry
later is more expensive than building it in from the start.

## High-Risk Systems
- **Currency System, Account/Auth, Save/Persistence** — bottlenecks, everything depends on these
- **Anti-Cheat/Replay Verification** — security-critical complexity, explicitly elevated beyond the prototype's MVP-grade clamping
- **3 Undesigned Mini-Games** — largest unscoped content gap; blocks Daily Quests' shared result-schema until defined

## Progress Tracker

| System | GDD Status |
|---|---|
| Account/Auth | In Review — `/design-review` (2026-07-20 + 2026-07-21 fixes): **ALL 7 BLOCKERS INTEGRATED**. Initial verdict: 7 blocking items. Follow-up fixes applied: (1) Password recovery — added email-link reset flow (Core Rule 1b). (2) Apple OIDC algorithm validation — clarified `alg` check before signature (Core Rule 6). (3) Device secret lifecycle — regenerated on password link to maintain silent refresh (Core Rule 5). (4) 8s timeout — documented as 95th-percentile network latency accommodation. (5) Rate-limiting — soft CAPTCHA challenge after 5 attempts instead of hard lockout (Core Rule 7). (6) Device loss recovery — cloud-sync backup to iCloud Keychain/Play Credential Manager (Core Rule 9). (7) Throttle boundary tests — ACs added for 4th/5th attempt and CAPTCHA success scenarios, with 3 boundary-test ACs verifying 5-attempt threshold and CAPTCHA success clears counter. Ready for re-review. Full findings in `design/gdd/reviews/account-auth-review-log.md`. |
| Save/Persistence | In Review — `/design-review` (2026-07-20 + 2026-07-21 fixes): **ALL 6 BLOCKERS INTEGRATED**. Initial verdict: 6 blocking items. Follow-up fixes applied: (1) Concurrent-write race condition — lock-order enforcement per ADR-0005, 2 ACs added to test violations and 3-attempt rejection. (2) Offline cache SLA undefined — Tuning Knob: cache TTL 60 seconds; conflict resolution: timestamp-based LWW (server wins). (3) Reward-gating undeclared to dependents — Interactions section updated with explicit REWARD-GATING CONTRACT to Daily Quests, Mascots, IAP systems declaring server-exclusive reward authority. (4) PostgreSQL migration non-existent — Core Rule 2b added: schema outline (Player table: id, playerId, wallet DECIMAL, xp INT, cosmetics JSONB, lastSync TIMESTAMP), SERIALIZABLE isolation, WAL-based rollback procedure. (5) Lock deadlock risk — Core Rule 4 strengthened to mandatory lock-order enforcement; ACs test single and 3-attempt violations timeout/reject as 409. (6) Offline UX undefined — Core Rules 5b-5d added: offline banner, sync-conflict feedback, error-recovery UI with retry button. Ready for re-review. Full findings in `design/gdd/reviews/save-persistence-review-log.md`. |
| Analytics/Event Tracking | Approved — `/design-review` completed 2026-07-21: **APPROVED** after targeted fixes. Initial review (2026-07-14) found 5 blocking items; AC revisions #6, #8, #11 closed identify-idempotency, PII deny-list, server-event-enforcement gaps. Follow-up fixes added: Core Rule 7 (device-scoped ID generation via UUID v4 in secure storage, new ID on uninstall/reinstall), session-boundary AC, AC #5 revised for uninstall/reinstall testing. All 6 original blockers + session boundary now resolved. Full findings in `design/gdd/reviews/analytics-event-tracking-review-log.md`. |
| Live-Ops Event/Flag Config | Not Started |
| Shared Hub | Designed — pending independent `/design-review` in a fresh session (`design/gdd/shared-hub.md`) |
| Currency System | Designed — fix verified by independent re-review (2026-07-09), pending `/design-review` |
| Currency Ledger/Transaction Log | Designed — CD-GDD-ALIGN APPROVED WITH CONDITIONS (2 conditions folded in same pass), pending independent `/design-review` in a fresh session (`design/gdd/currency-ledger-transaction-log.md`) |
| Anti-Cheat/Replay Verification | Designed — fix verified by independent re-review (2026-07-09), pending `/design-review` |
| Mascot Database + Rarity Logic | Designed — CD-GDD-ALIGN APPROVED WITH CONDITIONS (1 fixed same pass), pending independent `/design-review` in a fresh session (`design/gdd/mascot-database.md`) |
| Level/Difficulty Config (Ricochet) | Designed — boss-HP-vs-board-capacity divergence resolved via a hard cap at level 30 (2026-07-09); `maxBrickHp` remains a separate unbounded lever not yet addressed (drives the danger-line loss condition, unrelated to the boss-HP fix); both need `/balance-check` telemetry; pending `/design-review` |
| Obstacle Spawn/Difficulty Ramp (Runner) | Designed — CD-GDD-ALIGN APPROVED WITH CONDITIONS (2 conditions folded into Open Questions as binding gates), pending independent `/design-review` in a fresh session (`design/gdd/obstacle-spawn-difficulty-ramp-runner.md`) |
| Boss AI/Damage Model | Designed — fix verified by independent re-review (2026-07-09), pending `/design-review` |
| Super Ricochet | Designed — pending independent `/design-review` in a fresh session (`design/gdd/super-ricochet.md`) |
| Quack Runner | Designed — CD-GDD-ALIGN APPROVED WITH CONDITIONS (3 conditions folded in same pass), pending independent `/design-review` in a fresh session (`design/gdd/quack-runner.md`) |
| 3 Undesigned Mini-Games | Not Started |
| Daily Quests | Designed — CD-GDD-ALIGN APPROVED WITH CONDITIONS (2 conditions folded in same pass), pending independent `/design-review` in a fresh session (`design/gdd/daily-quests.md`) |
| Login Streak | Designed — CD-GDD-ALIGN APPROVED WITH CONDITIONS (2 conditions folded in same pass), pending independent `/design-review` in a fresh session (`design/gdd/login-streak.md`) |
| IAP/Receipt Validation | Not Started |
| Shop/Cosmetics/Battle Pass | Not Started |
| Hub UI | Designed — pending independent `/design-review` in a fresh session (`design/gdd/hub-ui.md`) |
| Mascot Gallery/Equip UI | Designed — CD-GDD-ALIGN APPROVED WITH CONDITIONS (2 conditions folded in same pass), pending independent `/design-review` in a fresh session (`design/gdd/mascot-gallery-equip-ui.md`) |
| Per-Mini-Game HUD | Ricochet instance Designed (`design/gdd/ricochet-hud.md`, pending review) — Runner instance Designed, CD-GDD-ALIGN APPROVED WITH CONDITIONS (2 conditions folded in same pass), pending independent `/design-review` in a fresh session (`design/gdd/runner-hud.md`) |
| Leaderboard | Designed — CD-GDD-ALIGN APPROVED WITH CONDITIONS (4 conditions folded in same pass), pending independent `/design-review` in a fresh session (`design/gdd/leaderboard.md`) — **closes out the entire Vertical-Slice tier: no undesigned systems remain in that priority group** |
