# ADR-0010: Client Cache & Offline Action Queue

## Status
Proposed

## Date
2026-07-10

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Unity 6.3 LTS (build `6000.3.0f1`) — client-side (iOS + Android) |
| **Domain** | Core / Persistence (client) |
| **Knowledge Risk** | MEDIUM — SQLite is OS-native on both platforms, but the **Unity plugin choice + IL2CPP/ARM64 AOT compatibility** must be verified (reflection-based ORMs can hit AOT stripping) |
| **References Consulted** | `save-persistence.md` (Rules 5–8, client half), ADR-0003 (tokens live in Keychain/Keystore, NOT here), ADR-0004/0007 (idempotency), ADR-0006 (analytics buffer shares this store), ADR-0009 (cache-first aggregation consumes this) |
| **Post-Cutoff APIs Used** | None (SQLite is long-stable); confirm the chosen plugin's 6.3/IL2CPP status |
| **Verification Required** | Offline run queued and replayed exactly-once on reconnect; server-wins overwrite; logout does not leak a queue to the next player; token is never stored here; works on an IL2CPP ARM64 device build |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-0004/0007 (idempotency keys / run-ID for safe replay); Account/Auth (player-ID scoping) |
| **Enables** | ADR-0009's cache-first Hub aggregation; offline play (`save-persistence.md` Rules 5–8) |
| **Resolves** | The shared `client_durable_local_storage_api` open-item — this SQLite store is also ADR-0006's analytics-buffer store |
| **Ordering Note** | Client-side; independent of server ADRs beyond the idempotency contract |

## Context

### Problem Statement
`save-persistence.md` Rules 5–8 require a **client-side** local cache (last-known-good server state for instant boot + offline browsing) and an **offline action queue** — with hard rules: the cache is **never authoritative for rewards** (display-only; every reward action round-trips), reward actions are **queued or blocked, never applied optimistically**, reconcile is **server-always-wins (full overwrite, no field merge)**, the queue is **tied to player ID not device**, and a cache write failure degrades to no-cache rather than crashing. ADR-0006 also needs a durable client store for its analytics buffer, and both flagged this as the *same* shared open-item. This ADR picks the store and the offline-queue semantics.

### Boundary: tokens are NOT here
The session token lives in **iOS Keychain / Android Keystore** (ADR-0003), **never** in this cache DB. This store holds only display/game state and queued actions — no credentials.

## Decision

### 1. One embedded SQLite DB as the shared client durable store
A single on-device **SQLite** database (via a maintained Unity plugin, e.g. `sqlite-net`) is *the* client durable store, resolving the shared open-item. Tables:
- `cached_state` — last-known-good server state, one row per `playerId` (a JSON blob for the aggregated display state).
- `analytics_buffer` — ADR-0006's event buffer (durable, FIFO-evict-by-`eventId`, dedup).
- `offline_queue` — ordered, durable, `playerId`-scoped reward-action queue.
SQLite gives transactions, ordering, and dedup for free (vs. hand-rolling atomic append/eviction across files), and is the OS-native DB on both platforms — a materially safer dependency than a third-party SDK.
- **IL2CPP note**: reflection-based ORMs can be stripped by IL2CPP AOT — verify the plugin on an ARM64 IL2CPP build, preserve types via `link.xml` if needed, or use an AOT-safe/source-generated variant (Verification).
- **Concurrency (mandatory)**: the analytics buffer flushes **off the main thread** (ADR-0006) while cache/queue ops run on the main thread — a single SQLite DB with its single-writer lock will throw `SQLITE_BUSY` during a reconnect flush storm. So: enable **WAL mode**, and route **all** DB access through **one owned connection behind a single serialized access layer** (a DB job queue) — subsystems never open their own connections. A `busy_timeout` is the floor, not a substitute for serialized access.
- **Corruption recovery (mandatory)**: run `PRAGMA integrity_check` on open. On failure — a corrupt DB *file* would otherwise silently take down `analytics_buffer` and `offline_queue` too, not just the cache — **recreate the DB from scratch**, explicitly accepting the loss of any queued/buffered rows (logged), rather than limping on a corrupt file. This is distinct from a single failed write (which SQLite rolls back atomically, §5).

