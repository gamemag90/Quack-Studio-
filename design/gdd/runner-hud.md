# Runner HUD

> **Status**: Designed
> **Author**: Abdulrahman Alenazan + Claude
> **Last Updated**: 2026-07-12
> **Implements Pillar**: Every mini-game is a real pillar, not a side loop
> **Creative Director Review (CD-GDD-ALIGN)**: APPROVED WITH CONDITIONS
> 2026-07-12 (2 conditions, both fixed same pass — C1: Dependencies
> listed only Quack Runner as a hard dependency despite Core Rule 7's
> pause mechanism directly depending on `SharedSimCore`'s tick-freeze
> behavior; added `SharedSimCore` (ADR-0001/ADR-0002) as a named hard
> dependency, matching Ricochet HUD's own precedent of listing Boss
> AI/Damage Model alongside Super Ricochet. C2: the header truncated
> the pillar to "Every mini-game is a real pillar," dropping ", not a
> side loop" from `game-concept.md`'s canonical text; restored.)
> **`/design-review` (2026-07-17)**: NEEDS REVISION — 1 blocking item
> (the Dependencies section's own "Consistency check" quoted
> `quack-runner.md`'s Depended-on-by entry as "Runner HUD (not yet
> designed...)" and asserted "matches, no gap to fix" — but
> `quack-runner.md` has said "Runner HUD (designed 2026-07-12, see
> `runner-hud.md`)" since that same day; the quote was stale and the
> verdict it supported was wrong) plus 2 recommended and 1 nice-to-have
> item. All folded in below; re-review pending.

## Overview

Runner HUD is the in-play interface for Quack Runner — carried over
from the prototype's proven `RunnerCanvas.tsx` DOM-overlay pattern (a
chip row: score, elapsed time, health, not a bar) plus `RunnerScreen.tsx`'s
separate post-run result modal. **[Corrected during self-check]**: the
prototype's own health chip literally repeats a heart icon per health
point, but Core Rule 2 below simplifies this to a single binary
alive/dead icon for the native version, since Quack Runner's health is
fixed at 1 with no regen — an early draft of this Overview still said
"health-as-repeated-heart-icons," which had drifted out of sync with
that later decision. Unlike
Ricochet HUD's pure 1:1 port, this GDD makes one deliberate elevation:
`quack-runner.md`'s own UI Requirements calls for a live `coinsCollected`
count chip during play, which the prototype's HUD never had (it only
surfaced coins in the aggregate post-run reward). The prototype also
already solved real accessibility work worth carrying forward exactly,
not reinventing: the gameplay canvas is `aria-hidden` since the HUD
chips and instructions text already mirror all state, and game-over is
announced via an `aria-live` region. Two real gaps the prototype never
addressed carry over from `quack-runner.md`'s own Open Questions and get
resolved here: app-backgrounding/pause behavior, and death-moment
screen-shake magnitude.

## Player Fantasy

Direct — the HUD is how a player perceives the "greed versus survival"
fantasy moment to moment: the coin count climbing with every grab,
hearts draining with every clipped obstacle, both racing in view at
once. It's the feedback layer that makes each split-second dodge-or-grab
call feel like a bet just paid off — or didn't.

## Detailed Design

### Core Rules

1. **Layout carries over `RunnerCanvas.tsx`'s chip row exactly**: score
   (⭐), elapsed time (⏱), health, and `coinsCollected` (new) — plus
   instructions text and a gameplay canvas equivalent excluded from
   accessibility navigation, since the chips and instructions already
   mirror all state needed for play.
2. **Health renders as a single binary alive/dead icon, not a repeated
   heart row.** `quack-runner.md`'s own `runner_health` constant is
   fixed at 1 (no regen, no extra life) — a multi-heart pattern would
   visually promise a buffer that doesn't exist. One icon: solid at
   health=1, swaps to a stumble pose exactly at the health→0 transition,
   synced with Rule 8's shake.
3. **`coinsCollected` updates immediately on each Coin collision** (Quack
   Runner Core Rule 3), not whole-second batched — it's the "coins
   climbing with every grab" moment Player Fantasy names, so batching
   would blunt it. Reuses Hub UI/Ricochet HUD's currency-chip styling
   per `quack-runner.md`'s UX Flag. Per ADR-0010's display-only pattern,
   this is a live echo, never authoritative; any downward reconciliation
   against the server-verified count shows on the run-result screen, not
   here.
