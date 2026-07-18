# Save/Persistence

> **Status**: Designed
> **Author**: Abdulrahman Alenazan + Claude
> **Last Updated**: 2026-07-09
> **Implements Pillar**: Server-authoritative economy; Shared hub/economy
> **Creative Director Review (CD-GDD-ALIGN)**: APPROVED 2026-07-09 (self-reviewed — no `creative-director` subagent registered in this environment)
> **`/design-review` (2026-07-14)**: NEEDS REVISION — 4 blocking items found (currency-chokepoint contradiction with ADR-0004, missing offline-queue idempotency-key ownership, orphaned staleness knob, factually-wrong logout/guest-link edge case). All folded into this revision below; re-review pending.

## Overview

Save/Persistence is the durable-storage layer for both halves of Quack
Studio. **Server-side**, it's the authoritative record of every player's
currency, progress, mascots, and quest state — queried and mutated by nearly
every other system, following the prototype's proven narrow-interface
pattern (find-by-id, find-by-username, insert, update, top-N query) rather
than letting every system touch storage directly. **Client-side**, it's new
scope the prototype never needed: a local cache that lets the game boot
instantly with last-known state and play offline or on a flaky connection,
syncing back to the server (the source of truth) once connectivity returns.
The two halves share a name but not a technology — server persistence needs
real ACID guarantees under concurrent writes (the prototype's
atomic-JSON-file store was an explicit, documented placeholder for this,
flagged in its own `ENHANCEMENTS.md`), while client persistence needs to be
fast, small, and disposable — safe to wipe and re-sync at any time.

## Player Fantasy

Save/Persistence has no direct player fantasy. Players feel it only through
its *absence of failure* — the game remembering exactly where they left off,
mascots and currency never mysteriously vanishing, offline play not feeling
broken. It directly underwrites the "always exactly where I left off"
promise that Daily Quests and Login Streak depend on to feel trustworthy
rather than punishing.

## Detailed Design

### Core Rules

**Server-side:**
1. **Data model**: one `Player` record per account (id, currency, progress,
   upgrades/mascots, daily/quest state, timestamps) — the same
   normalized-per-player shape as the prototype, extended with mascot
   ownership once that system exists.
2. **Storage backend**: migrate the prototype's atomic-JSON-file store to
   PostgreSQL (per master prompt) — but preserve the *same narrow interface
   contract* (find-by-id, find-by-username, insert, update-with-mutator,
   top-N query) so every calling system's code doesn't change, only the
   implementation underneath.
3. Every **non-currency** write goes through a single `updatePlayer(id, mutate)`-
   style entry point that loads, mutates, and persists atomically. This
   single-chokepoint pattern — not scattered field-by-field writes across
   routes — is what makes reward-clamping/anti-cheat auditable, since every
   mutation happens in one place. **[Correction, 2026-07-14]** Currency
   (coins/gems) is the one EXPLICIT EXCEPTION: it never goes through
   `updatePlayer`, it goes through ADR-0004's separate `mutateWallet`
   chokepoint (conditional atomic `UPDATE ... WHERE balance >= amt
   RETURNING`, its own idempotency scheme). This GDD previously stated
   "every write" with no exception noted — a programmer reading this GDD
   alone, without ADR-0004, would reasonably route currency through
   `updatePlayer` instead, exactly the mistake ADR-0004 warns about.
   `updatePlayer` remains the chokepoint for everything else (progress,
   mascots, quest/streak state).
4. Concurrent writes to the same player record must never corrupt or lose
   data. A real database's transaction/row-locking replaces the prototype's
   write-chain serialization, but the guarantee must hold. Composed
   operations that touch both non-currency state and currency in the same
   logical action (e.g., a future mascot purchase costing coins) must follow
   ADR-0005's canonical lock order (`player_state` before `wallet`) to avoid
   an ABBA deadlock between the two chokepoints — see ADR-0005 and ADR-0004
   for the authoritative mechanics; this GDD only owns the non-currency
   half.

**Client-side [NEW]:**
5. A local cache mirrors the last-known-good server state for the current
   player, refreshed after every successful server round-trip.
6. On launch, the client reads the local cache **first** for instant UI (no
   blank/loading screen), then reconciles with a live server fetch in the
   background.
7. Local cache is **never authoritative for anything that grants reward** —
   it's read-only display state. Every reward-granting action still
   round-trips to the server, preserving the server-authoritative economy
   pillar even with caching in play.