### 2. Cache: last-known-good, server-wins, full overwrite
- On launch, read `cached_state` **first** for instant UI (no blank/loading screen), then background-reconcile with a live server fetch (`save-persistence.md` Rules 5–6; feeds ADR-0009's cache-first aggregation).
- Reconcile is **server-always-wins**: on any divergence the cached blob is **overwritten wholesale**, never field-merged (GDD edge). Reconnect uses the shared `reconnect_backoff` (2s→60s).
- The cache is **display-only and never authoritative for rewards** (GDD Rule 7). A staleness indicator/offline banner surfaces when showing cached-while-offline (GDD 5-min staleness knob).

### 3. Offline action queue: queue-idempotent-or-block, never optimistic
- Reward-granting actions attempted offline are **queued only if safely replayable** (idempotent via a client run-ID / idem-key, ADR-0004/0007) — e.g. a completed run submission. Actions that can't be safely deferred are **blocked** with a clear "connect to play" message. **Nothing is ever applied optimistically to the cache** (GDD Rule 8) — the cache never shows a reward as granted before the server confirms it.
- On reconnect: **flush the queue in order**; each action is **replayed and re-validated server-side exactly as if freshly submitted** (idempotent by its key, so a lost-ack replay is a no-op, not a double-credit — ADR-0004/0007). **Server-wins**: a queued action that has since become invalid (price/balance changed, run no longer plausible) is **rejected with a clear message, never forced through** (GDD edge). Then a full reconcile overwrites the cache.
- **Remove-after-ack (mandatory for exactly-once)**: a queue entry is **deleted only after a confirmed server ack** — never removed-then-sent. If the ack is lost *after* the server applied the action, the entry is re-sent on the next flush and the server's idempotency (by run-ID) makes it a no-op. Removing before ack would lose an action whose send failed. This is the client analogue of ADR-0007's outbox discipline.
- **Inter-action independence (assumption, stated)**: queued reward actions (run submissions) are treated as **mutually independent** — no queued action depends on another's server-side effect. So if action 1 is rejected as stale, action 2 still applies against real server state correctly. If a future action type is *not* independent, it must abort its dependents on a rejection rather than apply against unexpected state; flagged so this assumption isn't silently violated.
- **Bounds**: **max queued-action age 24h** (GDD tuning) → older entries discarded rather than replayed stale; **plus a max-entry count** so an extended offline binge (or a flush storm) can't grow the queue unbounded — oldest-over-cap dropped with a log.
- **UX (never optimistic, but not opaque)**: the run's **score/result screen may display** immediately; only the **currency reward stays pending** until the server confirms on reconnect. The player sees "you scored X" but the coins land after sync — the cache never shows the reward as already granted (GDD Rule 8).

### 4. Player-ID scoping, link vs. switch (cross-account correctness)
- `cached_state` and `offline_queue` are keyed by **`playerId`**, not device. A different logged-in player sees only their own data — **never the previous player's cache/queue** (GDD edge: silently carrying a queue to a different player would be a serious cross-account data bug).
- **Guest→account link PRESERVES data** (do not discard): per `account-auth.md`, linking a guest to a social/password account **keeps the same `playerId`** and all prior progress. So the cache/queue rows remain valid and simply carry over — a link is *not* an account switch. (Defensive: if an implementation ever were to mint a *new* id on link, it must **re-key** the guest-id rows → new id **inside a transaction**, never discard them — but the stable-id design means no re-key is needed.)
- **Logout / true account switch (different `playerId`)**: on logout with unflushed queued actions, either **flush immediately (block logout until synced)** or **discard with a clear warning** — never carry them to the next player. This is the case the discard rule applies to, *not* linking.
- **Multi-device**: because the cache never grants rewards (Rule 7), two devices with stale caches simply each reconcile to the server's state — no double-spend (GDD edge).

### 5. Failure degradation
- A **cache write failure** (storage full, permissions, corruption) is treated as **no-cache**: fall through to online-only behavior with a loading state; never crash or retain a corrupt partial (GDD edge). SQLite transactions make each cache overwrite / queue op **atomic** — a failed write rolls back rather than leaving a half-updated row.

### Architecture Diagram
```
Launch ─► read cached_state (SQLite) ─► instant UI ─► bg reconcile (server-wins, full overwrite)
                                                          │ backoff 2–60s
Reward action while OFFLINE:
   idempotent/replayable? ── yes ─► enqueue offline_queue (playerId-scoped, ordered)   [NEVER touch cache]
                          └ no  ─► BLOCK with "connect to play"
Reconnect ─► flush offline_queue in order ─► server re-validates each (idempotent by run-ID)
                │ valid ─► applied server-side ─► reconcile overwrites cache
                └ stale ─► rejected w/ clear message (never forced)  ; >24h ─► discarded

SQLite DB (one, on-device): cached_state | analytics_buffer (ADR-0006) | offline_queue
Tokens NOT here — Keychain/Keystore (ADR-0003).   Cache is display-only, never authoritative for rewards.
```

## Alternatives Considered

### Alternative A: File-based JSON blob + append files (no native dep)
- **Pros**: No native plugin; simplest single-blob cache (matches prototype).
- **Cons**: You hand-roll atomic append, FIFO eviction, ordering, and dedup for the analytics buffer *and* the offline queue — two queue-like stores — reinventing what SQLite gives transactionally.
- **Rejection Reason**: The queue/buffer semantics are exactly what a tiny embedded DB does well; SQLite is low-risk (OS-native) and removes hand-rolled correctness risk.

### Alternative B: Block all reward actions while offline (no queue)
- **Pros**: Simplest — no queue, no replay/re-validation.
- **Cons**: A run completed offline is lost; contradicts the GDD's queued-action design and hurts UX on flaky mobile networks.
- **Rejection Reason**: The GDD explicitly wants safe queuing; idempotent replay makes it safe.

### Alternative C: No cache — always online
- **Pros**: No staleness/reconcile logic.
- **Cons**: Blank/loading screen every launch; no offline browsing — violates GDD Rules 5–6 and the "instant, cohesive" Hub fantasy.
- **Rejection Reason**: Instant boot is a stated requirement.

## Consequences

### Positive
- One durable store for cache + analytics buffer + offline queue — resolves the shared open-item, transactional correctness for free.
- Offline play works safely: idempotent replay + server-wins re-validation means no double-credit and no forced-stale actions.
- Cross-account correctness via player-ID scoping + logout flush/discard.
- Cache-as-display-only keeps the server-authoritative economy intact even with offline caching.

### Negative
- A native SQLite plugin per platform to vet/maintain (smaller risk than most native deps — OS-native engine — but non-zero; IL2CPP AOT must be verified).
- Two idempotency surfaces to keep aligned (client run-ID here + server dedup in ADR-0004/0007) — must use the *same* key.

### Risks
- **Risk**: IL2CPP AOT strips the ORM's reflected types → runtime failure on device only.
  **Mitigation**: Verify on an ARM64 IL2CPP build; `link.xml` preservation or an AOT-safe variant (Verification).
- **Risk**: A queued action replayed after the world changed double-acts or is forced through.
  **Mitigation**: Idempotent key = no double-act; server re-validates = stale rejected, never forced.
- **Risk**: A queue leaks across an account switch on a shared device.
  **Mitigation**: Player-ID scoping + logout flush/discard are registry stances + a validation test.
- **Risk**: Stale cache shown as current confuses the player.
  **Mitigation**: 5-min staleness threshold + offline indicator (GDD); cache is clearly display-only.

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|------------|-------------|---------------------------|
| save-persistence.md | Rule 5–6: local cache, read-first then reconcile | `cached_state` read-first + bg reconcile |
| save-persistence.md | Rule 7: cache never authoritative for rewards | Display-only; reward actions round-trip |
| save-persistence.md | Rule 8: queue or block, never optimistic | Idempotent-queue-or-block; cache untouched until server confirms |
| save-persistence.md | Edge: server-wins on divergence | Full wholesale overwrite, no field merge |
| save-persistence.md | Edge: queued action stale on reconnect | Server re-validates; stale rejected, not forced |
| save-persistence.md | Edge: queue tied to player, not device | `playerId`-scoped; logout flush/discard |
| save-persistence.md | Edge: cache write fails ⇒ no-cache | Treated as no-cache; SQLite txn atomicity, no corrupt partial |
| (shared open-item) | One client durable-storage mechanism | SQLite shared with ADR-0006 analytics buffer |

## Performance Implications
- **Storage/Memory**: Small; a blob + two bounded queues. SQLite is lightweight.
- **Launch**: One indexed read for instant UI; reconcile is async.
- **CPU**: Negligible; transactions are cheap at this scale.

## Migration Plan
Greenfield on the native client. The prototype's browser-storage cache is not carried over.

## Validation Criteria
- Offline: complete a run → it queues (cache shows no reward, but the score/result screen may display); reconnect → replayed exactly once (idempotent), reward appears after server confirm, cache reconciles.
- **Lost-ack test**: drop the ack after the server applied a queued action → on next flush it re-sends and the server dedups (no double-credit, and it is NOT lost — remove-after-ack).
- Divergence: local cache differs from server → server values fully overwrite the cache (no merge).
- Stale queued action (price changed) → rejected with a clear message on reconnect, not forced.
- **Guest→link test**: link a guest with unflushed queue → same `playerId`, cache/queue preserved (NOT discarded), progress intact.
- Logout / true account switch with unflushed actions → flushed or discarded; logging in as a different player shows none of the previous player's cache/queue.
- **Concurrency test**: trigger an analytics flush storm concurrent with cache/queue writes → no `SQLITE_BUSY` failures (WAL + serialized single-connection access holds).
- **Corrupt-file test**: corrupt the DB file → `integrity_check` on open detects it and recreates the DB (logged queue loss), rather than crashing or limping.
- Cache single-write failure (simulate storage full) → app falls through to online-only + loading state, no crash, no corrupt partial (SQLite txn rollback).
- Token is confirmed absent from the SQLite DB (it lives in Keychain/Keystore).
- Works on an ARM64 IL2CPP device build (no AOT-stripping failure).

## Related Decisions
- ADR-0003 — tokens in Keychain/Keystore (not here).
- ADR-0004/0007 — idempotency keys reused for safe replay.
- ADR-0006 — analytics buffer shares this SQLite store (resolves the shared open-item).
- ADR-0009 — consumes the cache-first data for Hub aggregation.
- `save-persistence.md` — the client-half design this implements.

## Open Questions
- Exact SQLite plugin (`sqlite-net` vs an AOT-safe/source-gen variant) — confirm against 6.3 IL2CPP at implementation.
- Whether `cached_state` needs at-rest obfuscation (it holds no credentials, but may hold display values) — likely unnecessary; confirm no PII lands there per ADR-0006's no-PII stance.
