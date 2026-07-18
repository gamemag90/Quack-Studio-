# UX Spec: Account/Auth

> **Status**: In Design
> **Author**: Abdulrahman Alenazan + Claude
> **Last Updated**: 2026-07-11
> **Journey Phase(s)**: unknown — no `design/player-journey.md` exists yet
> **Platform Target**: Touch, mobile (iOS 14+/Android API 25+) — per `technical-preferences.md`
> **Template**: UX Spec

---

## Purpose & Player Need

Account/Auth has no direct player fantasy — the GDD is explicit that this
is pure infrastructure, felt only through its *absence* of friction. But
it's also the very first thing a new player ever sees, so it carries a
first-impression weight the GDD's "invisible when working" framing doesn't
fully capture: this screen isn't just infrastructure, it's the game's front
door.

What would go wrong without it done well: forced re-logins, a confusing
choice between "guest" and "register," or a data-loss trap (an unlinked
guest losing everything on reinstall — a real risk the GDD names
explicitly) would all directly undermine retention before a player has even
played a single mini-game.

**The player arrives at this screen wanting to** get into the game as fast
as possible — ideally with the lowest-commitment path available (guest),
while still being able to make an informed choice if they'd rather commit
to an account up front.

---

## Player Context on Arrival

| Arrival | Immediately prior | Assumed emotional state |
|---|---|---|
| **Brand-new player, first-ever launch** | Just installed the app, no prior context | Curious but impatient — low tolerance for friction before reaching actual gameplay; this is a conversion-critical moment |
| **Returning player, valid stored token** | App was closed/backgrounded | Doesn't consciously "arrive" here at all — should skip straight through to Shared Hub, not see this screen |
| **Session expired mid-use** | Was actively doing something (mid-run, mid-shop-browse) | Interrupted, possibly mildly annoyed — the GDD is explicit this must be an "explicit session-expired prompt, not a silent failure," so the player understands *why* they're here, not just that they suddenly are |

The third context is the one most UX designs get wrong by treating it
identically to first-launch — a returning player being told to log in
again needs reassurance ("your progress is safe, just sign back in") more
than they need a fresh sales pitch for guest-vs-account.

---

## Navigation Position

**Login/Register/Guest entry** sits at the true root — the *only* thing
reachable before authentication resolves:

`[App launch] → Account/Auth (pre-auth root) → Shared Hub (post-auth root)`

**Session-expired interstitial** interrupts from *any* point in the
post-auth app (Shared Hub, a mini-game, a modal) and returns to the same
pre-auth entry point on completion.

**Account-linking** is a *different* position entirely — not a root
screen, but an in-context prompt surfaced *from within* the post-auth
experience (per the GDD, "ideally after a meaningful reward," not a cold
interruption) while already Authenticated as a Guest. It's the odd one out
in this spec: everything else here is pre-auth, this one is post-auth.

---

## Entry & Exit Points

**Entry sources:**

| Entry Source | Trigger | Player carries this context |
|---|---|---|
| App launch | No valid stored token | Nothing — clean slate |
| Anywhere post-auth (Shared Hub, mini-game, modal) | Token expires mid-session | Whatever they were doing is interrupted; shown as an explicit interstitial, not silently — see States & Variants |
| Hub UI header | Explicit Logout (with confirmation, per `hub-ui.md`) | Voluntary — returns to a clean Login/Register/Guest entry, not the interstitial variant |

**Exit destinations:**

| Exit Destination | Trigger | Notes |
|---|---|---|
| Shared Hub | Successful register, login, or guest session start | Matches `shared-hub.md`'s own documented entry source exactly |
| *(stays in place, no navigation)* | Account-linking success | Upgrades the auth state in place — the player doesn't leave whatever screen prompted the link |
| *(stays in place, no navigation)* | Account-linking rejected (conflict) | Shows an error, no data changes on either account (GDD Edge Case) |

One irreversible-adjacent note: an **unlinked guest account that's lost
(device lost, app reinstalled) has no recovery path** — this isn't a
navigation exit, but it's the single highest-stakes "point of no return"
this entire flow contains, and it's why the account-linking prompt exists
at all.

---

## Layout Specification

### Information Hierarchy

