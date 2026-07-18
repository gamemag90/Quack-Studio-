# Analytics/Event Tracking

> **Status**: Designed
> **Author**: Abdulrahman Alenazan + Claude
> **Last Updated**: 2026-07-09
> **Implements Pillar**: N/A (Foundation) — enables measurement of every other pillar
> **Creative Director Review (CD-GDD-ALIGN)**: APPROVED 2026-07-09 (self-reviewed — no `creative-director` subagent registered in this environment)
> **`/design-review` (2026-07-14)**: NEEDS REVISION — 5 blocking items found (event-catalog contradiction with ADR-0006's server-outbox split, missing device-ID→playerId reconciliation, no PII/FIFO/terminal-state acceptance criteria). All folded into this revision below; re-review pending.

## Overview

Analytics/Event Tracking is the instrumentation layer that lets every other
system report what actually happened — app opens, session starts,
level/run completions, purchases, currency earn/spend, quest claims — as
structured events with a consistent schema (name, timestamp, player ID,
parameters). It has no gameplay behavior of its own; its entire purpose is
to make every other system's real-world performance measurable after the
fact (retention, conversion, balance tuning) rather than guessed at. This is
explicitly new scope: the `quack-blaster` prototype has zero instrumentation
anywhere — a gap the master prompt calls out directly (its required KPIs —
D1/D7/D30 retention, ARPDAU, conversion rate — are unmeasurable without this
system existing first).

## Player Fantasy

No direct player fantasy — pure infrastructure, invisible to players
entirely. Its value shows up as the *studio's* ability to make good
decisions (which mini-game is underperforming, which quest reward is too
stingy, where players drop off) rather than anything a player experiences
directly.

## Detailed Design

### Core Rules

1. Every event has a fixed shape: `{eventId, name, timestamp, playerId,
   sessionId, seq, schemaVersion, appVersion, params}` — no ad-hoc event
   shapes invented per-system. **[Corrected 2026-07-14]** `eventId`
   (client-generated unique ID, required for server-side dedup — see Edge
   Cases) and `seq`/`schemaVersion`/`appVersion` were referenced elsewhere in
   this GDD and required by ADR-0006, but missing from this formal schema
   until now; the two must not drift apart again.
2. Events are buffered client-side and flushed in **batches**, not one HTTP
   call per event — avoids network chatter and battery drain on mobile.
   Flush triggers: buffer reaches batch size, OR the flush interval elapses,
   **OR [NEW 2026-07-14] the app is backgrounded** (`OnApplicationPause`) —
   this third trigger is a synchronous persist-to-durable-log, not just a
   send attempt, since a background-then-kill with no sync-persist is
   exactly the scenario this system's own durability requirement (see
   Interactions, Save/Persistence) exists to survive.