4. **Dirty-check applies per-field, not globally**: score, health, and
   `coinsCollected` each re-render only on their own discrete event;
   elapsed time alone uses whole-second granularity (re-renders only
   when the integer second changes) — carrying forward the prototype's
   pattern and Ricochet HUD's own Core Rule 3 precedent.
5. **Accessibility parity uses Unity's native Accessibility APIs, not
   DOM attributes.** Gameplay sprites are excluded from the
   accessibility node tree (parity with the prototype's `aria-hidden`
   canvas) since HUD nodes already mirror all state; game-over fires a
   native accessibility announcement (parity with the prototype's
   `aria-live` region). **Flagged for engineering verification**: the
   exact Unity 6.3 API surface for this is past this project's LLM
   knowledge cutoff (per `docs/engine-reference/unity/VERSION.md`'s own
   HIGH risk flag) — must be verified against the pinned engine docs
   before implementation, not assumed from training data.
6. **Screen shake queries the platform reduced-motion setting** (the
   OS-level accessibility flag, checked at launch and on resume) — same
   intent as the prototype's `prefersReducedMotion` check. When enabled,
   shake amplitude is 0 and a camera-hop substitute is used instead —
   an instantaneous cut, not a tweened motion, so the substitute itself
   never becomes the kind of motion reduced-motion is meant to
   suppress. The health icon's stumble-pose swap (Rule 2) is a separate,
   discrete pose change and is never suppressed by reduced-motion — only
   continuous/oscillating shake is in scope for that setting.
7. **Pause resolves `quack-runner.md`'s Open Question 1, and applies
   only from Playing.** Trigger is app-backgrounding only (Unity's
   `OnApplicationPause(true)`) — no manual pause button, matching the
   prototype's minimal header (Exit only) and this project's existing
   precedent for that lifecycle hook. Pausing freezes `SharedSimCore`'s
   tick advancement; since `obstacleSpeed(t)`/`spawnInterval(t)` are
   pure functions of sim-tick `t` (ADR-0001/ADR-0002), a frozen tick
   means `t` cannot advance while backgrounded — this closes the
   anti-cheat "pause can't freeze the ramp" concern by construction, no
   separate mechanism needed. The pause/resume pair is still logged in
   the replay stream for Anti-Cheat visibility. Maximum backgrounded
   duration: 120 seconds (Tuning Knobs), measured as a wall-clock
   timestamp diff evaluated once at `OnApplicationPause(false)` —
   **[Amended during Edge Cases review]** not a running countdown,
   since Unity does not tick `Update()` while backgrounded — checked
   *before* unfreezing the sim tick, so an over-limit resume forfeits
   immediately without ever re-entering Playing. Not an anti-cheat
   requirement, since pause is already non-exploitable, but a
   resource-hygiene bound; exceeding it auto-ends the run as forfeited
   (`GameOver`, uncredited). This state only exists coming from
   Playing — backgrounding during Ready (before the sim clock starts)
   is not Paused at all, since there is no active run to bound or
   forfeit.
