# UX Spec: Ricochet HUD

> **Status**: In Design
> **Author**: Abdulrahman Alenazan + Claude
> **Last Updated**: 2026-07-11
> **Journey Phase(s)**: unknown — no `design/player-journey.md` exists yet
> **Platform Target**: Touch, mobile (iOS 14+/Android API 25+) — per `technical-preferences.md`
> **Template**: UX Spec

---

## Purpose & Player Need

Unlike the prior three specs, Ricochet HUD has a **direct** player fantasy:
it's the feedback layer that makes Boss AI/Damage Model's invisible
number-crunching *feel* like a fight. The boss bar visibly draining with
every hit, the ball count ticking down mid-volley — this HUD is literally
how the "chip away the boss" fantasy is perceived moment to moment, not
just infrastructure around it.

What would go wrong without it: if the HUD lagged, snapped instead of
tweened, or showed stale numbers, the boss fight would feel disconnected
from the player's actions — hits wouldn't *register* emotionally even if
they registered mechanically. This is the one spec so far where "just
infrastructure" isn't true at all.

**The player arrives at this screen wanting to** stay oriented mid-action —
know their ball count, see the boss taking damage, without ever having to
look away from the playfield to figure out what's happening.

---

## Player Context on Arrival

| Game State | Player's mental mode | What the HUD needs to support |
|---|---|---|
| **Aiming** | Focused, deliberate — planning the shot, reading the board | Aim-hint text visible; nothing distracting from trajectory planning |
| **Firing** | Tense, reactive — balls in flight, can't intervene, watching for outcomes | Live ball count so the player tracks how many are still resolving; boss bar draining in real time as hits land |
| **Over (win)** | Relief, anticipation — the run's reward is about to be revealed | HUD hands off to the result screen (a separate future concern, out of scope here) |
| **Over (loss)** | Deflated but low-stakes (runs are casual, per the game concept) | Same handoff — HUD's job ends cleanly, doesn't linger awkwardly |

Unlike the previous three specs, this isn't a single "player arrives"
moment — the HUD is continuously present for the run's duration, and its
job is to *never* need conscious attention on its own; every element
should be readable in a glance while the player's actual focus stays on
the playfield.

---

## Navigation Position

`Shared Hub (root) → Hub UI (hero-card or level-tile tap) → Ricochet HUD
(Super Ricochet's in-play screen)`

