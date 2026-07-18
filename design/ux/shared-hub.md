# UX Spec: Shared Hub (Navigation Shell)

> **Status**: In Design
> **Author**: Abdulrahman Alenazan + Claude
> **Last Updated**: 2026-07-11
> **Journey Phase(s)**: unknown — no `design/player-journey.md` exists yet
> **Platform Target**: Touch, mobile (iOS 14+/Android API 25+) — per `technical-preferences.md`
> **Template**: UX Spec

---

## Purpose & Player Need

Shared Hub is not a destination the player consciously seeks out — it's the
connective tissue that makes moving between mini-games, the shop, and
progress feel like navigating *one game*, not launching separate apps. The
player's implicit goal here is continuity: they want to jump from "just
finished a Super Ricochet run" to "check my coins" to "try Quack Runner"
without ever feeling like they left and re-entered something.

What would go wrong without it: without a single source of truth for "what's
active," the game would either force a jarring full reload between every
mini-game (killing the "one cohesive game with a home" fantasy the GDD names
directly — `shared-hub.md` Player Fantasy), or risk state bugs — stale
currency, a modal that traps the player, a mini-game that won't load — that
erode trust in the whole app, not just one screen.

**The player arrives at this screen wanting to** get back to what they were
doing, or decide what to do next — quickly, and without the game feeling
like it forgot where they left off.

---

## Player Context on Arrival

Shared Hub has three distinct arrival contexts, each with a different
emotional baseline (per `shared-hub.md`'s States table):

| Arrival | Immediately prior | Assumed emotional state |
|---|---|---|
| **Fresh app launch / post-auth** | Was outside the app, or just registered/logged in | Neutral-to-eager — arriving with intent to play, low patience for friction |
| **Return from a mini-game run** | Just finished (won or lost) a Super Ricochet/Runner run | Anticipatory — checking "what did I earn," carrying either satisfaction (win) or a shrug (loss, since runs are low-stakes) |
| **Return from a modal (Shop/Mascot Gallery)** | Just browsed or purchased | Low-friction continuation — didn't "leave" the game in their mind, expects to resume exactly where they were |

Players never arrive here involuntarily in a punitive sense — even a
session-expiry redirect (Edge Case in the GDD) is a neutral "please
re-auth," not a penalty state. The one thing all three contexts share:
**the player should never have to re-orient themselves** — the GDD's
cache-first-then-refresh rule exists specifically so returning feels
instant, not like a reload.

---

## Navigation Position

Shared Hub sits at the **root** of the navigation hierarchy — it is not
itself reached "from" anywhere; rather, it *is* the always-present
orchestration layer that every other screen (Hub UI content, mini-games,
Shop, Mascot Gallery) exists inside of. Post-authentication, there is no
navigation path that leads *away* from Shared Hub's ownership — a player is
always "in" it, even while a mini-game or modal is the active foreground
content, because Shared Hub owns `current_screen` state and never tears
itself down.

**Position statement**: `[Account/Auth] → Shared Hub (root, persistent) →
{ Hub content | Mini-Game | Shop modal | Mascot Gallery modal }`

It is reachable exactly once per session, at the Authenticated transition,
and remains the active container until logout or session expiry returns the
player to Account/Auth.

---

## Entry & Exit Points

**Entry into Shared Hub (the shell itself):**

| Entry Source | Trigger | Player carries this context |
|---|---|---|
| Account/Auth | Successful login, register, or guest session start | Verified session token; player ID |

**Internal transitions (within the shell, once inside):**

| From | To | Trigger | Notes |
|---|---|---|---|
| Hub | Mini-Game | Player selects a mini-game entry point | Hub state preserved, not torn down (GDD Rule 3) |
| Mini-Game | Hub | Run ends, player exits | Currency + Daily Quests re-aggregated at minimum (a run likely changed both) |
| Hub | Shop / Mascot Gallery modal | Player opens the modal | Hub remains underneath, paused not destroyed |
| Shop / Mascot Gallery modal | Hub | Player closes modal | Re-aggregates if the modal could have changed state (e.g. a purchase) |

**Exit from Shared Hub (the shell itself):**

| Exit Destination | Trigger | Notes |
|---|---|---|
| Account/Auth | Explicit logout | Player-initiated, reversible (can log back in) |
| Account/Auth | Session expiry | Forced; if a modal was open it closes first (Edge Case — never leave a purchase-capable modal interactive with an invalid session) |

No exit here is truly irreversible from the player's perspective — even
session expiry just re-prompts login, and their progress is
server-authoritative, so nothing is lost by being routed out.

---

## Layout Specification

> **Note**: `shared-hub.md` is explicit that this system "is deliberately
> not the visual implementation — that's a separate future Presentation-layer
> system (Hub UI)." Accordingly, this section documents Shared Hub's
> **render-stack/orchestration model**, not a pixel layout — the actual
> visual zones belong to the Hub UI, Ricochet HUD, etc. specs.

### Information Hierarchy

N/A in the usual "what's most important on screen" sense — Shared Hub has
no persistent visual chrome of its own. Its "information" is entirely
state, not display: which single screen/modal is currently active
(`current_screen`), and what's paused-but-alive underneath it.

### Layout Zones

Not applicable as pixel zones. Instead, Shared Hub owns a **render-stack
model**: exactly one foreground layer (Hub / Mini-Game) plus at most one
modal layer on top (Shop / Mascot Gallery) that dims/pauses — never
destroys — whatever's beneath it (GDD Rule 4: exactly one screen/modal
active at a time).

