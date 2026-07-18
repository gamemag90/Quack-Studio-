# ADR-0008: UI System — UGUI Primary, UI Toolkit Deferred

## Status
Proposed

## Date
2026-07-10

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Unity 6.3 LTS (build `6000.3.0f1`) — client-side |
| **Domain** | UI |
| **Knowledge Risk** | MEDIUM — UI Toolkit's 6.3 additions (native SVG, CSS-style filters, Shader Graph integration) are post-cutoff, but this ADR **does not adopt them**; the chosen system (UGUI) is stable and predates the cutoff |
| **References Consulted** | `docs/engine-reference/unity/current-best-practices.md` (UI section), `breaking-changes.md`, `deprecated-apis.md`, `.claude/docs/technical-preferences.md` (perf budgets) |
| **Post-Cutoff APIs Used** | None (UGUI is long-stable) |
| **Verification Required** | Canvas Scaler + Safe Area verified on a notched iPhone (Dynamic Island) and an Android display-cutout device; draw calls within the ≤150 budget on a mid-range device |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | None |
| **Enables** | Hub UI + navigation implementation (future shared-hub-nav ADR), Ricochet HUD, boss-AI event display |
| **Blocks** | Any UI screen implementation |
| **Ordering Note** | Independent; MEDIUM-risk client decision |

## Context

### Problem Statement
Unity 6.3 ships **two** UI systems — UGUI (Canvas/`RectTransform`, mature, "maintenance mode" but fully supported) and UI Toolkit (UXML/USS retained-mode, modern, gaining native SVG + CSS-style filters in 6.3). They can coexist, but each is a distinct mental model and UI Toolkit is *not* a drop-in for Canvas patterns. Quack Studio needs UI for a data-driven **Hub** (`hub-ui.md`: currency chips, mascot grid, quests) and a gameplay-coupled **in-game HUD** (`ricochet-hud.md`: top bar, boss HP fill, ball count, overlaid on the playfield). The team has **no Unity UI specialist** (`.claude/docs/technical-preferences.md`). This ADR picks the UI system(s).

### Constraints
- Mobile (iOS + Android): many screen densities, aspect ratios, and **notches/cutouts/Dynamic Island** → safe-area and resolution-scaling correctness is non-negotiable.
- Perf budget (`technical-preferences.md`): 60fps / ≤150 draw calls / mid-range memory ceilings.
- The Ricochet HUD wants **juice** (score pops, HP-bar shake) per the game-feel direction — tight coupling to gameplay animation/particles.
- No UI specialist → favor the mature, best-documented system with one mental model, consistent with this project's risk posture in ADR-0002/0003.

## Decision

**UGUI (Canvas) is the primary UI system for both the Hub and the in-game HUD.** UI Toolkit is **not** used now, but is **explicitly permitted later for a single, whole, data-heavy screen** (e.g. a large shop/collection) if and when it earns its place (see the gate below).

### 1. UGUI for everything now
- One mental model, mature, huge documentation/community base — the lowest-risk choice with no specialist to lean on.
- Best fit for the **gameplay-coupled, juicy HUD**: particles, tweens, world-space anchoring, and animation attach naturally to Canvas elements, supporting the game-feel direction.
- Mobile perf characteristics are well-understood and predictable.

### 2. Mobile-correct UGUI setup (non-negotiable specifics)
- **EventSystem / input module** (load-bearing — without it the UI receives **zero touches**): Unity 6 defaults to the new Input System, and UGUI's legacy `StandaloneInputModule` is incompatible with it. The `EventSystem` **must** use **`InputSystemUIInputModule`**, with `EnhancedTouch` for mobile touch routing. This is a hard prerequisite for any UI interaction, not a detail.
- **Canvas Scaler**: `Scale With Screen Size`, a defined reference resolution, and a `match` value. Name the tradeoff (exact value → `/ux-design`): portrait wants a **height-biased `match` (≈1)** so tall 19.5:9 phones don't overflow horizontally, but that then over-scales 4:3 tablets — so **anchoring does the real responsive work, not scale**, and tablets likely need a **max-width clamp** on content. Scaler alone is not a responsive layout.
- **Safe Area**: a `SafeArea` component drives its children's `RectTransform` `anchorMin`/`anchorMax` (normalized) from `Screen.safeArea`. It **must recompute when `Screen.safeArea`, orientation, or resolution changes** (cache the last value, compare each frame) — computing only in `Start`/`OnEnable` is the classic shipped safe-area bug. Covers notch, Dynamic Island, Android cutout, and gesture bar. Verified on real notched hardware (see Verification).
- **Render mode**: Screen Space – Overlay (or – Camera where an effect needs it) for the HUD; anchor-based responsive layout (no absolute pixel positions).
- **Text**: TextMeshPro (bundled in `com.unity.ugui` in 6.3, no separate install) for crisp scaling across densities.