8. If the server round-trip fails (offline), the player can still browse the
   Hub/mascots/currency from cache, but reward-granting actions (submitting
   a run, buying from shop) are queued or blocked until connectivity returns
   — **never applied optimistically to cache and assumed synced.**
   **[Clarified 2026-07-14]** Every queued action is assigned a stable,
   client-generated **idempotency key at the moment it is queued** (not at
   replay time) — a UUID persisted alongside the queued action in local
   storage. Every replay attempt for that action, including retries caused
   by an ack timeout or a dropped response mid-reconnect, reuses the same
   key. The server's replay handler treats a repeated key as a no-op
   (returns the original result, does not re-apply the reward) — this is
   the client-side half of the contract ADR-0004's `mutateWallet`
   idempotency and ADR-0007's replay-verification already assume exists;
   without minting the key at queue-time specifically, a retry during
   replay could double-credit a reward. Queued actions replay in **FIFO
   order** (oldest-queued first) since a later action may depend on a
   balance an earlier one produces (e.g., a reward credit queued before a
   shop purchase). Flush must **complete** (all queued actions replayed or
   definitively rejected) before the post-reconnect full reconcile (State
   table row 5) begins — reconcile does not run concurrently with flush,
   or it could read pre-flush state.
9. **[NEW, 2026-07-14]** The "Local cache staleness threshold" (5 minutes,
   see Tuning Knobs) is not just a launch-time concept: if the app stays
   foregrounded and more than 5 minutes have elapsed since the last
   successful reconcile, the client proactively triggers a background
   reconcile (not just on launch/reconnect) and may show a subtle "syncing"
   indicator, distinct from the offline banner. This gives the knob an
   actual behavior to govern — previously nothing in this GDD referenced it,
   even though `adr-0010-client-cache-offline-queue.md` assumed a
   staleness-badge behavior existed here.

### States and Transitions

| State | Trigger | Result |
|---|---|---|
| No local cache | First launch on a device | → Fetch from server, populate cache, then Hub |
| Cache present, online | App launch | → Show cached state instantly, background-refresh, reconcile if different |
| Cache present, offline | App launch, no connectivity | → Show cached state, mark UI "offline" (limited interactions), retry connectivity in background |
| Any state | Reward-granting action attempted while offline | → Queued (if safely replayable) or blocked with a clear "connect to play" message |
| Offline → Online | Connectivity restored | → Flush queued actions, then full reconcile from server (server wins on conflict) |

### Interactions with Other Systems

- **Account/Auth**: the player record is keyed by the ID Account/Auth mints
  — Save/Persistence never generates its own player IDs.
- **Every Core/Feature-layer system** (Currency, Mascot Database, Daily
  Quests, etc.): all reads/writes to persistent player state go through this
  system's interface, never touch storage directly.
- **Anti-Cheat/Replay Verification**: depends on the atomic-mutation
  guarantee (Rule 3) so a reward computation and its resulting balance
  change happen as one indivisible operation, not two racy steps. Any
  composed operation spanning non-currency state and a reward credit
  together must follow ADR-0005's canonical lock order and ADR-0005's
  shared `idem_key` discipline for composed ops — this GDD's chokepoint
  alone does not cover the money half; see ADR-0004/ADR-0005/ADR-0007.

## Formulas

The `reconnect_backoff` formula is defined as:

`reconnect_backoff_seconds = min(max_backoff, base_backoff × 2^attempt)`

**Variables:**
| Variable | Symbol | Type | Range | Description |
|---|---|---|---|---|
| base_backoff | `base_backoff` | float | 2 | Starting delay in seconds before the first retry |
| attempt | `attempt` | int | 0–∞ | Number of consecutive failed reconnection attempts |
| max_backoff | `max_backoff` | float | 60 | Hard ceiling on delay between retries |

**Output Range:** 2s to 60s. **Example:** attempt=0 → 2s, attempt=3 → 16s,
attempt=5+ → capped at 60s.

Everything else (which fields sync, how records are keyed) is structural,
not mathematical — covered in Detailed Design.

## Edge Cases