Reached exactly one way: tapping a Super Ricochet hero-card or an unlocked
level tile on Hub UI (per `hub-ui.md`'s Exit Destinations table). Not a
top-level destination — only reachable while a Super Ricochet run is
active, and exits back to Hub UI (via Shared Hub's Mini-Game → Hub
transition) when the run ends.

---

## Entry & Exit Points

**Entry:**

| Entry Source | Trigger | Player carries this context |
|---|---|---|
| Hub UI | Tap a Super Ricochet hero-card or unlocked level tile | Selected level (if from level-select grid) or default/next level (if from hero-card) |

**Exit:**

| Exit Destination | Trigger | Notes |
|---|---|---|
| Hub UI (via Shared Hub) | Tap top-bar Exit button, during **Aiming** or **Over** | Instant — nothing to confirm; during Aiming nothing is committed yet, during Over the run has already resolved |
| Hub UI (via Shared Hub) | Tap top-bar Exit button, during **Firing** | **Requires confirmation** ("Quit run?") — this is the one moment a mis-tap could forfeit real progress (balls in flight, a boss possibly near defeat). **Gameplay pauses while the confirmation is shown** (balls freeze in place, boss bar stops updating) — chosen over letting the sim keep running, because otherwise the boss could be defeated or all balls could resolve *while the player is still deciding*, making the question itself stale or misleading by the time they answer. Cancel resumes exactly where it paused, no state loss; Confirm abandons the run. See Data Requirements for the implementation cross-reference to ADR-0002's fixed-timestep accumulator. |
| Hub UI (via Shared Hub) | Run reaches **Over** (win or loss) | Automatic handoff to the result screen, then Hub UI — no player action required to trigger the transition itself |

---

## Layout Specification

### Information Hierarchy

1. **Boss HP bar** — the core fantasy driver ("chip away the boss"), must be glanceable at all times without looking away from the playfield
2. **Playfield** (out of scope, owned by Super Ricochet) — the player's primary visual focus throughout
3. **Ball count** (footer) — critical specifically during Firing, when the player is tracking in-flight resolution
4. **Aim-hint text** (footer) — critical only during Aiming, irrelevant/hidden otherwise
5. **Exit button** (top bar) — needed but must not compete visually for attention during action
6. **Level pill, currency display** (top bar) — lowest attention-frequency, present but peripheral
7. **Turn number, bricks destroyed** (footer) — informational/stat-tracking, lowest priority of the numbered elements

### Layout Zones

Fixed vertical stack, per `ricochet-hud.md` Core Rule 1: **top bar** (exit,
level pill, currency, mute) → **boss bar** (name, HP text, HP fill) →
**playfield canvas** (full remaining space, owned by Super Ricochet — this
system only reserves the space, doesn't render into it) → **footer** (ball
count, turn number, bricks destroyed, aim-hint text conditional on Aiming
state).

No alternatives considered — this is a direct, low-ambiguity port of the
prototype's proven `GameScreen.tsx` pattern, and the GDD itself states
there are no open questions here.

### Component Inventory

| Zone | Component | Content | Interactive? | Pattern |
|---|---|---|---|---|
| Top bar | Exit button | Icon | **Yes** → confirm (Firing) or instant (else) exit | Reuses `header-icon-action` (Hub UI) |
| Top bar | Level pill | Current level number | No | New — `level-pill` |
| Top bar | Currency display | Coin balance | No | **Existing, GDD-named**: `currency-system.md`'s shared currency-chip component — reused per this GDD's explicit instruction |
| Top bar | Mute toggle | Icon (muted/unmuted) | **Yes** — toggles shared audio state | New — `mute-toggle` (shared across all mini-games' HUDs, not Ricochet-specific — GDD Core Rule 4) |
| Boss bar | Boss name | Text | No | New — `boss-name-label` |
| Boss bar | HP text | Numeric HP display | No | New — `hp-text` |
| Boss bar | HP fill bar | Tweened fill bar (0.12s linear) | No | New — `tweened-hp-bar` |
| Playfield | *(out of scope — Super Ricochet owns this)* | — | — | — |
| Footer | Ball count | Live count of resolving balls | No | New — `live-counter` |
| Footer | Turn number | Current turn | No | Reuses `live-counter` |
| Footer | Bricks destroyed | Running count | No | Reuses `live-counter` |
| Footer | Aim-hint text | Guidance text, visible only during Aiming | No | New — `conditional-hint-text` |

**5 new patterns** flagged for the future library; the currency chip
correctly reuses the existing GDD-named component (consistent with how
`hub-ui.md` handled the same reuse).

### ASCII Wireframe

```
┌─────────────────────────────────┐
│ [✕]   Lv.3    🪙 1,240    [🔊]  │  ← top bar
├─────────────────────────────────┤
│  [Boss Name]      ▓▓▓▓▓░░░░ 620 │  ← boss bar (tweens on change)
├─────────────────────────────────┤
│                                   │
│                                   │
│         (playfield canvas —      │
│          owned by Super           │
│          Ricochet, out of         │
│          scope here)              │
│                                   │
│                                   │
├─────────────────────────────────┤
│ Balls: 3   Turn: 5   Bricks: 47  │  ← footer
│      (aim-hint text here,         │
│       Aiming state only)          │
└─────────────────────────────────┘
```

---

## States & Variants

| State / Variant | Trigger | What Changes |
|---|---|---|
| **Loading/entering level** | Level first loads | Playfield initializes (Super Ricochet's concern); HUD shows correct starting boss HP, ball count, level pill immediately — no flash of zero/placeholder values |
| **Aiming** | Player is aiming | Aim-hint text visible in footer |
| **Firing** | Balls released | Aim-hint text hidden; live ball count updates in real time |
| **Boss HP tweening** | Any hit lands | HP bar tweens smoothly over 0.12s toward the new value — if multiple hits land faster than the tween resolves, it continuously re-targets the latest value rather than queuing discrete tweens (GDD Edge Case, avoids a janky "catch-up" bar) |
| **Mute toggled mid-volley** | Player taps mute during Firing | In-flight sounds finish naturally; only new sounds are silenced going forward (GDD Edge Case — an abrupt cutoff would feel broken mid-action) |
| **Exit confirmation shown** | Exit tapped during Firing | Confirmation prompt overlays the HUD; **simulation is paused** — balls freeze, boss bar stops updating (see Entry & Exit Points) |
| **Exit confirmation cancelled** | Player taps Cancel | Simulation resumes exactly where it paused, no state loss |
| **Over** | Boss defeated or danger line crossed | HUD hands off to the result screen — explicitly out of scope here per the GDD ("a separate future concern") |

No loading-failure or empty-data states apply here — by the time this HUD
is active, Level/Difficulty Config, Boss AI, and Currency have already
supplied everything it needs (this GDD's own framing: "everything it
displays is already fully specified... this GDD is purely about
composition").

---

## Interaction Map

Mapping interactions for: **Touch only** (per `technical-preferences.md`) —
no gamepad, no hover. Only two components in this HUD are interactive;
everything else is display-only, and the actual aim/fire gestures belong
to Super Ricochet's own playfield (out of scope here).

| Component | Touch Action | Immediate Feedback | Outcome |
|---|---|---|---|
| Exit button | Tap | Press-state, haptic tick | During Aiming/Over: instant exit. During Firing: shows "Quit run?" confirmation first |
| Mute toggle | Tap | Icon swaps (muted/unmuted), haptic tick | Toggles shared audio-mute state (persists across sessions, all mini-games — GDD Core Rule 4) |
| Boss bar, ball count, turn number, bricks destroyed, level pill, currency display | None (display-only) | — | — |
| Aim-hint text | None (display-only, visibility is state-driven) | — | — |

---

## Events Fired

Checking against the catalog: `run_start`/`run_complete` exist, but
there's no event for a player **abandoning** a run mid-Firing — a real,
useful signal (quit rate, possibly correlated with difficulty spikes)
that's currently invisible.

| Player Action | Event Fired | Payload / Data |
|---|---|---|
| Exit tapped, run abandoned (confirmed quit during Firing, or instant exit during Aiming) | **proposed new**: `run_abandoned{miniGame:"super_ricochet", gameState, turnNumber}` | Which state it was abandoned in, how far into the run |
| Run reaches Over | `run_complete` (existing, owned by Super Ricochet — this HUD doesn't emit it, just hands off) | — |
| Mute toggled | *(none required)* — a UI preference, not a funnel/product signal worth tracking | — |
| Boss HP tween, ball count updates, aim-hint visibility | No event — passive display updates, not player actions | — |

**One proposed new event**: `run_abandoned` — client-emitted, since it's
the client observing its own local exit action, not a server-authoritative
state change. Flagged for the Analytics GDD owner, not decided
unilaterally.

---

## Transitions & Animations

- **Hub UI → Ricochet HUD (level entry)**: transition owned jointly with
  Shared Hub's navigation (per `shared-hub.md`); HUD elements should be
  populated and ready the instant the playfield becomes visible — no
  separate HUD-specific fade-in that lags behind the playfield's own
  appearance.
- **Boss HP bar**: the defining animation of this spec — **0.12s linear
  tween** on every HP change (GDD Tuning Knob, carried over exactly from
  the prototype). Continuously re-targets on rapid successive hits rather
  than queuing (States & Variants).
- **Ball count / turn number / bricks destroyed**: instant update, no
  tween — these are discrete counters, not a "draining" resource; a tween
  here would feel laggy rather than smooth (the tween is specifically
  reserved for the boss bar's "chip away" fantasy, not applied
  indiscriminately).
- **Exit confirmation prompt**: lightweight overlay fade-in/out, consistent
  with the non-heavy treatment used for Hub UI's own confirmation-style
  prompts.
- **Ricochet HUD → Hub UI (run ends)**: handoff to the result screen is
  explicitly out of scope (GDD: "a separate future concern") — this spec
  doesn't define that transition.

**Reduced motion**: same project-wide gap flagged in every prior spec —
but this one has a **GDD-mandated** animation (the boss HP tween is Boss
AI/Damage Model's explicit Visual/Audio requirement, not optional polish),
so a reduced-motion alternative here specifically needs to preserve *some*
non-instant feedback (e.g. a shorter tween rather than removing it
entirely) rather than just disabling it outright, unlike purely decorative
animations in prior specs.

---

## Data Requirements

All read-only, and unusually **real-time** compared to the prior three
specs — this is the first spec where "how often does this update" is a
first-class design question, not an afterthought.

| Data | Source System | Read/Write | Notes |
|---|---|---|---|
| Boss name, HP | Boss AI/Damage Model | Read | Real-time, dirty-checked (GDD Core Rule 3) — only re-renders when the value actually changes |
| Ball count, turn number, bricks destroyed, game state (Aiming/Firing/Over) | Super Ricochet | Read | Same dirty-check discipline, driven by Super Ricochet's stats callback |
| Currency balance | Currency System (ADR-0004) | Read | Lower update frequency than the above — only changes on coin pickup during the run |
| Level number (for level pill) | Carried as navigation context from Hub UI's entry tap | Read | Not a live system read — it's the level the player selected to enter with |
| Mute state | Client-local shared preference (not a game-data system) | **Write** (on toggle) | Persists across sessions and all mini-games' HUDs (GDD Core Rule 4) — the one genuinely local piece of state this spec owns |

**Performance-relevant flag, not a UX decision**: the GDD's dirty-check
requirement ("avoiding 60fps re-render storms") is a real architectural
constraint this spec depends on, not just a nice-to-have — boss HP and
ball count can change many times per second during a fast volley, and
naive re-rendering on every stats callback would be the exact performance
bug the prototype already learned to avoid.

**Implementation cross-reference for the exit-confirmation pause**: this
spec requires the simulation to pause while "Quit run?" is shown (States &
Variants). ADR-0002's client integration already has a mechanism this
naturally extends — the fixed-timestep accumulator's spiral-of-death clamp
(`MaxCatchUp`), which exists precisely to handle "time passed that
shouldn't be simulated." Gating the accumulator (stop feeding it
`Time.deltaTime`, or clamp accrued time to zero) while the confirmation is
up is the natural mechanism, not a new one — flagged here for whoever
implements this so it isn't rediscovered from scratch.

---

## Accessibility

No accessibility tier committed yet — WCAG-AA baseline (same project-wide
gap).

- **Minimum touch target size**: same 44×44pt/48×48dp standard established
  in `hub-ui.md`, applies to the Exit button and Mute toggle — the only two
  truly interactive elements here.
- **Screen-reader behavior during active gameplay is a real tension, not a
  checkbox**: continuously announcing every boss-HP tween or ball-count
  change would make VoiceOver/TalkBack unusable during a fast volley
  (potentially many announcements per second). The right pattern is
  **on-demand query, not continuous narration** — e.g., a screen-reader
  user can request current boss HP/ball count via an explicit gesture,
  rather than the HUD auto-announcing every dirty-checked update. This is a
  real design decision for the accessibility spec, flagged here rather than
  assumed solved by "just add labels."
- **Color-independent communication**: the boss HP bar already pairs its
  fill with numeric HP text (Component Inventory) — this satisfies "don't
  rely on color alone" by design, not as an afterthought, since a
  color-only low-HP indicator (e.g. shifting to red) would be unreadable to
  colorblind players without the accompanying number.
- **Contrast against a dynamic background**: unlike a static menu screen,
  the top bar and boss bar overlay a playfield whose visual content changes
  constantly — contrast must be guaranteed against a *range* of backgrounds
  (e.g. a solid-backed bar, not text floating directly over gameplay art),
  not verified against one static screenshot.
- **Reduced motion**: already flagged — the boss HP tween needs a reduced
  (not removed) alternative since it's GDD-mandated feedback, not
  decorative.

---

## Localization Considerations

| Element | Longest expected (EN) | Layout-critical? | Note |
|---|---|---|---|
| Boss name | Variable — depends on the boss roster (not yet finalized, per the IP-risk note on the existing roster) | Yes, within boss bar width | Must truncate gracefully rather than push the HP text/fill off-position |
| Footer stat labels ("Balls:", "Turn:", "Bricks:") | Short by design | **Yes — tight footer space, three labels must coexist** | Same short-label expansion risk flagged in every prior spec; consider icon+number instead of text labels to sidestep this entirely, given how cramped the footer already is |
| Aim-hint text | Unspecified length (not detailed in the GDD) | Yes, within footer space (shares the zone with the stat labels, mutually exclusive by state) | Flagged in Open Questions — exact copy isn't written yet |
| "Quit run?" confirmation | Short phrase | Yes | Same short-button-label risk category as every prior spec's primary CTAs |

**HIGH PRIORITY**: footer stat labels — three short labels competing for
the tightest space in this entire spec. Worth strongly considering
icon-based labels (a ball icon + number, rather than "Balls: 3")
specifically *because* of this localization risk, not just aesthetic
preference.

---

## Acceptance Criteria

- [ ] HUD is fully populated (correct boss HP, ball count, level) the instant the playfield becomes visible — no flash of zero/placeholder values (performance/core purpose)
- [ ] When boss HP changes, the HP bar tweens smoothly over 0.12s rather than snapping instantly (core purpose — GDD-mandated)
- [ ] When game state is not Aiming, the aim-hint text is hidden (core purpose)
- [ ] When a stats value hasn't changed since the last frame, the HUD does not re-render for that value (performance — dirty-check holds)
- [ ] Tapping Exit during Aiming or Over exits instantly; tapping Exit during Firing shows a "Quit run?" confirmation first (navigation/core purpose)
- [ ] When mute is toggled mid-volley, in-flight sounds finish naturally and only new sounds are silenced (core purpose)
- [ ] The Exit button and Mute toggle meet the 44×44pt/48×48dp minimum touch target size (accessibility)
- [ ] The boss HP bar's fill and numeric text remain legible against a range of playfield background states, not just one static screenshot (accessibility/error state — contrast against a dynamic background)
- [ ] The top bar and footer remain fully visible and unobstructed by device safe areas (notch, home indicator) across the target device range, for the entire play session (resolution/device coverage)
- [ ] Tapping Exit during Firing pauses the simulation (balls freeze, boss bar stops); Cancel resumes with no state loss, Confirm abandons the run cleanly (core purpose)

---

## Open Questions

1. **Player journey map not yet created** — same project-wide gap.
2. **Accessibility tier not yet defined** — defaulted to WCAG-AA.
3. **No reduced-motion policy exists** — same project-wide gap, with the
   added nuance that this HUD's tween is GDD-mandated feedback, not purely
   decorative, so "reduced" ≠ "removed" here specifically.
4. **`run_abandoned` event unapproved** — proposed here, needs Analytics
   GDD owner sign-off.
5. **Screen-reader on-demand-query pattern for real-time stats isn't
   designed** — flagged as the right *approach* (query, not continuous
   narration) but the actual interaction (what gesture triggers it, what
   gets read) isn't specified.
6. **Aim-hint text's exact copy isn't written** — the GDD names its
   existence and visibility rule but not its wording; affects the
   localization risk assessment above.
7. **Boss name localization risk is entangled with the existing IP-risk
   note** on the current boss roster (deliberately deferred per an earlier
   user decision) — a reminder this spec doesn't resolve that, and any
   final boss-name list should be checked for localization length *and*
   the IP question together, not separately.
8. **Footer icon-vs-text-label decision** for the three stat counters is
   recommended (icons sidestep the localization risk) but not finalized —
   a call for whoever implements the footer.
