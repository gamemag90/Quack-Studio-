# Hub UI

> **Status**: Designed
> **Author**: Abdulrahman Alenazan + Claude
> **Last Updated**: 2026-07-12
> **Implements Pillar**: Shared hub/economy; collectible mascots
> **Creative Director Review (CD-GDD-ALIGN)**: APPROVED 2026-07-09 (self-reviewed — no `creative-director` subagent registered in this environment)
> **`/design-review` (2026-07-17)**: NEEDS REVISION — 2 blocking items
> (missing a fourth "Syncing" state to mirror Shared Hub's newly-added
> flush-wait indicator, semantically distinct from Stale-but-shown; no
> visual treatment specified for undesigned soft-dependency placeholders,
> despite Hub UI being the layer that actually renders them) plus 4
> blocking AC gaps and 2 recommended items. All folded in below; re-review
> pending.

## Overview

Hub UI is the visual implementation of Shared Hub's orchestration/
aggregation layer — the actual screen composition, layout, and presentation
of currency, quests, mini-game entry points, mascots, and the leaderboard.
It carries over the prototype's proven `Hub.tsx` structure and "1980s arcade
cabinet" visual theme as the baseline, extended for the mascot-collection
system and additional mini-game entry points the concept doc scopes as new.

## Player Fantasy

Direct — this is literally the "home base" screen: my duck, my progress, my
currency, all in one legible, satisfying place I want to return to between
sessions. It's the screen a player sees most often, so it carries the
weight of "does this feel like one cohesive game" more than any other
single screen.

## Detailed Design

### Core Rules

1. **Layout** (carried over from the prototype's `Hub.tsx`): header (avatar,
   username, level/progress summary, currency chips, shop/logout actions) →
   games section (one hero-card per mini-game with a "Play" CTA) → stat grid
   (best score, bricks destroyed, bosses defeated) → daily quests card →
   level-select grid → leaderboard. **[Updated 2026-07-12]**: the header
   avatar sources from Mascot Gallery/Equip UI's `equippedMascotId` (a
   placeholder icon before any mascot is unlocked, auto-equipping on first
   unlock) — not a hardcoded prototype duck, matching the native pivot's
   collectible-mascots pillar.
2. **[NEW]** a mascot preview/entry point is added to this layout — surfacing
   collection progress (owned/total, rarity highlights) with a "View
   Collection" CTA — since Mascot Database is a headline new pillar absent
   from the prototype's layout entirely.
3. **[NEW]** the games section must scale gracefully from 2 hero-cards
   (today) to 5 (once all mini-games exist) without a redesign — a
   scrollable/grid section, not a layout hardcoded to a fixed 2-card stack.
4. **Visual theme carries over**: the prototype's arcade-cabinet palette
   (marquee orange, duck-pond teal, brick red) and typography system (Bungee
   display, Manrope body, Space Mono for scoreboard numbers) — proven and
   distinctive, adopted as the native app's baseline visual identity pending
   formal `/art-bible` work.
5. All interactive elements need visible focus states and must respect
   reduced-motion — carrying forward the accessibility investment already
   made in the prototype's later polish pass, treated as a baseline
   requirement, not native-exclusive new work.

### States and Transitions

Mirrors Shared Hub's states visually: **Loading** (cache-first
skeleton/spinner on first-ever load), **Loaded** (full hub from
cache-then-refreshed data), **Stale-but-shown** (a background refresh
failed — cached data remains visible without an alarming error state, per
Shared Hub's Edge Cases). **[NEW 2026-07-17]** **Syncing** (Shared Hub's
Rule 5: waiting for a just-completed mini-game run's reward to flush
before re-aggregating) — a **subtle, non-alarming** indicator distinct
from Stale-but-shown, since the two mean opposite things: Syncing is a
brief, expected wait for a known-good update that's already on its way;
Stale-but-shown is a failed fetch falling back to old data. Showing the
Stale-but-shown treatment during a normal post-run sync would incorrectly
read as something having gone wrong.

### Interactions with Other Systems

- **Shared Hub**: consumes its aggregated state directly — Hub UI has no
  data-fetching logic of its own.
- **Currency System, Daily Quests, Login Streak, Mascot Database,
  Leaderboard**: rendered via Shared Hub's read-through aggregation, never
  fetched independently by this system.
- **Mascot Gallery/Equip UI** *(added 2026-07-12, one-directional gap
  fixed here)*: the header avatar slot (Core Rule 1) reads
  `equippedMascotId` from this system — the first hard dependency this
  GDD has on it, distinct from the generic Mascot Database aggregation
  above. **[NEW 2026-07-17]** If `equippedMascotId` references a mascot the
  player no longer owns (a data-integrity edge case, not expected in normal
  play), the header falls back to the placeholder icon rather than
  rendering a broken/missing-asset reference.
- **[NEW 2026-07-17] Save/Persistence** (transitively, via Shared Hub):
  Shared Hub's proactive 5-minute staleness reconcile can update the
  aggregated data while the player is idly parked on the Hub, not only on
  a navigation transition — Hub UI must re-render in place when that
  happens, the same as any other data update Shared Hub delivers.

## Formulas

None — pure presentation layer.

## Edge Cases