1. **Guest CTA** — primary action, per the confirmed decision (biggest, top-most interactive element)
2. **Login/Register access** — secondary but clearly visible, not buried (username/password fields + toggle)
3. **Social login buttons** (Apple/Google Play Games) — grouped with Login/Register as another account-creation path, not competing with Guest for top billing
4. **Error messaging** — not part of the default hierarchy, but takes visual priority *when present* (e.g., "username taken," generic invalid-credentials message)
5. **Branding/legal** (logo, terms-of-service link) — lowest priority, footer-level

### Layout Zones

**Single vertical stack**: Logo → large "Play as Guest" button (hero CTA)
→ divider ("or") → a **toggle, not tabs** (a single field group whose
submit button and helper text relabel between "Log In"/"Create Account"
via a text link, e.g. "New here? Create an account") → social login
buttons below the fields → footer legal.

Toggle chosen over tabs deliberately: tabs visually imply two
equally-weighted destinations, which fights the confirmed Guest-primary
decision. A toggle keeps Login/Register compact and visibly secondary
without hiding it.

**On-screen keyboard avoidance (required, not optional)**: this is the
first spec with real text-entry fields, and a static layout risks the
keyboard covering the password field or the submit button on smaller
devices — one of the most common real mobile-form bugs. The layout must
scroll/resize so the currently-focused field **and** the submit button
remain visible above the keyboard at all times. This is a binding layout
requirement, not a nice-to-have.

### Component Inventory

| Screen/Moment | Component | Content | Interactive? | Pattern |
|---|---|---|---|---|
| Main entry | Logo/branding | App logo | No | New — `branding-header` |
| Main entry | Guest CTA | "Play as Guest" hero button | **Yes** → Guest session → Shared Hub | New — `hero-cta-button` |
| Main entry | Divider | "or" separator | No | New — `divider-label` |
| Main entry | Username field | Text input | **Yes** | New — `text-input-field` |
| Main entry | Password field | Masked text input | **Yes** | New — `text-input-field` (masked variant) |
| Main entry | Toggle link | "New here? Create an account" / "Have an account? Log in" | **Yes** — relabels the form, no navigation | New — `form-mode-toggle` |
| Main entry | Submit button | "Log In" / "Create Account" (relabels with toggle) | **Yes** → Shared Hub, or inline error | New — `text-cta-button` (reused from Hub UI) |
| Main entry | Social login buttons (×2) | Apple, Google Play Games | **Yes** → OAuth flow → Shared Hub | New — `social-login-button` (platform-convention icon+label) |
| Main entry | Inline error message | Contextual validation/auth error text | No (display, appears conditionally) | New — `inline-error-text` |
| Session-expired interstitial | Reassurance message | "Your progress is safe — log back in to continue" | No | New — `interstitial-message` |
| Session-expired interstitial | Re-auth entry | Same Login/Register/Guest* fields as main entry | **Yes** | Reuses main-entry components |
| Account-linking prompt | Trigger context | Appears after a meaningful reward (e.g. post-run screen), not a cold interrupt | No | New — `contextual-prompt-banner` |
| Account-linking prompt | Message | "Don't lose your progress — link an account" | No | Reuses `interstitial-message` |
| Account-linking prompt | Link CTA | Leads into the same Login/Register/social fields, scoped to linking not creating | **Yes** | Reuses main-entry components |
| Account-linking prompt | Dismiss/skip | "Maybe later" | **Yes** — closes prompt, no state change | New — `dismiss-link` |

`*` — **Open question, not resolved here**: should a session-expiry
interstitial still offer "Play as Guest" as an option? A guest whose
session expired presumably already has a device-stored identity —
starting a *new* guest session here could orphan their old one. Flagged
in Open Questions rather than guessed at.

### ASCII Wireframe

```
┌─────────────────────────────────┐
│                                   │
│           [Duck Logo]            │
│          QUACK STUDIO            │
│                                   │
│  ┌─────────────────────────────┐ │
│  │      🦆 Play as Guest       │ │  ← hero CTA
│  └─────────────────────────────┘ │
│                                   │
│  ─────────────  or  ───────────  │
│                                   │
│  ┌─────────────────────────────┐ │
│  │ Username                     │ │
│  └─────────────────────────────┘ │
│  ┌─────────────────────────────┐ │
│  │ Password                     │ │
│  └─────────────────────────────┘ │
│  [ Log In ]                      │
│  New here? Create an account     │
│                                   │
│  ┌───────────┐  ┌───────────┐    │
│  │  Apple    │  │  Google   │    │
│  └───────────┘  └───────────┘    │
│                                   │
│        Terms · Privacy           │
└─────────────────────────────────┘
```