3. **Standard event catalog** (master prompt's required set + game-specific
   extensions), split by **who emits it** — **[CORRECTED 2026-07-14]** this
   split was missing entirely in the prior version of this GDD, which listed
   every event below as client-buffered; ADR-0006 already decided the
   client/server split for exactly the reason given there (preventing
   reward double-counting and spoofing), and this GDD's own catalog must
   match it, not silently diverge:
   - **Client-buffered** (this system's normal buffer/flush path):
     `app_open`, `session_start`, `session_end`, `level_start`,
     `level_complete{duration, score, bossDefeated}` *(no `rewards` field —
     see below)*, `run_start`, `run_complete{miniGame, score, duration}`,
     `purchase_initiated`, `iap_start` *(pre-transaction intent signals
     only — no reward or money has moved yet, so client self-report is
     safe)*.
   - **Server-emitted only, via the ADR-0004/ADR-0006 transactional outbox
     — NEVER through this system's client buffer**: `purchase_completed`,
     `iap_complete`, `currency_earned{source, amount, currency}`,
     `currency_spent{source, amount, currency}`,
     `quest_claimed{questType, reward}`, `mascot_acquired{mascotId,
     rarity}`. These all represent a reward or money actually granted — the
     server is already the sole authority for that mutation (Save/
     Persistence, Currency System, ADR-0004's `mutateWallet`), so it is also
     the sole source of the corresponding analytics event, at-least-once via
     the same outbox, not a second, independently-timed client report of
     the same fact. `level_complete`'s `rewards` field is removed for the
     same reason — the reward itself is reported via `currency_earned`/
     `mascot_acquired` from the server, not duplicated inside a client
     event. **[Added 2026-07-17]** `anti_cheat_mismatch{runId, miniGame,
     tolerance_units, delta}` and `degraded_verification{runId, miniGame,
     reason}` (Anti-Cheat/Replay Verification's Tier-2 flags, per ADR-0007)
     belong in this same server-emitted category — both are detected purely
     server-side during replay verification, with no legitimate
     client-buffered path at all. `anti-cheat-replay-verification.md`
     already stated this; this catalog had not been updated to list either
     event until now. `streak_claimed{playerId, streakCount,
     coinsGranted, gemsGranted, isGemBonusDay, claimedAtUtc}` (Login
     Streak's Core Rule 12) belongs here too — a server-validated claim
     crediting real currency, emitted via the same ADR-0004 outbox
     pattern as `quest_claimed`. `login-streak.md` already flagged this
     event as proposed-but-not-yet-formally-catalogued; this is that
     catalog entry. (A separate, still-open question — Open Question 2
     below and login-streak.md's own Open Question 4 — is whether this
     also needs a formal ADR-0004 schema amendment; adding it here closes
     only the catalog-listing gap, not that one.) `mascot_equipped{
     mascotId, rarityTier, source}` (Mascot Gallery/Equip UI's Core Rule
     7, `source` ∈ {`gallery_tap`, `auto_equip_first_unlock`}) belongs
     here too, for the same reason and with the same open ADR-0004
     question — that GDD already flagged it as proposed-but-uncatalogued
     in its own Open Question 2.
4. Events must **never block gameplay** — a failed or slow analytics flush
   must not stall or crash any other system. Fire-and-forget with local
   retry, not a synchronous dependency.
5. **No PII beyond the player ID** in event params — usernames, emails, and
   any other identifying info are excluded, per the master prompt's
   GDPR/CCPA compliance requirement.
6. **[NEW 2026-07-14] Identity reconciliation**: pre-auth events use a
   device-scoped ID (Core Rule/Edge Case below) in place of `playerId`. The
   moment Account/Auth resolves to Authenticated for the first time on that
   device (registration, login, or guest creation), the client fires a
   one-time **identify/alias call** (`POST /analytics/identify {deviceId,
   playerId}`) that tells the analytics backend to attribute all
   already-ingested device-scoped events for that `deviceId` to the newly
   resolved `playerId` retroactively — the standard alias/identify pattern
   used by mainstream analytics backends (Amplitude, Firebase), not a novel
   mechanism. Without this, the pre-auth `app_open` that starts a player's
   funnel would never link to their post-auth retention/conversion data,
   silently corrupting the exact KPIs (D1/D7/D30 retention) this system
   exists to produce.

### States and Transitions

| State | Trigger | Result |
|---|---|---|
| Queued | Any system emits an event | → Added to local buffer |
| Queued → Flushing | Buffer reaches batch size OR flush interval elapses OR app backgrounds | → Batch send attempted (backgrounding case: synchronous persist first, send if possible) |
| Flushing → Sent | Server returns 2xx for the batch | → Buffer cleared for those events |
| Flushing → Partially Quarantined **[NEW 2026-07-14]** | Server returns 400/422 for specific event IDs within the batch | → Those specific malformed events are quarantined (removed from retry buffer, logged locally) — the rest of the batch is NOT blocked and is retried normally. One bad event never stalls the other 19. |
| Flushing → Failed | Network error, 5xx, or 429 | → Retry with backoff (same shape as Save/Persistence's `reconnect_backoff`, own instance) |
| Flushing → Reauth | Server returns 401 | → Refresh the session token (per Account/Auth) and retry the same batch — not a generic failure/backoff case |
| Failed (repeated) | Backoff ceiling reached, still failing | → Events held in buffer, capped (see Tuning Knobs) — never silently dropped without a cap. **[Clarified 2026-07-14]** New events continue to be accepted into the capped buffer (oldest dropped first per FIFO, per Edge Cases); retry resumes on the next app-foreground event or the next successful network-reachability signal, whichever comes first — this is not a permanently-abandoned state. |

### Interactions with Other Systems

- **Every Core/Feature-layer system**: emits events through this system's
  single API — no system talks to the analytics backend directly.
- **Save/Persistence**: the client-side event buffer needs its own local
  durability (survive an app kill before flush) — a small, separate concern
  from Save/Persistence's player-state cache, but the same underlying
  local-storage mechanism.
- **Account/Auth**: every event includes `playerId` from the verified
  session — anonymous/pre-auth events (e.g. `app_open` before login) use a
  device-scoped ID instead, reconciled to the real `playerId` via the
  identify/alias call in Core Rule 6 the moment auth first resolves.
- **[NEW 2026-07-14] Currency System, Mascot Database + Rarity Logic, Daily
  Quests, IAP/Receipt Validation**: these systems are the ones that emit
  the server-side-only events in Core Rule 3 (`currency_earned/spent`,
  `quest_claimed`, `mascot_acquired`, `purchase_completed`/`iap_complete`)
  via the ADR-0004/ADR-0006 transactional outbox at the moment their own
  authoritative mutation commits — never via this system's client buffer.

## Formulas

No distinct mathematical formulas beyond the flush-trigger rule already
captured in States and Transitions (buffer size OR time threshold, whichever
fires first). Retry timing reuses Save/Persistence's `reconnect_backoff`
formula rather than duplicating it.

## Edge Cases

- **If the buffer exceeds its cap during an extended offline period**:
  oldest events are dropped first (FIFO) — losing old `app_open` events is
  less costly than losing recent gameplay events, but this is a real
  data-loss trade-off, not silently fine.
- **If a retry resends an event whose first attempt actually succeeded**
  (ack lost to a network flake): each event carries a client-generated
  unique ID for server-side dedup — without this, every downstream KPI
  (retention, ARPDAU) would be corrupted by double-counting.
- **If a system emits an event before Account/Auth resolves** (e.g. the very
  first `app_open`): use the device-scoped ID path rather than blocking or
  dropping the event. It is reconciled to the real `playerId` once auth
  resolves (Core Rule 6) — it is never a permanently orphaned identity.
- **[NEW 2026-07-14] `sessionId` generation and session boundaries**: a
  fresh `sessionId` (client-generated UUID) is minted on cold launch or on
  foreground-return after exceeding an inactivity threshold (30 minutes,
  matching typical mobile-analytics convention — see Tuning Knobs);
  returning from a brief background (e.g., a notification check) reuses the
  existing `sessionId`. **`session_end` is a best-effort signal only, not
  authoritative** — an app kill never fires it. The server independently
  closes a session via inactivity timeout (no events received under that
  `sessionId` for the threshold period) for session-length KPIs, exactly as
  `session_end` cannot be trusted to always fire.
- **If analytics ingestion is down for an extended period**: the client
  buffer caps and drops oldest per above — an accepted trade-off since
  analytics is explicitly non-blocking/best-effort, never a source of truth
  for gameplay-critical state.

## Dependencies

- **Depends on**: none (Foundation).
- **Depended on by**: every Core/Feature-layer system.

**Related Architecture [added 2026-07-14]**: `adr-0006-analytics-buffer-flush.md`
governs the client buffer/flush mechanics in detail (durability boundary,
poison-event quarantine, single-flight flush) and is the source of the
client/server event-ownership split now reflected in Core Rule 3 above —
read alongside this GDD, not as a substitute for it.

## Tuning Knobs

| Knob | Suggested Value | Too Low | Too High |
|---|---|---|---|
| Batch size | 20 events | Excessive network calls | Larger loss on a single flush failure |
| Flush interval | 30 seconds | Battery/data cost | Stale analytics data |
| Buffer cap | 500 events (FIFO-drop-oldest) | Data loss during short outages | Unbounded memory growth during long outages |
| Retry backoff | Reuses `reconnect_backoff` (2s–60s) | — | — |
| **[NEW, 2026-07-14]** Session inactivity threshold | 30 minutes | Too low: a brief backgrounding (notification check) wrongly starts a new session, inflating session-count KPIs | Too high: genuinely separate play sessions get merged into one, deflating session-count KPIs |

## Visual/Audio Requirements

N/A — Foundation-layer infrastructure, no visible surface of its own.

## UI Requirements

None — this system has no player-facing screen or element.

## Acceptance Criteria

- **[REVISED 2026-07-14] GIVEN** any client-buffered event type (per Core
  Rule 3's split) triggers, **WHEN** the event is emitted, **THEN** it's
  added to the local buffer with a unique `eventId`, `timestamp`,
  `sessionId`, and `playerId`/device-scoped ID as appropriate.
- **[REVISED 2026-07-14] GIVEN** the buffer reaches exactly 20 events,
  **WHEN** that threshold fires, **THEN** a batch send is attempted
  immediately, without waiting for the 30-second interval. **GIVEN** fewer
  than 20 events are queued, **WHEN** 30 seconds elapse since the last
  flush, **THEN** a batch send is attempted with whatever is queued.
- **[REVISED 2026-07-14] GIVEN** a batch send fails with a network error or
  5xx, **WHEN** retried, **THEN** delays follow `reconnect_backoff` exactly
  (2s, 4s, 8s, ... capped at 60s) via a mock clock, and no event is dropped
  before the 500-event buffer cap is reached.
- **GIVEN** the same event is sent twice due to a lost ack, **WHEN** the
  server receives both, **THEN** it's counted once via the event's
  `eventId`.
- **GIVEN** an event fires before authentication resolves, **WHEN** it's
  queued, **THEN** it uses a device-scoped ID, not a blocked/dropped state.
- **[NEW 2026-07-14] GIVEN** a device-scoped ID has emitted events pre-auth,
  **WHEN** Account/Auth first resolves to Authenticated on that device,
  **THEN** an identify/alias call links that `deviceId` to the resolved
  `playerId`, and all previously-ingested device-scoped events for that
  device are attributable to the same player in downstream KPI queries.
- **[NEW 2026-07-14] GIVEN** the buffer holds exactly 500 events (at cap),
  **WHEN** a 501st event is emitted, **THEN** buffer position #1 (the
  oldest) is discarded and the new event is retained — buffer size stays at
  500, never growing unbounded and never dropping the newest.
- **[NEW 2026-07-14] GIVEN** any event's `params` payload, **WHEN** it is
  serialized for flush, **THEN** it contains only catalog-defined fields
  and fails an automated param audit if any key matches a PII deny-list
  (username, email, phone number, raw device identifier, IP address).
- **[NEW 2026-07-14] GIVEN** a batch containing one malformed event among
  20, **WHEN** the server returns 400/422 for that specific `eventId`,
  **THEN** only that event is quarantined (removed from the retry buffer)
  and the remaining 19 are retried/accepted normally — no single bad event
  blocks the batch indefinitely.
- **[NEW 2026-07-14] GIVEN** the backoff ceiling is reached with sends still
  failing, **WHEN** the app is later foregrounded or network reachability
  is restored, **THEN** flushing automatically resumes — the buffer does
  not remain permanently stalled.
- **[NEW 2026-07-14] GIVEN** `currency_earned`, `quest_claimed`, or
  `mascot_acquired` occurs, **WHEN** the event is emitted, **THEN** it
  originates from the server-side outbox (per ADR-0004/ADR-0006), never
  from this system's client buffer — verified by confirming no client code
  path calls `Analytics.Emit` for these event names.

## Open Questions

1. Exact backend choice (Amplitude/Firebase-parity per master prompt vs. a
   custom events table) isn't decided — affects the batch-send API shape.
   *Target: resolve during `/create-architecture`.*
2. Should the event catalog in Core Rules be considered locked, or does each
   new system's GDD get to propose additions? *Target: resolve before the
   first Feature-layer GDD that needs a new event type.*