### Component Inventory

Not UI widgets — orchestration primitives: `current_screen` state holder,
the modal stack (max depth 1 for MVP), the 300ms navigation debounce timer,
and the read-through aggregation calls fired on Hub (re-)entry (Currency,
Daily Quests/Streak, Mascot Database preview, Leaderboard top-N, Level
Select).

### ASCII Wireframe

Skipped — no pixel layout to diagram. A state diagram is more honest here:

```
                ┌──────────────┐
  Account/Auth ─▶│     Hub      │◀─────────────┐
                └──────┬───────┘               │
                       │ select mini-game        │ run ends
                       ▼                        │
                ┌──────────────┐               │
                │  Mini-Game   │───────────────┘
                └──────────────┘
         Hub ──open──▶ [Shop / Mascot Gallery modal] ──close──▶ Hub
                         (Hub paused underneath, not destroyed)
```

---

## States & Variants

| State / Variant | Trigger | What Changes |
|---|---|---|
| **Default** | Normal Hub entry, cache present | Aggregated data renders instantly from cache, then background-refreshes |
| **Cold-start empty** | Very first-ever app launch, no local cache exists yet | No cached values to render-first with — must show a loading/skeleton state until the initial server fetch completes (not blank) |
| **Stale-on-fetch-failure** | Background re-aggregation fetch fails (e.g. Currency re-fetch after a run) | Last-known cached values remain displayed — never a blank/error state (GDD Edge Case, non-blocking philosophy) |
| **Debounced-navigation** | Player taps two mini-game entries within 300ms | Only the last tap's target loads; the first is silently discarded, no error shown |
| **Session-expired-with-modal-open** | Session expires while Shop/Mascot Gallery is open | Modal force-closes, player routed to re-auth — never left able to interact with a modal that can no longer validate purchases |

Platform variants: none — single target input model (touch, portrait,
mobile-only per `technical-preferences.md`), so no alternate layout variant
exists at the orchestration layer. Progression-gating (locked mini-games,
etc.) is explicitly Level Select's concern, not Shared Hub's.

---

## Interaction Map

Mapping interactions for: **Touch only** (per `technical-preferences.md`) —
no gamepad support, no hover states.

| Component | Touch Action | Immediate Feedback | Outcome |
|---|---|---|---|
| Mini-game entry point | Tap | Press-state visual (owned by Hub UI); haptic tick | Hub → Mini-Game (debounced 300ms, last-tap-wins) |
| Shop entry point | Tap | Press-state visual; haptic tick | Hub → Shop modal (opens on top, Hub paused underneath) |
| Mascot Gallery entry point | Tap | Press-state visual; haptic tick | Hub → Mascot Gallery modal |
| Modal close control (X / outside-tap) | Tap | Modal dismiss animation (owned by the modal's own spec) | Modal → Hub, re-aggregate if state may have changed |
| **Android hardware/gesture back** | System back gesture | Same as modal close, or none if no modal is open | If a modal is open, back = close modal (same as the X control); if on Hub with nothing open, back = standard OS behavior (background/exit app) — Shared Hub does not intercept back on the Hub's bare state. Not covered by the GDD; confirmed as a default here, extending the existing modal-close transition rather than introducing new behavior. |

**Out of scope for this spec**: the actual in-mini-game "exit/pause" button
is owned by each mini-game's own UX spec — Shared Hub only owns what
happens *after* that exit fires (Mini-Game → Hub re-aggregation), not the
button itself.

---

## Events Fired

Checking each interaction against `analytics-event-tracking.md`'s fixed
catalog surfaces a gap: there is no event for hub navigation itself — the
catalog has `run_start`/`level_start` (fired *inside* a mini-game) but
nothing for "player tapped a hub entry point." Proposed as new catalog
scope below rather than silently invented, per ADR-0006's process (new
events allowed via GDD, must declare emission site).