---

## States & Variants

| State / Variant | Trigger | What Changes |
|---|---|---|
| **Default** | Main entry, no input yet | Empty form, Guest CTA and social buttons enabled |
| **Submitting** | Register/Login/Guest/Social request in flight | Submit button shows a loading indicator, disabled to prevent double-submit — required by the GDD, since bcrypt cost-10 is deliberately slow and must not be silently worked around |
| **Validation error (username taken)** | Register with a taken username | Inline error near the username field, generic "409" mapped to human copy — form retains entered values, doesn't clear |
| **Validation error (invalid credentials)** | Wrong password or non-existent username | Identical generic message either way (GDD's enumeration-protection requirement) — must not reveal which field was wrong |
| **Validation error (malformed input)** | Username/password fails length/charset rules before submission | Inline, real-time field-level validation — catches it before a network round-trip |
| **Network/server error** | Connectivity failure, 5xx | Distinct from validation errors — a retry-oriented message ("Couldn't connect — try again"), not blamed on the player's input |
| **Keyboard visible** | A text field is focused | Layout scrolls/resizes so the focused field and the submit button both remain visible above the keyboard — never obscured |
| **Session-expired interstitial** | Token expiry mid-session | Separate reassurance-first screen (see Player Context on Arrival) |
| **Account-linking prompt shown** | Contextual trigger post-reward | Non-blocking banner/card, dismissible |
| **Account-linking conflict** | Linking an identity already tied to another account | Clear conflict error, both accounts' data untouched (GDD Edge Case) |

---

## Interaction Map

Mapping interactions for: **Touch only** (per `technical-preferences.md`) —
no gamepad, no hover.

| Component | Touch Action | Immediate Feedback | Outcome |
|---|---|---|---|
| Guest CTA | Tap | Press-state, haptic tick → Submitting state | Guest session created → Shared Hub |
| Username/Password fields | Tap | Field focus, on-screen keyboard appears | Text entry; real-time validation feedback on blur |
| Toggle link | Tap | Form relabels (fields persist if compatible, e.g. username carries over) | No navigation — same screen, different submit mode |
| Submit button | Tap | Press-state → Submitting state (loading indicator, disabled) | Login/Register success → Shared Hub, or inline error |
| Social login button | Tap | Press-state, haptic tick | → Native OAuth flow (platform-provided UI, outside this spec's control) → Shared Hub on success |
| Account-linking "Link Account" | Tap | Press-state | Opens Login/Register/social fields scoped to linking |
| Account-linking "Maybe later" | Tap | Prompt dismisses | Stays exactly where the player was, no state change |

**Platform convention worth confirming, not assumed here**: social button
*order* typically differs by OS (Apple first on iOS per App Store
guidelines, Google first on Android) — flagged in Open Questions as an
implementation detail rather than decided in this spec.

---

## Events Fired

Checking against the catalog surfaces a real gap: there's `app_open`/
`session_start` but nothing that distinguishes signup method (guest vs.
login vs. register vs. social) or tracks the guest→linked conversion —
exactly the metric the GDD's own data-loss risk (Edge Cases) makes worth
watching closely.

| Player Action | Event Fired | Payload / Data |
|---|---|---|
| Guest session started | `app_open` (existing) + **proposed new**: `auth_completed{method:"guest", isNewPlayer:true}` | — |
| Register success | `auth_completed{method:"register", isNewPlayer:true}` (proposed) | — |
| Login success | `auth_completed{method:"login", isNewPlayer:false}` (proposed) | — |
| Social login success | `auth_completed{method:"apple"|"google", isNewPlayer}` (proposed) | — |
| Validation/auth error | *(none — not a funnel step, avoid noisy events on every keystroke error)* | — |
| Account-linking success | **proposed new**: `account_linked{fromMethod:"guest", toMethod}` | — |
| Account-linking dismissed | *(optional, not decided)*: could inform how naggy the prompt feels — flagged, not required | — |
| Session-expiry re-auth | Same `auth_completed` as a fresh login, no distinct event needed | — |

**Two proposed new events**: `auth_completed` and `account_linked` — both
**client-emitted** per ADR-0006's ownership split (these are
client-observable outcomes of a server call, not server-authoritative
economy events). The guest→linked conversion rate is a genuinely important
product metric given the data-loss risk this GDD names explicitly — worth
prioritizing this catalog addition. Routed to the Analytics GDD owner, not
decided unilaterally here.

---

## Transitions & Animations

- **App launch → Main entry**: standard cold-start splash → fades into the
  entry screen (or straight to Shared Hub if a valid token exists — no
  Account/Auth screen flash for returning players).
- **Main entry → Shared Hub**: on auth success, a brief celebratory beat
  feels wrong for Login (routine) but appropriate for first-time
  Register/Guest (a small "welcome" moment) — differentiate: Login = quick
  fade/cut into Shared Hub; Register/Guest-first-time = a slightly warmer
  transition (matches the art bible's "character-first moments" principle —
  perhaps the duck mascot reacting).
- **Session-expired interstitial**: appears as an overlay/takeover, not a
  hard app-restart feel — reinforces "your progress is safe" rather than
  "something broke."
- **Account-linking prompt**: slides/fades in as a non-blocking banner or
  card over whatever screen triggered it (per the GDD, "not a cold
  interruption") — dismissal is equally lightweight, never a heavy
  modal-close animation that overstates its importance.
- **Submitting state**: loading indicator on the submit button itself (not
  a full-screen blocker) — keeps the form visible so the player isn't left
  wondering if their input was lost.
- **Inline errors**: a brief shake or highlight on the offending field,
  text fades in below it — not a jarring full-screen error state for
  what's usually a simple typo.

**Reduced motion**: same project-wide gap flagged in every prior spec —
this flow's candidate animations (welcome beat, field shake) need reduced
alternatives once that policy exists.

---

## Data Requirements

Unlike the previous two specs, this screen genuinely **writes** data.

| Data | Source System | Read/Write | Notes |
|---|---|---|---|
| Username, password (input) | Account/Auth (server) | **Write** | Validated server-side (uniqueness, length/charset); bcrypt-hashed before storage — client never sees the hash |
| JWT session token | Account/Auth (server) → client | **Write** (client stores it) | Stored via `ISecureTokenStore` (ADR-0003, iOS Keychain/Android Keystore) — never `PlayerPrefs` |
| New player record (default currency/progress) | Save/Persistence, via Account/Auth | **Write** | Created on successful Register or Guest session start |
| Guest device identity | Client-generated | **Write** | Per the GDD, "a device-generated identity requiring no registration step" |
| Account-link mapping (guest → social/password) | Account/Auth (server) | **Write** | Same player ID persists, per GDD Acceptance Criteria — this is an identity mutation, not a new record |

**Architectural attention flag** (per this skill's own instruction — UI
defines the need, doesn't dictate delivery): every row above is a write,
several of them security- or identity-sensitive (password handling, token
storage, identity linking). This spec assumes ADR-0003's
`ISecureTokenStore` contract for the one piece it can point to concretely;
the actual register/login/link request/response shapes are the backend's
implementation, not decided here.

---

## Accessibility

No accessibility tier committed yet — WCAG-AA baseline (same project-wide
gap).

- **Keyboard/gamepad navigation**: N/A — touch-only platform. But
  **screen-reader element order** matters: VoiceOver/TalkBack
  swipe-navigation order through the form must be logical (Guest CTA →
  username → password → submit → toggle → social buttons), not
  visual-position-only.
- **Minimum touch target size**: same standard established in `hub-ui.md`
  — **44×44pt (iOS) / 48×48dp (Android)** minimum for every interactive
  element (Guest CTA, submit button, toggle link, social buttons,
  dismiss/skip link).
- **Form field labels**: every input needs an explicit accessible label
  ("Username," "Password"), not just placeholder text (placeholder-as-label
  is a common, real accessibility failure — it disappears once the user
  starts typing).
- **Password field**: include a "show password" toggle — genuinely useful
  for anyone, and particularly reduces error rate for users relying on
  screen magnification or with motor-control difficulty correcting typos
  blind.
- **Password-manager/autofill support**: fields must use correct semantic
  input types (username/new-password/current-password) so iOS/Android
  system password managers and autofill work correctly — a real usability
  and security benefit (encourages stronger passwords than users would type
  manually).
- **Error announcement**: inline errors must be announced to screen readers
  when they appear (not just visually shown) — a validation error a
  screen-reader user can't detect is effectively a silent failure.
- **Color-independent communication**: error states use an icon + text, not
  red-border-only; the "submitting" disabled state must be conveyed to
  screen readers (e.g. "button, dimmed, Log In, disabled"), not just
  visually grayed out.
- **Reduced motion**: already flagged — welcome-beat and field-shake
  animations need alternatives.

---

## Localization Considerations

| Element | Longest expected (EN) | Layout-critical? | Note |
|---|---|---|---|
| "Play as Guest" CTA | 14 chars | **Yes — hero button, must stay one line** | Highest-risk short-button-label category again (same pattern as Hub UI's "Play" CTA) |
| "Create Account" / "Log In" (toggle-relabeled submit) | "Create Account" (15 chars) | Yes | Must accommodate the longer of the two relabeled states |
| "New here? Create an account" toggle link | 28 chars | Moderate — can wrap to 2 lines if needed | Lower risk than button labels since it's not button-boxed |
| Enumeration-safe error ("Incorrect username or password") | 31 chars | Yes, within an inline error area | Already fairly long in English — 40% expansion needs a multi-line-capable error area, not a single-line-only design |
| Social button labels ("Continue with Apple"/"Google") | ~20 chars | Yes | Platform convention often dictates exact wording — flag for confirmation against Apple/Google's own localization guidelines, not just this project's translators |
| Session-expiry reassurance message | Full sentence | No, multi-line by design | Lower risk — already designed to wrap |

**HIGH PRIORITY**: "Play as Guest" and the toggle-relabeled submit button —
same short-button-label risk category flagged in Hub UI's spec, now
appearing in the single most first-impression-critical screen in the game.

---

## Acceptance Criteria

- [ ] Screen loads within 100ms of app launch when no valid token exists (performance)
- [ ] "Play as Guest" is visually the most prominent interactive element on the main entry screen (core purpose — matches the confirmed Guest-primary decision)
- [ ] Registering with a valid unique username and ≥6-char password creates a new player and navigates to Shared Hub (navigation/core purpose)
- [ ] Registering with a taken username shows an inline error and does not navigate away or clear the form (error state)
- [ ] Logging in with invalid credentials (wrong password OR non-existent username) shows the identical generic error message in both cases (core purpose — enumeration protection)
- [ ] A session-expiry interstitial is visually and textually distinct from a validation error — includes a "your progress is safe" reassurance, not a bare error state (core purpose)
- [ ] Linking a guest account to a social/password identity preserves the same player ID and all prior progress (core purpose)
- [ ] Attempting to link an identity already tied to another account shows a conflict error and changes no data on either account (error state)
- [ ] All form fields have accessible labels and the submit button's disabled/loading state is announced to screen readers (accessibility)
- [ ] The submit button shows a loading state and is disabled during an in-flight request, preventing double-submission (core purpose — GDD-required given deliberate bcrypt slowness)
- [ ] With any field focused and the on-screen keyboard visible, both that field and the submit button remain visible and unobscured, on the smallest target device (core purpose — mobile-form keyboard avoidance)

---

## Open Questions

1. **Player journey map not yet created** — same project-wide gap as prior
   specs.
2. **Accessibility tier not yet defined** — defaulted to WCAG-AA.
3. **No reduced-motion policy exists** — same project-wide gap.
4. **`auth_completed` and `account_linked` events unapproved** — proposed
   here, need Analytics GDD owner sign-off; flagged as higher-priority than
   usual given the data-loss risk they'd help measure.
5. **Guest option on the session-expiry interstitial is ambiguous** — could
   orphan an existing device-stored guest identity by starting a new one.
   Not resolved here.
6. **Social button ordering/wording should defer to Apple/Google's own
   platform guidelines**, not just this project's translators — needs
   confirmation at implementation.
7. **No password-reset/forgot-password flow exists anywhere in
   `account-auth.md`.** This is a genuine gap, not a deliberate scope
   decision — a real login screen needs one. Routed back to the GDD owner,
   since this is a product/security decision (e.g., email-based reset
   requires an email field this GDD never mentions collecting), not
   something this UX spec should invent unilaterally.
8. **Account-linking prompt's exact trigger moment** ("after a meaningful
   reward," per the GDD) isn't concretely specified — which reward, how
   many times shown, whether it escalates urgency over time. A
   product/design decision, not resolved here.
9. **Guest-account retention window** (GDD's own Open Question #2) —
   inherited, unresolved; relevant to how urgently this flow should push
   account-linking.
10. **Server-side logout/revocation** (GDD's own Open Question #1, tied to
    ADR-0003's `auth_token_revocation_and_ttl` registry item) — inherited;
    doesn't change this spec's Logout UX, but the underlying security
    posture is still open.
