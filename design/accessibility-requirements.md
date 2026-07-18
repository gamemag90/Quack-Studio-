---
status: committed
author: Abdulrahman Alenazan + Claude
date: 2026-07-11
---

# Accessibility Requirements

> This document did not exist when `shared-hub.md`, `hub-ui.md`,
> `account-auth.md`, and `ricochet-hud.md` were written — all four
> defaulted to "WCAG-AA as a working baseline" and flagged the tier as
> **not yet committed** in their own Open Questions. This document commits
> it, formally, and resolves the one policy every single one of those
> specs left open: reduced motion. Nothing below contradicts what those
> four specs already built against — it consolidates and formalizes
> content they'd already independently converged on, then closes the gaps
> they couldn't close themselves.

## Committed Tier

**AA-equivalent for mobile touch games** — not the token "Basic" tier the
gate-check said would be minimally acceptable, and not a claim of formal
WCAG 2.2 certification either. The four approved UX specs already wrote
AA-level content (4.5:1 contrast assumptions, mandatory color-independence,
explicit screen-reader labels) as their working default; formally
downgrading to "Basic" now would contradict already-approved, already-built
specs for no real benefit. What follows is WCAG 2.1 AA's substance, adapted
for a touch-only mobile game rather than a web page — full formal
certification audit is out of scope for Pre-Production and deferred to
Polish.

## Scope & Rationale

- **Platform reality**: touch-only, no keyboard/gamepad input path exists
  anywhere in the design (`account-auth.md` already decided this). WCAG's
  keyboard-operability criteria don't transfer to this platform and are
  explicitly out of scope, not silently ignored — see Non-Goals.
- **Real-time gameplay is not a web page**: `ricochet-hud.md`'s Accessibility
  section already reasoned through why continuous screen-reader narration
  of a fast-changing HUD would make VoiceOver/TalkBack unusable during a
  volley. This document adopts that reasoning as the project-wide pattern
  for any future real-time HUD, not just Ricochet's.
- **Small team, no dedicated a11y engineer**: the tier is chosen to be
  genuinely achievable by the team already assembled, not aspirational.

## Requirements by Category

### Visual — Contrast & Color-Independence

- **Contrast ratios**: 4.5:1 minimum for body/label text, 3:1 for large
  text (≥18pt) and UI component boundaries (button outlines, focus
  indicators) — WCAG 2.1 AA's own numeric thresholds, adopted directly
  since they're measurable and engine-agnostic.