- **If a queued offline action becomes stale by the time connectivity
  returns** (e.g., a shop purchase queued offline, but the item's price or
  the player's balance has since changed server-side): the server
  re-validates it exactly as if freshly submitted — reject with a clear
  error rather than force it through. Queuing never bypasses normal
  validation.
- **If local cache and server state genuinely diverge** (not just staleness
  — conflicting data, e.g. local cache shows a 5-day streak but the server
  reset it to 1 after too much offline time): **server always wins**; the
  local cache is discarded and fully overwritten on reconcile, never merged
  field-by-field.
- **If a player has two devices both reconnecting with stale caches around
  the same time**: no special handling needed — since local cache never
  grants rewards directly (Rule 7), there's no double-spend risk. Both
  devices simply reconcile to whatever the server's current state is.
- **If the local cache write itself fails** (device storage full,
  permissions issue): treat as "no cache" — fall through to online-only
  behavior with a loading state. Never crash or silently retain a corrupt
  partial cache.
- **If a player logs out with unflushed offline actions queued**: the queue
  is tied to player ID, not device. On logout, the client first attempts to
  flush (replay) the queue against the server with a short timeout (10s);
  if flush completes, logout proceeds normally. If flush fails or times out,
  the player sees an explicit warning ("you have unsynced progress that may
  be lost") and must confirm before logout proceeds — silently discarding
  without warning, or silently carrying the queue over to whichever
  *different* player next logs in on the same device, would both be
  unacceptable: the former is silent data loss, the latter is a serious
  cross-account data bug.
- **[CORRECTED 2026-07-14] If a guest links to a social/password account
  with unflushed offline actions queued**: this is **not** the scenario
  above. Per `account-auth.md`'s linking rule, linking preserves the
  **same player ID** — it is not a different player taking over the device.
  The queue is tied to that player ID and simply continues belonging to the
  same player after linking; it must **not** be flushed-or-discarded as if
  a different account were involved. (A prior version of this edge case
  incorrectly bundled linking together with logout as if both risked the
  same cross-account leak — `adr-0010-client-cache-offline-queue.md`
  already caught and corrected this point during architecture review; this
  GDD's own text had not been updated to match until now.)

## Dependencies

- **Depends on**: none (Foundation — zero dependencies, confirmed).
- **Depended on by** (hard, all of them): Account/Auth, Shared Hub, Currency
  System, Currency Ledger, Anti-Cheat/Replay Verification, Mascot Database +
  Rarity Logic, Daily Quests, Login Streak, Mascot Gallery/Equip UI *(added
  2026-07-12 — that GDD writes `player_state.data.equippedMascotId` through
  this system's `updatePlayer` mutator, a one-directional gap fixed here)*,
  Leaderboard *(added 2026-07-12 — the underlying `leaderboard_scores`
  table, per that GDD's own Core Rule 9 and its required ADR-0005
  addendum)* — everything that needs durable player state. **Depended on
  by (soft)**: Level/Difficulty Config
  (Ricochet), for reading the player's Extra Balls upgrade level *(added
  2026-07-09 — `/review-all-gdds` found this edge existed one-directionally:
  that GDD declared the dependency, this list omitted it)*.

**Consistency check**: Account/Auth's GDD says "Depends on (hard):
Save/Persistence" — this GDD says "Depended on by: Account/Auth." ✅
Bidirectionally consistent.

**Related Architecture [added 2026-07-14]**: This GDD's server-side rules
are refined and, in places, corrected by three existing ADRs which should
be read alongside it, not just this document in isolation:
`adr-0005-server-persistence-postgres.md` (Postgres backend, locked
read-modify-write for non-currency state, canonical lock order),
`adr-0004-currency-atomic-credit-debit.md` (the `mutateWallet` exception to
Rule 3), and `adr-0010-client-cache-offline-queue.md` (SQLite client store,
staleness-badge behavior, the guest-link-preserves-queue correction folded
into Edge Cases above).

## Tuning Knobs

| Knob | Suggested Value | Too Low | Too High |
|---|---|---|---|
| Reconnect backoff (base/max) | See Formulas section — not duplicated here | N/A | N/A |
| Local cache staleness threshold | 5 minutes — governs the proactive foregrounded-reconcile trigger added in Core Rule 9, not just launch-time behavior | Constant unnecessary background refreshes, battery/data cost | Player sees visibly outdated currency/mascot state for too long during a long foregrounded session |
| Queued offline-action max age | 24 hours | Legitimate short offline sessions get their actions discarded unfairly | Stale queued actions pile up and produce confusing rejections long after the player forgot submitting them |

## Visual/Audio Requirements

N/A — Foundation-layer infrastructure, no VFX/audio identity of its own.

## UI Requirements

Save/Persistence itself has no dedicated screen, but it drives visible UI
elsewhere: an "offline" indicator/banner when the client can't reach the
server, and a distinct (non-blocking where possible) treatment for actions
that are queued vs. actions blocked entirely while offline. Detailed UX
belongs in `/ux-design`; this section just flags that the offline state
needs a defined visual treatment, not silence or a generic error.

## Acceptance Criteria

- **GIVEN** a fresh device with no local cache, **WHEN** the app launches
  with connectivity, **THEN** it fetches from server, populates the cache,
  and reaches the Hub.
- **[REVISED 2026-07-14] GIVEN** a populated local cache, **WHEN** the app
  launches online, **THEN** the Hub is interactive within 200ms of launch
  using cached values (zero network wait), AND a background fetch resolves
  and replaces any field where cache≠server.
- **GIVEN** a populated local cache, **WHEN** the app launches offline,
  **THEN** the Hub renders from cache with an "offline" indicator, and
  reward-granting actions are unavailable or queued.
- **[REVISED 2026-07-14] GIVEN** a specific reward-granting action type is
  attempted offline (e.g., submitting a completed run — a replayable
  action), **WHEN** connectivity is unavailable, **THEN** it is queued with
  a client-generated idempotency key (never applied to local cache as if it
  already succeeded). **GIVEN** a non-replayable action is attempted offline
  (e.g., one requiring a live server-side check with no safe queued form),
  **THEN** it is blocked with the message "connect to play."
- **GIVEN** a queued action becomes invalid by reconnect time, **WHEN** it's
  replayed against the server, **THEN** it's rejected with the same
  validation as a fresh submission, never silently forced through.
- **GIVEN** local cache and server state diverge on reconnect, **WHEN**
  reconciliation runs, **THEN** the server's values fully overwrite the
  local cache — no partial merge.
- **[REVISED 2026-07-14] GIVEN** balance=100, **WHEN** two concurrent
  `updatePlayer` mutations (+5, +3) race against the same player record,
  **THEN** the final balance is exactly 108 under forced write-interleaving
  — never 105 or 103 (no lost update).
- **[NEW 2026-07-14] GIVEN** a local cache write fails (e.g., device storage
  full), **WHEN** the client attempts to persist state, **THEN** the app
  falls through to online-only behavior with a loading state, does not
  crash, and no partial/corrupt cache file remains on disk (verified via
  read-back).
- **[NEW 2026-07-14] GIVEN** reconnect attempts 0, 1, 3, and 5, **WHEN** the
  backoff formula is evaluated, **THEN** the computed delays are exactly
  2s, 4s, 16s, and 60s (capped) respectively.
- **[NEW 2026-07-14] GIVEN** two devices for the same player both
  reconciling with divergent stale caches at approximately the same time,
  **WHEN** both reconcile, **THEN** both converge to identical post-reconcile
  state with no crash, error, or lost mutation.
- **[NEW 2026-07-14] GIVEN** a queued action older than the 24-hour max age,
  **WHEN** connectivity returns, **THEN** the action is discarded rather
  than replayed, and the player is notified rather than left to silently
  wonder why it never applied.
- **[NEW 2026-07-14] GIVEN** an app that stays foregrounded and online for
  longer than the 5-minute staleness threshold with no intervening
  reconcile, **WHEN** the threshold is crossed, **THEN** the client
  proactively triggers a background reconcile rather than waiting for the
  next launch or reconnect event.
- **[NEW 2026-07-14] GIVEN** a guest links a social/password account with
  queued offline actions pending, **WHEN** the link completes, **THEN** the
  queue is neither flushed-as-a-special-case nor discarded — it continues
  belonging to the same (unchanged) player ID exactly as it would without
  linking.
- **[NEW 2026-07-14] GIVEN** a player logs out with unflushed queued actions
  and the flush attempt times out (10s), **WHEN** logout is requested,
  **THEN** the player sees an explicit unsynced-progress warning and must
  confirm before logout proceeds.

## Open Questions

1. Exact Unity 6.3 client-side local-storage API choice (file-based JSON, a
   package, Unity Cloud Save) was never independently verified — flagged in
   the Technical Feasibility Brief. *Target: verify before implementation.*
2. Should queued-but-rejected offline actions surface to the player
   immediately on reconnect, or silently drop with a log entry? Affects
   trust in the "queued" promise made in Rule 8. *Target: resolve during
   `/ux-design` for the offline-state UI.*