| Player Action | Event Fired | Payload / Data |
|---|---|---|
| Tap mini-game entry point | *(none in current catalog)* — **proposed new**: `hub_navigation{destination, source:"hub"}` | destination mini-game id |
| Tap Shop entry point | *(none in current catalog)* — **proposed new**: `hub_navigation{destination:"shop", source:"hub"}` | — |
| Tap Mascot Gallery entry point | *(none in current catalog)* — **proposed new**: `hub_navigation{destination:"mascot_gallery", source:"hub"}` | — |
| Modal close | No event needed — not a distinct funnel step | — |
| Return from mini-game (run ends) | `run_complete` — already owned by the mini-game itself, not Shared Hub; Shared Hub only reacts to the transition, doesn't emit | — |

**Proposed new event**: `hub_navigation` — emission site: **client** (per
ADR-0006's ownership split, this is purely client-observable UI navigation,
not a server-authoritative state change, so it belongs in the client
buffer, not the server outbox). Flagged as a genuine addition to
`analytics-event-tracking.md`'s catalog, not decided unilaterally here —
routed to the Analytics GDD owner in Open Questions.

No action here writes persistent game state — Shared Hub's own
interactions are all navigation, not economy/progress mutations — so none
of this needs the architecture team's attention the way a state-writing
action would.

---

## Transitions & Animations

Shared Hub does not define visual transition style (fade/slide/scale) —
that belongs to Hub UI, each mini-game, and each modal's own spec. What
Shared Hub *does* own: **`current_screen` state updates immediately on
trigger, before any transition animation completes.** The animation is a
cosmetic overlay on an already-committed state change, never a gate on it —
this matters because the 300ms debounce (Tuning Knob) must measure against
actual state-commit time, not animation-visual-complete time, or rapid taps
could still race during a slow transition.

Modal open/close: modal appears/dismisses on top of a paused (not
destroyed) Hub — whatever visual treatment Hub UI/the modal specs choose,
the underlying Hub state must not reset during that animation.

