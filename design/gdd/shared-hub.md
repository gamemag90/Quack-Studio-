# Shared Hub

> **Status**: Designed
> **Author**: Abdulrahman Alenazan + Claude
> **Last Updated**: 2026-07-09
> **Implements Pillar**: Shared hub/economy
> **Creative Director Review (CD-GDD-ALIGN)**: APPROVED 2026-07-09 (self-reviewed — no `creative-director` subagent registered in this environment)
> **`/design-review` (2026-07-17)**: NEEDS REVISION — 3 blocking items
> (session-expiry edge case didn't gate on account type, contradicting
> account-auth.md's silent guest-refresh path; modal-during-mini-game
> transition was undesigned in this GDD's own States/Transitions table;
> offline-queue flush vs. Hub re-aggregation race unaddressed) plus 3
> recommended items. All folded in below; re-review pending.

## Overview

Shared Hub is the navigation and screen-orchestration layer — the single
source of truth for "what's currently active" (the Hub itself, a mini-game,
or a modal like Shop/Mascot Gallery) and the read-through aggregator that
pulls display data from every other system without owning any of it itself.
It is deliberately **not** the visual implementation — that's a separate
future Presentation-layer system ("Hub UI") this GDD hands off to. Shared
Hub is the plumbing that makes "one game, not five disconnected demos"
(the concept doc's framing) actually true at the code level.

## Player Fantasy

Both direct and infrastructural. Directly: this is the screen players spend
the most time in, tapping between mini-games and checking progress — it
needs to feel instant and cohesive, not like a loading-screen-riddled menu.
Infrastructurally: its orchestration logic is invisible when working, and
only noticed when it fails (a stale currency display, a mini-game that
won't load, a modal that traps the player). The fantasy it serves is "this
is one cohesive game with a home," not a launcher for five separate apps.

## Detailed Design

### Core Rules

1. Shared Hub owns **"current screen" state**: Hub itself, an active
   mini-game, or an open modal (Shop, Mascot Gallery) — a single source of
   truth for navigation, avoiding scattered ad-hoc screen-state bugs.
2. On entering the Hub, it **aggregates** (read-through, doesn't own) the
   latest state from every hub-visible system: Currency System (balance),
   Daily Quests + Login Streak (today's quests/streak), Mascot Database
   (owned mascots preview), Leaderboard (top N), Level Select (unlocked
   mini-game levels).
3. Navigating into a mini-game or modal **does not tear down** the Hub's
   aggregated state — returning to the Hub should feel instant, rendering
   from what's already cached (per Save/Persistence's cache-first pattern),
   then refreshing in the background since a run likely changed
   currency/quest progress. **[Clarified 2026-07-17]** This also applies
   when Save/Persistence's own Core Rule 9 (proactive reconcile after 5
   minutes foregrounded) fires while the Hub is the visible screen: the
   Hub must re-render with the reconciled values at that point, not merely
   let the underlying cache update invisibly until the next navigation
   event — otherwise a player parked on the Hub could see minutes-stale
   figures despite Save/Persistence having already fetched fresh ones.
4. Exactly **one** screen/modal is active at a time. Running multiple
   mini-games simultaneously (picture-in-picture, multitasking) is
   explicitly out of scope for MVP. **[Clarified 2026-07-17]** Opening a
   modal while a mini-game is the active screen is **rejected outright** —
   the player must exit the mini-game back to the Hub first. This was
   already ADR-0009's decision but had never been stated as a rule in this
   GDD itself, nor given a row in the States/Transitions table below.
5. **[NEW 2026-07-17] Re-aggregation on mini-game exit waits for the just-
   completed run's reward to finish flushing before it reads Currency/Daily
   Quest state.** Save/Persistence's offline-queue guarantees (flush
   completes before reconcile begins) are server/cache-side only — they say
   nothing about when the Hub itself is safe to read post-exit. Without this
   rule, a player on a flaky connection could exit a mini-game, have the
   Hub re-aggregate against a still-queued (not-yet-applied) reward, and
   immediately debounce-navigate into a second mini-game against a stale
   balance. Concretely: the Mini-Game→Hub transition blocks re-aggregation
   on the same flush-acknowledgment (or its timeout) Save/Persistence's own
   offline-queue flush produces, showing a brief "syncing" state if the
   flush hasn't resolved yet, rather than reading stale cache immediately.

### States and Transitions

| State | Trigger | Result |
|---|---|---|
| Hub | App launch, or return from a mini-game/modal | → Aggregated hub data displayed (cache-first, then refreshed) |
| Hub → Mini-Game | Player selects a mini-game | → Hub state preserved (not torn down); mini-game becomes the active screen |
| Mini-Game → Hub | Run ends, player exits | → Hub re-aggregates — at minimum, refresh Currency + Daily Quests, since a run likely changed both |
| Hub → Modal (Shop/Mascot Gallery) | Player opens a modal | → Hub remains underneath (paused, not destroyed); modal is topmost |
| Modal → Hub | Player closes modal | → Hub re-aggregates if the modal could have changed state (e.g. a Shop purchase) |
| Mini-Game (active) → Modal request | **[NEW 2026-07-17]** Player attempts to open a modal while a mini-game is active | → Rejected outright — must exit to Hub first (Rule 4) |
| Hub (foregrounded, idle) | **[NEW 2026-07-17]** Save/Persistence's Core Rule 9 proactive staleness reconcile fires (5 min foregrounded) | → Hub's already-rendered screen re-renders with the reconciled values, not just the underlying cache silently updating unseen |

### Interactions with Other Systems

- **Currency System, Daily Quests, Login Streak, Mascot Database,
  Leaderboard**: Shared Hub reads current state from each on entry/re-entry
  — it never owns or duplicates their data, and can render a
  loading/placeholder state for any system not yet implemented.
- **Account/Auth**: the Hub only renders once Authenticated; a session
  expiry's handling depends on account type — silent refresh for guests,
  redirect-to-reauth only for linked accounts or a failed guest refresh
  (see Edge Cases, corrected 2026-07-17).
- **Save/Persistence**: the instant-render-then-refresh pattern reuses
  Save/Persistence's local cache directly rather than the Hub inventing its
  own separate cache. **[Clarified 2026-07-17]** This includes Save/
  Persistence's own Core Rule 9 proactive reconcile (Rule 3, above) and its
  offline-queue flush-before-reconcile guarantee (Rule 5, above) — Shared
  Hub's re-aggregation is not a simpler, independent refresh trigger; it
  rides on Save/Persistence's actual state-freshness contract rather than
  assuming its own.

## Formulas

None — Shared Hub is a pure orchestration/state-machine layer with no
mathematical relationships of its own.

## Edge Cases

- **If a background re-aggregation fetch fails** (e.g. Currency re-fetch
  after a run): show the last-known cached value rather than a blank/error
  state — consistent with the non-blocking philosophy already established
  by Save/Persistence and Analytics. One subsystem's fetch failure must
  never break the whole Hub.
- **If a player rapidly taps between multiple mini-game entry points**
  before the Hub has finished re-aggregating: navigation is debounced — the
  last tap wins, never queuing up multiple simultaneous mini-game loads.
- **[CORRECTED 2026-07-17] If a modal (Shop) is open and the player's
  session expires mid-interaction**: behavior branches on account type,
  matching account-auth.md's own distinction — it must **not** be a single
  uniform "always redirect" rule. **Guest** accounts attempt the silent
  `/auth/guest/refresh` path first; if that succeeds, the modal stays open
  uninterrupted and the player never sees anything happened. Only if silent
  refresh itself fails (e.g. the device secret is missing/invalid) does the
  modal close and the player get routed to re-authenticate. **Linked
  (password/social) accounts** have no silent-refresh path per account-auth.md,
  so their expiry always closes the modal and redirects immediately — the
  original behavior described here was correct for this case only. A prior
  version of this edge case (and ADR-0009 §5) applied the linked-account
  behavior uniformly to guests too, which would force an unnecessary
  disruptive redirect on every guest JWT expiry — exactly the friction
  account-auth.md's silent-refresh path exists to avoid.

## Dependencies

- **Depends on** (hard): Account/Auth, Save/Persistence.
- **Depends on** (soft, read-through — renders placeholder if undesigned):
  Currency System, Daily Quests, Login Streak, Mascot Database, Leaderboard.
- **Depended on by**: Hub UI (Presentation layer — the visual
  implementation of this orchestration logic, a separate future system).

## Tuning Knobs

| Knob | Suggested Value | Too Low | Too High |
|---|---|---|---|
| Navigation debounce | 300ms | Rapid taps still race and double-load | Hub feels sluggish/unresponsive to legitimate quick taps |

## Visual/Audio Requirements

Deferred to the future **Hub UI** (Presentation layer) system, which owns
the actual visual implementation of this orchestration logic — matching the
same Core/Presentation split already established for Currency System (wallet
logic vs. shared chip UI). This GDD only establishes the screen inventory
below.

## UI Requirements

Screens/modals this system must support (detailed UX spec belongs to
`/ux-design` + the future Hub UI GDD, not here): **Hub** (home — currency,
quests, streak, level select, leaderboard, per the prototype's existing
`Hub.tsx` layout), **Mini-Game** (active game screen), **Shop** (modal),
**Mascot Gallery** (modal, new).

## Acceptance Criteria

- **GIVEN** a player returns to the Hub after completing a mini-game run,
  **WHEN** the Hub re-renders, **THEN** Currency and Daily Quest progress
  reflect the run's outcome (post server-authoritative crediting).
- **[NEW 2026-07-17] GIVEN** a mini-game's reward is still queued (offline
  or slow connection) at the moment the player exits to the Hub, **WHEN**
  the Hub attempts to re-aggregate, **THEN** it waits for the flush
  acknowledgment (or its timeout) before reading Currency/Daily Quest
  state, showing a brief "syncing" indicator rather than displaying a stale
  pre-run balance.
