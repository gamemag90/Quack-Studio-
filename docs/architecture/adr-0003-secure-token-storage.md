# ADR-0003: Secure On-Device Auth Token Storage

## Status
Proposed

## Date
2026-07-10

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Unity 6.3 LTS (build `6000.3.0f1`) |
| **Domain** | Platform / Core (secure storage, native interop) |
| **Knowledge Risk** | HIGH — both the Unity 6.3 Platform Toolkit and the *current* Android secure-storage recommendation are post-LLM-cutoff and churning; treat specific API names here as **verify-before-implement**, not settled |
| **References Consulted** | `docs/engine-reference/unity/VERSION.md` (Platform Toolkit note), `breaking-changes.md`, `deprecated-apis.md` |
| **Post-Cutoff APIs Used** | None committed. Unity 6.3 Platform Toolkit is considered and **not** adopted for secret storage (see Alternatives). |
| **Verification Required** | (1) Confirm the current Android Jetpack Security / `EncryptedSharedPreferences` status and its recommended successor at implementation time — Google deprecated `androidx.security:security-crypto`; the concrete Android impl must use whatever the maintained Keystore-backed path is then. (2) Confirm the iOS Keychain accessibility class choice (`kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly` recommended). |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | None |
| **Enables** | Any authenticated client feature (all systems consume the stored session token) |
| **Blocks** | Account/Auth client implementation — the client cannot persist a session until this is decided |
| **Ordering Note** | Independent of ADR-0001/0002 (RNG/physics); can proceed in parallel |

## Context

### Problem Statement
`account-auth.md` issues a signed **JWT with a 7-day expiry, no refresh token, and no server-side revocation endpoint**; the client presents it as `Authorization: Bearer <token>` on every call and must persist it across app launches (otherwise the player re-authenticates constantly, defeating the 7-day TTL's purpose). The GDD's Open Question #3 explicitly defers "exact Unity 6.3 secure on-device token storage APIs" — this ADR resolves *where and how* that token is stored. Two hard facts constrain the answer:
- **Unity `PlayerPrefs` is not secure** — it persists as plaintext (Windows registry / `.plist` / XML) readable on a rooted/jailbroken device or via local backup extraction. It must never hold a bearer credential.
- **Unity ships no built-in cross-platform secure store.** The genuine secure locations are the **iOS Keychain** (hardware / Secure-Enclave backed) and the **Android Keystore** (hardware-backed keys), both reachable from Unity only through a native plugin bridge.

### The revocation coupling (scoped out, but recorded)
Because there is **no server-side revocation and no refresh token**, a token extracted from storage is fully usable until its 7-day expiry with no way to invalidate it. Secure storage is therefore the *sole* line of defense for a stolen-token scenario. This ADR deliberately does **not** redesign auth (shorter TTL / refresh / revocation) — that is a separate Auth-design decision — but it records the coupling as an explicit risk and routes it onward (see Open Questions).

### Requirements
- The session token must be stored in hardware-backed, app-private secure storage on both iOS and Android — never `PlayerPrefs`, never plaintext on disk.
- A single C# abstraction so game code is platform-agnostic and unit-testable in the editor without real Keychain/Keystore.
- The token must not leak into cloud backups or migrate silently to another device.

## Decision

Introduce a thin C# interface **`ISecureTokenStore`** with a native-backed implementation per platform, selected at runtime; game/auth code depends only on the interface.

```csharp
public interface ISecureTokenStore {
    void Save(string token);          // overwrites any existing token
    bool TryLoad(out string token);   // false if absent/unreadable
    void Clear();                     // logout / session-expired
}
```

- **iOS** — Keychain Services via a native Objective-C/Swift plugin. Item stored with accessibility `kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly`: readable after first unlock (so a backgrounded app can still call the API), **`ThisDeviceOnly`** so it is excluded from iCloud/iTunes backups and never migrates to a new device. Keyed to the app's bundle identifier. (Keychain protects the item via the OS/passcode, not by storing the token *inside* the Secure Enclave — don't overclaim SE storage.) **If the app makes no genuine while-locked network calls**, prefer the strictly-tighter `kSecAttrAccessibleWhenUnlockedThisDeviceOnly` — confirm the background requirement is real before choosing `AfterFirstUnlock`.
- **Android** — a native Java/Kotlin plugin storing the token as an app-private blob encrypted with an Android Keystore key. **Recommended concrete default**: a Keystore-held (non-exportable) AES-GCM key encrypting the token, or Google Tink's Keystore-backed AEAD — **not** `androidx.security:security-crypto` / `EncryptedSharedPreferences`, which Google has **deprecated** (older docs/training data still wrongly recommend it). Final library is verify-at-implementation (see Engine Compatibility), but the *mechanism* (Keystore-wrapped key + app-private encrypted blob) is decided here, not deferred. The Keystore key is **hardware-backed where a TEE/StrongBox is present**; on low-end API-25 devices it may be software-backed — the implementation should check `KeyInfo.isInsideSecureHardware()` / key attestation and accept the software fallback rather than assuming hardware backing.
- **Editor / test** — an in-memory `ISecureTokenStore` mock so auth flows run in the editor and CI without native modules. This mock **must be physically un-shippable**, not merely "not selected": it lives in an **Editor-only assembly** (`.asmdef` scoped to the Editor platform, or guarded by `#if UNITY_EDITOR`) so it cannot compile into a player build at all. This is a hardening over "runtime selection + code review," which could silently fall through to an in-memory store on a misconfigured platform and store the bearer token unprotected while appearing to work.
- **The runtime selector fails closed**: on an unknown/unsupported platform it **throws**, never silently defaulting to any in-memory or plaintext store. A missing secure store is a hard error, not a degraded mode.
- **`PlayerPrefs` is banned for any secret/credential** (registry forbidden pattern) — enforced by review and a CI grep for `PlayerPrefs` in auth code.