### 3. Perf discipline (to hold the ≤150 draw-call / 60fps budget)
"Atlases + canvas split" alone is **not** enough to hit the budget; the full load-bearing checklist:
- **Split static and dynamic elements onto separate Canvases**: a UGUI canvas rebuild is triggered **per-canvas**, so a frequently-updating HUD element (score, boss HP) on its own sub-Canvas means its change doesn't rebuild/re-batch the entire UI. Canvas rebuild cost is UGUI's classic mobile pitfall. **Caveat**: don't over-split — extra canvases break cross-canvas batching and *add* draw calls; balance rebuild isolation against the ≤150 budget.
- **Disable `Raycast Target`** on every non-interactive Graphic (all text, decorative images) — the single highest-value UGUI mobile CPU win (the raycaster otherwise walks every raycastable graphic per touch).
- **Use `RectMask2D`, not `Mask`** — stencil `Mask` breaks batching and adds draw calls, directly undercutting the ≤150 target.
- **No Layout Groups / `ContentSizeFitter` on dynamic content** — they force a rebuild on every change (worst exactly on the HUD/lists that update often); position dynamic items manually or use a pooled list.
- **Watch overdraw** — full-screen or stacked transparent images are a fill-rate killer on mid-range mobile GPUs; keep transparent layering minimal.
- **Sprite atlases** for UI art so Canvas elements batch into few draw calls.
- **Event-driven HUD updates, not per-frame polling** — the HUD reacts to gameplay events (score changed, HP changed) rather than reading state every `Update()`, consistent with the game-ui-ux patterns.

