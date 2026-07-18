# UX Spec: Hub UI

> **Status**: In Design
> **Author**: Abdulrahman Alenazan + Claude
> **Last Updated**: 2026-07-11
> **Journey Phase(s)**: unknown — no `design/player-journey.md` exists yet
> **Platform Target**: Touch, mobile (iOS 14+/Android API 25+) — per `technical-preferences.md`
> **Template**: UX Spec

---

## Purpose & Player Need

Hub UI is literally "home base": my duck, my progress, my currency, all in
one legible, satisfying place I want to return to. Unlike most screens in
the game, the player's goal here isn't a single task — it's an at-a-glance
status check that branches into whatever they came to do next (play a
specific game, check the shop, admire mascots, see how they rank).

What would go wrong without it: this is the screen a player sees most
often, so — per the GDD — it "carries the weight of 'does this feel like
one cohesive game' more than any other single screen." A cluttered,
illegible, or slow-to-parse Hub UI wouldn't just be one bad screen; it
would undermine the entire "one game, not five demos" promise Shared Hub's
architecture was built to deliver.

**The player arrives at this screen wanting to** quickly see where they
stand (currency, progress, mascots) and decide what to do next — with
minimal friction between "I'm here" and "I'm playing" or "I'm browsing."

---

## Player Context on Arrival

Reuses `design/ux/shared-hub.md`'s three arrival contexts directly, since
Hub UI renders whenever Shared Hub's `current_screen` is "Hub":

| Arrival | Emotional state (from shared-hub.md) | What Hub UI specifically needs to communicate |
|---|---|---|
| Fresh launch / post-auth | Neutral-to-eager | Immediate legibility — currency/progress visible without hunting, since this may be a returning player checking in briefly |
| Return from a mini-game run | Anticipatory | The stat/currency changes from that run should be visible and feel *connected* to the run just played — not a generic refresh that could be mistaken for "nothing happened" |
| Return from Shop/Mascot Gallery modal | Low-friction continuation | Any purchase should be immediately reflected (currency delta, new mascot in the preview) — confirms the purchase "landed" |

One addition specific to Hub UI as the *visual* layer: a first-time player
(0 mascots, default currency, no quest history) needs this screen to read
as *inviting*, not *empty* — per the GDD's Edge Cases, an empty mascot
gallery must show a "start collecting" state, not a blank space that looks
broken.

---

## Navigation Position

Hub UI is the visual content rendered when Shared Hub's `current_screen`
state equals "Hub" — it inherits Shared Hub's root position rather than
having its own separate place in the hierarchy. It is the default/landing
content: whenever a player isn't inside a mini-game or a modal, Hub UI is
what's on screen.

**Position statement**: `[Account/Auth] → Shared Hub (root) → Hub UI
(default content state)`

Reachable from anywhere in the app via any exit path that returns
`current_screen` to "Hub" (per `shared-hub.md`'s Entry & Exit Points) —
exiting a mini-game, closing a modal, or a fresh post-auth landing.

---

## Entry & Exit Points

**Entry**: Whenever Shared Hub's `current_screen` becomes "Hub" (see
`shared-hub.md` Entry & Exit Points for the full transition table) — Hub UI
has no entry logic of its own; it simply renders when selected.

**Exit points (the concrete tappable elements this screen provides):**

| Exit Destination | Trigger Element | Player carries this context |
|---|---|---|
| A specific Mini-Game | Tap a games-section hero-card | Selected game id |
| Shop modal | Tap the header's shop action | — |
| Mascot Gallery modal | Tap "View Collection" CTA on the mascot preview | — |
| Account/Auth | Tap the header's logout action | Explicit, player-initiated |
| *(read-only, no exit)* | Stat grid, daily quests card, leaderboard | These are informational — quest *claiming* is a future interaction once Daily Quests has its own GDD/spec, not yet in scope here |

Level-select grid entries route into their associated mini-game (same
destination as a hero-card, different entry point) — carries the selected
level, not just the game id.

---

## Layout Specification

### Information Hierarchy

Most-important-first, resolving the GDD's own open placement question for
the new mascot preview (confirmed: immediately after Games section):

1. **Currency chips** — always-visible, non-negotiable status (header, never scrolls away)
2. **Games section (hero-cards)** — the primary action; "what do I do right now"
3. **Mascot preview** *(NEW)* — the newest headline pillar; placed right after the primary CTA for early prominence, not buried below secondary/reference content
4. **Daily Quests card** — retention driver, visible but not the primary action
5. **Stat grid** — reference/bragging-rights info, lower urgency
6. **Level-select grid** — secondary navigation, subordinate to the games section itself
7. **Leaderboard** — social/competitive context, lowest immediate priority, naturally scrolls to the bottom

