# Interaction Pattern Library

> **Status**: In Design
> **Author**: Abdulrahman Alenazan + Claude
> **Last Updated**: 2026-07-11
> **Template**: Interaction Pattern Library

---

## Overview

This library consolidates the reusable interaction patterns that emerged
across the four UX specs written and approved so far (`shared-hub.md`,
`hub-ui.md`, `account-auth.md`, `ricochet-hud.md`). Every pattern here was
**extracted from already-approved work**, not newly invented in this
session — this document formalizes and cross-references what those specs
already committed to, so the next screen designed (Shop, Mascot Gallery,
a second mini-game's HUD, etc.) reuses rather than reinvents.

30 patterns total: 5 Navigation, 4 Input, 5 Feedback, 11 Data Display
(including one pre-existing GDD-named component), 5 CTA/Branding.

---

## Pattern Catalog

| Pattern | Category | Used In |
|---|---|---|
| `debounced-navigation-tap` | Navigation | Shared Hub |
| `modal-pause-not-destroy` | Navigation | Shared Hub |
| `header-icon-action` | Navigation | Hub UI, Ricochet HUD |
| `hero-card` (+locked variant) | Navigation | Hub UI |
| `level-tile` | Navigation | Hub UI |
| `text-input-field` (+masked) | Input | Account/Auth |
| `form-mode-toggle` | Input | Account/Auth |
| `mute-toggle` | Input | Ricochet HUD |
| `dismiss-link` | Input | Account/Auth |
| `inline-error-text` | Feedback | Account/Auth |
| `contextual-prompt-banner` | Feedback | Account/Auth |
| `interstitial-message` | Feedback | Account/Auth |
| `conditional-hint-text` | Feedback | Ricochet HUD |
| `tweened-hp-bar` | Feedback | Ricochet HUD |
| `avatar-display` | Data Display | Hub UI |
| `progress-summary` | Data Display | Hub UI |
| `stat-tile` | Data Display | Hub UI |
| `quest-list-item` | Data Display | Hub UI |
| `leaderboard-row` | Data Display | Hub UI |
| `rarity-thumbnail` | Data Display | Hub UI |
| `level-pill` | Data Display | Ricochet HUD |
| `boss-name-label` | Data Display | Ricochet HUD |
| `hp-text` | Data Display | Ricochet HUD |
| `live-counter` | Data Display | Ricochet HUD |
| currency-chip *(pre-existing, GDD-named)* | Data Display | Hub UI, Ricochet HUD |
| `text-cta-button` | CTA/Branding | Hub UI, Account/Auth |
| `hero-cta-button` | CTA/Branding | Account/Auth |
| `social-login-button` | CTA/Branding | Account/Auth |
| `branding-header` | CTA/Branding | Account/Auth |
| `divider-label` | CTA/Branding | Account/Auth |

---

## Patterns

### Navigation

#### `debounced-navigation-tap`

**Category**: Navigation
**Used In**: Shared Hub (governs all Hub-originated navigation, inherited by Hub UI's hero-cards and level-tiles)

**Description**: Rapid repeated taps on navigation targets (mini-game entries, modal openers) within a debounce window resolve to a single navigation — the last tap wins, earlier ones are silently discarded rather than queued.

**Specification**:
- Debounce window: 300ms (`shared-hub.md` Tuning Knob).
- Debounce measures against actual state-commit time, not animation-visual-complete time (`shared-hub.md` Transitions & Animations) — a slow transition must not let a second tap sneak through.
- Assistive-technology caveat (`hub-ui.md` Accessibility): VoiceOver/TalkBack's double-tap-to-activate gesture can register as two rapid raw taps — implementation must distinguish an AT-mediated activation from two independent taps, or a screen-reader user could trigger the wrong target.

**When to Use**: Any tap target that triggers navigation and could plausibly be double-tapped by an impatient or overzealous player.
**When NOT to Use**: Non-navigation actions (toggles, form submission) — those have their own state (e.g. `Submitting`) that should disable re-entry instead of debouncing.

**Reference**: `shared-hub.md` States & Variants, "Debounced-navigation."

---

#### `modal-pause-not-destroy`

**Category**: Navigation
**Used In**: Shared Hub (Shop, Mascot Gallery modals)

**Description**: Opening a modal pauses/dims whatever is beneath it without tearing it down — closing the modal returns to the exact prior state, re-aggregating data only if the modal could have changed it (e.g. a Shop purchase).

**Specification**:
- Exactly one foreground layer + at most one modal layer (`shared-hub.md` GDD Rule 4, MVP scope).
- Underlying screen's state must not reset during the modal's open/close animation (`shared-hub.md` Transitions & Animations).
- On close: re-aggregate only if state may have changed; otherwise resume from cache with no re-fetch.

**When to Use**: Any modal/overlay that sits on top of a screen the player will likely return to unchanged.
**When NOT to Use**: A full navigation to a new root-level screen (e.g. logout to Account/Auth) — that's a genuine state transition, not a pause.

**Reference**: `shared-hub.md` Layout Specification, Layout Zones.

---

#### `header-icon-action`

**Category**: Navigation
**Used In**: Hub UI (Shop, Logout), Ricochet HUD (Exit)

**Description**: A small icon-only button in a persistent header/top bar, triggering a navigation or state-changing action. Press-state + haptic tick on tap.

**Specification**:
- Minimum touch target: 44×44pt (iOS) / 48×48dp (Android).
- Destructive-adjacent actions (Logout, Exit mid-action) require a confirmation step; purely navigational ones (Shop) do not.
- Screen-reader label required (icon-only, no visible text).

**When to Use**: Persistent, low-frequency-of-use actions that shouldn't compete visually with primary content.
**When NOT to Use**: Primary/frequent actions — those get a full CTA button (`text-cta-button`, `hero-cta-button`), not an icon tucked in a header.

**Reference**: `hub-ui.md` Component Inventory (Header); `ricochet-hud.md` Component Inventory (Top bar).

---

#### `hero-card`

**Category**: Navigation
**Used In**: Hub UI (Games section)

**Description**: A large, prominent card representing a navigable destination (a mini-game), with a **locked variant** for content not yet unlocked.

**Specification**:
- Unlocked: game art, name, "Play" CTA. Tap → navigates.
- Locked: grayed/dimmed art, unlock-requirement text (e.g. "Reach Level 5 to unlock"), no CTA. Tap → non-navigating feedback only (subtle shake), never routes to content the player hasn't unlocked.
- Lives in a horizontal-scroll strip that scales from 2 to N cards with zero layout change (`hub-ui.md` Layout Zones — resolves that GDD's own open question).

**When to Use**: Any "pick one of several unlockable destinations" selection surface.
**When NOT to Use**: A destination that's always available (no lock state) — use a simpler CTA instead; the locked-variant machinery is overhead you don't need.

**Reference**: `hub-ui.md` Component Inventory, ASCII Wireframe.

---

#### `level-tile`

**Category**: Navigation
**Used In**: Hub UI (Level-select grid)

**Description**: A compact grid tile representing one level within a mini-game, unlocked or locked.

**Specification**:
- Unlocked: tap → navigates to that mini-game at that level.
- Locked: must be visually distinct **without relying on color alone** (`hub-ui.md` Accessibility) — pair with a lock icon, not just a duller shade.
- Same minimum touch-target standard as `header-icon-action`.

**When to Use**: A dense grid of many similarly-shaped selectable items (levels, stages).
**When NOT to Use**: A small number of visually-distinct destinations — that's `hero-card` territory instead.

**Reference**: `hub-ui.md` Component Inventory, States & Variants.

---

### Input

#### `text-input-field`

**Category**: Input
**Used In**: Account/Auth (Username, Password)

**Description**: A standard text-entry field, with a masked variant for passwords.

**Specification**:
- Explicit accessible label — never placeholder-text-as-label (disappears once typing starts).
- Correct semantic input type (username / new-password / current-password) so platform password managers and autofill work.
- Masked variant includes a "show password" toggle.
- Real-time, on-blur validation feedback before a network round-trip where possible.

**When to Use**: Any free-text player input.
**When NOT to Use**: Constrained choices — use a toggle, selection, or button group instead of a text field with implied valid values.

**Reference**: `account-auth.md` Component Inventory, Accessibility.

---

#### `form-mode-toggle`

**Category**: Input
**Used In**: Account/Auth (Login ⇄ Register)

**Description**: A single field group whose submit button and helper text relabel between two related modes, rather than two separate tab-equivalent destinations.

**Specification**:
- Chosen deliberately over tabs when one mode should read as visually secondary to the other (Account/Auth: Guest is primary, Login/Register is secondary — tabs would have implied equal weight).
- Compatible field values (e.g. username) persist across the toggle.

**When to Use**: Two closely-related input modes where one should not visually compete with a higher-priority primary action on the same screen.
**When NOT to Use**: Two genuinely equal-weight destinations — use real tabs.

**Reference**: `account-auth.md` Layout Zones (rationale for toggle-over-tabs).

---

#### `mute-toggle`

**Category**: Input
**Used In**: Ricochet HUD (shared across all mini-game HUDs)

**Description**: A persistent, shared (not per-screen) audio-mute toggle.

**Specification**:
- State persists across sessions and across every mini-game's HUD (`ricochet-hud.md` Core Rule 4) — not re-implemented per mini-game.
- Mid-action toggle: in-flight sounds finish naturally; only new sounds are silenced (`ricochet-hud.md` Edge Case) — an abrupt full cutoff feels broken.

**When to Use**: Any screen with active audio during real-time gameplay.
**When NOT to Use**: N/A for this project — mute state is global, so this pattern's placement is the only per-screen decision, not its existence.

**Reference**: `ricochet-hud.md` Component Inventory, States & Variants.

---

#### `dismiss-link`

**Category**: Input
**Used In**: Account/Auth (account-linking prompt's "Maybe later")

**Description**: A low-visual-weight dismiss action for a non-blocking, optional prompt.

**Specification**:
- No state change beyond closing the prompt — never a punitive or data-altering action.
- Deliberately lighter-weight than a full button, matching the prompt's own "not a cold interruption" framing.

**When to Use**: Dismissing an optional, non-blocking suggestion the player can act on later.
**When NOT to Use**: Any action with a real consequence — that needs a real button (and likely a confirmation), not a soft link.

**Reference**: `account-auth.md` Component Inventory, Interaction Map.

---

### Feedback

#### `inline-error-text`

**Category**: Feedback
**Used In**: Account/Auth

**Description**: Contextual error text appearing near the field/action it relates to, rather than a global/modal error state.

**Specification**:
- Must be announced to screen readers when it appears, not just shown visually.
- Icon + text, never color/red-border alone.
- Enumeration-safety constraint (Account/Auth specific but pattern-general): when an error could reveal sensitive information (e.g. "which credential was wrong"), use identical generic text regardless of the actual cause.

**When to Use**: Validation or action failures tied to a specific, visible field or control.
**When NOT to Use**: System-wide failures (network/server errors) unrelated to a specific field — those need their own distinct treatment so they aren't mistaken for input mistakes.

**Reference**: `account-auth.md` States & Variants, Accessibility.

---

#### `contextual-prompt-banner`

**Category**: Feedback
**Used In**: Account/Auth (account-linking prompt)

**Description**: A non-blocking banner/card surfaced in context (e.g. after a meaningful reward), not a cold interruption.

**Specification**:
- Slides/fades in over the triggering screen, dismissal is equally lightweight (`account-auth.md` Transitions & Animations) — never a heavy modal-close treatment that overstates its importance.
- Trigger moment matters: surfaced after something positive happened, not at an arbitrary or punitive point.

**When to Use**: An optional suggestion tied to a specific moment where it's most relevant and least disruptive.
**When NOT to Use**: Anything the player must act on before proceeding — that's a blocking modal, not this pattern.

**Reference**: `account-auth.md` Component Inventory, Transitions & Animations.

---

#### `interstitial-message`

**Category**: Feedback
**Used In**: Account/Auth (session-expiry, account-linking)

**Description**: A short reassurance/context message explaining *why* the player is seeing a particular screen state.

**Specification**:
- Session-expiry variant must reassure ("your progress is safe") rather than read as a bare error — the player didn't do anything wrong.
- Distinct visual/textual treatment from a validation error (`account-auth.md` Acceptance Criteria) — conflating the two undermines the reassurance.

**When to Use**: Any forced state transition that could otherwise feel like a punitive failure to the player.
**When NOT to Use**: Player-initiated transitions (they already know why they're there) — this pattern exists specifically for *involuntary* transitions.

**Reference**: `account-auth.md` Player Context on Arrival, Component Inventory.

---

#### `conditional-hint-text`

**Category**: Feedback
**Used In**: Ricochet HUD (aim-hint text)

**Description**: Guidance text visible only during a specific game state, hidden otherwise.

**Specification**:
- Visibility is state-driven (Aiming only for Ricochet HUD), not a toggle the player controls.
- Shares layout space with other conditional/state elements when mutually exclusive by state, to avoid wasting persistent screen space on a rarely-relevant hint.

**When to Use**: Guidance that's only meaningful during one specific phase of an interaction.
**When NOT to Use**: Guidance relevant throughout — that belongs in persistent UI, not a conditional hint.

**Reference**: `ricochet-hud.md` Component Inventory, States & Variants.

---

#### `tweened-hp-bar`

**Category**: Feedback
**Used In**: Ricochet HUD (boss HP)

**Description**: A fill bar that animates smoothly toward a new value on change, rather than snapping instantly — paired with numeric text so the value is never communicated by fill-position/color alone.

**Specification**:
- Tween duration: 0.12s linear (`ricochet-hud.md` Tuning Knob, carried over from the prototype).
- On rapid successive changes (faster than the tween resolves), continuously re-targets the latest value rather than queuing discrete tweens (avoids a janky "catch-up" effect).
- This is **GDD-mandated feedback**, not decorative polish — a reduced-motion alternative must shorten the tween, not remove it entirely.
- Always paired with numeric text (accessibility — never color/fill-only).

**When to Use**: Any resource bar whose *change* (not just current value) is emotionally significant to communicate.
**When NOT to Use**: Discrete counters (ball count, turn number) — those update instantly; a tween on a non-"draining" value feels laggy rather than smooth.

**Reference**: `ricochet-hud.md` Transitions & Animations, Tuning Knobs.

---

### Data Display

#### `avatar-display`

**Category**: Data Display
**Used In**: Hub UI (header)

**Description**: A static player-avatar image in a header.

**Specification**: Non-interactive in v1 (no profile/settings tap-through defined yet).

**When to Use**: Persistent player identity display.
**When NOT to Use**: N/A — narrow, single-purpose pattern.

**Reference**: `hub-ui.md` Component Inventory.

---

#### `progress-summary`

**Category**: Data Display
**Used In**: Hub UI (level/progress header, mascot collection count)

**Description**: A compact text-or-text+bar summary of a progression metric ("Lv.7", "12/40 owned").

**Specification**: Read-only display; reused verbatim for structurally similar but semantically different progress metrics (level progression vs. collection completion) rather than building a separate component for each.

**When to Use**: Any "X of Y" or leveling-style progress summary that needs to be compact.
**When NOT to Use**: A progress metric that needs its own dedicated visual treatment (e.g. the boss HP bar, which uses `tweened-hp-bar` instead because the *change itself* is significant).

**Reference**: `hub-ui.md` Component Inventory.

---

#### `stat-tile`

**Category**: Data Display
**Used In**: Hub UI (stat grid)

**Description**: A small tile displaying one labeled statistic (best score, bricks destroyed, bosses defeated).

**Specification**: Read-only; labels are short by design and flagged as a localization expansion risk (see `hub-ui.md` Localization Considerations).

**When to Use**: Reference/bragging-rights stats, lower priority than primary content.
**When NOT to Use**: Real-time, frequently-updating stats during active gameplay — see `live-counter` instead.

**Reference**: `hub-ui.md` Component Inventory, Localization Considerations.

---

#### `quest-list-item`

**Category**: Data Display
**Used In**: Hub UI (Daily Quests card)

**Description**: A single quest's name, progress, and reward, display-only in the current scope.

**Specification**: Claiming interaction deliberately out of scope — no Daily Quests GDD/spec exists yet; this pattern covers display only.

**When to Use**: Listing discrete, individually-progressing objectives.
**When NOT to Use**: Once Daily Quests gets its own GDD/spec, this pattern will need a claimable-state variant — flagged in Gaps below.

**Reference**: `hub-ui.md` Component Inventory.

---

#### `leaderboard-row`

**Category**: Data Display
**Used In**: Hub UI (leaderboard)

**Description**: A single ranked row: rank, avatar, name, score.

**Specification**: Read-only; player names are player-generated and unbounded-length, requiring truncation (`hub-ui.md` Localization Considerations).

**When to Use**: Any ranked list of players/scores.
**When NOT to Use**: N/A — narrow, single-purpose pattern.

**Reference**: `hub-ui.md` Component Inventory, Localization Considerations.

---

#### `rarity-thumbnail`

**Category**: Data Display
**Used In**: Hub UI (mascot preview)

**Description**: A small mascot portrait color-coded by rarity tier.

**Specification**: **Must pair color with an icon/border shape, never color alone** — the art bible's Fern Green (Common) vs. Brick Red (danger, used elsewhere) is a known red-green confusion pair for colorblind players.

**When to Use**: Any rarity/tier-coded collectible thumbnail.
**When NOT to Use**: N/A — but the colorblind-safety requirement applies to *any* future rarity-coded UI, not just this thumbnail.

**Reference**: `hub-ui.md` Component Inventory, Accessibility.

---

#### `level-pill`

**Category**: Data Display
**Used In**: Ricochet HUD (top bar)

**Description**: A compact pill showing the current level number.

**Specification**: Not a live system read — displays the level carried as navigation context from how the player entered (hero-card default vs. level-select grid choice).

**When to Use**: A single-number, low-attention-frequency status display in a persistent bar.
**When NOT to Use**: N/A — narrow, single-purpose pattern.

**Reference**: `ricochet-hud.md` Component Inventory, Data Requirements.

---

#### `boss-name-label`

**Category**: Data Display
**Used In**: Ricochet HUD (boss bar)

**Description**: Text display of the current boss's name.

**Specification**: Must truncate gracefully within the boss bar's width rather than push the HP text/fill out of position — boss names are variable-length and the roster isn't finalized (see the project's existing IP-risk note on boss naming, tracked separately, not resolved by this pattern).

**When to Use**: Naming a specific enemy/entity in a persistent combat HUD.
**When NOT to Use**: N/A — narrow, single-purpose pattern.

**Reference**: `ricochet-hud.md` Component Inventory, Localization Considerations.

---

#### `hp-text`

**Category**: Data Display
**Used In**: Ricochet HUD (boss bar)

**Description**: Numeric HP value, always paired with `tweened-hp-bar`'s fill — never fill-only.

**Specification**: Real-time, dirty-checked update (only re-renders when the value actually changes) — an architectural performance requirement, not just a UX nicety, given how often HP can change during a fast volley.

**When to Use**: Anywhere a resource bar's fill needs a color-independent numeric backup.
**When NOT to Use**: N/A — this pattern exists specifically to accompany a fill bar, not standalone.

**Reference**: `ricochet-hud.md` Component Inventory, Data Requirements.

---

#### `live-counter`

**Category**: Data Display
**Used In**: Ricochet HUD (ball count, turn number, bricks destroyed)

**Description**: A real-time, instantly-updating (no tween) discrete counter.

**Specification**: Dirty-checked like `hp-text`; updates instantly rather than tweening, since these are discrete counts, not a "draining" resource — a tween here would feel laggy rather than smooth.

**When to Use**: Any frequently-changing discrete count during real-time gameplay.
**When NOT to Use**: A value whose *change* is itself meant to feel significant (use `tweened-hp-bar` instead) or a low-frequency stat (use `stat-tile` instead).

**Reference**: `ricochet-hud.md` Component Inventory, Transitions & Animations.

---

#### currency-chip *(pre-existing, GDD-named — not invented in UX)*

**Category**: Data Display
**Used In**: Hub UI (header), Ricochet HUD (top bar)

**Description**: The shared coin/gem balance display, named and owned by `currency-system.md` (Currency System GDD) — UX specs reference it, they don't redefine it.

**Specification**: Read-only display of `currency-system.md`'s wallet balance. Locale-specific number formatting (thousands separators) required — flagged in both `shared-hub.md` and `hub-ui.md`.

**When to Use**: Any screen displaying the player's coin/gem balance.
**When NOT to Use**: Never reinvent a local currency display — always reference this shared component.

**Reference**: `currency-system.md` (GDD, source of truth); `hub-ui.md` and `ricochet-hud.md` Component Inventory (usage sites).

---

### CTA / Branding

#### `text-cta-button`

**Category**: CTA/Branding
**Used In**: Hub UI ("View Collection"), Account/Auth (Login/Register submit)

**Description**: A standard labeled call-to-action button, text-only.

**Specification**: Minimum touch target 44×44pt/48×48dp; short labels are a recurring localization-expansion risk (flagged in every spec that uses this pattern) — reserve generous horizontal padding.

**When to Use**: A clear, singular, moderately-important action.
**When NOT to Use**: The single most important action on a screen — use `hero-cta-button` for that instead, to establish visual hierarchy.

**Reference**: `hub-ui.md`, `account-auth.md` Component Inventory.

---

#### `hero-cta-button`

**Category**: CTA/Branding
**Used In**: Account/Auth ("Play as Guest")

**Description**: The single most visually prominent action on a screen — bigger/bolder than a standard `text-cta-button`.

**Specification**: Reserved for the one action a screen's Information Hierarchy names as primary; using more than one hero CTA per screen defeats its purpose.

**When to Use**: Exactly one per screen, for the action the Purpose & Player Need section identifies as the primary goal.
**When NOT to Use**: Secondary or tertiary actions — those dilute the hierarchy this pattern exists to establish.

**Reference**: `account-auth.md` Layout Specification (Information Hierarchy rationale).

---

#### `social-login-button`

**Category**: CTA/Branding
**Used In**: Account/Auth (Apple, Google Play Games)

**Description**: A platform-convention-styled button for third-party OAuth login.

**Specification**: Icon + label per each platform's own branding guidelines; ordering and exact wording should defer to Apple/Google's own platform conventions, not just this project's general button styling or translators (`account-auth.md` Open Questions).

**When to Use**: Any social/platform authentication option.
**When NOT to Use**: N/A — but always verify current platform guidelines at implementation time rather than assuming this project's general button style applies.

**Reference**: `account-auth.md` Component Inventory, Localization Considerations, Open Questions.

---

#### `branding-header`

**Category**: CTA/Branding
**Used In**: Account/Auth (logo)

**Description**: App logo/wordmark display.

**Specification**: Static, non-interactive.

**When to Use**: First-impression screens establishing app identity.
**When NOT to Use**: Repeated on every screen — that would compete with each screen's own primary content hierarchy.

**Reference**: `account-auth.md` Component Inventory.

---

#### `divider-label`

**Category**: CTA/Branding
**Used In**: Account/Auth ("or" separator)

**Description**: A short labeled visual divider separating two alternative paths.

**Specification**: Non-interactive, purely a visual/semantic separator.

**When to Use**: Separating two genuinely alternative (not sequential) paths, e.g. "Play as Guest" **or** "Log In."
**When NOT to Use**: Separating sequential steps — that's a different visual language (e.g. a stepper), not this pattern.

**Reference**: `account-auth.md` Component Inventory, ASCII Wireframe.

---

## Gaps & Patterns Needed

Patterns this library doesn't have yet, because the screens that would need
them aren't GDD'd/specced yet (confirmed via `Glob` — no Shop, Mascot
Database, or Quack Runner GDDs currently exist in `design/gdd/`):

- **Shop/purchase patterns**: item card, price display, purchase-confirm flow, IAP-specific states (pending/failed/restored). Blocked on a Shop/Monetization GDD.
- **Mascot Gallery patterns**: a full gallery grid (vs. Hub UI's compact preview strip), mascot detail view, acquisition/reveal animation. Blocked on a Mascot Database GDD.
- **Claimable quest-item variant**: `quest-list-item` currently covers display only; a claim button + claimed-state variant is needed once Daily Quests has its own GDD.
- **A second mini-game's HUD patterns**: Quack Runner (or any future mini-game) will likely reuse `header-icon-action`, `live-counter`, and currency-chip, but will need its own game-specific feedback patterns (analogous to `tweened-hp-bar`) once that mini-game has a GDD for the native pivot.
- **Reduced-motion pattern variants**: every animated pattern here (`tweened-hp-bar`, transitions in Account/Auth, Hub UI's count-up/pop-in effects) needs a reduced-motion alternative once the project-wide reduced-motion policy (still an open item across all four specs) is decided.

---

## Open Questions

1. **No project-wide accessibility tier exists yet** — every pattern above defaults to a WCAG-AA assumption, inherited from the specs it was extracted from. Consider running `/gate-check` to see whether this blocks a phase gate.
2. **No reduced-motion policy exists yet** — blocks finalizing the reduced-motion variant for every animated pattern (see Gaps).
3. **Four analytics events remain proposed but unapproved** by an Analytics GDD owner: `hub_navigation`, `auth_completed`, `account_linked`, `run_abandoned` — none of them are pattern-library concerns directly, but several patterns here (`hero-card`, `text-cta-button`, `header-icon-action`) are the trigger points for those events, so approving/rejecting them may add detail to those patterns' Specification sections later.
4. **Currency-chip's exact formatting spec** (thousands separators, RTL digit rendering) is flagged in two source specs but not fully resolved — belongs to whoever implements the shared component, not this library.