### Architecture Diagram
```
Auth / game code (platform-agnostic)
        │  depends only on
        ▼
   ISecureTokenStore  ──────────────┬───────────────┬─────────────────┐
        ▲                           │               │                 │
        │ runtime-selected impl     ▼               ▼                 ▼
                              iOS Keychain     Android Keystore   Editor mock
                              (Swift plugin)   (Kotlin plugin)    (in-memory,
                              ThisDeviceOnly    non-exportable     never shipped)
                              AfterFirstUnlock  key + app-private
                                                encrypted blob
```

## Alternatives Considered

### Alternative A: Unity 6.3 Platform Toolkit (new built-in accounts/saves API)
- **Description**: Use Unity 6.3's new cross-platform Platform Toolkit (accounts/achievements/saves) as the token store.
- **Pros**: First-party, single API surface, less native glue to maintain.
- **Cons**: Advertised for accounts/achievements/**saves**, not verified to provide hardware-backed *secret* storage with Keychain/Keystore semantics; brand-new (this LTS), HIGH knowledge-risk, no community track record for security-critical use.
- **Rejection Reason**: Security-critical storage should not ride on an unverified, brand-new abstraction. Revisit only if, at implementation time, Unity documents Platform Toolkit as explicitly backing credentials with Keychain/Keystore.

### Alternative B: Self-encrypt the token, store the ciphertext in `PlayerPrefs`
- **Description**: AES-encrypt the token in C#, put the ciphertext in `PlayerPrefs`.
- **Cons**: The encryption key must itself be stored somewhere — and the only secure place for it is Keychain/Keystore, which is circular. A key embedded in the binary is trivially extractable; this is security theater.
- **Rejection Reason**: Doesn't solve the problem — it relocates it to the key, which still needs the exact secure storage this alternative was trying to avoid.

### Alternative C: Don't persist the token; re-authenticate every launch
- **Description**: Hold the token only in memory; require login on each cold start.
- **Cons**: Directly contradicts the GDD's 7-day-TTL intent and casual-mobile retention goals; forces guests to lose sessions constantly.
- **Rejection Reason**: Unacceptable UX for the product; the TTL exists precisely to avoid this.

## Consequences

### Positive
- Tokens live in hardware-backed, app-private storage — raises extraction difficulty from "trivial" (PlayerPrefs) to "requires a compromised/rooted device and real effort."
- `ThisDeviceOnly` + app-private means no accidental leakage via cloud backup or device migration.
- The `ISecureTokenStore` seam keeps auth code testable in the editor/CI and isolates all platform risk to two small native modules.

### Negative
- Two native plugins (Swift, Kotlin) to write and maintain — real per-platform surface, including OS-version behavior differences.
- On device migration or reinstall the token is gone by design → the player logs in again (acceptable; guest/social linking preserves progress per `account-auth.md`).

### Risks
- **Risk**: On a rooted/jailbroken or actively compromised device, even Keystore/Keychain contents can sometimes be reached. Secure storage raises the bar; it is not absolute.
  **Mitigation**: Accepted as residual — combined with the (separately-owned) need for revocation/short TTL to limit a stolen token's usefulness. Do not overstate this as "unstealable."
- **Risk**: The Android impl is written against the deprecated `EncryptedSharedPreferences` because training data / older tutorials still recommend it.
  **Mitigation**: Engine Compatibility flags this explicitly as verify-at-implementation; the maintained Keystore-backed path must be confirmed before coding.
- **Risk (cross-ADR)**: No server revocation + 7-day bearer token means a stolen token is valid until expiry.
  **Mitigation**: Out of scope here by decision; routed to a future Auth ADR and the Account/Auth GDD owner (Open Questions).
- **Risk**: The token leaks out of secure storage via a side channel — application logs, or a third-party crash/analytics SDK capturing request headers.
  **Mitigation**: Scrub the `Authorization` header / token value from all logging and crash-reporting SDK payloads; add a lint/review check that the token string is never passed to `Debug.Log` or a crash SDK.
- **Risk (sibling)**: Because there is no revocation, a token stolen via a man-in-the-middle intercept is exactly as damaging as one stolen from storage — securing storage while leaving transport weak defeats the purpose.
  **Mitigation**: Enforce TLS on all API calls; consider certificate pinning. Transport security is out of scope for *this* ADR but is a sibling requirement flagged for the networking/Auth layer, not something secure storage alone can cover.

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|------------|-------------|---------------------------|
| account-auth.md | Client persists the 7-day JWT and presents it as a Bearer token across launches | `ISecureTokenStore` defines exactly where/how, in hardware-backed secure storage |
| account-auth.md | Open Question #3: "exact Unity 6.3 secure on-device token storage APIs weren't determined" | Resolved: Keychain (iOS) / Keystore (Android) via native bridge, PlayerPrefs banned |
| account-auth.md | Logout discards the token client-side | `ISecureTokenStore.Clear()` |
| save-persistence.md | Durable client storage | Tokens are the security-sensitive subset carved out into secure storage, distinct from ordinary cached save data |

## Performance Implications
- **CPU / Memory / Load / Network**: Negligible. Keychain/Keystore access is a rare, small operation (login, launch, logout), not a hot path.

## Migration Plan
Greenfield on the native client; no existing device token store to migrate. (The `quack-blaster` web prototype used browser storage — not applicable to the native build.)

## Validation Criteria
- Token written on login is retrievable after an app restart on real iOS and Android hardware.
- Token is confirmed **absent** from `PlayerPrefs`, plaintext files, and device backups (inspect an iTunes/iCloud backup and an `adb backup`).
- `Clear()` on logout makes the token unretrievable.
- Editor/CI auth tests pass against the mock with no native module present.

## Related Decisions
- `account-auth.md` — the auth design this serves.
- Future: **Auth revocation / token-TTL ADR** — should own the "no server-side revocation + 7-day bearer" risk this ADR only records.

## Open Questions
- **Revocation & TTL** (`account-auth.md` Open Question #1): given secure storage is the *only* defense against a stolen token, should auth add a revocation endpoint and/or shorten the access-token TTL with a refresh token? Deliberately out of scope here — routed to a future Auth ADR + the GDD owner.
- Whether Unity 6.3 Platform Toolkit gains documented Keychain/Keystore-backed secret storage by implementation time (would reopen Alternative A).
