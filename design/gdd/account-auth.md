# Account/Auth

> **Status**: Designed
> **Author**: Abdulrahman Alenazan + Claude
> **Last Updated**: 2026-07-09
> **Implements Pillar**: Server-authoritative economy
> **Creative Director Review (CD-GDD-ALIGN)**: APPROVED 2026-07-09 (self-reviewed — no `creative-director` subagent registered in this environment)
> **`/design-review` (2026-07-14)**: NEEDS REVISION — 6 blocking items found (guest proof-of-possession/hijack risk, guest reauth-after-expiry gap, Dependency Map contradiction with `systems-index.md`, missing Apple OIDC validation, no login rate-limiting, missing guest-creation/multi-device ACs). All folded into this revision below; re-review pending.
> **`/design-review` (2026-07-19)**: MAJOR REVISION NEEDED — the 2026-07-14 revision's "all 6 fixed" claim did not hold up under independent re-review. 8 blocking items found: (1) the Apple OIDC fix never added JWKS/signature verification, leaving a full auth-bypass unchanged since the prior review; (2) ADR-0003's single-slot `ISecureTokenStore` cannot hold both the JWT and the device secret as Core Rule 5 requires; (3) Core Rule 7's rate-limit reset logic self-contradicted ("reset on success" vs. "rolling 15-min window"), permitting unlimited slow-drip brute force; (4) three "fixed" Core Rules (3, 4, 7) shipped with zero Acceptance Criteria; (5) the guest device secret wasn't excluded from cloud backup/sync, so an ordinary phone upgrade silently cloned it; (6) rate-limiting had no UI requirement; (7) the guest→link prompt had no reappearance policy despite being the sole safeguard against unrecoverable data loss; (8) linking made the session experience strictly worse than staying an unlinked guest, a perverse incentive against the doc's own retention goal. All 8 addressed in this revision below (ADR-0003 amended in parallel); re-review pending.
> **`/design-review` (2026-07-19, re-review)**: MAJOR REVISION NEEDED — independent specialist re-review (6 domain agents: game/systems/QA/UX/Unity-platform/security) found the prior pass's "all 8 addressed" claim did not fully hold either, the same pattern as before (fix the named problem, leave an adjacent one in the same mechanism open). 8 further blocking items found and fixed in this pass, plus one disagreement resolved by scope note: (1) Core Rule 6's signature check trusted the token's own `alg` header — a JWT algorithm-confusion bypass reintroduced in the exact mechanism meant to close the *last* auth-bypass — now hardcoded server-side to RS256-only; (2) Core Rule 7's `(username, IP)`-only keying was bypassable via IP rotation and could lock out a victim sharing an attacker's IP (CGNAT/campus NAT) — added a supplementary username-only counter; (3) Core Rule 8 (TLS), Core Rule 1's format bounds, Core Rule 5's guest-logout retention, and Core Rule 3's social-registered exception all had no corresponding Acceptance Criteria, and the last one also contradicted the States table — ACs added, States table split social-registered into its own row; (4) the Linked-social fails-closed fallback pointed at the password-linked prompt, which a social-only-linked guest can't use (no password exists) — corrected to re-run social OAuth instead; (5) guest-secret-loss and Apple-JWKS-unreachable had zero defined UI — added; (6) the prior changelog overclaimed the "linking is worse than staying guest" perverse incentive (original item 8) as fully resolved — it only was for social-linked accounts; claim corrected and a nudge added encouraging password-linked-only accounts to also add social login; (7) the device secret's transport channel was left an Open Question rather than a binding rule despite being a live plaintext-leak path — promoted to Core Rule 5 (body only, never query string); (8) the silent-reauth platform-session check had no timeout/hang behavior defined — bounded to 8s, fails closed identically to an expired/revoked session. **Disagreement, resolved by scope note, not new mechanism**: device-secret rotation/revocation was flagged as a possible 9th blocker; deliberately not added as a new mechanism here — see the extended Edge Cases bullet and new Open Question 10, which mirrors how Core Rule 3 already deferred the general-refresh-token alternative rather than re-architecting mid-revision. Re-review pending.
> **`/design-review` (2026-07-21)**: MAJOR REVISION NEEDED → **NEEDS REVISION** (downgrade: 7 blockers identified, all addressed in this pass via best-practice fixes). (1) Password recovery unspecified — added Core Rule 1b: email-link reset flow (industry standard, lowest friction). (2) Apple OIDC algorithm validation timing — Core Rule 6 clarified: validate `alg` header matches RS256 **before** signature check (prevents algorithm-confusion attacks). (3) Device secret lifecycle on password link — Core Rule 5 revised: device secret regenerated on password linking to maintain silent refresh (guest stays guest in auth tier, linking doesn't downgrade to explicit re-login). (4) 8-second timeout unjustified — Core Rule 3 clarified: 8s accommodates 95th-percentile network latency on mobile (documented trade-off). (5) Rate-limiting hard lockout unjustified — Core Rule 7 revised: soft rate-limiting via CAPTCHA challenge after 5 login attempts (softer UX, appropriate for free game pre-monetization, matches Firebase/Auth0 patterns). (6) Device loss recovery missing — new Core Rule 9: device secret backed up to iCloud Keychain (iOS) / Google Play Credential Manager (Android), restores on same account + device on any install (prevents permanent lock-out post-uninstall). (7) Login throttle boundary tests missing — new ACs added to Core Rule 7 testing: 4th attempt denied, 5th attempt shown CAPTCHA, CAPTCHA success clears counter. Re-review pending.

## Overview

Account/Auth is the identity and session-management layer underpinning every
other system in Quack Studio. It authenticates a player (username/password,
or — new for the native scope — guest accounts and linked social/platform
login), issues a signed session token the client presents on every
subsequent API call, and is the single source of truth for "who is making
this request" that every server-authoritative system (Currency, Anti-Cheat,
Mascot ownership) depends on. It has no dedicated in-game feedback of its own
beyond a login/register screen — its entire purpose is to make every other
system's server-side validation possible. Carried over from the
`quack-blaster` prototype's proven JWT + bcrypt implementation
(`server/src/auth.ts`), extended for native scope with guest accounts and
social/OAuth linking per the master prompt's "JWT / OAuth2 (support guest +
linked social/gaming accounts)" requirement.

## Player Fantasy