### 4. The UI Toolkit deferral gate (when it may be introduced)
UI Toolkit may be adopted **later** only when **all** hold:
- The screen is genuinely **data-heavy** (large dynamic lists/grids where UI Toolkit's retained-mode + USS styling pays off — e.g. a big shop or mascot-collection browser), AND
- it is an **entire screen**, not UI Toolkit widgets mixed *inside* a UGUI screen (mixing the two within one screen is fiddly and is disallowed), AND
- the **two-system cost is consciously accepted** at that point (a new ADR or an amendment records it).
Until then, **do not** introduce UI Toolkit. This defers the two-system maintenance cost until a concrete need justifies it, while keeping Unity's supported coexistence path open.

### Architecture Diagram
```
                 UGUI (Canvas) — primary, both Hub and HUD
   ┌───────────────────────────────┬───────────────────────────────┐
   │ Hub screens (menu/data)        │ In-game Ricochet HUD (overlay) │
   │ currency chips, mascot grid,   │ top bar, boss HP fill, balls   │
   │ quests                         │ juice: pops/shake via tweens    │
   │ Canvas Scaler + SafeArea        │ dynamic elems on own Canvas     │
   │ static/dynamic canvas split     │ event-driven updates            │
   └───────────────────────────────┴───────────────────────────────┘
                 UI Toolkit — NOT used now
                 (allowed later for ONE whole data-heavy screen, via gate §4)
```

## Alternatives Considered

### Alternative A: Hybrid now — UI Toolkit for menus/Hub, UGUI for HUD
- **Description**: Unity's "right tool for the job" split from day one.
- **Pros**: UI Toolkit's SVG/resolution-independence is nice for data-heavy menus across densities; retained-mode suits list/grid menus.
- **Cons**: Two UI systems to learn and maintain immediately, with no specialist; more upfront cost and cognitive load for a small team; the Hub isn't yet data-heavy enough to require it.
- **Rejection Reason**: The deferral gate (§4) captures this benefit *when actually needed* without paying the two-system cost up front.

### Alternative B: UI Toolkit everywhere
- **Description**: Single modern system for all UI.
- **Pros**: One system, crisp SVG at any density, modern styling.
- **Cons**: Retained-mode is a weaker fit for the gameplay-coupled juicy HUD (particles/world-space/tight animation are more work); historical mobile input/perf gotchas; smaller community; team unfamiliar → highest risk exactly where reliability matters (in-game).
- **Rejection Reason**: Highest risk for this team on the most gameplay-critical surface; the HUD is where UGUI is strongest.

## Consequences

### Positive
- One mental model, mature and well-documented — lowest risk with no UI specialist.
- Strong fit for the juicy, gameplay-coupled HUD.
- Predictable mobile perf; the ≤150 draw-call budget is achievable with atlases + canvas separation.
- Coexistence path preserved: UI Toolkit can still arrive later for the one screen that warrants it.

### Negative
- UGUI is in "maintenance mode" — it gets fewer new features; the project forgoes UI Toolkit's SVG/styling niceties for now.
- Canvas rebuild/batching is a known footgun requiring discipline (mitigated by §3).
- If a very data-heavy screen appears, introducing UI Toolkit later means a second system then (a deliberate, gated trade).

### Risks
- **Risk**: Safe-area/scaling bugs on notched devices ship because they were only tested in the editor.
  **Mitigation**: Verification requires real notched iPhone + Android-cutout testing; `SafeArea` component is mandatory, not optional.
- **Risk**: A monolithic Canvas causes rebuild jank as the HUD updates.
  **Mitigation**: Static/dynamic canvas split + event-driven updates are registry stances, not suggestions.
- **Risk**: UI Toolkit creeps in ad hoc, creating an unplanned two-system mess.
  **Mitigation**: §4 gate is a registry forbidden-until-justified pattern (whole-screen only, never mixed in-screen, cost consciously accepted).

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|------------|-------------|---------------------------|
| hub-ui.md | Data-driven Hub screens (currency chips, mascot grid, quests) | UGUI Canvas with Canvas Scaler + atlases; UI-Toolkit escape hatch if it later grows data-heavy |
| ricochet-hud.md | Gameplay-coupled overlay HUD (top bar, boss HP fill, balls) | UGUI overlay Canvas, dynamic elements on their own Canvas, event-driven, juice via tweens |
| (both) | Mobile iOS/Android across densities + notches | Canvas Scaler `Scale With Screen Size` + mandatory `SafeArea` |

## Performance Implications
- **Draw calls**: Held to budget via sprite atlases + few Canvases; measured on a mid-range device (Verification).
- **CPU**: Canvas rebuilds bounded by static/dynamic split; event-driven updates avoid per-frame UI work.
- **Memory**: UI atlases sized within the mobile memory ceilings.

## Migration Plan
Greenfield UI. The prototype's web overlay patterns (`GameScreen.tsx` top bar) inform the *layout*, re-implemented natively in UGUI.

## Validation Criteria
- **Touch works at all**: confirm the `EventSystem` uses `InputSystemUIInputModule` and buttons/drags respond on device (the new-Input-System gotcha).
- Hub + HUD render correctly across portrait aspect ratios and on a notched iPhone (Dynamic Island) and an Android cutout device — nothing critical under a safe-area intrusion; **rotate/resize to confirm `SafeArea` recomputes** (not just Start-time). Use the **Device Simulator** for editor-side cutout/aspect coverage before hardware.
- HUD score/HP updates are event-driven (no per-frame polling) and do not force full-canvas rebuilds (profile the rebuild count).
- Draw calls ≤150 on a mid-range device with the busiest screen shown, with `Raycast Target` disabled on non-interactive graphics and `RectMask2D` (not `Mask`) verified.
- Juice (score pop, HP-bar shake) plays on the HUD without frame drops.

## Related Decisions
- `hub-ui.md`, `ricochet-hud.md` — the UI designs this system implements.
- Future: shared-hub navigation ADR (built on UGUI per this decision).
- The game-ui-ux and game-feel skill patterns (anchors/scaling/safe-area; juice) inform the UGUI implementation.

## Open Questions
- Which future screen (if any) actually justifies invoking the §4 UI Toolkit gate — unknown until the shop/collection scope firms up.
- Exact reference resolution + `match` value and the supported aspect-ratio matrix — pending `/ux-design`.