**Reduced motion**: no project-wide reduced-motion policy exists yet for
`quack-studio` (the `quack-blaster` web prototype had one —
`prefersReducedMotion` gating screen-shake — but that hasn't been carried
into this native pivot's GDDs or art bible). Flagged as a real gap in Open
Questions rather than assumed covered.

---

## Data Requirements

| Data | Source System | Read / Write | Notes |
|---|---|---|---|
| Currency balance (coins, gems) | Currency System (ADR-0004 `mutateWallet`) | Read | Refreshed on every Hub re-entry |
| Daily Quests progress | Daily Quests (not yet GDD'd/ADR'd) | Read | Renders placeholder until that system exists (GDD explicit fallback) |
| Login Streak | Login Streak (not yet GDD'd/ADR'd) | Read | Same placeholder fallback |
| Owned mascots preview | Mascot Database (not yet GDD'd/ADR'd) | Read | Same placeholder fallback |
| Leaderboard top-N | Leaderboard (not yet GDD'd/ADR'd) | Read | Same placeholder fallback |
| Unlocked mini-game levels | Level Select (not yet GDD'd/ADR'd) | Read | Same placeholder fallback |
| `current_screen` state | Shared Hub itself | **Write** (owned) | The one piece of state this system actually owns, not read-through |

**Architectural note, not a UX decision**: four of the six read sources
(Daily Quests, Login Streak, Mascot Database, Leaderboard, Level Select)
don't have their own GDD or ADR yet — this spec relies on
`shared-hub.md`'s own "renders placeholder for undesigned systems" rule to
stay implementable now. Flagged, not silently assumed away.

---

## Accessibility

**No accessibility tier is committed yet for this project** — defaulting to
WCAG-AA as a working baseline; see Open Questions.

Most standard accessibility checklist items (text contrast, keyboard-only
navigation, color-independent communication) are N/A **at this layer
specifically** — Shared Hub has no visual chrome of its own, so those
requirements belong to Hub UI, the modals, and each mini-game's own specs.

What genuinely belongs here:
- **Screen-reader transition announcements**: since navigation here doesn't
  reload a page/DOM in the traditional sense (it's a state-driven
  show/hide), VoiceOver (iOS)/TalkBack (Android) need an explicit
  accessibility-focus/announcement event on every `current_screen` change
  (e.g., "Shop opened") — otherwise a screen-reader user gets no signal the
  screen changed at all.
- **Debounce vs. assistive-technology double-tap risk** (non-obvious):
  VoiceOver/TalkBack's "double-tap to activate" gesture can, depending on
  implementation, register as two rapid taps to the underlying app. If the
  300ms debounce logic treats that as "two navigation attempts, last wins,"
  it could silently activate the *wrong* target for an assistive-technology
  user. Implementation must distinguish an AT-mediated activate event from
  two independent raw taps — flagged as an implementation risk, not
  resolved here.

---

## Localization Considerations

No owned visible text — Shared Hub renders no labels, buttons, or copy of
its own; all player-facing text belongs to Hub UI, the modals, and each
mini-game's spec.

One thing worth flagging even though it's not rendered here: the currency
values this layer aggregates (coin/gem balances) will need locale-specific
number formatting (thousands separators, RTL digit rendering if Arabic is
ever a target locale) — that's Hub UI's formatting responsibility when it
displays the value Shared Hub hands it, but noting it here since this is
where the raw number originates in the read path.

---

## Acceptance Criteria

- [ ] Hub renders from local cache within 100ms of becoming active on a warm launch — no visible blank/loading flash (performance)
- [ ] Cold-start (no local cache exists) shows a loading/skeleton state, never a blank screen, until the initial server fetch completes (performance/empty-state)
- [ ] Tapping two different mini-game entry points within 300ms results in exactly one mini-game loading — the last tapped, never both, never neither (navigation)
- [ ] A failed background re-aggregation fetch (e.g. Currency re-fetch after a run) leaves the last-known cached values displayed — never a blank or error state (error state)
- [ ] Returning to the Hub after a mini-game run reflects the run's updated Currency and Daily Quest state, sourced from the server-authoritative result, not an optimistic local guess (core purpose)
- [ ] A session expiry while a modal (Shop/Mascot Gallery) is open force-closes the modal and routes to re-authentication before any further purchase-capable interaction is possible (core purpose / security-adjacent)
- [ ] Every `current_screen` transition fires a screen-reader accessibility-focus/announcement event (VoiceOver/TalkBack) (accessibility)

No display-resolution criterion is listed: this system owns no rendered
content of its own (see Layout Specification), so a resolution/pixel-density
acceptance check does not apply here — it belongs to Hub UI, Ricochet HUD,
and each modal's own acceptance criteria instead.

---

## Open Questions

1. **Player journey map not yet created.** Template available at
   `.claude/docs/templates/player-journey.md`. Designing this spec without
   one meant assumptions about emotional state on arrival (see Player
   Context on Arrival) — worth validating once a journey map exists.
2. **Accessibility tier not yet defined** for the project
   (`design/accessibility-requirements.md` doesn't exist). This spec
   defaulted to WCAG-AA. Consider running `/gate-check` to see whether this
   blocks any phase gate.
3. **No reduced-motion policy exists** for `quack-studio` (the
   `quack-blaster` prototype had one; it wasn't carried into this native
   pivot's GDDs or art bible). Needs a decision before any transition
   animation is implemented.
4. **New `hub_navigation` analytics event proposed** in this spec (Events
   Fired) — not yet approved by the Analytics GDD owner. Needs to be added
   to `analytics-event-tracking.md`'s catalog or an alternative agreed.
5. **Debounce vs. assistive-technology double-tap** (Accessibility section)
   is a flagged implementation risk, not a resolved design — needs
   engineering input on how VoiceOver/TalkBack activation events are
   distinguished from raw taps before implementation.
6. **Android back-button behavior** (Interaction Map) was confirmed as a
   working default in this session, but isn't yet written into
   `shared-hub.md` itself — worth a small GDD addendum so it's not only
   discoverable from the UX spec.
