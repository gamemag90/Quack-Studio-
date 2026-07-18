# Architecture Review Report

> **Date:** 2026-07-18
> **Mode:** `/architecture-review` full
> **Engine:** Unity 6.3 LTS (build `6000.3.0f1`)
> **GDDs Reviewed:** 22 · **ADRs Reviewed:** 14 active (+1 rejected/retained)
> **Verdict:** CONCERNS

## Method note

Requirements baseline was taken from `systems-index.md`'s 24-system
enumeration plus each ADR's own *GDD Requirements Addressed* section, rather
than re-deriving every atomic technical requirement across all 22 GDDs. The
ADRs map their covered requirements explicitly and consistently, so coverage
is assessed at the system → ADR level. GDDs whose coverage was genuinely
ambiguous (Daily Quests, Mascot Database) were full-read to confirm they need
no dedicated ADR — both compose onto the foundational ADRs (0004/0005/0006/0007)
by explicit design.

---

## Traceability Summary

- Total systems (systems-index): 24 (incl. empty Polish tier + "3 Undesigned Mini-Games" placeholder)
- ✅ Covered: 19 designed systems
- ⚠️ Partial/implicit: 1 (Runner HUD — covered by ADR-0008's UGUI decision, which names only ricochet-hud)
- ❌ Gap: 4 — **all Not-Started systems** (no GDD exists, so no ADR can): Live-Ops Flag Config, 3 Undesigned Mini-Games, IAP/Receipt Validation, Shop/Cosmetics/Battle Pass

**Every *designed* system has architectural coverage.** All gaps are Alpha-tier
Not-Started systems, expected at this project stage.

### Coverage Matrix (system → ADR)

| System | Layer | ADR Coverage | Status |
|---|---|---|---|
| Account/Auth | Foundation | ADR-0003 (secure token storage); auth core = prototype port | ✅ |
| Save/Persistence | Foundation | ADR-0005 (server), ADR-0010 (client cache/offline queue) | ✅ |
| Analytics/Event Tracking | Foundation | ADR-0006 | ✅ |
| Live-Ops Event/Flag Config | Foundation | — | ❌ Not Started |
| Shared Hub | Core | ADR-0009 | ✅ |
| Currency System | Core | ADR-0004 | ✅ |
| Currency Ledger/Transaction Log | Core | ADR-0004, ADR-0012 | ✅ |
| Anti-Cheat/Replay Verification | Core | ADR-0001, ADR-0002, ADR-0007 | ✅ |
| Mascot Database + Rarity Logic | Core | composed on 0004/0005/0006/0007 (100% deterministic, no RNG) | ✅ |
| Level/Difficulty Config (Ricochet) | Core | ADR-0014, ADR-0002 | ✅ |
| Obstacle Spawn/Difficulty Ramp (Runner) | Core | ADR-0013, ADR-0014 | ✅ |
| Boss AI/Damage Model | Feature | ADR-0011 | ✅ |
| Super Ricochet | Feature | ADR-0002, ADR-0011, ADR-0014 | ✅ |
| Quack Runner | Feature | ADR-0013 | ✅ |
| Daily Quests | Feature | composed on 0004/0005/0006/0007 | ✅ |
| Login Streak | Feature | composed on 0004/0005/0006 | ✅ |
| 3 Undesigned Mini-Games | Feature | — | ❌ Not Started |
| IAP/Receipt Validation | Feature | — (ADR-0004 names a future IAP purchase-credit ADR) | ❌ Not Started |
| Shop/Cosmetics/Battle Pass | Feature | — (coin-sink gap, architecture.md QQ-04) | ❌ Not Started |
| Hub UI | Presentation | ADR-0008, ADR-0009 | ✅ |
| Mascot Gallery/Equip UI | Presentation | ADR-0005 (`mascot_equipped`), ADR-0008 | ✅ |
| Ricochet HUD | Presentation | ADR-0008 | ✅ |
| Runner HUD | Presentation | ADR-0008 (UGUI decision; names ricochet-hud only) | ⚠️ Implicit |
| Leaderboard | Presentation | ADR-0012 (per-game `leaderboard_scores` table) | ✅ |

### Coverage Gaps (no ADR — all Not-Started, expected)

- ❌ Live-Ops Event/Flag Config — Foundation tier, Alpha priority. No GDD yet.
  Suggested ADR when designed: "Live-ops feature-flag / remote-config architecture."
- ❌ IAP/Receipt Validation — ADR-0004 already reserves the seam ("future IAP
  purchase-credit ADR — reuses this path with receipt-derived idempotency keys").
- ❌ Shop/Cosmetics/Battle Pass — coin-sink gap (architecture.md QQ-04).
- ❌ 3 Undesigned Mini-Games — needs a `/brainstorm` pass before any ADR.

---

## Cross-ADR Conflicts

No **blocking** conflicts. The ADR set is unusually well-reconciled — most
cross-cutting concerns are already annotated in-place. Three items to close:

### CI-1 — ADR-0001 "clawback" vs ADR-0007 "flag-only" (reconciled in-text, not fully closed)
- ADR-0001 Decision §4 still contains retracted "clawback" wording, carrying a
  ⚠️ superseding note to ADR-0007's flag-only model.
- ADR-0007 explicitly requests "a one-line correction to ADR-0001."
- **Resolution:** apply the one-line correction to ADR-0001 so its prose matches
  the flag-only reward model (the direction is already agreed; only the wording lags).

### CI-2 — `tolerance_units` reconciliation (open, tracked — GDD-owner call)
- ADR-0002's fixed-point physics is bit-exact ⇒ `tolerance_units = 0` for that path,
  but `anti-cheat-replay-verification.md` + ADR-0001 justify 0–5 for float-rounding.
- ADR-0002 flags "⚠️ GDD/ADR-0001 SYNC REQUIRED" and routes it to the Anti-Cheat GDD owner.
- **Resolution:** GDD owner decides — 0 for the fixed-point physics path, vs. a small
  defense-in-depth tolerance for any display-derived values that leak into a submission.
  Do not silently change the constant.

### Hygiene — duplicate ADR-0002 number
- Two files share `0002`: `adr-0002-super-ricochet-physics-api.md` (REJECTED,
  retained as a rejected-alternatives record) and
  `adr-0002-deterministic-fixedpoint-physics.md` (active, Proposed).
- Intentional, but the shared number is a lookup trap. Consider renumbering the
  active file or clearly marking the rejected one in its filename.

---

## ADR Dependency Order (topologically sorted — no cycles)

```
Foundation (no dependencies):
  ADR-0001 Deterministic RNG
  ADR-0003 Secure token storage
  ADR-0005 Server persistence (Postgres)
  ADR-0008 UI system (UGUI)

Depends on Foundation:
  ADR-0002 Deterministic fixed-point physics (requires ADR-0001)
  ADR-0004 Currency atomic credit/debit (requires ADR-0005)
  ADR-0006 Analytics buffer/flush (requires ADR-0005)

Feature layer:
  ADR-0007 Replay re-simulation service (requires 0001, 0002, 0004, 0005)
  ADR-0010 Client cache / offline queue (requires 0004, 0007)
  ADR-0012 Persistence schema addendum (requires 0004, 0005)
  ADR-0013 Runner deterministic collision (requires 0001, 0002)
  ADR-0014 Ricochet deterministic spawn rolls (requires 0001)
  ADR-0011 Boss damage events (requires 0002, 0004, 0006, 0007)
  ADR-0009 Shared Hub navigation (requires 0008, 0010)
```

### 🔴 Linchpin risk (not a cycle)
**All 14 ADRs are `Status: Proposed`; none Accepted.** ADR-0002's determinism
**spike gate is BLOCKING** and transitively gates the entire scored-gameplay /
anti-cheat chain (0002 → 0007, 0011, 0013, 0014). Nothing in that cluster can be
promoted to Accepted until the spike proves ARM-IL2CPP == x86-CLI byte-identical
results. This is the single highest-leverage item in the architecture.

---

## GDD Revision Flags (Architecture → Design Feedback)

No engine-reality-contradicts-GDD flags — all GDD assumptions are consistent with
verified Unity 6.3 behavior.

Separately, the ADRs already routed determinism/exploit design-feedback to GDDs
(tracked, not silent — listed here for visibility, not as new findings):

| GDD | Flag | Source |
|-----|------|--------|
| obstacle-spawn-difficulty-ramp-runner.md | Cap `obstacleSpeed(t)` (unbounded → confirmed-structural replay/economy exploit) | ADR-0013 §2 |
| level-difficulty-config-ricochet.md | `level` input validation for `SpawnDensityThreshold` | ADR-0014 §2 |
| level-difficulty-config-ricochet.md | `max_brick_hp` remains unbounded | architecture.md QQ-03, systems-index |

---

## Engine Compatibility — CLEAN

- **Version consistency:** Unity 6.3 LTS (`6000.3.0f1`) stated identically in all 14 ADRs.
- **Engine Compatibility section:** present in 14/14 ADRs — no blind spots.
- **Deprecated API references:** none. ADRs actively avoid the traps —
  ADR-0003 bans `PlayerPrefs` and the deprecated `EncryptedSharedPreferences`;
  ADR-0008 mandates `InputSystemUIInputModule` (not `StandaloneInputModule`),
  `RectMask2D` (not `Mask`).
- **Post-cutoff APIs:** `UnityEngine.LowLevelPhysics2D`, UI Toolkit 6.3 features,
  and Platform Toolkit are each *considered and consciously rejected* with
  consistent reasoning — no two ADRs make contradictory assumptions about the
  same post-cutoff API.

---

## Architecture Document Coverage

⚠️ **`architecture.md` is STALE.** Dated 2026-07-09, it predates every ADR. It still
states *"ADR Audit: No existing ADRs… 0/28 TRs have ADR coverage"* and lists *"11
required ADRs… none written"* — but 14 ADRs now exist. Its layer map + module
ownership cover only the 11 MVP systems, omitting Vertical-Slice systems now designed
and ADR-backed (Quack Runner, Mascot DB, Currency Ledger, Leaderboard). Needs a refresh.

⚠️ **`control-manifest.md` is PARTIALLY STALE.** Covers ADR-0001–0011 only (missing
0012, 0013, 0014) and references the superseded singular `leaderboard_score` column
(ADR-0012 replaced it with the per-game `leaderboard_scores` table). It self-flags
provisional pending this review.

No orphaned architecture — every module in the architecture doc maps to a GDD.

---

## Verdict: CONCERNS

**Not PASS:** documentation-consistency drift (stale architecture.md +
control-manifest.md), plus every ADR still `Proposed` pending the ADR-0002
determinism spike.

**Not FAIL:** no Foundation/Core *designed* requirement is uncovered, and no
blocking cross-ADR conflict exists. All coverage gaps are Not-Started, Alpha-tier
systems.

### Path to PASS (priority order)
1. Run/pass **ADR-0002's determinism spike** — gates the entire scored-gameplay
   chain; unblocks Accepting ADR-0002/0007/0011/0013/0014.
2. Refresh **`architecture.md`** (ADR-audit + required-ADRs sections, Vertical-Slice
   systems) and **`control-manifest.md`** (add ADR-0012–0014; drop singular
   `leaderboard_score`).
3. Close **CI-1** (one-line ADR-0001 clawback correction) and **CI-2**
   (`tolerance_units` GDD-owner decision).
4. Author later-tier ADRs when their GDDs land: IAP/receipt-credit, Shop/coin-sink,
   Live-Ops flag config.

### Blocking Issues
None (FAIL-only section — no blocking issues at CONCERNS).

---

## Re-verification Addendum — 2026-07-18 (post task #27, fresh session)

An independent fresh session re-ran `/architecture-review` after the analytics-catalog
annotations (task #27: `streak_claimed` / `mascot_equipped` folded into the server outbox
across ADR-0004/0005/0006 + registry). Rather than re-derive the same-day matrix above,
it verified each open finding against current file state:

- **CI-1** (ADR-0001 §4 clawback wording) — still present, one-line fix still outstanding.
- **CI-2** (`tolerance_units`) — still open, GDD-owner call; not silently changed (correct).
- **Duplicate ADR-0002** — `super-ricochet-physics-api` = REJECTED/superseded, `deterministic-fixedpoint-physics` = Proposed. Clean split, lookup-trap only.
- **architecture.md** — audit section refreshed; layer map still 11-MVP-only (semi-stale, self-flagged).
- **control-manifest.md** — still PROVISIONAL, still missing ADR-0012–0014. Its client-emission-ban list also doesn't yet include `streak_claimed`/`mascot_equipped` (derived-doc lag; resolves on regeneration).
- **All 14 ADRs** — still `Proposed`, pending the ADR-0002 determinism spike.

**Task #27 annotations validated:** the two-transaction outbox split
(`streak_claimed`→`mutateWallet` / ADR-0004; `mascot_equipped`→`updatePlayer` / ADR-0005)
is coherent across all three ADRs and the registry — no new cross-ADR conflict introduced.

**Verdict: CONCERNS (unchanged).** No file edits were made in this review session
(author/review independence preserved); the fixes below are handed off to a fresh session.
