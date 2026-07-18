# Ricochet HUD

> **Status**: Designed
> **Author**: Abdulrahman Alenazan + Claude
> **Last Updated**: 2026-07-09
> **Implements Pillar**: Super Ricochet's core fantasy ("chip away the boss")
> **Creative Director Review (CD-GDD-ALIGN)**: APPROVED 2026-07-09 (self-reviewed — no `creative-director` subagent registered in this environment)
> **`/design-review` (2026-07-17)**: NEEDS REVISION — 2 blocking items
> (the boss-HP tween Edge Case described a scenario Boss AI/Damage Model's
> revision made structurally impossible, missing the real hazard it
> introduced instead; no clamp-at-0 rule for the HUD's own fill-bar
> calculation, given boss HP can now go internally negative) plus 5
> blocking AC gaps. All folded in below; re-review pending.

## Overview

Ricochet HUD is the in-play interface for Super Ricochet — carried over
from the prototype's proven `GameScreen.tsx` overlay pattern (top bar, boss
bar, playfield, footer). It's the last of the 11 MVP systems: everything it
displays is already fully specified by systems designed earlier in this
pass (Super Ricochet's stats, Boss AI's HP/name, Currency System's balance)
— this GDD is purely about composition and update behavior, not new logic.

## Player Fantasy

Direct — the HUD is how a player *perceives* the "chip away the boss"
fantasy moment to moment: the boss bar visibly draining, the ball count
ticking down mid-volley. It's the feedback layer that makes Boss AI/Damage
Model's invisible number-crunching feel like a fight.

## Detailed Design

### Core Rules

1. **Layout** (carried over from `GameScreen.tsx`): top bar (exit button,
   level pill, currency display, mute toggle) → boss bar (name, HP
   text, HP fill bar) → playfield canvas (owned by Super Ricochet, not this
   system) → footer (ball count, turn number, bricks destroyed, aim-hint
   text shown only during the Aiming state).
2. The boss HP bar **tweens smoothly** on change — this is where Boss AI/
   Damage Model's Visual/Audio requirement ("smooth tween on depletion, not
   an instant snap") gets implemented. **[Clarified 2026-07-17]** The HP
   value driving this tween must be **clamped to a minimum of 0** before
   it's used in any fill-percentage calculation (`width = max(hp, 0) /
   maxHp`) — Boss AI/Damage Model's own revision states its internal HP can
   go negative (e.g. −1 on a lethal multi-hit frame) and that the display
   value is clamped at 0, but that clamp is Boss AI's stated *intent*, not
   logic it exposes; this HUD is the one place that actually renders a
   width/percentage, so the clamp must live here, or a negative internal
   value would produce a negative-width fill bar.
3. HUD updates are **dirty-checked** — driven by Super Ricochet's stats
   callback, only re-rendering a displayed value when it actually changes.
   Carries over a real, already-learned performance lesson from the
   prototype (avoiding 60fps re-render storms), not a new invention.
4. The mute toggle is **shared state** across every mini-game's HUD, not
   Ricochet-specific — persists across sessions.

### States and Transitions

Mirrors Super Ricochet's own state machine: **Aiming** (aim-hint text
shown), **Firing** (aim-hint hidden, live ball count shown), **Over**
(HUD hands off to the result screen, a separate future concern).
**[Clarified 2026-07-17]** Super Ricochet's own Firing→Aiming transition
(a turn ends without a win/loss — including Boss AI/Damage Model's
"boss survives the volley cap" outcome — and the next turn begins) is not
a distinct HUD *state*, but the footer's turn-number counter must visibly
increment on this transition; this GDD's Core Rule 1 lists "turn number"
in the footer but never explicitly tied its update to this specific
transition until now.

### Interactions with Other Systems

- **Super Ricochet**: source of ball count, turn number, bricks-destroyed,
  and current game state.
- **Boss AI/Damage Model**: source of boss name and HP.
- **Currency System**: source of the currency display.

## Formulas

None — pure presentation layer.

## Edge Cases

- **[CORRECTED 2026-07-17]** Boss AI/Damage Model's revision means HP is
  decremented **exactly once per frame** (an accumulated hit count for that
  frame, never several discrete same-frame writes) — so the original
  framing of this Edge Case ("multiple hits in rapid succession" causing
  multiple same-frame updates) describes a scenario that can no longer
  happen by construction. The real, related hazard is: **a single frame's
  HP update can itself be a large multi-point jump** (e.g. −5 HP in one
  frame if five bricks were hit that frame). The tween must still handle
  this the same way — continuously tracking toward the latest target value
  rather than queuing discrete tweens per point of damage — avoiding a
  janky "catch-up" bar even when the jump arrives as one big step rather
  than several small ones.
- **If mute is toggled mid-volley**: in-flight sounds finish naturally;
  only *new* sounds are silenced going forward — an abrupt full audio
  cutoff would feel broken mid-action.

## Dependencies

- **Depends on** (hard): Super Ricochet, Boss AI/Damage Model.
- **Depends on** (soft): Currency System (currency display).
- **Depended on by**: none.

**Consistency check**: Super Ricochet's GDD lists "Depended on by: Ricochet
HUD" — matches. ✅

## Tuning Knobs

| Knob | Value | Too Low | Too High |
|---|---|---|---|
| Boss HP bar tween duration | 0.12s linear (carried over exactly from the prototype's CSS transition) | Feels like an instant snap, undercutting the "smooth depletion" requirement | Feels laggy/disconnected from the hit that caused it |

## Visual/Audio Requirements

Reuses Hub UI's established component styling (chips, level-pill) for visual
consistency across screens — not a separate visual language. No new sound
design needed here; audio is entirely owned by Super Ricochet's own
Visual/Audio spec.

## UI Requirements

This GDD *is* the UI specification for Super Ricochet's in-play screen —
see Core Rule 1 for the full layout. Pixel-level spec belongs in
`/ux-design`.

## Acceptance Criteria

- **GIVEN** boss HP changes, **WHEN** the HUD updates, **THEN** the HP bar
  tweens smoothly over the tuned duration rather than snapping instantly.
- **GIVEN** the game state is not Aiming, **WHEN** the footer renders,
  **THEN** the aim-hint text is hidden.
- **GIVEN** a stats value hasn't changed since the last frame, **WHEN** the
  engine emits a stats update, **THEN** the HUD does not re-render (the
  dirty-check holds).
- **GIVEN** mute is toggled mid-volley, **WHEN** toggled, **THEN** in-flight
  sounds finish naturally and only new sounds are silenced.
- **[NEW 2026-07-17] GIVEN** mute is toggled while playing Super Ricochet,
  **WHEN** the player later opens a different mini-game's HUD, **THEN**
  it's already muted (cross-mini-game shared state). **GIVEN** the app is
  closed and relaunched, **WHEN** the HUD next renders, **THEN** the mute
  state from the prior session persists — verifying both halves of Core
  Rule 4, not just the mid-volley behavior.
- **[NEW 2026-07-17] GIVEN** the top bar renders, **WHEN** the exit button
  is tapped, **THEN** it functions (exits the run) — previously named in
  Core Rule 1's layout with no corresponding test.
- **[NEW 2026-07-17] GIVEN** boss HP changes, **WHEN** the fill bar begins
  tweening, **THEN** the HP text label updates in sync with the tween's
  current value, not instantly ahead of it — preventing a visible
  number/bar mismatch during the 0.12s tween window.
- **[NEW 2026-07-17] GIVEN** boss HP goes internally negative (a lethal
  multi-hit frame, per Boss AI/Damage Model), **WHEN** the fill bar
  renders, **THEN** it clamps at a width of 0, never a negative width.
- **[NEW 2026-07-17] GIVEN** a turn ends without a win or loss (including
  the boss surviving the volley cap), **WHEN** the next turn begins,
  **THEN** the footer's turn-number counter increments visibly.

## Open Questions

None — this system is a direct, low-ambiguity port of an already-proven
prototype pattern.