- **GIVEN** a background re-aggregation fetch fails, **WHEN** the Hub
  renders, **THEN** it shows the last-known cached values rather than a
  blank/error state.
- **[REVISED 2026-07-17] GIVEN** two taps on different mini-game entry
  points 100ms apart, **WHEN** the 300ms debounce window is evaluated,
  **THEN** exactly one mini-game loads (the last tapped). **GIVEN** two
  taps on different entry points 400ms apart, **WHEN** evaluated, **THEN**
  both register as distinct legitimate navigations (the first mini-game
  loads, is exited, then the second loads) — the debounce does not
  suppress a genuine second tap outside its window.
- **[NEW 2026-07-17] GIVEN** a mini-game is the active screen, **WHEN** a
  modal (e.g. Shop) is requested, **THEN** the request is rejected outright
  — the player must exit to the Hub first; no modal ever opens on top of
  an active mini-game.
- **[NEW 2026-07-17] GIVEN** one or more soft-dependency systems (Mascot
  Database, Leaderboard, Daily Quests/Login Streak) are not yet
  implemented, **WHEN** the Hub aggregates on entry, **THEN** it renders an
  explicit placeholder for each missing system rather than crashing,
  blank-screening, or omitting that section silently.
- **[REVISED 2026-07-17] GIVEN** a guest player's session expires while a
  modal is open, **WHEN** expiry is detected, **THEN** silent
  `/auth/guest/refresh` is attempted first and, on success, the modal
  remains open uninterrupted. **GIVEN** a linked-account player's session
  expires (or a guest's silent refresh itself fails), **WHEN** detected,
  **THEN** the modal closes and the player is routed to re-authenticate.

## Open Questions

1. Should the Hub prefetch the next-likely mini-game's assets while idle
   (to reduce load time on tap), or is on-demand loading sufficient for
   MVP? *Target: resolve during `/perf-profile` once asset sizes are known.*
