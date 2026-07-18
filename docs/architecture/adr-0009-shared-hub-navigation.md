# ADR-0009: Shared Hub Navigation Architecture

## Status
Proposed

## Date
2026-07-10

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Unity 6.3 LTS (build `6000.3.0f1`) — client-side |
| **Domain** | Core / Navigation / Scene Management |
| **Knowledge Risk** | LOW–MEDIUM — Addressables and additive `SceneManager` loading are stable; the 6.3 Addressables package version's exact API should be confirmed at implementation (`deprecated-apis.md` notes Addressables is the recommended content path) |
| **References Consulted** | `design/gdd/shared-hub.md`, `hub-ui.md`, ADR-0008 (UGUI), `CLAUDE.md` (Addressables pipeline), `.claude/docs/technical-preferences.md` (memory budgets) |
| **Post-Cutoff APIs Used** | None committed; confirm the pinned Addressables package API at implementation |
| **Verification Required** | Mini-game load/unload frees its memory (no leak across repeated entries); return-to-Hub is instant; memory stays within mid-range ceiling with one mini-game resident |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-0008 (UGUI — screens/modals are UGUI); **[corrected 2026-07-17]** `adr-0010-client-cache-offline-queue.md` (this ADR existed by the time of this correction pass, superseding the earlier "future" placeholder — it supplies the cache-first aggregation data and the offline-queue flush/staleness-reconcile mechanics §3 and §5 now build on); Account/Auth + Save/Persistence (GDD hard deps) |
| **Enables** | Hub UI presentation implementation; every mini-game's entry/exit |
| **Blocks** | Mini-game integration into the shell |
| **Ordering Note** | Uses ADR-0008's UGUI; independent of server ADRs |

## Context

### Problem Statement
`shared-hub.md` makes Shared Hub the single source of truth for "current screen" (Hub / mini-game / modal, exactly one active), a read-through aggregator (cache-first render then background refresh), and requires that entering a mini-game or modal **does not tear down** the Hub (instant return), with debounced navigation and session-expiry handling. The GDD deliberately leaves the *technical* mechanism open — specifically **how mini-games load**, which on mobile is a **memory** decision: the device cannot hold all 5 mini-games' assets resident (`technical-preferences.md` memory ceilings). This ADR fixes the navigation state model and the scene/content-loading strategy.

### Constraints
- Mobile memory ceilings; only **one** mini-game resident at a time (GDD Rule 4).
- Instant return to Hub (GDD Rule 3) — Hub must stay alive, not reload.
- `CLAUDE.md` asset pipeline is **Addressables**.
- UI is UGUI (ADR-0008).

## Decision

### 1. A single navigation authority: `HubNavigator` + a screen-state model
One service (`HubNavigator`) owns the current-screen state — no scattered screen toggling (GDD Rule 1). The model:
- **Two GameObject roots (load-bearing)**: (a) a **never-deactivated persistent root** holding `HubNavigator`, the auth observer, and the aggregation/refresh coroutines — this must NOT sit under any object that gets `SetActive(false)`, because deactivating a root **stops its coroutines and `Update`**; and (b) a **deactivatable Hub-view root** holding the Hub's Camera, EventSystem, AudioListener, and UI. Navigation deactivates the *view* root, never the persistent-authority root.
- **Base**: the persistent **Hub view** (loaded while in the shell).
- **Modals** (Shop, Mascot Gallery) push onto a **modal stack** rendered *over* the Hub as **in-Hub UGUI panels** — the Hub stays loaded and visible underneath, input-blocked but not destroyed (GDD: "Hub remains underneath"). Closing pops the stack. A mini-game entry tap is **blocked while a modal is open** (the modal blocks input).
- **Mini-game**: a full active-screen swap — the Hub *view* root is deactivated (instant return; the persistent-authority root keeps running), while the mini-game runs in an additively-loaded scene (below).
- Exactly one mini-game OR the Hub is the active gameplay surface at a time; modals compose over the Hub. (GDD Rule 4: no simultaneous mini-games.)