8. **Death-shake resolves `quack-runner.md`'s Open Question 2.** Default
   5px (down from the prototype's raw 14px, roughly 35% magnitude),
   same duration/frequency curve as the carried-over shake — only
   amplitude is attenuated, keeping the stumble legible without reading
   as a hard impact. Flagged as a Tuning Knob starting point, not
   playtested — `/balance-check` must confirm it before lock, matching
   `level-difficulty-config-ricochet.md`'s boss-HP-cap precedent for
   structural placeholders.

### States and Transitions

| State | HUD Behavior |
|---|---|
| Ready | Chips at rest (score 0, coins 0, health icon solid, time 0:00); instructions shown |
| Playing | All chips live per Rules 3–4; instructions hidden after first input |
| Paused (new — resolves Open Question 1) | Chips freeze at last value; a "Paused" overlay shows; resumes on `OnApplicationPause(false)`, or auto-forfeits at the 120s cap (Rule 7) |
| GameOver | Health icon switches to the stumble pose plus shake (Rule 8); accessibility announcement fires; hands off to the run-result screen |

**[Corrected 2026-07-17]** `quack-runner.md`'s own States table already
has `Paused` fully integrated (Playing→Paused, Paused→Playing,
Paused→GameOver), confirmed by that document's own "[Updated 2026-07-12]"
note naming this GDD's Core Rule 7 as the canonical source — the
follow-up edit this line previously called for was already completed
elsewhere; this note simply hadn't been reconciled afterward.

### Interactions with Other Systems

- **Quack Runner** (reads): source of score/health/`coinsCollected` and
  the state machine this HUD only displays; never computes its own copy.
- **Obstacle Spawn/Difficulty Ramp (Runner) + `SharedSimCore`**
  (ADR-0001/ADR-0002): sim-tick freeze is the pause mechanism (Rule 7).
- **Anti-Cheat/Replay Verification**: pause/resume events are logged in
  the replay stream; the run-result screen shows the reconciliation
  between the live `coinsCollected` echo and the server-verified count.
- **Currency System**: the coin chip is a display-only echo of the
  `creditMultiplied` pipeline, never authoritative for reward crediting.
- **Hub UI / Ricochet HUD**: reused currency-chip and level-pill
  component styling, not a separate visual language.
- **`accessibility-requirements.md`**: governs Rules 5–6's native-AT
  parity requirements.

## Formulas

None — pure presentation layer, matching Ricochet HUD's own precedent
exactly. The shake magnitude (Tuning Knobs) and backgrounding timeout
are constants, not derived formulas; every displayed value is owned and
computed by Quack Runner, this GDD only renders it.

## Edge Cases

- **If the app backgrounds while in Ready** (before first touch input,
  sim tick not yet started): the Paused state and its 120s cap do not
  engage at all — the app simply suspends and resumes to Ready with no
  timer, overlay, or forfeit. Rule 7's cap exists to bound an *active*
  run with a `GameOver, uncredited` outcome to forfeit into; Ready has
  no run and nothing at risk.
- **If health reaches 0 and app-backgrounding occur in the same
  frame**: `GameOver` wins, not a true race. `OnApplicationPause` is
  delivered between frames, not mid-frame, so the frame's game logic
  (health→0, Playing→GameOver) fully resolves first. By the time
  backgrounding is handled, there is no active run left to pause — Rule
  7 is moot and the app backgrounds normally from the run-result
  hand-off.
- **If a run ends (`GameOver` fires)**: Runner HUD's responsibility
  ends completely at the transition (stumble pose + shake, the
  accessibility announcement). The run-result screen's `saveFailed`/
  reconciliation handling (Rule 3's own carve-out) is entirely separate
  system territory — no further HUD involvement.
- **If a coin collision would occur while the sim tick is frozen
  (Paused)**: it cannot happen. Pausing freezes tick `t` entirely
  (Rule 7), so there is nothing to collect and nothing to reconcile at
  resume — no special-casing is needed for a "collected during pause"
  scenario, because that scenario is impossible by construction.

## Dependencies

- **Depends on** (hard): Quack Runner (sole source of all displayed
  data — score, health, `coinsCollected`, elapsed time — and the state
  machine this HUD renders); `SharedSimCore` (ADR-0001/ADR-0002) —
  Core Rule 7's pause mechanism directly depends on its tick-freeze
  behavior, matching the precedent Ricochet HUD's own Dependencies
  section set by listing Boss AI/Damage Model as a hard dependency
  alongside Super Ricochet whenever a Core Rule is governed by a
  specific system, not just Quack Runner in general. `quack-runner.md`
  itself already lists `SharedSimCore` as a named hard dependency.
- **Depends on** (soft): Currency System (currency-chip display styling
  only, per Rule 3).
- **Depended on by**: none.

**Consistency check [CORRECTED 2026-07-17]**: this section previously
quoted `quack-runner.md`'s Depended-on-by entry as "Runner HUD (not yet
designed — consumes live score/health/coinsCollected display state...)"
and concluded "matches, no gap to fix." That quote was stale —
`quack-runner.md` has read "Depended on by: ... Runner HUD (designed
2026-07-12, see `runner-hud.md`)" since the same day this GDD itself was
written, and the "matches" verdict built on the stale quote was
therefore also wrong, not just outdated phrasing. Corrected: the current
text is accurate and does match; there is no gap. Currency System's soft
dependency is not individually named back, matching the same looser
pattern Ricochet HUD's own Dependencies section already established for
HUD-level soft dependencies rather than a new inconsistency introduced
here.

## Tuning Knobs