- **If a player somehow has 0 unlocked mini-games** (shouldn't happen —
  onboarding always unlocks at least Super Ricochet — but defensively):
  show an onboarding/empty state, not a blank games section.
- **If the hero-card count grows to 5 games on a small mobile screen**: must
  remain legible and tappable at minimum touch-target size — a real
  layout stress-test the prototype's 2-card design never had to pass.
- **If mascot collection is empty** (new player, no mascots owned yet): show
  an inviting "start collecting" state, not a blank or broken-looking
  gallery preview.
- **[NEW 2026-07-17] If a soft-dependency system Shared Hub aggregates from
  (Mascot Database, Leaderboard, Daily Quests, Login Streak) is not yet
  implemented**: Hub UI renders that section as an explicit, clearly-labeled
  placeholder ("Coming soon" or equivalent) — never a blank gap, a broken
  layout, or an omitted section that shifts the rest of the page. Shared
  Hub's own Core Rule 2 requires this placeholder behavior generically;
  this is the layer that actually draws it, and until now this GDD never
  specified what it looks like.

## Dependencies

- **Depends on** (hard): Shared Hub.
- **Depended on by**: none — top of the Presentation layer.

**Consistency check**: Shared Hub's GDD lists "Depended on by: Hub UI" —
matches. ✅

## Tuning Knobs

None at the GDD level — visual/layout tuning belongs to `/art-bible` and
`/ux-design`, not this document.

## Visual/Audio Requirements

**[Self-review — art-director consult, performed directly]**: carries the
prototype's proven arcade-cabinet identity forward as the starting point,
not a from-scratch redesign — see Core Rule 4. Specific new asset needs
(mascot gallery cards, expanded games-section layout) are captured in UI
Requirements below and should feed `/asset-spec` once `/art-bible` exists.

## UI Requirements

Full screen inventory: **Hub** (home, per Core Rule 1's layout), **Mascot
Collection** (new — gallery/grid of owned + locked mascots, rarity
indicators), expanded **Games Section** (scales to 5 entries). Detailed
screen-by-screen UX spec belongs in `/ux-design`, run after `/art-bible`;
this GDD establishes the required screens and their data needs, not pixel
layout.

📌 **Asset Spec** — once the art bible is approved, run
`/asset-spec system:hub-ui` for per-asset visual specs.

## Acceptance Criteria

- **GIVEN** a player with 2+ unlocked mini-games, **WHEN** the Hub renders,
  **THEN** each game has its own legible, tappable entry point.
- **GIVEN** currency/quest data is still loading, **WHEN** the Hub first
  renders, **THEN** cached (or skeleton) state shows immediately — never a
  blank screen.
- **GIVEN** a background refresh fails, **WHEN** the Hub is showing,
  **THEN** stale cached values remain visible without an alarming error
  state.
- **GIVEN** 0 mascots owned, **WHEN** the mascot preview renders, **THEN**
  an inviting onboarding-style empty state shows, not a broken-looking
  blank gallery.
- **[NEW 2026-07-17] GIVEN** no mascot is yet equipped (`equippedMascotId`
  unset), **WHEN** the header renders, **THEN** a placeholder icon shows.
  **GIVEN** a player's first-ever mascot unlock occurs, **WHEN** the Hub
  next renders, **THEN** that mascot is auto-equipped and shown in the
  header — verifying both halves of Core Rule 1's avatar behavior, not
  just the placeholder case.
- **[NEW 2026-07-17] GIVEN** a player has 0 unlocked mini-games (the
  defensive edge case), **WHEN** the Hub renders, **THEN** an onboarding
  empty state shows in the games section, never a blank area.
- **[NEW 2026-07-17] GIVEN** the games section renders with 2, 3, 4, and 5
  hero-cards, **WHEN** each count is checked, **THEN** all cards render
  fully without clipping, overflow, or truncation at every count — a
  distinct claim from the 5-card touch-target/legibility Edge Case, since
  rendering completeness and usability are different failure modes.
- **[NEW 2026-07-17] GIVEN** all interactive elements on the Hub, **WHEN**
  keyboard/switch-control focus moves through them, **THEN** each shows a
  visible focus state; **GIVEN** the platform's reduced-motion setting is
  enabled, **WHEN** the Hub animates (transitions, hero-card scroll, etc.),
  **THEN** those animations are suppressed or minimized per Core Rule 5 —
  previously a stated baseline requirement with zero test coverage.
- **[NEW 2026-07-17] GIVEN** a player owns 3 of 12 total mascots including
  one rare-tier unlock, **WHEN** the mascot preview renders, **THEN** it
  shows the correct owned/total count (3/12) and highlights the rare
  mascot specifically — not just the empty-state case, which was the only
  scenario previously tested.
- **[NEW 2026-07-17] GIVEN** `equippedMascotId` references a mascot the
  player no longer owns, **WHEN** the header renders, **THEN** it falls
  back to the placeholder icon rather than displaying a broken or
  missing-asset reference.

## Open Questions

1. Exact visual composition once 5 mini-game hero-cards exist (grid vs.
   horizontal scroll vs. paginated) isn't decided. *Target: resolve during
   `/ux-design`, once more mini-games are actually designed.*