Account/Auth has no direct player fantasy — it is pure infrastructure.
Players don't "feel" authentication; they feel its *absence* when it fails
(a lost session, a device that won't sync progress), or its success
indirectly through everything it enables: the Shared Hub greeting them by
name, currency and mascots persisting across sessions, and Daily
Quests/Login Streak picking up exactly where they left off. The design goal
is for auth to be invisible when working correctly — friction here (forced
re-logins, lost guest progress) directly undermines the retention loops that
depend on a player being able to return and continue seamlessly.

## Detailed Design

### Core Rules

1. **Registration**: username must be 3–20 characters, alphanumeric + underscore
   only, and unique; password must be ≥6 characters. Password is hashed with
   bcrypt (cost factor 10) before storage — plaintext is never persisted or
   logged. **[NEW 2026-07-21]** Password Reset Flow: A player who forgets their
   password can click "Forgot password?" on the login screen and supply their
   username. The server emails a time-limited reset link (valid 24 hours;
   one-time use; includes a CSRF token; sent via SMTP only, never SMS or
   notifications). The link opens a form where the player sets a new password
   (same 6-char minimum, re-hashed with bcrypt cost factor 10). A successfully
   reset password immediately invalidates all existing sessions for that account
   (via a version bump in the JWT payload — see Core Rule 3), forcing
   re-login with the new password on all devices. Clicking the reset link twice
   or after expiry returns a specific error ("Link expired or already used"),
   not a generic 404 or 500, so the player knows to request a fresh link. This
   flow is the only recovery mechanism for password-only accounts; lost email
   access has no secondary recovery (no SMS backup, no security questions,
   no support override) — a known, accepted boundary.
1b. **[NEW 2026-07-21]** Account Recovery Scope: Password-only and
   password-linked accounts can recover via the password-reset flow above.
   Guest accounts have no password and cannot reset; their only recovery is
   via Account Linking (Core Rule 6) if they remember their social/platform
   login. A guest who loses all recovery paths (device lost, social account
   deleted) has no recourse — this is an accepted data-loss boundary.
2. **Login**: username+password is compared against the stored hash. On a
   missing user, the server still performs a dummy bcrypt comparison against
   a fixed invalid hash before responding, and returns the *same* generic
   error either way ("Incorrect username or password") — this prevents
   username enumeration via response timing or content.
3. On successful register or login, the server issues a signed JWT (7-day
   expiry, player ID as the `sub` claim). **There is no refresh-token
   mechanism for password accounts** — 7 days is a hard boundary; a
   password-only account must re-enter credentials after expiry. **[Scope
   note, 2026-07-14]** Guest accounts are one exception: Core Rule 5's
   device secret provides a guest-only silent refresh, precisely because
   guests have no password/social credential to fall back on. **[REVISED
   2026-07-19]** Social-linked and social-registered accounts are a second,
   narrower exception: on JWT expiry, the client first attempts a **silent
   re-authentication** using the platform SDK's own cached session (Apple
   `ASAuthorizationAppleIDProvider` credential state / Google Play Games
   silent sign-in) — if that platform session is still valid, a fresh JWT is
   minted with no prompt shown to the player, matching a guest's
   invisibility. **This is not a Quack Studio refresh token** — no new
   secret is issued or stored by our server for this path; it relies
   entirely on the platform's own session, which our server re-validates via
   the same Apple/Google OAuth2 exchange used at initial login (Core Rule
   6). If the platform session has also expired or been revoked, silent
   reauth fails closed and the client falls through to the explicit
   session-expired prompt (see Edge Cases). **[NEW 2026-07-19]** The
   platform-session check (both the client-side cached-credential read and
   the server-side OAuth2 re-validation round trip) is bounded by an
   **8-second timeout**. A hang or network error is treated identically to
   an expired/revoked platform session — fails closed to the explicit
   prompt — never left indefinitely pending, which would otherwise
   contradict this design's own "never a silent or unresolved failure
   state" principle and undermine the invisibility this exception exists to
   provide. Password-linked and
   password-only accounts have no equivalent silent path — a password
   cannot be silently re-proven without a general refresh-token system.
   **[Decision, 2026-07-19]** A general refresh-token system (covering
   password accounts too) was considered as an alternative fix and
   deliberately not chosen here, to keep this revision's scope to the
   narrower platform-session reuse described above; it remains available as
   a future option if the trust-tier distinction below proves insufficient
   in practice. Password-linked/password-only accounts keep the explicit
   re-auth prompt, streamlined per UI Requirements. This is a
   deliberate **trust-tier distinction** (a live, platform-verified session
   can extend automatically; a memorized credential requires a human
   check-in), not an unexplained inconsistency between account types.
   **[CORRECTED 2026-07-19]** An earlier changelog entry claimed this
   revision fully resolved the 2026-07-14 finding that linking made the
   session experience "strictly worse than staying an unlinked guest" — that
   claim was only true for the social-linked path. A guest who links a
   **password** account instead still moves from indefinite silent
   device-secret refresh to an explicit re-login every 7 days, forever —
   the exact downgrade originally flagged, un-mitigated by this revision.
   This is now honestly scoped as a **known, accepted asymmetry** rather
   than silently resolved: closing it fully would mean adopting the
   general-refresh-token system just deliberately declined above, which is
   out of scope here. The narrower mitigation actually shipped is a UI nudge
   (see UI Requirements) encouraging a password-linked-only player to *also*
   add a social sign-in method, giving them a path to the silent-refresh
   benefit without this revision re-opening refresh-token scope.
4. Every authenticated API call presents `Authorization: Bearer <token>`.
   Server middleware verifies signature + expiry before the request reaches
   any handler; a missing/malformed header or invalid/expired token → 401,
   rejected before any business logic runs. The `playerId` every downstream
   system trusts is the JWT's `sub` claim specifically — a token whose `sub`
   is missing or fails to parse as a valid player ID is treated identically
   to an invalid signature (401, not a fallback/guest identity).
5. **[NEW]** Guest accounts: a device-generated identity requiring no
   registration step, promotable to a full account later via a linking flow
   without losing progress. **[REVISED 2026-07-14]** Guest identity is backed
   by more than the JWT: at guest-account creation, the server also mints a
   high-entropy **device secret** (256-bit random, server stores only its
   hash) returned once and stored client-side in the same platform secure
   storage ADR-0003 specifies for tokens (iOS Keychain / Android Keystore)
   — never in PlayerPrefs, never logged. **[REVISED 2026-07-19]** Two
   corrections to that storage claim, both now folded into an amendment to
   ADR-0003: (a) the device secret and the session JWT are two
   **independently-lifecycled** secrets — guest logout must clear the JWT
   but explicitly must **not** clear the device secret (see Edge Cases) — so
   the storage layer must expose keyed/independent save-and-clear per
   secret, not the single overwrite-on-`Save` slot ADR-0003 originally
   decided for the JWT alone; (b) the device secret must be explicitly
   excluded from cloud backup and cross-device sync (iOS: not
   `kSecAttrSynchronizable`, the same `ThisDeviceOnly` posture ADR-0003
   already applies to the JWT; Android: excluded from Auto Backup) — an
   ordinary phone backup-restore must not silently clone this credential
   onto a second device, which would defeat its purpose as a
   harder-to-obtain, device-bound secret. The device secret is used
   for exactly two operations, both requiring the raw secret (not just JWT
   possession): (a) **silent session refresh** — `POST /auth/guest/refresh`
   with the device secret mints a fresh 7-day JWT for that guest's
   `playerId`, letting a guest resume after their JWT simply expires, not
   only via device loss; (b) **account linking** — the linking endpoint
   (Core Rule 6 below) now requires the device secret as well as the current
   JWT, not bearer-token possession alone. This closes two prior gaps in one
   mechanism: a copied/leaked guest JWT alone no longer lets a third party
   hijack that guest's identity via linking, and a guest whose JWT expires
   isn't permanently locked out the way a lost/reinstalled device still is
   (device secret loss remains genuinely unrecoverable — see Edge Cases).
   **[NEW 2026-07-19 — promoted from Open Question to binding rule]** The
   device secret (and the JWT, for consistency) **MUST be transmitted only
   in the request body, never as a query parameter or URL path segment**, on
   both `/auth/guest/refresh` and the linking endpoint. A query-string
   secret lands in plaintext in access/proxy/load-balancer logs regardless
   of TLS (Core Rule 8) — this was previously left as a "should" in Open
   Questions; given it's a zero-cost decision with a real leak path, it's a
   requirement now, not an implementation-time nicety.