| Knob | Suggested Value | Too Low | Too High |
|---|---|---|---|
| Death-shake magnitude | 5px default (down from the prototype's 14px) — **structural placeholder, unplaytested, pending `/balance-check`** | The death moment feels weightless, doesn't register as impact | Fights the "gentle disappointment" guardrail (Quack Runner Visual/Audio Requirements), reads as harsh |
| Max backgrounded duration | 120 seconds | A normal phone-call/notification interruption forfeits a run unfairly | Stale paused sessions linger longer — a minor resource-hygiene cost, no exploit risk either way since the sim tick is frozen (Core Rule 7) |

## Visual/Audio Requirements

Reuses Hub UI/Ricochet HUD's established chip and level-pill styling for
the score/time/coin chips — not a separate visual language, matching
Ricochet HUD's own precedent. Three items go beyond that baseline and
need their own spec:

**Health icon**: two poses, not one — solid/idle at health=1, a stumble
pose at the health→0 transition (Core Rule 2), synced with the 5px
shake. Per the art bible's exaggerated, instantly-readable expression
style and its "Run Loss" mood row ("light disappointment, quick to
recover — dimmed but never dark, muted warm tones, gentle, apologetic,
still warm, brief"), the stumble pose should read as a clear "oof," not
a hard hit — consistent with this GDD's own 5px-not-14px shake
rationale.

**Paused overlay**: no established precedent elsewhere in the art bible
or any sibling GDD for a full-screen pause treatment — this is the
first system needing one. Following the art bible's warm/saturated,
never-muddy principle: a warm-neutral scrim with a chunky-bordered
"Paused" panel, not a dark-scrim genre convention. *Flagged*: exact
scrim opacity/color and panel layout aren't art-bible-specified — needs
`/asset-spec` resolution before production, not assumed here.

**Coin chip**: a per-grab pop/tween, not purely functional like the
time chip — Player Fantasy names "coins climbing with every grab" as
the feel to hit. This stays lightweight/iconic (a small scale-punch),
not a Daily-Quest-claim-style character-first celebration — that
treatment is reserved for discrete milestone moments, and coin grabs
here are high-frequency. **[Flagged 2026-07-17]** `quack-runner.md`'s
Open Question 7 removes the prototype's 100-coin/run cap entirely, so
`coinsCollected` can now reach values the chip's layout was never
stress-tested against (the prototype's chip only ever needed to display
up to 3 digits). Exact formatting/truncation behavior for large values
(e.g. thousands separators, a compact `1.2k`-style abbreviation, or
simply trusting the chip's existing width to accommodate more digits) is
not specified here — flagged for `/asset-spec` or `/ux-design`
resolution, not assumed.

No new audio design — entirely owned by Quack Runner's own Visual/Audio
spec, matching Ricochet HUD's own pattern.

## UI Requirements

This GDD *is* the UI specification for Quack Runner's in-play screen —
see Core Rule 1 for the full layout, matching `ricochet-hud.md`'s own
precedent exactly (that GDD says the same thing about itself).
Pixel-level spec belongs in `/ux-design`. The separate run-result screen
(`quack-runner.md`'s own UI Requirements) is out of scope here — this
GDD covers only the in-play chip row and the Paused overlay.

## Acceptance Criteria

- **GIVEN** Ready state loads, **WHEN** the HUD renders, **THEN**
  score/time/health/coin chips plus instructions text are visible, and
  the gameplay canvas is absent from the accessibility node tree.
- **GIVEN** health=1, **WHEN** the HUD renders, **THEN** exactly one
  health icon shows, solid pose. **GIVEN** health transitions to 0,
  **WHEN** that transition fires, **THEN** the icon swaps to stumble
  pose in the same tick as the death-shake (see below).
- **GIVEN** Playing with `coinsCollected`=N, **WHEN** a Coin collision
  fires, **THEN** the coin chip shows N+1 within that same render
  frame, not deferred to the next whole second.
- **[Engineering-verified, not manual QA]** **GIVEN** score/health are
  unchanged, **WHEN** elapsed time crosses a whole-second boundary,
  **THEN** only the time chip's render call fires that tick — requires
  render-call instrumentation or a debug overlay to observe, since
  per-field dirty-checking has no black-box-visible difference from a
  full re-render.
- **GIVEN** native assistive technology is enabled, **WHEN** any state
  is active, **THEN** gameplay sprites are unreachable via AT
  navigation. **GIVEN** `GameOver` fires with AT enabled, **THEN** a
  native accessibility announcement is issued. *(These validate
  observable behavior only — they cannot certify correctness against
  the underlying Unity 6.3 Accessibility API, which Core Rule 5 already
  flags as unverified/post-cutoff and requiring separate engineering
  verification.)*
- **GIVEN** reduced-motion is on, **WHEN** `GameOver` triggers, **THEN**
  shake amplitude is 0 and the camera-hop substitute completes within a
  single frame (~16ms at 60fps) rather than tweening. **GIVEN**
  reduced-motion is on, **WHEN** health hits 0, **THEN** the
  stumble-pose swap still occurs (never suppressed).
- **GIVEN** Playing, **WHEN** `OnApplicationPause(true)` fires, **THEN**
  state becomes `Paused`, chips freeze at their last value, the sim
  tick stops, and the Paused overlay shows.
- **GIVEN** `Paused` for less than the configured max backgrounded
  duration (Tuning Knobs), **WHEN** the app resumes, **THEN** state
  returns to `Playing` and the tick resumes from exactly where it froze.
- **GIVEN** `Paused` for at or beyond the configured max backgrounded
  duration at the resume instant, **WHEN** evaluated, **THEN** the run
  forfeits immediately to `GameOver` (uncredited) without ever
  re-entering `Playing`.
- **GIVEN** `GameOver` triggers, **WHEN** the shake plays, **THEN** peak
  amplitude equals the Tuning Knobs' currently configured death-shake
  value (not a hardcoded number in this criterion, since that value is
  an explicit unplaytested placeholder pending `/balance-check` and
  will change).
- **GIVEN** Ready (no active run), **WHEN** the app backgrounds and
  resumes, **THEN** state remains `Ready` with no overlay, timer, or
  forfeit.
- **[Automation-only, not reproducible via manual play]** **GIVEN** a
  scripted harness forces health reaching 0 in the same frame
  backgrounding is signaled, **WHEN** the frame resolves, **THEN**
  state is `GameOver`, not `Paused` — this same-frame ordering isn't
  reliably reproducible through standard manual QA and needs a
  scripted test.
- **GIVEN** `GameOver` has fired and the stumble/shake/announcement
  sequence completes, **WHEN** the run-result screen takes over,
  **THEN** Runner HUD issues no further updates or reconciliation.
- **GIVEN** `Paused` (tick frozen), **WHEN** a coin/player overlap that
  would normally collide occurs, **THEN** `coinsCollected` does not
  change and no collision event fires.

**QA harness note (flagged by qa-lead review, not a design gap in this
GDD):** two criteria above require test-tooling support that doesn't
exist yet — render-call instrumentation for the dirty-check criterion,
and a scripted harness to force the GameOver/Paused same-frame race.
These are `/qa-plan` / test-harness scope, matching the same pattern
Daily Quests' and Mascot Gallery/Equip UI's own Acceptance Criteria
sections used for their equivalent gaps — carried to Open Questions so
it isn't silently dropped.

## Open Questions

1. **QA test-harness gaps** (qa-lead review, Acceptance Criteria):
   render-call instrumentation or a debug overlay to observe the
   per-field dirty-check working, and a scripted harness to force the
   `GameOver`/`Paused` same-frame ordering (not reproducible via manual
   play). None of these tools currently exist — scope for a future
   `/qa-plan` pass, not this GDD.
2. **Death-shake magnitude (5px default, Tuning Knobs) is an
   unplaytested structural placeholder**, not a locked value — needs
   `/balance-check` validation once real playtest data exists, matching
   `level-difficulty-config-ricochet.md`'s boss-HP-cap precedent.
3. **The exact Unity 6.3 Accessibility API surface (Core Rule 5) needs
   engineering verification against the pinned engine docs before
   implementation** — this project's own `VERSION.md` flags the engine
   version as HIGH risk (past the LLM's reliable knowledge cutoff), so
   the API names in Core Rule 5 are a best-effort proposal, not
   confirmed fact.
4. **The Paused overlay's exact visual treatment (scrim
   opacity/color, panel layout) is not art-bible-specified** — this is
   the first system needing a full-screen pause treatment (Visual/Audio
   Requirements). *Target: resolve during `/asset-spec` once the art
   bible covers it, or as its own small addendum.*
5. **One-handed reachability of direct-drag movement remains
   unresolved, and deliberately out of scope here too.**
   `quack-runner.md`'s own Open Question 3 already routed this to
   `/ux-design` as an input/control-layer decision, not a HUD
   Core Rule — this GDD does not silently resolve it either, since it
   isn't this system's territory.