### 2. Mini-games: Addressables additive scene load/unload; Hub scene persists
- The **Hub is a persistent loaded scene** containing the modal panels. It is never unloaded while in the shell — return-to-Hub is instant (GDD Rule 3), no re-instantiation.
- Selecting a mini-game **loads its scene additively via Addressables** (`Addressables.LoadSceneAsync(key, LoadSceneMode.Additive)`), behind a **loading screen**. Then, **on activate** (the additive-scene runtime rules that actually break on device if skipped):
  - **Disable the Hub's Camera, EventSystem, and AudioListener** (they live under the deactivatable view root, §1) — otherwise two EventSystems make input flaky, two AudioListeners break audio ("multiple AudioListeners" warning), and two cameras both render (overdraw / wrong output).
  - Call **`SceneManager.SetActiveScene(miniGameScene)`** so new instantiations, lighting settings, and skybox belong to the mini-game (active-scene ownership).
  - Reverse both on return to Hub.
- **On exit, unload via the stored Addressables handle** — `Addressables.UnloadSceneAsync(sceneInstanceHandle)`, **never `SceneManager.UnloadScene`** (mixing the two leaks). Any separately `LoadAssetAsync`'d handles for that mini-game are released too. Releasing the scene handle alone does not guarantee asset reclamation — a `Resources.UnloadUnusedAssets()` + GC settle completes the reclaim. Only one mini-game scene is ever resident.
- **Modals are UGUI panels in the Hub scene**, not scenes — instant, lightweight, Hub visibly underneath. (No `SceneManager` load for a Shop modal.)
- **Content is local Addressables for MVP** (all content shipped in the build): Addressables gives async loading + memory release + a clean handle-lifecycle, without CDN/download-on-demand infra. Migrating specific groups to **remote** later (install-size / live-ops) is an Addressables *config* change, not a code rewrite — deferred until content growth justifies it.

### 3. Cache-first read-through aggregation
On Hub entry/re-entry, `HubNavigator` triggers each hub-visible system (Currency, Daily Quests, Login Streak, Mascot DB, Leaderboard, Level Select) to render **from cache first** (Save/Persistence client cache), then background-refresh (GDD Rule 2/3). Each system loads **independently and non-blocking**: a placeholder for any undesigned/loading system, and a **failed background fetch shows last-known cached values, never a blank/error** (GDD edge). **[Corrected 2026-07-17]** On mini-game return, `HubNavigator` first waits for that run's reward flush to acknowledge (or its timeout) before reading Currency/Daily Quest state — per ADR-0010's offline-queue flush-before-reconcile guarantee and shared-hub.md's own newly-added Rule 5 — showing a brief "syncing" state if the flush hasn't resolved, rather than re-aggregating against a still-queued (not-yet-applied) reward. Separately, if the Hub is foregrounded and idle when ADR-0010's Core Rule 9 proactive staleness reconcile fires (5 minutes), `HubNavigator` re-renders the already-visible Hub with the reconciled values — the reconcile is not allowed to update the cache invisibly while the rendered screen keeps showing stale figures until the next navigation event.