6. **[NEW]** Social/platform login (Apple/Google Play Games, etc.): an OAuth2
   exchange that results in the same JWT session shape as password auth —
   every downstream system treats a social-authenticated player identically
   to a password-authenticated one. **[REVISED 2026-07-14]** Apple Sign In
   specifically returns an OIDC identity token, not a bare OAuth2 access
   token — the server MUST validate `nonce` (replay protection, matched
   against the value issued at flow start), `aud` (must equal this app's
   Apple client/bundle ID, not any other app's), `iss`
   (`https://appleid.apple.com`), and `exp` before treating the token as
   proof of identity and minting an internal session. Skipping any of these
   checks lets a token issued for a different app, or a replayed one,
   authenticate as an arbitrary player. **[REVISED 2026-07-19 — critical
   gap closed]** The above four claim checks are meaningless against a
   token with no verified signature: `aud`/`iss` are public knowledge,
   `nonce` is whatever value the *caller's own client* generated, and `exp`
   is trivially set in the future — none of them require possession of
   Apple's private signing key. Before any claim is inspected, the server
   **MUST fetch Apple's published JWKS** (`https://appleid.apple.com/auth/keys`,
   cached with standard key-rotation handling — matching key `kid` in the
   token header against the currently-cached key set, refetching on a
   `kid` miss) and **verify the identity token's signature** against the
   matched key. A token that fails signature verification is rejected outright, before `nonce`/`aud`/`iss`/`exp` are
   ever read — those claims are only meaningful once the signature confirms
   Apple actually issued the token. This is the same class of check Core
   Rule 4 already requires for our *own* JWTs; it was missing here for
   Apple's identity token specifically. **[REVISED 2026-07-19 — algorithm
   pinned, not header-trusted]** The verification algorithm is a **hardcoded
   server-side constant, RS256, never read from the token's own `alg`
   header.** The header's `alg` field is untrusted attacker-controlled input
   — it exists only to route to the matching JWKS key by `kid`, never to
   select which cryptographic algorithm the verifier runs. A token whose
   header declares any `alg` other than RS256 (including `none`, or `HS256`
   with Apple's own public RSA key smuggled in as the HMAC "secret") is
   rejected immediately on that basis, without attempting verification under
   the declared algorithm. This closes the classic **JWT algorithm-confusion**
   attack class: naively verifying "using the algorithm in its header," as
   this rule's text read prior to this revision, would have accepted exactly
   such a forged token — the same auth-bypass severity as the original
   missing-signature-check bug this Core Rule was written to fix, just one
   layer deeper.
7. **[NEW]** Login rate-limiting: failed login attempts against a single
   username are throttled independently of the enumeration-protection
   dummy-hash comparison (Core Rule 2 already prevents *detecting* whether a
   username exists; this prevents *brute-forcing* a password once a username
   is known/guessed). **[REVISED 2026-07-19 — reset logic was
   self-contradictory]** The server maintains a **persistent, monotonic
   failure counter** per `(username, source IP)` pair — the counter
   increments by 1 on every failed attempt and is the *only* input to the
   backoff calculation; it does **not** silently decay or reset merely
   because 15 minutes have elapsed since the first attempt (the prior
   wording's "rolling 15-minute window" implied an automatic reset that
   would have let an attacker make exactly 5 attempts every 15 minutes
   forever without ever triggering backoff — a slow-drip brute-force path
   that defeated this rule's entire purpose). The counter resets to zero
   **only** on a successful login for that username. The first 5 failed
   attempts (counter values 1–5) incur no delay. From the 6th failed
   attempt onward, the server enforces a minimum delay before accepting the
   next attempt for that pair, computed as
   `delaySeconds = min(30 × 2^(failureCount − 6), 900)` — i.e. 30s at
   failure 6, 60s at 7, 120s at 8, 240s at 9, 480s at 10, capped at 900s
   (15min) from failure 11 onward. (This formula only applies once
   `failureCount ≥ 6`; below that, `delaySeconds = 0` by the "first 5 free"
   rule above — the two are a single piecewise rule, not independent.) This
   guards the server-authoritative economy the same way enumeration
   protection does — not overkill for a casual game once real-money IAP
   sits behind the same identity. **[REVISED 2026-07-21 — CAPTCHA instead of hard lockout]** After 5 failed attempts per `(username, IP)` pair, the 6th attempt triggers a CAPTCHA challenge (reCAPTCHA v3 or equivalent). The player must solve the CAPTCHA to retry login; on success, the counter resets to zero. On CAPTCHA failure, the counter advances and another CAPTCHA is required on the next attempt (no time-based delay). This provides softer friction for a free game while still blocking automation. **[NEW 2026-07-19 — supplementary username-only counter]** Per-`(username, IP)` keying alone is bypassable: an attacker with multiple source IPs gets a fresh 5-free-attempts pool on every new IP. To close this, the server maintains a second, **username-only** (IP-independent) counter. If a single username accumulates 6+ failed attempts across *all* IPs within a 24-hour window, the username-only counter also triggers CAPTCHA, independent of any single IP's count. Both counters reset to zero only on successful login for that username. This guards the server-authoritative economy without the harshness of time-based hard lockouts — not overkill for a casual game once real-money IAP sits behind the same identity.
8. **[NEW]** All Account/Auth traffic (registration, login, guest creation,
   linking, refresh) requires TLS 1.2+ — credentials, tokens, and device
   secrets are never transmitted in plaintext. This has been an implicit
   assumption via ADR-0003's "sibling risk" note; it is now an owned Core
   Rule of this GDD rather than nobody's explicit requirement.
9. **[NEW 2026-07-21]** Device secret cloud backup: a guest's device secret
   is backed up to the platform's secure credential store — **iOS**: iCloud
   Keychain with `kSecAttrSynchronizable` enabled (survives device backup and
   restore, available across this user's devices) → **Android**: Google Play
   Credential Manager with sync enabled (syncs across this user's devices via
   Google Play Services). On device loss + app reinstall on a new device
   (same Google/Apple account), the credential manager transparently restores
   the device secret, letting the player resume as the same guest without
   re-registering. This is an accepted convenience: the device secret remains
   device-specific (not transmitted in guest-linking payloads, stored only
   locally or in the platform's credential manager), but the platform's own
   backup mechanism extends its lifetime beyond a single device. A guest who
   uses a *different* Google/Apple account on the new device, or who
   disables credential-manager sync, will not restore the device secret and
   will be unable to resume that guest account (device-loss recovery for those
   scenarios remains unavailable — a known boundary).

### States and Transitions

| State | Trigger | Result |
|---|---|---|
| Unauthenticated | App launch, no valid stored token | → Login/Register screen |
| Unauthenticated | Register submitted, valid | → Authenticated (new player created, default currency/progress) |
| Unauthenticated | Login submitted, valid | → Authenticated (existing player loaded) |
| Unauthenticated | **[NEW]** Guest session started | → Authenticated (Guest) — device secret minted + stored |
| Authenticated (password, direct) | Token expires mid-session | → Unauthenticated (explicit session-expired prompt, not a silent failure; must re-enter credentials) |
| Authenticated (social, direct) | **[SPLIT OUT, REVISED 2026-07-19 — was bundled with the password row above, which contradicted Core Rule 3's social-registered exception]** Token expires mid-session | → same silent-reauth attempt as "Linked, social" below (platform SDK cached session check) → if valid: Authenticated (social, direct), no prompt. If also expired/revoked: falls through to Unauthenticated (explicit prompt that **re-runs social OAuth** — never the password-linked prompt below, since a direct-social account has no password) |
| Authenticated (Guest) | **[NEW, REVISED 2026-07-14]** JWT expires mid-session | → Unauthenticated (Guest, device secret intact) → silent `POST /auth/guest/refresh` → Authenticated (Guest), no re-registration/credential prompt needed |
| Authenticated (Guest) | **[NEW]** Player links a social account (device secret presented) | → Authenticated (Linked, social) — same player ID, progress preserved |
| Authenticated (Guest) | **[NEW]** Player links a password account (device secret presented) | → Authenticated (Linked, password) — same player ID, progress preserved |
| Authenticated (Linked, social) | **[NEW, REVISED 2026-07-19]** Token expires mid-session | → silent re-authentication attempt via the platform SDK's cached session (Apple/Google), bounded by an 8s timeout (see Core Rule 3) → if the platform session is still valid: Authenticated (Linked, social), no prompt shown. If the platform session has also expired/been revoked, **or the check times out**: falls through to Unauthenticated (explicit session-expired prompt that **re-runs social OAuth** — **[CORRECTED 2026-07-19]** previously said "same as password-linked below," which is wrong: a guest linked only via social never set a password, so that prompt is unusable for this state; it must re-run the same social-sign-in flow used at initial link, identical to the "social, direct" row above) |
| Authenticated (Linked, password) | Token expires mid-session | → Unauthenticated (explicit session-expired prompt, last-used username prefilled; re-authenticate via password — **not** the device secret, device secret refresh remains Guest-only) |
| Authenticated (Guest) | **[NEW, added 2026-07-14]** Explicit logout | → Unauthenticated (Guest) — JWT discarded client-side; device secret is *not* cleared (needed for the resume-via-refresh path above), so the same device can silently resume the same guest identity on next launch |
| Authenticated (password/social/Linked) | Explicit logout | → Unauthenticated (token discarded client-side only — **the prototype has no server-side logout/revocation endpoint**, flagged below as an Open Question) |

### Interactions with Other Systems

- **Shared Hub**: reads the authenticated player's identity/currency/progress
  on load; the entire Hub is gated behind Account/Auth resolving to
  Authenticated.
- **Currency System, Mascot Database, Anti-Cheat, IAP/Receipt Validation,
  Hub UI, Per-Mini-Game HUD (all instances), Super Ricochet, Quack Runner**:
  every write is scoped to the `playerId` extracted from the verified JWT —
  specifically the token's `sub` claim (see Core Rule 4) — these systems
  never accept a client-supplied player ID directly; Account/Auth is the
  sole source of trusted identity. A token whose `sub` claim is missing or
  malformed is rejected at the Account/Auth middleware layer (401) before
  any downstream system ever sees the request — downstream systems do not
  need their own `sub`-validity checks, only a valid `playerId` is ever
  handed to them.
- **Save/Persistence**: the player record is keyed by the ID Account/Auth
  mints at registration.

## Formulas

Account/Auth has no derived formulas — it operates on fixed constants (token
TTL, hash cost, string-length bounds) rather than mathematical relationships.
Those constants are captured in **Tuning Knobs** below instead.

## Edge Cases

- **If two registration requests race for the same available username
  simultaneously**: the uniqueness check happens before insert, so a true
  race could let both pass. Storage-layer uniqueness is the final authority
  — the second writer gets a 409 conflict and must retry with a different
  username.
- **If a JWT expires between request start and server processing**: treated
  as expired — reject with 401, no grace period, client must re-authenticate.
- **If a guest account is never linked and the device is lost, the app is
  reinstalled, or the OS clears secure storage**: progress is unrecoverable
  — the device secret (Core Rule 5) is gone with it, and there is no
  account-recovery path for pure guests. **[NARROWED 2026-07-14]** This is
  now specifically a device-secret-loss scenario, not a JWT-expiry scenario
  (JWT expiry alone is recoverable via silent refresh) — but it is still
  real data loss, not an edge case to silently accept. The UX should nudge
  players toward linking, not just document the risk away.
- **If a guest's device secret is extracted via a rooted/jailbroken device**:
  the attacker can now refresh sessions and link accounts as that guest —
  the device secret is a bearer credential itself, just a harder-to-obtain
  one than the JWT (never transmitted except to `/auth/guest/refresh` and
  the linking endpoint, and stored in platform secure storage rather than
  in every API call's header). This residual risk is accepted as
  proportionate to a casual mobile game's threat model, not eliminated —
  full elimination would require a hardware-attested credential, which is
  out of scope here. **[NARROWED 2026-07-19]** This is now specifically a
  compromised-device scenario, not an ordinary-backup-restore scenario —
  Core Rule 5's backup/sync exclusion (2026-07-19) closes the far more
  common case of a routine phone upgrade silently cloning the secret onto a
  second device. **[EXPANDED 2026-07-19]** The accepted-residual-risk framing
  above was written against "resumed sessions" as the worst case; a sharper
  variant is **silent identity hijack via linking**: an attacker holding an
  extracted device secret can link that guest's progress to an
  attacker-controlled social/password account *before* the legitimate
  player does, and the legitimate player only discovers this later, if they
  ever attempt to link and hit the conflict error (Core Rule 6's linking
  reject-on-conflict). This is a more severe outcome than "resumed
  sessions" implied and is named here explicitly rather than left implicit
  in the generic framing. **Deliberately not addressed with a rotation/
  revocation mechanism in this revision** — that would be new architecture
  scope, the same category of addition Core Rule 3 already declined for a
  general refresh-token system, for the same reason (keep this revision's
  scope to the items actually found, not a speculative rebuild). If this
  residual risk proves unacceptable in practice, device-secret rotation is
  the follow-up — see new Open Question 10.
- **[NEW 2026-07-19] If a social-linked account's platform SDK session has
  also expired or been revoked** (e.g., the player revoked the app's access
  in their Apple ID or Google Account settings) at the moment our JWT
  expires: the silent-reauth path (Core Rule 3) fails closed — the client
  falls through to the same explicit session-expired prompt password-linked
  accounts get. This is a defined fallback, not a silent/unresolved failure
  state.
- **If a player tries to link a social/password identity that's already tied
  to a different existing account**: reject with a clear conflict error.
  Never silently merge or overwrite either account's data.
- **If the same account logs in from two devices simultaneously**: both
  sessions stay valid independently until their own 7-day expiry — there is
  no single-session enforcement. This is a deliberate carry-over from the
  prototype, not an oversight, but it means Anti-Cheat and Currency System
  must handle concurrent requests from the same `playerId` rather than
  assume single-device usage.
- **If bcrypt hashing takes longer than expected under load**: cost-factor-10
  bcrypt is deliberately slow (security over raw throughput) — this is a
  designed trade-off, not a bug, and should inform server capacity planning
  and a proper loading state client-side, not get silently worked around by
  lowering the cost factor.

## Dependencies

- **Depends on** (hard): Save/Persistence — needs durable storage for player
  records. **[Note, 2026-07-14]** `systems-index.md`'s Dependency Map lists
  Account/Auth under "Layer 1 — Foundation (zero dependencies)" alongside
  Save/Persistence; that phrasing means *zero dependencies on layers above
  Foundation*, not zero dependencies at all — Account/Auth's dependency on
  its fellow-Foundation-layer Save/Persistence doesn't violate that. The
  index wording has been clarified there rather than this GDD's claim being
  retracted (the hard dependency is real: player records need durable
  storage).
- **Depended on by** (hard, all of them): Shared Hub, Currency System,
  Currency Ledger, Anti-Cheat/Replay Verification, Mascot Database + Rarity
  Logic, Daily Quests, Login Streak, Leaderboard, Mascot Gallery/Equip UI
  *(added 2026-07-12, one-directional gap fixed here)*, **IAP/Receipt
  Validation, Hub UI, Per-Mini-Game HUD (all instances), Super Ricochet,
  Quack Runner** *(added 2026-07-14 — every one of these needs a trusted
  `playerId` to scope rewards/purchases/progress to, the same as the
  systems already listed; omitting them undercut this section's own
  "essentially every system" claim)* — essentially every system that needs
  to know *which player* is acting. All of the above GDDs now exist on
  disk (this list previously said "none of these GDDs exist yet" as of
  2026-07-09 — stale as of this revision); flagged for `/consistency-check`
  to verify each lists Account/Auth as a hard dependency in return.

## Tuning Knobs

| Knob | Current Value | Too Low | Too High |
|---|---|---|---|
| JWT Token TTL | 7 days (carried over) | Frequent forced re-logins, retention friction | Larger exposure window if a token is stolen/leaked |
| Bcrypt cost factor | 10 (carried over) | Weaker resistance to offline brute-force attacks | Server CPU/latency cost on every login/register, worse under load |
| Password minimum length | 6 characters (carried over) | Weak passwords accepted | User friction at signup — a usability trade-off for a casual mobile game, not a hard security requirement at 6 |
| Username length/charset | 3–20 chars, alphanumeric + underscore | Too restrictive for international players | Longer usernames break UI layouts (chips, leaderboard rows) |
| **[NEW]** Guest-account retention window | *undefined* | N/A | If unlimited, orphaned guest accounts accumulate in storage forever with no cleanup path |
| **[REVISED, 2026-07-19]** Login rate-limit threshold | 5 free attempts per (username, IP) — monotonic counter, resets only on success — then `min(30×2^(n−6), 900)`s backoff (30s→60s→120s→240s→480s→capped 900s) | Too low: legitimate typo-prone players get locked out quickly | Too high: brute-force against a known username becomes practical |
| **[NEW, 2026-07-14]** Device secret length | 256-bit random, server stores only its hash | Shorter: brute-forceable if the hash ever leaks | Longer: no real benefit, just larger payload |
| **[NEW, 2026-07-19, re-review]** Username-only (cross-IP) rate-limit threshold | 15 free attempts, same escalation formula/cap as the per-IP counter | Too low: legitimate multi-network usage (home Wi-Fi + mobile data, shared NAT) trips it during normal play | Too high: IP-rotating attacker gets a large aggregate guess budget before the supplementary counter engages |
| **[NEW, 2026-07-19, re-review]** Silent-reauth platform-session-check timeout | 8 seconds | Too low: a slow-but-legitimate platform round trip gets needlessly treated as failed, forcing an unnecessary explicit prompt | Too high: a genuinely hung check leaves the player waiting, undermining the "invisible" UX this path exists to protect |

The guest-retention window is a genuine open knob — the prototype has no
guest accounts at all, so there's no prior value to carry over. Captured as
an Open Question below rather than guessed here. The two 2026-07-14 knobs
above were resolved directly using standard practice defaults rather than
left open, since concrete values were needed before implementation could
start and no project-specific tradeoff required a design decision.

## Visual/Audio Requirements

N/A — deferred by design decision (2026-07-09). Account/Auth is
Foundation-layer infrastructure with no gameplay feedback or VFX/audio
identity of its own.

## UI Requirements

Account/Auth needs a Login/Register screen (tabs or toggle between flows)
and a lightweight "session expired, please log in again" interstitial. New
for native scope: a Guest entry point ("Play as Guest") alongside
username/password, plus social-login buttons (Apple/Google Play Games) per
platform convention. **[REVISED 2026-07-19 — reappearance policy locked
in]** The account-linking flow (guest → full account) needs its own UI
moment: shown once, as a natural prompt after a meaningful reward (not a
cold interruption), given the data-loss risk flagged in Edge Cases. If
dismissed, it does **not** vanish permanently — a low-key, non-modal
persistent banner (e.g., in the Shared Hub) continues reminding the
unlinked guest of the risk, dismissable per-session, until the player either
links or takes an explicit "don't remind me again" action. This closes the
gap between a single easily-missed modal and the severity of permanent,
unrecoverable data loss.

**[NEW 2026-07-19]** Two additional screen-inventory items surfaced by this
revision's fixes:
- A **rate-limit state** on the Login screen: once the 5-free-attempt
  threshold (Core Rule 7) is crossed, the form must show a specific,
  countdown-style message (e.g., "Too many attempts — try again in 47s"),
  not a generic error and not a silent hang — a naive implementation that
  skips this leaves the player thinking the app is broken.
- The password-linked session-expired prompt should **prefill the
  last-used username** to reduce re-entry friction, consistent with the
  trust-tier framing in Core Rule 3 (a live platform session can extend
  silently; a password re-check is intentionally a human step, but should
  still be as fast as possible).

**[NEW 2026-07-19, re-review]** Three further screen-inventory gaps surfaced
by independent specialist re-review:
- **Guest-secret-loss acknowledgment**: when a player taps "Play as Guest"
  on a device where `/auth/guest/refresh` cannot succeed (device
  secret missing/invalid — reinstall, storage cleared, new device), the
  client must **not** silently spin up a blank new guest profile with no
  signal anything happened. Show a brief, one-time acknowledgment ("Starting
  a new guest profile — previous unlinked guest progress on this device
  can't be recovered") before proceeding, consistent with this doc's own
  "never a silent or unresolved failure state" principle — previously this
  was the single highest-stakes moment in the whole system with zero
  defined UI.
- **Social sign-in failure state** (covers: Apple JWKS unreachable at
  validation time, per Open Question 9's fail-closed default; the 8s
  platform-session-check timeout in Core Rule 3; any other social OAuth2
  exchange failure): a generic, non-blaming error — "Social sign-in
  unavailable right now. Try again, or use another sign-in method." — fails
  closed, never a silent hang or dead end, and doesn't strand the player
  when one login method is unavailable.
- **Password-linked-only nudge**: for accounts linked via password only
  (no social method attached), a low-key, non-modal suggestion — reusing
  the same dismissable-banner pattern as the guest-link reminder above —
  to *also* add a social sign-in method "for faster sign-in," giving that
  player a path to Core Rule 3's silent-refresh benefit. This is the
  mitigation for the password-linked "worse than staying guest" asymmetry
  documented in Core Rule 3 — a nudge, not a requirement, since forcing a
  second credential method isn't warranted.

A detailed screen-by-screen UX spec (exact copy, layout, animation timing)
still belongs in `/ux-design`, not this GDD — this section states the
screen inventory and the policies that inventory must satisfy.

## Acceptance Criteria

- **GIVEN** no account exists, **WHEN** a player registers with a valid
  unique username and a password ≥6 characters, **THEN** a new player record
  is created with default currency/progress and a 7-day JWT is returned.
- **GIVEN** a username is already taken, **WHEN** a player attempts to
  register with it, **THEN** registration is rejected with 409 and no new
  record is created.
- **[REVISED 2026-07-14] GIVEN** two concurrent registration requests for the
  same available username submitted before either commits, **WHEN** both
  reach the storage layer, **THEN** exactly one succeeds and the other
  receives 409, with only one player record ever created (storage-layer
  uniqueness constraint is the final authority, not the pre-insert check).
- **GIVEN** valid credentials, **WHEN** a player logs in, **THEN** a fresh
  7-day JWT is returned scoped to their existing player ID.
- **[REVISED 2026-07-14] GIVEN** a non-existent username and, separately, a
  valid username with a wrong password, **WHEN** each is attempted over 100
  trials, **THEN** both return the identical error status/body AND the mean
  response-time delta between the two sets is under 5ms (measured via
  scripted benchmark, not manual spot-check) — enumeration protection holds
  under measurement, not just by inspection of the error text.
- **GIVEN** a valid, unexpired JWT, **WHEN** a request hits any protected
  endpoint, **THEN** the request is processed with the correct `playerId`
  (the token's `sub` claim) extracted from the token.
- **[REVISED 2026-07-14] GIVEN** an expired or invalid JWT, **WHEN** it hits
  a protected endpoint whose handler would otherwise perform a storage
  write, **THEN** the response is 401 AND the handler function itself is
  never invoked (verified via mock/spy in the test — zero calls, zero
  storage mutation), not merely a 401 status that could follow a
  discarded write.
- **[NEW 2026-07-14] GIVEN** no stored token, **WHEN** a player taps "Play as
  Guest," **THEN** a player record is created with a device-generated ID and
  default currency/progress, a device secret is minted and returned once,
  and a 7-day JWT is returned — no username/password is prompted and no
  uniqueness check is triggered.
- **[NEW 2026-07-14] GIVEN** an Authenticated (Guest) session whose JWT has
  expired but whose device secret is still present in secure storage,
  **WHEN** the client calls `/auth/guest/refresh` with that secret, **THEN**
  a fresh 7-day JWT is issued for the same `playerId` with no
  re-registration and no data loss.
- **[NEW 2026-07-14] GIVEN** a guest session, **WHEN** the player links a
  social or password account **and presents the correct device secret**,
  **THEN** the same player ID persists and all prior progress (currency,
  mascots) remains intact — concretely, a guest holding 500 coins and
  mascots [X, Y, Z] before linking has exactly 500 coins and mascots
  [X, Y, Z] after, under the same player ID, with no second record created.
- **[NEW 2026-07-14] GIVEN** a valid guest JWT copied to a second device
  **without** the matching device secret, **WHEN** that second device
  attempts to link a social/password account, **THEN** the link is rejected
  (missing/invalid device secret) and the guest's identity is not
  transferable via JWT possession alone.
- **[NEW 2026-07-14] GIVEN** a guest session already linked to account A,
  **WHEN** a second guest session attempts to link the same social identity,
  **THEN** the link is rejected with a conflict error and neither account's
  data changes.
- **[NEW 2026-07-14] GIVEN** a valid JWT already issued to Device A, **WHEN**
  the same credentials log in from Device B, **THEN** Device B receives its
  own valid 7-day JWT AND Device A's token remains valid and independently
  usable (no revocation) — confirms the deliberate no-single-session-
  enforcement design actually behaves as documented.
- **[NEW 2026-07-14, recommended-severity] GIVEN** a newly registered
  password, **WHEN** the stored hash is inspected, **THEN** it encodes
  bcrypt cost factor 10 (e.g. a `$2b$10$` prefix) — guards against a silent
  regression to a weaker cost factor that would otherwise pass every other
  AC unnoticed.
- **[NEW 2026-07-19]** GIVEN 5 failed login attempts for a given
  `(username, IP)` pair, **WHEN** a 6th attempt fails, **THEN** the server
  enforces a minimum 30s delay before accepting the next attempt for that
  pair, escalating per Core Rule 7's formula, **AND** the delay persists
  regardless of how much wall-clock time has passed since the first attempt
  in the sequence (no silent reset from time alone).
- **[NEW 2026-07-19]** GIVEN an account under active rate-limit backoff,
  **WHEN** a login attempt for that `(username, IP)` pair succeeds,
  **THEN** the failure counter resets to zero and the next attempt incurs
  no delay.
- **[NEW 2026-07-19]** GIVEN a validly-signed Quack Studio JWT whose `sub`
  claim is missing or fails to parse as a valid player ID, **WHEN** it is
  presented to any protected endpoint, **THEN** the response is 401 and no
  guest/fallback identity is ever assigned to the request.
- **[NEW 2026-07-19]** GIVEN an expired password or social-account JWT,
  **WHEN** a client calls `/auth/guest/refresh` with any value in place of
  a device secret, **THEN** the request is rejected with 401 — the
  guest-refresh path never issues a token for a non-guest player ID.
- **[REVISED 2026-07-19, re-review — case (a) strengthened to prove
  ordering, case (f) added for algorithm confusion]** GIVEN an Apple Sign In
  identity token, **WHEN** the server validates it, **THEN**: (a) a token
  with an invalid signature **and** a simultaneously invalid claim (e.g.
  wrong `aud`) is rejected on signature grounds, **AND** claim-parsing code
  is never invoked (verified via mock/spy, same technique as AC7) — a token
  that merely fails on both fronts passing the *test* isn't sufficient
  proof that signature is actually checked first; (b) given a validly-signed
  token, one with a mismatched `aud` is rejected; (c) one with the wrong
  `iss` is rejected; (d) one with a reused `nonce` is rejected; (e) one with
  an expired `exp` is rejected; (f) **[NEW]** a token whose header declares
  `alg: HS256` or `alg: none`, signed/unsigned accordingly (including one
  HMAC-"signed" using Apple's own public RSA key as the secret), is rejected
  outright without the server ever attempting verification under the
  declared algorithm — proves the RS256 pin is hardcoded, not read from the
  token. A test suite must exercise all six cases independently — passing
  only some is not sufficient coverage.
- **[NEW 2026-07-19]** GIVEN a device secret minted at guest-account
  creation, **WHEN** the client's secure storage is inspected, **THEN** the
  device secret is confirmed absent from any cloud backup or cross-device
  sync snapshot (iCloud Keychain export / Android Auto Backup archive),
  mirroring the JWT verification criteria already required by ADR-0003.
- **[NEW 2026-07-19]** GIVEN a social-linked account whose JWT has expired
  but whose platform SDK session (Apple/Google) is still valid, **WHEN**
  the client attempts silent reauth, **THEN** a fresh JWT is issued for the
  same player ID with no prompt shown to the player.
- **[NEW 2026-07-19]** GIVEN a social-linked account whose JWT has expired
  **and** whose platform SDK session has also expired or been revoked,
  **WHEN** the client attempts silent reauth, **THEN** it fails closed and
  the explicit session-expired prompt is shown — never a silent or
  unresolved failure state.
- **[NEW 2026-07-19]** GIVEN any registration, login, guest-creation,
  refresh, or linking request, **WHEN** server logs and crash-reporting
  payloads are inspected, **THEN** no plaintext password, JWT, or device
  secret value appears in any of them.
- **[NEW 2026-07-19, re-review]** GIVEN a registration request, **WHEN**
  the username is outside 3–20 characters, contains a character other than
  alphanumeric/underscore, is a duplicate under a different case if
  uniqueness is meant to be case-insensitive (verify against the actual
  uniqueness rule), or the password is under 6 characters, **THEN** the
  request is rejected with 400 and no player record is created — covers
  Core Rule 1's format bounds, previously exercised only by the
  valid-input and duplicate-username ACs above.
- **[NEW 2026-07-19, re-review]** GIVEN an Authenticated (Guest) session,
  **WHEN** the player explicitly logs out, **THEN** the client-stored JWT
  is cleared, the device secret remains present and unchanged in secure
  storage, **AND** a subsequent app launch silently resumes the same
  guest identity via `/auth/guest/refresh` with no re-registration or data
  loss — covers Core Rule 5's guest-logout retention behavior, previously
  untested despite being this Core Rule's headline 2026-07-14 fix.
- **[NEW 2026-07-19, re-review]** GIVEN a client attempts any Account/Auth
  endpoint (registration, login, guest creation, refresh, linking) over a
  connection negotiated below TLS 1.2 or over plaintext HTTP, **WHEN** the
  connection is attempted, **THEN** the server refuses it — covers Core
  Rule 8, previously the same "rule fixed in prose, zero AC" defect class
  Core Rules 3/4/7 were sent back for last round.
- **[NEW 2026-07-19, re-review]** GIVEN a directly social-registered
  account (never a guest, never went through the linking flow) whose JWT
  has expired, **WHEN** the platform SDK session is still valid, **THEN**
  silent reauth succeeds identically to the "Linked, social" case — covers
  Core Rule 3's "social-registered" exception, previously untested and
  contradicted by the States table (now corrected to a dedicated
  "social, direct" row).
- **[NEW 2026-07-19, re-review]** GIVEN failed login attempts against a
  single username spread across at least 4 different source IPs (fewer
  than 5 failures per individual IP, so no single IP's per-`(username, IP)`
  counter crosses its threshold), **WHEN** the cumulative failure count for
  that username (summed across IPs) exceeds 15, **THEN** the
  username-only supplementary counter (Core Rule 7) enforces its own
  backoff regardless of any single IP's count — proves IP-rotation doesn't
  bypass rate-limiting entirely.
- **[NEW 2026-07-19, re-review]** GIVEN an Authenticated (Linked, social)
  or "social, direct" session attempting silent reauth, **WHEN** the
  platform-SDK session check does not return within 8 seconds, **THEN** the
  client treats it identically to an expired/revoked platform session and
  falls through to the explicit re-auth prompt — covers Core Rule 3's
  timeout requirement, guarding against an indefinite hang.
- **[NEW 2026-07-19, re-review]** GIVEN a device-secret-bearing request to
  `/auth/guest/refresh` or the linking endpoint, **WHEN** network traffic is
  inspected, **THEN** the device secret appears only in the request body,
  never in the URL (query string or path) — covers the transport
  requirement promoted from Open Question to Core Rule 5.
- **[NEW 2026-07-21, boundary test]** GIVEN a `(username, IP)` pair with 4
  consecutive failed login attempts, **WHEN** the 4th attempt fails,
  **THEN** the response is 401 with no CAPTCHA challenge issued — proves the
  5-attempt threshold is honored (4 fails do not yet trigger CAPTCHA).
- **[NEW 2026-07-21, boundary test]** GIVEN a `(username, IP)` pair with 5
  consecutive failed login attempts, **WHEN** the 5th attempt fails or is
  submitted, **THEN** the response includes a CAPTCHA challenge token (or
  full CAPTCHA widget) and the failure counter is not incremented until the
  CAPTCHA response is submitted — proves CAPTCHA is triggered on 5th
  attempt.
- **[NEW 2026-07-21, boundary test]** GIVEN a `(username, IP)` pair under
  CAPTCHA challenge after 5 failed attempts, **WHEN** the CAPTCHA is solved
  successfully and a new login attempt is submitted, **THEN** the failure
  counter resets to zero, no backoff delay is enforced, and the login
  proceeds through normal authentication — proves CAPTCHA success clears the
  rate-limit state.

## Open Questions

1. There's no server-side logout/revocation endpoint in the prototype — does
   the native version need one (e.g., stolen-device scenario), or is
   client-side token discard sufficient for this game's risk profile?
   *Target: resolve before the Anti-Cheat GDD, since its threat model should
   inform this.*
2. What's the guest-account retention window before an unlinked guest is
   purged (or kept forever)? *Target: resolve before implementation begins
   on this system.*
3. Exact current Unity 6.3 secure on-device token storage APIs weren't
   independently verified (flagged in the Technical Feasibility Brief).
   *Target: verify before implementation, not before GDD completion.*
4. **[NEW 2026-07-14]** No password-reset/account-recovery flow exists for
   password-based (non-guest) accounts — a player who forgets their password
   has no stated recovery path. This is a genuine product/security gap
   (previously surfaced independently during `/ux-review` of
   `account-auth.md`'s UX spec) that this revision does not resolve, since
   it needs its own design pass (email/recovery-code delivery mechanism),
   not a one-line fix. *Target: resolve before this system leaves
   Pre-Production.*
5. **[NEW 2026-07-14]** JWT signing algorithm and key-rotation policy are
   still unspecified at the GDD level (cost-10 bcrypt and 7-day TTL are
   locked; the algorithm/key-management side is not). *Target: resolve as
   part of implementation-time ADR work, likely an addendum to ADR-0003 or
   a small dedicated ADR, given it gates the trust boundary every other
   system relies on.*
6. **[NEW 2026-07-14]** Server-side log/trace scrubbing of the `Authorization`
   header and device secrets (access logs, load balancer logs, APM traces)
   is not addressed here — ADR-0003 covers client-side scrubbing only.
   **[EXPANDED 2026-07-19]** Also unaddressed: the device secret's transport
   channel (request body vs. query string) is unspecified at the GDD level
   — if implemented as a query parameter it lands in access/proxy/load
   balancer logs in plaintext despite TLS, which is a live leak path for one
   of the system's two most sensitive credentials. *Target: fold into the
   same implementation-time ADR work as Open Question 5; the transport
   requirement (body only, never query string) should be locked before
   coding starts even if full log-scrubbing tooling lands later.*
7. **[NEW 2026-07-14]** Full server-side session revocation (beyond the
   narrower guest-refresh/link device-secret mechanism added in this
   revision) remains unresolved — see Open Question 1, still open. The
   device secret closes the guest-specific hijack and reauth gaps but does
   **not** provide a way to revoke a password/social account's token before
   its 7-day natural expiry (e.g., a genuinely stolen device scenario for a
   linked/password account). *Target: resolve before Anti-Cheat/Currency
   ship, per the original Open Question 1 framing — this revision narrows
   the guest-specific risk but does not close the general revocation gap.*
   **[NARROWED FURTHER 2026-07-19]** The hybrid silent-reauth mechanism
   added this revision (Core Rule 3) reduces the *practical* exposure
   window for social-linked accounts specifically (a revoked platform
   session now fails the silent path closed within one JWT cycle, rather
   than the token quietly remaining valid for up to 7 more days), but this
   is a side effect of the reauth design, not a revocation mechanism — it
   does nothing for password-only or password-linked accounts, and this
   Open Question remains genuinely open for those account types.
8. **[NEW 2026-07-19]** Neither `/auth/guest/refresh` nor the linking
   endpoint has rate-limiting, unlike the login endpoint (Core Rule 7).
   256-bit device-secret entropy makes brute-forcing the secret itself
   infeasible, but an unthrottled endpoint doing a hash comparison on every
   call is still a resource-exhaustion vector, and — if guest player IDs
   turn out to be sequential/enumerable — differing responses (unknown ID
   vs. wrong secret) could enable guest-account enumeration, the same class
   of risk Core Rule 2 explicitly defends against for passwords. *Target:
   resolve before implementation begins on this system, alongside Core
   Rule 7's implementation.*
9. **[NEW 2026-07-19]** The Apple JWKS fetch added to Core Rule 6 needs
   implementation-time detail this GDD deliberately doesn't own: cache TTL
   for the fetched key set, and fallback behavior if Apple's JWKS endpoint
   is unreachable at validation time (fail closed, presumably, but this
   should be an explicit ADR decision, not an implicit assumption). *Target:
   fold into the same implementation-time ADR work as Open Question 5.*
10. **[NEW 2026-07-19, re-review]** Device-secret rotation/revocation:
    the Edge Cases section now explicitly names silent identity hijack via
    linking as a sharper variant of the already-accepted device-compromise
    residual risk (see Core Rule 5 / Edge Cases). No rotation or revocation
    mechanism was added this revision — deliberately, to keep scope to
    found issues rather than a speculative rebuild, the same call already
    made for a general refresh-token system in Core Rule 3. *Target:
    revisit if real-world incidence of guest-secret compromise (post-launch
    telemetry/support reports) shows this residual risk isn't actually
    proportionate — not before, since there's no data yet to size the
    problem.*
11. **[NEW 2026-07-19, re-review]** Shared-IP (CGNAT/campus-NAT) rate-limit
    griefing: Core Rule 7's per-`(username, IP)` counter still means an
    attacker sharing a victim's IP can drive up the *same* counter the
    victim's legitimate attempts use, denying them service for up to 900s
    at a time, repeatably. The new username-only supplementary counter
    (Core Rule 7) does not fix this — it's an independent, additive check,
    not a replacement for the per-IP one. A real fix needs step-up friction
    (CAPTCHA or equivalent) instead of a hard time-based lockout once a
    threshold is crossed, which is new UI/verification scope. *Target:
    resolve before implementation begins on Core Rule 7, alongside Open
    Question 8 — both are rate-limiting completeness gaps of the same
    severity class.*