### Layout Zones

**Page structure**: sticky header (currency chips always visible, per
Information Hierarchy #1) + a single vertical scroll column for everything
else, in hierarchy order: Games section → Mascot preview → Daily Quests
card → Stat grid → Level-select grid → Leaderboard. Chosen over a tabbed
layout to keep the "one glance, one home" fantasy intact — nothing is
tucked behind a tab the player has to remember to check.

**Games section internal layout — resolves `hub-ui.md` Open Question #1**:
a **horizontal-scroll strip** of hero-cards, not a wrapping grid or
pagination. This scales from 2 to 5 cards with zero layout change (just a
longer strip) — a wrapping grid would orphan a half-empty row at 3 or 5
cards (exactly the stress-test the GDD's Edge Cases flag), and pagination
adds a tap between the player and a game they might want, working against
this screen's own stated purpose ("quick decide what to do next"). A
horizontal swipe is a natural, low-friction gesture on touch-only mobile.

### Component Inventory

| Zone | Component | Content | Interactive? | Pattern |
|---|---|---|---|---|
| Header | Avatar | Player's duck avatar | No (v1) | New — `avatar-display` |
| Header | Username + level/progress | Text + progress indicator | No | New — `progress-summary` |
| Header | Currency chips | Coin/gem balance | No | **Existing, GDD-named**: `currency-system.md`'s shared currency-chip component — reference, don't reinvent |
| Header | Shop action | Icon button | **Yes** → Shop modal | New — `header-icon-action` |
| Header | Logout action | Icon/text button | **Yes** → Account/Auth | Reuses `header-icon-action` |
| Games section | Hero-card, unlocked (×2-5) | Game art, name, "Play" CTA | **Yes** → Mini-Game | New — `hero-card` |
| Games section | Hero-card, **locked** | Grayed/dimmed game art + unlock requirement text (e.g. "Reach Level 5 to unlock"), no "Play" CTA | **No** — tap gives feedback, no navigation (see Interaction Map) | New — `hero-card` locked variant |
| Mascot preview | Collection progress | "12/40 owned" style count | No | New — `progress-summary` (reused) |
| Mascot preview | Rarity highlight thumbnails | Small mascot portraits, rarity-colored | No | New — `rarity-thumbnail` (must pair color with an icon/shape — art bible's colorblind-safety rule, Brick Red/Fern Green confusion risk) |
| Mascot preview | "View Collection" CTA | Button | **Yes** → Mascot Gallery modal | New — `text-cta-button` |
| Daily Quests card | Quest list items | Name, progress, reward | No (v1) | New — `quest-list-item`. Claiming is out of scope — no Daily Quests GDD/spec exists yet |
| Stat grid | Stat tiles (×3) | Best score / bricks destroyed / bosses defeated | No | New — `stat-tile` |
| Level-select grid | Level tiles | Level number, unlocked/locked state | **Yes** (unlocked only) → Mini-Game at that level | New — `level-tile` (locked variant must be visually distinct without relying on color alone) |
| Leaderboard | Top-N rows | Rank, avatar, name, score | No | New — `leaderboard-row` |

**10 new patterns** flagged for the future pattern library (none exist yet
to add to); 1 component correctly reuses a component the Currency System
GDD already named rather than reinventing it.

### ASCII Wireframe

```
┌─────────────────────────────────┐
│ [Avatar] Username  Lv.7  ▓▓▓░░  │  ← sticky header
│  🪙 1,240   💎 15      [🛒][⏻] │
├─────────────────────────────────┤
│  GAMES              ← swipe →   │
│ ┌────────┐┌────────┐┌────────┐  │
│ │ Super  ││ Quack  ││ (locked │  │  ← horizontal-scroll strip
│ │Ricochet││ Runner ││  game)  │  │
│ │ [Play] ││ [Play] ││         │  │
│ └────────┘└────────┘└────────┘  │
├─────────────────────────────────┤
│  MASCOTS            12/40  🏆   │
│ [🦆][🦆][🦆][🦆]  [View Collection >]│
├─────────────────────────────────┤
│  DAILY QUESTS                   │
│  • Win 3 runs        2/3        │
│  • Collect 50 coins  50/50 ✓    │
├─────────────────────────────────┤
│  STATS                          │
│ [Best Score] [Bricks] [Bosses]  │
├─────────────────────────────────┤
│  LEVELS                         │
│ [1][2][3][4][5][🔒][🔒][🔒]     │
├─────────────────────────────────┤
│  LEADERBOARD                    │
│  1. PlayerX      9,820          │
│  2. You          7,410          │
│  3. PlayerY      6,900          │
└─────────────────────────────────┘
       (scrolls below header)
```

---

## States & Variants

| State / Variant | Trigger | What Changes |
|---|---|---|
| **Loading** | First-ever load, no cache yet | Skeleton/spinner placeholders in place of header, games strip, and all content zones — never a blank screen (matches `shared-hub.md`'s cold-start state) |
| **Loaded** | Cache present (or refreshed) | Full hub renders from cache, then background-refreshes |
| **Stale-but-shown** | Background refresh failed | Cached data remains visible, no alarming error state |
| **0 unlocked mini-games** | Shouldn't happen (onboarding always unlocks Super Ricochet) but defensively handled | Games section shows an onboarding/empty state instead of a blank strip |
| **5-card games strip** | All mini-games unlocked | Horizontal-scroll strip (resolved above) keeps every card at minimum touch-target size regardless of count — the GDD's explicit stress test |
| **Empty mascot collection** | New player, 0 mascots owned | Mascot preview shows an inviting "start collecting" state (e.g., silhouette placeholders + a clear CTA), never a blank or broken-looking gallery strip |

Platform variants: none beyond what `shared-hub.md` already established
(touch-only, portrait, mobile-only — no alternate layout).

---

## Interaction Map

Mapping interactions for: **Touch only** (per `technical-preferences.md`) —
no gamepad, no hover.

| Component | Touch Action | Immediate Feedback | Outcome |
|---|---|---|---|
| Hero-card (unlocked) | Tap | Press-state visual, haptic tick | Hub UI → Mini-Game (via Shared Hub's debounced transition) |
| Hero-card (**locked**) | Tap | Non-interactive visual response (same subtle-shake/no-op feedback as locked level tiles, for consistency), no navigation | Stays on Hub UI — shows/reiterates the unlock requirement text rather than silently no-opping |
| Games strip | Horizontal swipe/drag | Scroll-follow with momentum + edge bounce at strip ends | Reveals additional hero-cards, no navigation |
| Shop header action | Tap | Press-state visual, haptic tick | → Shop modal |
| Logout header action | Tap | Press-state visual; **confirmation expected** (destructive-adjacent) | → Account/Auth |
| Mascot thumbnail strip | Horizontal swipe/drag (if more thumbnails than fit) | Scroll-follow | Reveals more rarity highlights, no navigation |
| "View Collection" CTA | Tap | Press-state visual, haptic tick | → Mascot Gallery modal |
| Level tile (unlocked) | Tap | Press-state visual, haptic tick | → Mini-Game at that level |
| Level tile (locked) | Tap | Non-interactive visual response (e.g. subtle shake), no navigation | Stays on Hub UI — locked tiles must not silently no-op with zero feedback |
| Stat tiles, quest items, leaderboard rows | None (display-only) | — | — |

**New decision, not in the GDD**: tapping **Logout** requires confirmation
(a destructive, session-ending action) rather than firing immediately on
tap — a standard mobile pattern to prevent accidental taps from ending a
session. Confirmed as a default here.

---

## Events Fired

Reuses the `hub_navigation` event **proposed in `shared-hub.md`** rather
than inventing a second one — this is the same navigation action, just
triggered from the concrete UI layer.

| Player Action | Event Fired | Payload / Data |
|---|---|---|
| Tap hero-card | `hub_navigation{destination, source:"hub"}` (proposed in `shared-hub.md`) | destination mini-game id |
| Tap Shop action | `hub_navigation{destination:"shop", source:"hub"}` | — |
| Tap "View Collection" CTA | `hub_navigation{destination:"mascot_gallery", source:"hub"}` | — |
| Tap level tile (unlocked) | `hub_navigation{destination, source:"hub", level}` | mini-game id + level number |
| Confirm Logout | `session_end` — already in the catalog | — |
| Tap locked level tile | *(none required)* — optional product-analytics candidate: a `locked_content_tapped{contentId}` event would tell the team what players *want* but can't reach yet — flagged as a nice-to-have, not decided here |
| Games/mascot strip swipe, stat tiles, quest items, leaderboard rows | No event — passive browsing, not a funnel step | — |

All navigation events here reuse `shared-hub.md`'s already-proposed
`hub_navigation` — this spec doesn't introduce a second, competing event
for the same action.

---

## Transitions & Animations

Since Shared Hub keeps Hub UI alive underneath mini-games/modals (never
destroyed), there's no discrete "enter/exit" in the usual sense — the
animations that matter here are state-change animations within an
already-visible screen.

- **Currency/stat value changes** (returning from a run, or a purchase in
  Shop): count-up animation on the changed number, not an instant jump —
  makes the change *felt*, consistent with the art bible's "chunky
  tactility" principle (physical, not printed-flat).
- **Hero-card / tile press states**: slight scale-down on tap-down, spring
  back on release — the standard tactile-button feel the art bible calls
  for throughout.
- **New mascot appearing** in the preview strip (after a Mascot Gallery
  acquisition): a brief pop-in/highlight, distinct from the static
  rest-state thumbnails, so a new acquisition is noticeable on return to
  Hub.
- **Quest completion**: a checkmark/complete-state animation on the quest
  item when its progress crosses 100% (relevant once Daily Quests exists —
  flagged as forward-looking, not blocking this spec).
- **Games/mascot strip scroll**: standard momentum scroll with edge bounce
  (native platform feel, not custom-eased).

**Reduced motion**: same unresolved project-wide gap already flagged in
`shared-hub.md` — this spec has real candidate animations (count-up,
pop-in, press-scale) that would need reduced-motion alternatives (e.g.,
instant value change instead of count-up) once that policy exists. Carried
into this spec's Open Questions too, not assumed resolved.

---

## Data Requirements

All read-only — per the GDD, "Hub UI has no data-fetching logic of its
own," consuming everything through Shared Hub's aggregation.

| Data | Source System | Read/Write | Notes |
|---|---|---|---|
| Avatar, username | Account/Auth | Read | Static per session, no refresh needed |
| Level/progress summary | Not yet GDD'd (implied by "level" in header) | Read | Source system unclear — flagged in Open Questions |
| Currency balance | Currency System (ADR-0004) | Read | Via Shared Hub, refreshed per its re-aggregation rules |
| Games unlocked/hero-card list | Level Select (not yet GDD'd) | Read | Placeholder fallback per Shared Hub's rule |
| Mascot collection progress + rarity highlights | Mascot Database (not yet GDD'd) | Read | Same placeholder fallback |
| Daily Quests list | Daily Quests (not yet GDD'd) | Read | Same placeholder fallback |
| Stat grid values | Not yet GDD'd — likely per-mini-game aggregate stats | Read | Source system unclear — flagged in Open Questions |
| Level-select unlock state | Level Select (not yet GDD'd) | Read | Same placeholder fallback |
| Leaderboard top-N | Leaderboard (not yet GDD'd) | Read | Same placeholder fallback |

**Two genuine gaps this spec surfaced that `shared-hub.md` didn't need to
name** (since it only aggregates, doesn't render specific fields): the
**level/progress summary** and **stat grid** don't have an obvious owning
system yet — "level" isn't Level Select (that's mini-game unlock
progression, a different concept), and per-mini-game stats (best score,
bricks destroyed, bosses defeated) aren't owned by any named system either.
Flagged in Open Questions rather than guessed at.

---

## Accessibility

No accessibility tier committed yet — defaulting to WCAG-AA as a working
baseline (same project-wide gap `shared-hub.md` already flagged).

- **Keyboard/gamepad navigation**: N/A — touch-only platform, no keyboard
  or gamepad target.
- **Minimum touch target size**: **44×44pt (iOS) / 48×48dp (Android)**
  minimum for every interactive element — explicitly binding for the games
  strip and level-select grid, since the GDD names the 5-card stress test
  as a real legibility/tappability risk. The horizontal-scroll strip layout
  (chosen above) keeps cards at a fixed comfortable size regardless of
  count, which is what makes this target achievable at 5 cards, not just 2.
- **Color-independent communication** (real, concrete cases on this screen):
  - **Rarity thumbnails**: Fern Green (Common) vs. Brick Red (danger, used
    elsewhere in-game) is a known red-green confusion pair per the art
    bible — rarity must be conveyed by an icon/border shape too, never
    color alone.
  - **Locked level tiles / locked hero-cards**: must use a lock icon +
    reduced opacity, not merely a duller color, to communicate "locked."
  - **Quest completion**: a checkmark icon, not just a color/fill change.
- **Screen reader labels**: every icon-only element needs an explicit
  label — currency chips ("1,240 coins, 15 gems"), avatar, hero-cards (game
  name + locked/unlocked state spoken), stat tiles, leaderboard rank rows.
  None of this is optional given how icon-dense this screen is.
- **Reduced motion**: already flagged in Transitions & Animations and in
  Open Questions — the count-up/pop-in/press-scale animations all need a
  reduced alternative once the project-wide policy exists.

---

## Localization Considerations

| Element | Longest expected (EN) | Layout-critical? | Note |
|---|---|---|---|
| Hero-card "Play" CTA | "Play" (4 chars) | **Yes — must stay one line** | Short button labels are the highest-risk category for 40% expansion; reserve generous horizontal padding |
| Hero-card game title | "Super Ricochet" (15 chars) | Yes, within card width | Future games' names are unknown length — card must truncate with ellipsis rather than overflow or wrap unpredictably |
| Username | Player-generated, unbounded | **Yes** | Must truncate with ellipsis at a fixed max width — never push the currency chips off-screen |
| Stat tile labels | "Bosses Defeated" (16 chars) | Yes, within tile width | Already long in English; 40% expansion (e.g. German) is HIGH PRIORITY risk — flag for the localization engineer explicitly |
| Quest text | Variable, potentially a full sentence | Yes, single line preferred | May need to allow 2-line wrap rather than forcing truncation, since quest meaning matters more than a game name would |
| Leaderboard player names | Player-generated, unbounded | Yes | Same truncation treatment as Username |
| Currency numbers | Locale-dependent formatting (thousands separators) | No layout risk, but correctness risk | Flagged in `shared-hub.md` already — this is where it actually renders |

**HIGH PRIORITY for the localization engineer**: stat tile labels and the
"Play" CTA — both are short, layout-committed text where 40% expansion has
nowhere to go without a design accommodation (e.g., icon-only CTA instead
of a text label, or abbreviated stat labels with a tooltip/long-press for
the full name).

---

## Acceptance Criteria

- [ ] Hub UI renders within 100ms of becoming active on a warm launch (matches `shared-hub.md`'s render budget) (performance)
- [ ] A player with 2+ unlocked mini-games sees each game as its own legible, tappable hero-card at minimum 44×44pt/48×48dp size (navigation/core purpose)
- [ ] With 5 unlocked mini-games, all 5 hero-cards remain individually legible and tappable via the horizontal-scroll strip — no orphaned or undersized card (core purpose, GDD stress test)
- [ ] While currency/quest data is still loading, cached or skeleton state shows immediately — never a blank screen (error/empty state)
- [ ] With 0 mascots owned, the mascot preview shows an inviting "start collecting" state, not a broken-looking blank gallery strip (error/empty state)
- [ ] Tapping Logout requires an explicit confirmation step before the session actually ends (core purpose / prevents accidental logout)
- [ ] Rarity is distinguishable on the mascot preview without relying on color alone (icon/shape present) (accessibility)
- [ ] All icon-only elements (currency chips, avatar, hero-cards, stat tiles) have screen-reader labels (accessibility)
- [ ] Header and content remain fully visible and unobstructed by device safe areas (notch, home indicator) across the target device range (resolution/device coverage)
- [ ] A locked hero-card is visually distinct from an unlocked one (grayed art + unlock requirement text) and does not navigate to the mini-game when tapped (core purpose)

---

## Open Questions

1. **Player journey map not yet created** — same project-wide gap as
   `shared-hub.md`.
2. **Accessibility tier not yet defined** — this spec defaulted to
   WCAG-AA; consider `/gate-check`.
3. **No reduced-motion policy exists** — this spec has real candidate
   animations (count-up, pop-in, press-scale) waiting on that decision.
4. **`hub_navigation` event still unapproved** — proposed in
   `shared-hub.md`, reused here consistently; still needs the Analytics
   GDD owner's sign-off.
5. **Level/progress summary source system is unclear** — "level" in the
   header isn't Level Select (that's mini-game unlock progression). No
   owning system identified yet.
6. **Stat grid source system is unclear** — best score / bricks destroyed
   / bosses defeated aren't owned by any named system yet.
7. **Optional `locked_content_tapped` analytics event** — flagged as a
   nice-to-have product-analytics signal, not decided.
8. **Logout confirmation UX** was confirmed as a default in this session
   but isn't written into `hub-ui.md` itself — worth a small GDD addendum,
   same pattern as Shared Hub's Android back-button note.
9. **Exact games-strip scroll mechanics** (snap-to-card vs. free scroll,
   partial-card-visible-as-affordance) — resolved *that* it's a horizontal
   strip, not resolved at the pixel/interaction-feel level; likely an
   art-director/UI-programmer implementation call.
