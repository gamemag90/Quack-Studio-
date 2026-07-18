# Account/Auth

> **Status**: Designed
> **Author**: Abdulrahman Alenazan + Claude
> **Last Updated**: 2026-07-09
> **Implements Pillar**: Server-authoritative economy
> **Creative Director Review (CD-GDD-ALIGN)**: APPROVED 2026-07-09 (self-reviewed — no `creative-director` subagent registered in this environment)
> **`/design-review` (2026-07-14)**: NEEDS REVISION — 6 blocking items found (guest proof-of-possession/hijack risk, guest reauth-after-expiry gap, Dependency Map contradiction with `systems-index.md`, missing Apple OIDC validation, no login rate-limiting, missing guest-creation/multi-device ACs). All folded into this revision below; re-review pending.

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
   logged.
2. **Login**: username+password is compared against the stored hash. On a
   missing user, the server still performs a dummy bcrypt comparison against
   a fixed invalid hash before responding, and returns the *same* generic
   error either way ("Incorrect username or password") — this prevents
   username enumeration via response timing or content.
3. On successful register or login, the server issues a signed JWT (7-day
   expiry, player ID as the `sub` claim). **There is no refresh-token
   mechanism for password/social accounts** — 7 days is a hard boundary;
   the client must re-authenticate (re-enter credentials or re-run OAuth)
   after expiry. **[Scope note, 2026-07-14]** Guest accounts are the one
   exception: Core Rule 5's device secret provides a guest-only silent
   refresh, precisely because guests have no password/social credential to
   fall back on. This is a narrow, guest-specific mechanism, not a general
   refresh-token system — password/social/Linked accounts still have none.
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
   storage ADR-0003 already specifies for tokens (iOS Keychain / Android
   Keystore) — never in PlayerPrefs, never logged. The device secret is used
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
   authenticate as an arbitrary player.
7. **[NEW]** Login rate-limiting: failed login attempts against a single
   username are throttled independently of the enumeration-protection
   dummy-hash comparison (Core Rule 2 already prevents *detecting* whether a
   username exists; this prevents *brute-forcing* a password once a username
   is known/guessed). No delay on the first 5 failed attempts per
   `(username, source IP)` pair in a rolling 15-minute window; the 6th+
   attempt triggers exponential backoff (30s → 60s → 120s, capped at 15min)
   enforced server-side, reset on a successful login. This guards the
   server-authoritative economy the same way enumeration protection does —
   not overkill for a casual game once real-money IAP sits behind the same
   identity.
8. **[NEW]** All Account/Auth traffic (registration, login, guest creation,
   linking, refresh) requires TLS 1.2+ — credentials, tokens, and device
   secrets are never transmitted in plaintext. This has been an implicit
   assumption via ADR-0003's "sibling risk" note; it is now an owned Core
   Rule of this GDD rather than nobody's explicit requirement.

### States and Transitions

| State | Trigger | Result |
|---|---|---|
| Unauthenticated | App launch, no valid stored token | → Login/Register screen |
| Unauthenticated | Register submitted, valid | → Authenticated (new player created, default currency/progress) |
| Unauthenticated | Login submitted, valid | → Authenticated (existing player loaded) |
| Unauthenticated | **[NEW]** Guest session started | → Authenticated (Guest) — device secret minted + stored |
| Authenticated (password/social) | Token expires mid-session | → Unauthenticated (explicit session-expired prompt, not a silent failure; must re-enter credentials or re-run social OAuth) |
| Authenticated (Guest) | **[NEW, REVISED 2026-07-14]** JWT expires mid-session | → Unauthenticated (Guest, device secret intact) → silent `POST /auth/guest/refresh` → Authenticated (Guest), no re-registration/credential prompt needed |
| Authenticated (Guest) | **[NEW]** Player links social/password account (device secret presented) | → Authenticated (Linked) — same player ID, progress preserved |
| Authenticated (Linked) | Token expires mid-session | → Unauthenticated (explicit session-expired prompt; re-authenticate via the now-linked password/social credential, **not** the device secret — device secret refresh is Guest-only) |
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
- **If a guest's device secret is copied off-device (e.g., a backup,
  rooted/jailbroken extraction) and presented from a different device**:
  the attacker can now refresh sessions and link accounts as that guest —
  the device secret is a bearer credential itself, just a harder-to-obtain
  one than the JWT (never transmitted except to `/auth/guest/refresh` and
  the linking endpoint, and stored in platform secure storage rather than
  in every API call's header). This residual risk is accepted as
  proportionate to a casual mobile game's threat model, not eliminated —
  full elimination would require a hardware-attested credential, which is
  out of scope here.
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
| **[NEW, 2026-07-14]** Login rate-limit threshold | 5 free attempts / 15min window per (username, IP), then exponential backoff 30s→60s→120s capped at 15min | Too low: legitimate typo-prone players get locked out quickly | Too high: brute-force against a known username becomes practical |
| **[NEW, 2026-07-14]** Device secret length | 256-bit random, server stores only its hash | Shorter: brute-forceable if the hash ever leaks | Longer: no real benefit, just larger payload |

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
platform convention. The account-linking flow (guest → full account) needs
its own UI moment — ideally surfaced as a natural prompt (e.g., after a
meaningful reward) rather than a cold interruption, given the data-loss risk
flagged in Edge Cases. A detailed screen-by-screen UX spec belongs in
`/ux-design`, not this GDD — this section just states the screen inventory
needed.

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
- **[NEW 2026-07-14] GIVEN** an Apple Sign In identity token, **WHEN** the
  server validates it, **THEN** a token with a mismatched `aud`, wrong
  `iss`, reused `nonce`, or expired `exp` is rejected before any session is
  minted.
- **[NEW 2026-07-14, recommended-severity] GIVEN** a newly registered
  password, **WHEN** the stored hash is inspected, **THEN** it encodes
  bcrypt cost factor 10 (e.g. a `$2b$10$` prefix) — guards against a silent
  regression to a weaker cost factor that would otherwise pass every other
  AC unnoticed.

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
   *Target: fold into the same implementation-time ADR work as Open
   Question 5.*
7. **[NEW 2026-07-14]** Full server-side session revocation (beyond the
   narrower guest-refresh/link device-secret mechanism added in this
   revision) remains unresolved — see Open Question 1, still open. The
   device secret closes the guest-specific hijack and reauth gaps but does
   **not** provide a way to revoke a password/social account's token before
   its 7-day natural expiry (e.g., a genuinely stolen device scenario for a
   linked/password account). *Target: resolve before Anti-Cheat/Currency
   ship, per the original Open Question 1 framing — this revision narrows
   the guest-specific risk but does not close the general revocation gap.*