- **Dynamic backgrounds**: where UI overlays gameplay content that changes
  continuously (Ricochet HUD's top bar/boss bar over the playfield),
  contrast must hold against a *range* of underlying content, not one
  static screenshot — enforced via a solid-backed bar, never text floating
  directly over gameplay art. Already specified in `ricochet-hud.md`;
  formalized here as the general rule for any future real-time overlay.
- **Never color-alone**: every state, rarity tier, or status that's
  currently color-coded must also carry a shape/icon/text cue. This is
  already binding practice across every approved spec (locked-tile
  graying + lock icon in `hub-ui.md`; rarity thumbnails pairing color with
  icon/shape per the art bible's Brick Red/Fern Green confusion-pair
  warning; error states using icon + text in `account-auth.md`; boss HP
  bar always paired with numeric text in `ricochet-hud.md`). Formalized
  here as a hard, project-wide rule — not spec-by-spec convention.

### Motor — Touch Targets

- **Minimum touch target: 44×44pt (iOS) / 48×48dp (Android)** for every
  interactive element, with no exceptions for icon-only controls. Already
  the consistent standard across `hub-ui.md`, `account-auth.md`,
  `ricochet-hud.md`, and `interaction-patterns.md` — formalized here as
  the project's one committed number, not four specs independently
  agreeing by coincidence.
- **Destructive-adjacent actions** (Logout, mid-run Exit) require an
  explicit confirmation step before firing — already established in
  `hub-ui.md`/`ricochet-hud.md`/`interaction-patterns.md`, restated here
  as a general accessibility-adjacent safety rule (protects against
  accidental activation, which disproportionately affects motor-impaired
  and assistive-tech users).

### Screen Reader / Assistive Technology

- **Explicit accessible labels** on every icon-only or icon-dense element
  — currency chips ("1,240 coins, 15 gems"), avatar, hero-cards, stat
  tiles, mascot thumbnails. Never placeholder-text-as-label (it disappears
  once the user starts typing, a documented real failure mode already
  called out in `account-auth.md`).
- **Screen-reader transition announcements**: since Shared Hub's navigation
  is a state-driven show/hide (not a traditional page reload), every
  `current_screen` change must fire an explicit VoiceOver/TalkBack
  accessibility-focus or announcement event — otherwise a screen-reader
  user gets no signal the screen changed at all. Already specified in
  `shared-hub.md`; formalized as the general rule for any future
  state-driven (non-navigational-reload) screen change.
- **Real-time HUD stats: on-demand query, not continuous narration.**
  Continuously announcing every boss-HP tween or ball-count change during
  a fast volley would make VoiceOver/TalkBack unusable (potentially many
  announcements per second). The committed pattern, project-wide: a
  screen-reader user can query current stats via an explicit gesture;
  the HUD never auto-announces every dirty-checked update. First specified
  in `ricochet-hud.md`; this document promotes it from a per-spec decision
  to the standard pattern for any future real-time gameplay HUD.
- **Form field labels are explicit**, never placeholder-as-label; correct
  semantic input types (username / new-password / current-password) so
  platform password managers and autofill work — `account-auth.md`.
- **Inline errors are announced to screen readers when they appear**, not
  just shown visually — a validation error a screen-reader user can't
  detect is a silent failure. Disabled/loading button states are announced
  ("button, dimmed, Log In, disabled"), not just visually grayed —
  `account-auth.md`.

### Motion — Reduced Motion Policy (RESOLVED)

This is the one policy every single approved UX spec flagged as an open,
project-wide gap and none of them could close on their own. Resolved here:

- **The project ships a reduced-motion setting** — respecting the OS-level
  signal (iOS "Reduce Motion" / Android "Remove animations") automatically
  on first launch, with an in-app override in Settings for players whose
  OS setting doesn't match their in-game preference.
- **Two classes of animation, two different treatments**:
  - **Decorative/optional** (Hub UI's count-up numbers, pop-in card
    entrances, press-scale feedback): fully removed or replaced with an
    instant state change when reduced motion is on.
  - **GDD-mandated feedback** (the boss HP bar tween in `ricochet-hud.md`,
    which Boss AI/Damage Model's own Visual/Audio Requirements section
    specifies as required feedback, not decorative polish; screen shake
    tied to hit intensity in `super-ricochet.md`): **shortened, never
    fully removed**. A player with reduced motion enabled still needs to
    perceive that a hit landed — the signal just arrives with less motion,
    not with no signal at all. This was `ricochet-hud.md`'s own reasoning
    for its one animation; this document generalizes it as the project
    rule for any future GDD-mandated feedback animation.
- **What this does NOT resolve**: the exact tween-duration numbers for the
  "shortened" state (e.g. is 0.12s → 0.04s the right ratio?) are a
  `/balance-check`/playtest question, not a policy question — left as a
  tuning knob, not a blocking gap.

## Explicit Non-Goals

- **Keyboard/gamepad navigation**: N/A. This is a touch-only mobile game
  with no keyboard or gamepad input path anywhere in the design
  (`account-auth.md`'s own Accessibility section already made this call).
  WCAG's keyboard-operability success criteria do not apply.
- **Formal WCAG 2.2 certification audit**: out of scope for Pre-Production.
  This document commits to AA-equivalent *practice*, not a certified
  audit — revisit in Polish if the project pursues platform accessibility
  certification (e.g. for App Store/Play Store accessibility labeling).
- **Full live-region continuous narration** for real-time gameplay HUDs:
  deliberately rejected in favor of the on-demand-query pattern above —
  continuous narration was evaluated and found to make the HUD unusable
  during fast action, not simply skipped.

## Open Items (Genuinely Unresolved — Not Papered Over)

1. **Assistive-technology double-tap vs. the 300ms navigation debounce**
   (flagged in `shared-hub.md`, restated in `hub-ui.md` and
   `interaction-patterns.md`): VoiceOver/TalkBack's "double-tap to
   activate" gesture can, depending on implementation, register as two
   rapid taps to the underlying app — which could double-fire the
   debounce/nav-lock logic in ADR-0009's `HubNavigator` and mis-hit the
   wrong target. This is a genuine implementation risk requiring
   engineering validation once real input-handling code exists (whether
   the platform's accessibility APIs expose activation events distinctly
   from raw touch events). Not resolved by this document — carried
   forward as an open item for whoever implements `HubNavigator`'s input
   handling.
2. **Exact reduced-motion tween-duration ratios** (see Motion section
   above) — a tuning/playtest question, not a policy gap.
3. **Localization-expansion risk on short labels with generous touch
   targets** (`interaction-patterns.md`'s `text-cta-button` pattern) is
   accessibility-adjacent (longer translated strings could force smaller
   effective tap areas) but is primarily a localization concern — tracked
   there, not duplicated here as a blocking item.

## Consistency Check

Verified against all 4 approved UX specs (`shared-hub.md`, `hub-ui.md`,
`account-auth.md`, `ricochet-hud.md`) — every specific number and pattern
in this document (44×44pt/48×48dp, color-independence, screen-reader
labels, the on-demand-query HUD pattern) traces to content those specs
already wrote and had independently approved via `/ux-review`. Nothing
here overrides or contradicts an approved spec; this document only
formalizes what was already consistent practice and resolves the one gap
(reduced motion) that repeated across all four without ever closing.