### 4. Debounced, single-flight navigation
Two distinct phases (they don't contradict): **before a load commits**, taps are **debounced (300ms, last-tap-wins)** so a flurry coalesces to the last intended target. **Once a load commits**, a **nav-in-progress lock ignores further taps** until it completes — an in-flight Addressables load can't be cleanly cancelled/swapped, so "last-wins" applies only pre-commit; post-commit is "ignore," not "cancel-and-restart" (GDD edge + tuning knob).

### 5. Session-expiry & auth gating
**[Corrected 2026-07-17]** `HubNavigator` observes auth state (Account/Auth) and branches on account type — it does **not** apply one uniform expiry behavior. **Guest** sessions attempt account-auth.md's silent `POST /auth/guest/refresh` first; on success, no modal closes and no mini-game unloads — the player never sees anything happened. Only if silent refresh itself fails does the fallback below apply. **Linked (password/social) accounts**, which have no silent-refresh path, hit the fallback immediately on expiry: close any open modal, unload any active mini-game scene (release its Addressables handles), and route to re-authentication — the player can't keep tapping purchase buttons that can no longer be validated. The original single-path version of this section applied the linked-account fallback to guests too, which would force an unnecessary disruptive redirect on every guest JWT expiry (shared-hub.md's own 2026-07-17 revision corrects the same error on the GDD side).

### 6. Async-load failure, activation hitch & memory hygiene
- An Addressables scene load that **fails** (corrupt/missing content) surfaces an error and returns to the Hub — never a stuck loading screen.
- **Mask the activation frame**: the loading screen hides the async *download*, but scene *activation* causes an instantiation spike; hold the loading screen for **one frame past activation** (or stage activation via `allowSceneActivation`) so the hitch isn't visible.
- **Backgrounding mid-load**: if the app is suspended (`OnApplicationPause(true)`) during an in-flight load — common on iOS — the load must resume or cleanly abort on resume without leaking the handle or stranding the loading screen. Covered by the leak test.
- Addressables handles are released on every mini-game exit; verify no handle leak across repeated enter/exit cycles (Validation).

### Architecture Diagram
```
                         HubNavigator (single current-screen authority)
   ┌──────────────────────────────────────────────────────────────────┐
   │ Hub scene (PERSISTENT, UGUI)                                        │
   │   ├─ aggregates read-through (cache-first → bg refresh, non-block)  │
   │   ├─ modal stack (Shop, Mascot Gallery) = in-Hub UGUI panels ───────┼─ Hub visible underneath
   │   └─ on mini-game select ──► debounce 300ms + nav-lock               │
   │             │                                                        │
   │             ▼  Addressables.LoadSceneAsync(additive) + loading screen│
   │        Mini-game scene (ONE resident) ──exit──► release handles ─────┼─ memory freed
   │             │                                   re-aggregate Currency+Quests
   │             ▼                                                        │
   │        auth expiry ⇒ close modal + unload mini-game + route to login  │
   └──────────────────────────────────────────────────────────────────┘
   Content: local Addressables for MVP (remote = later config change)
```

## Alternatives Considered

### Alternative A: Single scene, all screens (incl. mini-games) as panels/prefabs
- **Pros**: Simplest navigation; no scene loading.
- **Cons**: Mini-game assets tend to sit resident; breaks the mobile memory budget as the 5-game collection grows; one giant scene is unwieldy.
- **Rejection Reason**: Memory — can't hold all mini-games resident on mobile.

### Alternative B: Every screen (including modals) as its own `SceneManager` scene
- **Pros**: Uniform model.
- **Cons**: Scene-loading a lightweight Shop modal is slower/heavier than a UGUI panel and complicates "Hub visible underneath the modal" (would need additive + camera stacking for every modal).
- **Rejection Reason**: Overkill for modals; the panel approach is instant and keeps the Hub underneath naturally.

### Alternative C: Remote Addressables (download-on-demand) now
- **Pros**: Smaller initial install; good for a growing collection.
- **Cons**: CDN infra, download-progress/failure UX, offline-availability, content-versioning — all before MVP needs them.
- **Rejection Reason**: Local Addressables gets the memory/async benefits now; remote is a later config-level migration.

## Consequences

### Positive
- One navigation authority — no scattered screen-state bugs (GDD Rule 1).
- Mobile memory respected: exactly one mini-game resident; released on exit.
- Instant Hub return (persistent scene) and instant modals (panels).
- Addressables now = memory discipline + a clean path to remote content later.

### Negative
- Two loading models to reason about (additive scene for mini-games vs panels for modals) — but each is the right tool and the split is simple.
- Addressables adds build/group configuration and a handle-lifecycle to manage correctly (leak risk if mishandled — mitigated by Validation).

### Risks
- **Risk**: Addressables handles leak across repeated mini-game entries → memory creep → OOM on mobile.
  **Mitigation**: Release-on-exit is mandatory; Validation includes a repeated enter/exit memory-stability test.
- **Risk**: A background aggregation fetch failure blanks the Hub.
  **Mitigation**: Cache-first + last-known-value fallback (GDD edge) is a registry stance.
- **Risk**: Rapid taps double-load two mini-games.
  **Mitigation**: 300ms debounce + nav-in-progress lock.
- **Risk**: Auth expiry leaves a modal interactive.
  **Mitigation**: Auth observer force-closes modal + unloads mini-game + routes to login.
- **Risk (device-only)**: Additive-scene component conflicts — dual EventSystem/AudioListener/camera, or `HubNavigator` coroutines dying because they sat under a deactivated root. These pass in-editor smoke tests and break on device.
  **Mitigation**: §1 two-root split (persistent-authority vs deactivatable view) + §2 disable Hub camera/EventSystem/AudioListener and `SetActiveScene` on mini-game activate; both are registry stances and in the Validation checklist.

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|------------|-------------|---------------------------|
| shared-hub.md | Rule 1: single current-screen authority | `HubNavigator` owns it |
| shared-hub.md | Rule 3: instant return, Hub not torn down | Persistent Hub scene; mini-game additive; modals = panels |
| shared-hub.md | Rule 4: one screen active, one mini-game at a time | Additive load of a single mini-game scene, released on exit |
| shared-hub.md | Rule 2/3: cache-first aggregation, bg refresh | Read-through from cache then refresh; non-blocking per system |
| shared-hub.md | Edge: bg fetch fail ⇒ show cached | Last-known-value fallback |
| shared-hub.md | Edge: rapid taps ⇒ one load | 300ms debounce + nav lock |
| shared-hub.md | Edge: session expiry mid-modal | Auth observer closes modal + unloads + routes to login |

## Performance Implications
- **Memory**: One mini-game resident; released on exit — the core mobile-budget lever.
- **Load time**: Async Addressables load behind a loading screen; local content avoids network latency. (Prefetch — GDD Open Q1 — deferred to `/perf-profile`.)
- **CPU**: Aggregation is event/entry-driven, not per-frame.

## Migration Plan
Greenfield native shell. The prototype's `Hub.tsx`/`GameScreen.tsx` structure informs the *screen inventory and layout*, re-implemented as a persistent UGUI Hub scene + Addressables mini-game scenes.

## Validation Criteria
- Enter and exit a mini-game repeatedly (e.g. 50×): after `Resources.UnloadUnusedAssets()` + a GC settle, memory returns to baseline each time (no Addressables handle leak) — sample *after* the settle to avoid false leaks.
- While a mini-game is active: exactly **one** EventSystem, **one** AudioListener, and **one** rendering camera are enabled (Hub's are disabled); `SceneManager.GetActiveScene()` is the mini-game scene. On return, the Hub's are re-enabled and the active scene is the Hub.
- Return-to-Hub renders instantly from the persistent scene (no reload/reaggregate stall); Currency + Quests reflect the run.
- Two rapid mini-game taps load exactly one (the last, pre-commit); taps during an in-flight load are ignored.
- A failed Addressables load returns to the Hub with an error, not a stuck loading screen; backgrounding the app mid-load resumes or aborts cleanly on resume (no leaked handle / stuck loading screen).
- Session expiry while a Shop modal is open closes it and routes to login.

## Related Decisions
- ADR-0008 — UGUI (screens/modals).
- `shared-hub.md`, `hub-ui.md` — the orchestration + presentation this implements.
- ADR-0010 (`client-cache-offline-queue.md`) — **[corrected 2026-07-17]** supplies the cache-first aggregation data, the offline-queue flush-before-reconcile guarantee (§3), and the proactive staleness-reconcile trigger (§3) this ADR now builds on directly, superseding the earlier "future client-cache ADR" placeholder.

## Open Questions
- **Asset prefetch** (`shared-hub.md` Open Q1): prefetch the next-likely mini-game while idle, or on-demand only? On-demand for MVP; revisit in `/perf-profile` once asset sizes are known.
- Exact pinned Addressables package API in 6.3 — confirm at implementation.
- When (if) to migrate specific Addressables groups to remote/CDN — a later live-ops/install-size call.
