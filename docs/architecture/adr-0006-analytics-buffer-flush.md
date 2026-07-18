# ADR-0006: Analytics Event Buffer & Flush (Client-Side)

## Status
Proposed

## Date
2026-07-10

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Unity 6.3 LTS client (buffer + flush pipeline); Node/TS backend (ingest endpoint) |
| **Domain** | Core / Telemetry (client) + Networking |
| **Knowledge Risk** | MEDIUM — the concrete Unity on-device local-storage API for buffer durability is a shared open question with `save-persistence.md` (Open Q1); `UnityWebRequest` main-thread constraint is stable/known |
| **References Consulted** | `design/gdd/analytics-event-tracking.md`, `save-persistence.md`, ADR-0004 (outbox), ADR-0005 (backend store) |
| **Post-Cutoff APIs Used** | None committed; on-device storage API deferred (see Verification) |
| **Verification Required** | Buffer survives a hard app-kill and re-flushes on next launch; flush never blocks a frame; server dedups a resent batch by event id |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-0005 (backend store for the ingest endpoint's landing table). Soft: shares the on-device local-storage mechanism whose concrete API `save-persistence.md` Open Q1 defers |
| **Enables** | Measurement of the master prompt's KPIs (D1/D7/D30, ARPDAU, conversion) — none measurable without this |
| **Blocks** | Nothing hard |
| **Ordering Note** | Coordinates with ADR-0004: money/reward events are server-emitted (see Decision §5), so this ADR must not also emit them client-side |

## Context

### Problem Statement
`analytics-event-tracking.md` requires client-side events buffered and flushed in **batches** (not per-event), non-blocking to gameplay, durable across an app-kill, deduplicated server-side via a client-generated event id, capped at 500 (FIFO-drop-oldest), retried with the `reconnect_backoff` curve, and carrying no PII beyond player/device id. The GDD fixes all the *tuning*; this ADR fixes the *architecture*: where the buffer lives and how it survives a kill, how flushing avoids blocking the main thread, the transport, and two decisions the GDD left open — the backend/transport choice (Open Q1) and the origin of money events (a latent conflict with ADR-0004).

### Two decisions taken here
- **Transport = first-party** (confirmed): the client is a dumb durable buffer that batch-POSTs to *our* backend `/events` endpoint; the server owns storage and may fan out to a third-party analytics tool later. No third-party SDK is embedded in the client — better GDPR/no-PII control, simpler client, reuses ADR-0005's store.
- **Money/reward events are server-authoritative** (confirmed): `currency_earned`/`currency_spent` (and reward grants) are emitted by ADR-0004's server-side transactional outbox, **not** the client buffer, preventing double-counting and client spoofing. The client buffer emits only client-observable events.

## Decision

### 1. Durable, capped, FIFO buffer
Events append to an on-device **durable** buffer (persisted, not memory-only) so they survive an app-kill before flush (GDD interaction note). Cap = 500; at cap, drop **oldest** first (FIFO), **keyed by `eventId`, never by list position/index** (an index-based remove after another cycle shifted the list would evict the wrong event → silent loss), and **excluding any events currently in an in-flight batch** (don't evict what a send is mid-way through). Each event carries its full shape at enqueue time:
```
{ eventId: clientUUID, name, timestamp, playerId|deviceId, sessionId,
  seq: perSessionSequenceNumber, schemaVersion, appVersion, params }
```
`seq` (per-session monotonic) lets the server restore intra-session ordering after batches/retries reorder them; `schemaVersion`/`appVersion` let ingest evolve `params` safely. The concrete Unity storage API (file-based append log vs. a package vs. Cloud Save) is the **same shared open question** as `save-persistence.md` Open Q1 — this ADR specifies the *contract* (durable, append-cheap, FIFO-evictable by id, survives kill), not the exact API, and defers it to the client-storage decision so the two don't pick different mechanisms.

### 2. Non-blocking emit → background flush (never stalls a frame)
- `Analytics.Emit(name, params)` is cheap and **must never block gameplay** (GDD Rule 4): it stamps the event and enqueues it to a thread-safe in-memory queue; the durable-store append happens off the main thread. Emit never awaits network or disk.
- A **flush pipeline** drains the queue to the durable store and sends batches. Trigger: buffer reaches **batch size (20)** OR **flush interval (30s)** elapses, whichever first.
- **Durability boundary = persistence, not send** (fixes the app-kill window): the async off-thread append is only a latency optimization. On `OnApplicationPause(true)` the pipeline **synchronously persists any in-memory-but-unpersisted events to the durable log before yielding** — so the boundary that "survives a kill" is the durable write, not a completed network round-trip. Residual loss window: only events emitted in the tiny gap between enqueue and the synchronous pause-persist during an *instant* kill — acknowledged and bounded, not hand-waved as zero.
- **On-background does NOT guarantee a network send.** iOS/Android suspend the player loop quickly after backgrounding, and `UnityWebRequest` needs the main thread + a running loop, so a POST started at pause usually won't complete. Treat backgrounding as a **persist-and-reflush-next-launch** guarantee, not a send guarantee. A best-effort send may be *attempted*, but correctness relies on the next-launch reflush of the durable log.
- Network send uses `UnityWebRequest` on the main thread — the pipeline marshals the actual POST to the main thread / a Unity coroutine, while serialization and durable-store I/O stay off it.

### 3. At-least-once delivery, single-flight, remove-after-ack by id
- **Single-flight flush**: at most one flush cycle runs at a time (a lock/guard). The three triggers (20-event, 30s, on-background) can all fire close together; without single-flight, overlapping cycles racing the durable store can mis-remove or mis-evict shifted entries → silent loss. One writer to the durable store at a time.
- A batch POST to `/events` carries N events. On **2xx ack**, those events are removed from the durable store **keyed by `eventId`** (never by list position/index — the list may have shifted since the batch was assembled). On failure they are **retained** and retried. Events are removed **only after** a successful ack — so a crash mid-send loses nothing (they re-send next launch).
- **Response taxonomy** (fixes "all 4xx = poison"):
  - **2xx** → remove the acked events by id.
  - **Network error / timeout / 5xx / 408 / 429** → transient: retain, retry with `reconnect_backoff` (2s→60s, own instance, per GDD). 429 honors `Retry-After` if present.
  - **401** → auth expired: refresh the session, then retry the same batch (not poison).
  - **400 / 422 (malformed)** → poison: the server returns the offending `eventId`(s); the client **quarantines only those** and re-sends the rest of the batch. A single bad event never discards the other 19 good ones.
- Because remove-after-ack means a lost-ack can resend an already-ingested batch, **every event carries a client-generated `eventId`** and the server **dedups by it** (idempotent ingest) — the client-side analogue of ADR-0004's idempotency. Mandatory: without it, retention/ARPDAU double-count (GDD Edge Case).
- **Client timestamps are not trusted for cohorting**: the device clock can be skewed/manipulated. The server records its own `received_at` alongside the client `timestamp`; KPI cohorting (D1/D7/D30) uses server time, with the client timestamp kept only for intra-session ordering (via `seq`).

### 4. Identity and sessions
- Events include `playerId` from the verified session (`account-auth.md`); events fired before auth resolves (e.g. the first `app_open`) use a **device-scoped id** rather than blocking or dropping (GDD Edge Case). The server may later stitch device→player; the client never stalls on auth to emit.
- **`sessionId`** is a client-generated UUID minted at app foreground/launch. Returning from background after an inactivity threshold (recommend the same 30s order, tuning-owned) mints a **new** `sessionId`; a brief background blip does not. `seq` resets per session.
- **`session_end` is best-effort, not authoritative.** A hard kill means it may never fire, so the server **closes a session by inactivity/heartbeat** (no events for a timeout ⇒ session considered ended) rather than depending on a client-sent `session_end`. The client still emits `session_end` on a clean pause as a hint, but session-length KPIs are computed server-side from event timestamps + `received_at`.

### 5. Event ownership split (reconciles GDD catalog with ADR-0004)
- **Client buffer emits**: `app_open`, `session_start`, `session_end`, `level_start`, `level_complete`, `run_start`, `run_complete`, `purchase_initiated`, `iap_start`, screen/UI events — things only the client observes.
- **Server outbox emits (backend, via the shared `analytics_outbox` table)**: `currency_earned`, `currency_spent`, `purchase_completed`/`iap_complete`, `quest_claimed`, `mascot_acquired`, and — **[ADDED 2026-07-18]** — `streak_claimed` and `mascot_equipped` — anything tied to an authoritative economy/state mutation. Emitting these server-side (from the same transaction that made the change) makes them exactly-once and unspoofable.
- **[ADDED 2026-07-18] Two outbox-writing transactions, one outbox table.** The `analytics_outbox` table (owned by ADR-0005's store) is written from **either** server-authoritative chokepoint, whichever made the change:
  - `mutateWallet` (ADR-0004) writes the outbox for **currency** mutations — `currency_earned`/`currency_spent`, and `streak_claimed` (Login Streak's claim credits currency via a `creditFlat` leg — `login-streak.md` Rule 11/12 — so its event rides that same transaction, exactly-once keyed by `op_id`, precisely like `quest_claimed`).
  - `updatePlayer` (ADR-0005) writes the outbox for **non-money player-state** mutations — `mascot_equipped` is the first such event: equipping changes `player_state.data.equippedMascotId` (`mascot-gallery-equip-ui.md` Rule 4) with **no currency change**, so it does **not** ride `mutateWallet`. Both GDDs' Open Questions loosely said "add to ADR-0004's outbox"; the mechanically-correct home for `mascot_equipped` is ADR-0005's `updatePlayer` transaction, writing the same shared outbox table. See ADR-0004 §4 and ADR-0005 §2 annotations.
- **Rule**: an event that corresponds to a server-authoritative state change is emitted **once, server-side**, from the same transaction (`mutateWallet` or `updatePlayer`) that made the change; the client never emits it too. This is the load-bearing reconciliation — the GDD catalog listed some of these as client events; the server outbox supersedes that for money/reward/authoritative-state.

### 6. PII exclusion
Event `params` carry **no PII beyond the id** — no username/email/etc. (GDD Rule 5, master prompt GDPR/CCPA). Enforced by a review checklist and, ideally, an allowlist of param keys per event name.

### Architecture Diagram
```
CLIENT                                             SERVER
Any system → Analytics.Emit(name, params)          POST /events (batch of N)
     │  (cheap, non-blocking, main thread)               │  dedup by eventId (idempotent)
     ▼                                                    ▼
 in-memory queue ──(off-thread)──► durable buffer   ingest → ADR-0005 store (events table)
                                    (cap 500, FIFO)        │
     flush trigger: 20 events | 30s | on-background        └─► (optional, later) fan-out to
     │                                                          Amplitude/BigQuery/etc. SERVER-side
     ▼  marshal POST to main thread (UnityWebRequest)
   batch send ──2xx──► remove those events from durable buffer
             └─fail──► retain + reconnect_backoff (2s–60s) retry

SERVER-AUTHORITATIVE events (currency_earned/spent, purchase_completed,
quest_claimed, mascot_acquired, streak_claimed, mascot_equipped) come from the
server outbox — via mutateWallet (ADR-0004) for currency ones and streak_claimed,
via updatePlayer (ADR-0005) for mascot_equipped — NOT this client path.
```

## Alternatives Considered

### Alternative A: Embed a third-party analytics SDK (Amplitude/Firebase) in the client
- **Pros**: Batching/retry/durability/dashboards out of the box; fastest to KPIs.
- **Cons**: Embeds a third-party in the client → PII/GDPR data-sharing review; less control over exactly what leaves the device; makes the GDD's custom buffer/flush design largely redundant; couples the client to a vendor.
- **Rejection Reason**: First-party keeps data ownership and no-PII control, and the server can still forward to such a tool later without the client depending on it.

### Alternative B: Memory-only buffer (no durable store)
- **Pros**: Simplest; no on-device storage dependency.
- **Cons**: Loses all unflushed events on an app-kill — directly violates the GDD's durability requirement and skews session-tail metrics.
- **Rejection Reason**: Durability across kill is a stated requirement.

### Alternative C: Per-event HTTP (no batching)
- **Pros**: Simplest delivery logic.
- **Cons**: Network chatter + battery drain the GDD explicitly rules out (Rule 2).
- **Rejection Reason**: Contradicts a core GDD rule.

### Alternative D: Client emits money/reward events too (GDD catalog as literally written)
- **Pros**: Matches the GDD catalog text without reinterpretation.
- **Cons**: Double-counts with ADR-0004's server outbox; a modified client could spoof economy KPIs.
- **Rejection Reason**: Server-authoritative emission is correct for an economy; the catalog is superseded for money/reward events.

## Consequences

### Positive
- Non-blocking, durable, deduped, capped — meets every GDD rule with an explicit mechanism.
- No third-party SDK in the client; data leaves the device only to our endpoint.
- Money/reward KPIs are exactly-once and unspoofable (server-emitted).
- Server can adopt/replace downstream analytics tooling without a client change.

### Negative
- We build (or later forward to) KPI querying/dashboards ourselves — not free like a vendor SDK.
- Two emission sites (client buffer + server outbox) to keep straight; the ownership split must be documented so no event is emitted from both.
- The client durable-store API is a shared open question with client-cache — must be resolved once, together.

### Risks
- **Risk**: A developer emits a server-authoritative event from the client too → double count.
  **Mitigation**: The §5 ownership split is a registry stance; the client `Emit` allowlist excludes server-owned event names.
- **Risk**: Durable-store writes on a hot path cause jank.
  **Mitigation**: Off-main-thread append; emit only enqueues in-memory; batch the disk writes.
- **Risk**: A poison event (one malformed event) blocks the whole queue.
  **Mitigation**: On 400/422 the server returns the offending `eventId`(s); the client quarantines only those and resends the rest — one bad event never discards the batch. 401 → refresh+retry; 408/429/5xx/network → backoff-retry (see §3 taxonomy).
- **Risk**: Overlapping flush triggers corrupt the durable store.
  **Mitigation**: Single-flight flush + id-keyed removal/eviction (§3, §1).

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|------------|-------------|---------------------------|
| analytics-event-tracking.md | Rule 2: batch, not per-event | Batch of 20 / 30s / on-background |
| analytics-event-tracking.md | Rule 4: never block gameplay | Cheap enqueue; off-thread store; main-thread-marshalled send |
| analytics-event-tracking.md | Rule 5: no PII beyond id | Param allowlist; server-side ingest only |
| analytics-event-tracking.md | Durability across app-kill | Durable buffer, remove-after-ack |
| analytics-event-tracking.md | Edge: lost-ack resend | Client `eventId` + server dedup (idempotent ingest) |
| analytics-event-tracking.md | Edge: buffer cap | 500 cap, FIFO-drop-oldest |
| analytics-event-tracking.md | Edge: pre-auth event | Device-scoped id path |
| analytics-event-tracking.md | Open Q1: backend choice | Resolved: first-party `/events` endpoint, optional server-side fan-out |

## Performance Implications
- **CPU/Memory**: In-memory queue small; durable buffer bounded at 500 events.
- **Battery/Network**: Batched sends on interval/threshold minimize radio wakeups (the GDD's whole point).
- **Frame time**: Emit is O(1) enqueue; no disk/network on the calling frame.

## Migration Plan
Greenfield — the prototype had zero instrumentation. No migration.

## Validation Criteria
- Kill test: background then force-kill with unflushed events; the synchronous pause-persist writes them to the durable log and on relaunch they flush and appear server-side exactly once.
- Non-blocking test: emit thousands of events in a frame; no frame-time spike / no main-thread disk or network.
- Dedup test: force a resend of an acked batch; server stores each `eventId` once.
- Cap test: exceed 500 while offline; oldest dropped by id, newest retained, in-flight batch never evicted.
- Poison test: a batch with one 422 event → the 19 valid events ingest, only the bad `eventId` is quarantined; a 429/401 does NOT drop events.
- Concurrency test: fire all three flush triggers near-simultaneously → single-flight holds, no event lost or duplicated-past-dedup.
- Ownership test: confirm no `currency_earned`/`currency_spent`/`quest_claimed`/`mascot_acquired` is emitted by the client (only by the server outbox).

## Related Decisions
- ADR-0004 — server outbox that emits money/reward events (the other emission site).
- ADR-0005 — backend store the `/events` endpoint lands into.
- `save-persistence.md` Open Q1 / future client-cache ADR — shares the on-device durable-storage API choice.

## Open Questions
- Exact Unity on-device durable-storage API (shared with client-cache ADR) — resolve once, together.
- Event-catalog governance (GDD Open Q2): is the catalog locked, or may each new system's GDD propose additions? Recommend: additions allowed via GDD, but each new event must declare its emission site (client vs server outbox) per §5. **[Exercised 2026-07-18]** `streak_claimed` (Login Streak) and `mascot_equipped` (Mascot Gallery) are the first GDD-proposed additions to be folded in under this rule — each was assigned an explicit emission site per §5 (both server-outbox, but on different transactions: `mutateWallet` vs `updatePlayer`). The governance recommendation is now demonstrated, not just proposed; a fully locked catalog is still not adopted.
