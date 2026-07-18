# Active Session State

- Task: Master Architecture Document
- Status: Complete, self-signed-off (APPROVED WITH CONDITIONS)
- File: docs/architecture/architecture.md
- TD verdict: APPROVED WITH CONDITIONS (write RNG-determinism + physics-API
  ADRs before any Super Ricochet code)
- LP verdict: FEASIBLE
- 28 Technical Requirements extracted from 11 GDDs, 0 currently have ADR
  coverage (expected — no ADRs written yet)
- 11 required ADRs identified, prioritized by layer (Foundation first)
- Highest-priority ADR: "Deterministic RNG strategy for Anti-Cheat replay"
  — Anti-Cheat's entire Tier-2 verification is void without this being
  right; flagged as the single most consequential decision in the
  architecture
- ADR-0001 "Deterministic RNG strategy for Anti-Cheat replay" — WRITTEN
  (docs/architecture/adr-0001-deterministic-rng-replay-strategy.md).
  Decision: shared C# SharedSimCore (.NET Standard 2.1, PCG32, unchecked
  integer math) referenced by both Unity client and a self-contained CLI
  executable invoked as a short-lived child process by the Node backend
  (not a persistent microservice — that was the original draft, revised
  after independent review flagged it as disproportionate operational
  weight). Fail-open on verifier outage + async re-verify/clawback.
  Independently reviewed via a real general-purpose subagent (engine-
  specialist + TD stand-in combined) before finalizing — found 3 real
  issues (target framework/AOT risk, algorithm left too loose, proportion-
  ality contradiction vs rejected WASM alternative), all folded in.
- docs/registry/architecture.yaml created — 2 interfaces, 2 forbidden
  patterns, 3 API decisions locked from ADR-0001.
- ADR-0002 "Deterministic Fixed-Point Physics Engine for Super Ricochet" —
  WRITTEN (docs/architecture/adr-0002-deterministic-fixedpoint-physics.md).
  IMPORTANT history: a FIRST draft (adr-0002-super-ricochet-physics-api.md)
  was REJECTED on independent review for 3 blocking flaws — (1) fixed TIME
  step silently broke the GDD's fixed DISTANCE half-radius tunnelling
  guarantee when speed varies, (2) single-precision float collision can't
  agree on discrete hit/miss across iOS-ARM vs x86 server and tolerance_units
  can't absorb a branch fork, (3) a custom physics engine was mis-scoped as
  an RNG-ADR "extension". That file is retained marked REJECTED/superseded.
  The REWRITE fixes all three: Q16.16 fixed-point integer math (Int32 +
  Int64 mul intermediates, no float/no Int128/no unsafe in scored path,
  deterministic IntSqrt, unchecked+range-analyzed) → byte-exact across
  platforms → tolerance_units can be 0 for this path; REMAINING-DISTANCE inner
  loop (advance min(half_radius, remaining)) preserves tunnelling invariant at
  any speed incl. mid-frame nudge/bounce; fixed-60Hz-timestep accumulator with
  spiral-of-death clamp; 12s volley cap counted in SIM FRAMES (720) not
  wall-clock; SharedSimCore is sole outcome authority, Unity Rigidbody2D
  kinematic = VISUAL ONLY (high-level API chosen over new LowLevelPhysics2D
  Box2D v3 since Unity isn't the authority here). Includes a BLOCKING SPIKE
  GATE before Accepted: prove ARM64-IL2CPP == x86 CLI byte-identical
  (incl. signed-unchecked overflow + IntSqrt convention) on a 1-ball case,
  else fall back to Alternative D (statistical anti-cheat). Reviewed twice by
  the same independent general-purpose subagent (agentId ae170b220379e5b79):
  1st pass REJECT (found the 3 blockers), 2nd pass APPROVE WITH CONDITIONS
  (no hard blockers) — all 3 conditions folded in (mid-frame speed hole,
  accumulator clamp + sim-frame cap, spike must freeze Q-format/overflow
  proof/IL2CPP-codegen assertion). Status stays Proposed until spike passes.
- ⚠️ Cross-ADR open item raised by ADR-0002: bit-exact physics invalidates
  ADR-0001's + anti-cheat-replay-verification.md's float-rounding
  justification for a nonzero tolerance_units on the Ricochet physics path.
  NOT silently changed — routed to Anti-Cheat GDD owner as an open question.
- Registry: pending user approval to append ADR-0002 stances to
  docs/registry/architecture.yaml (created by ADR-0001).
- ADR-0002 registry stances APPENDED to docs/registry/architecture.yaml
  (state_ownership: ball pos/vel → SharedSimCore; interfaces:
  IDeterministicPhysics2D, Fix32; 3 forbidden patterns: float in scored path,
  Unity physics as authority, wall-clock in scored logic; 5 api_decisions;
  open_items: tolerance_units_reconciliation).
- ADR-0003 "Secure On-Device Auth Token Storage" — WRITTEN
  (docs/architecture/adr-0003-secure-token-storage.md). Decision: thin C#
  ISecureTokenStore interface; iOS Keychain (AfterFirstUnlockThisDeviceOnly +
  ThisDeviceOnly, or tighter WhenUnlocked if no locked-state net call) via
  Swift plugin; Android Keystore-wrapped AES-GCM / Tink app-private blob via
  Kotlin plugin (NOT deprecated EncryptedSharedPreferences); editor mock in
  Editor-ONLY assembly (physically un-shippable) + fail-closed selector
  (throws on unknown platform); PlayerPrefs BANNED for secrets. Scoped NARROW
  (storage only) — no-revocation/7-day-bearer risk recorded + routed to a
  future Auth ADR, not redesigned here. Resolves account-auth.md Open Q#3.
  Independently security-reviewed (fresh agent ab2175f39e9662575): APPROVE
  WITH CONDITIONS, no redesign blockers; both conditions folded in (C1 mock
  un-shippable + fail-closed; C2 don't overclaim Android hardware-backing —
  TEE/StrongBox where available, software fallback on low-end API 25) plus
  minor notes (concrete Android default named, tighter iOS class option,
  scrub Authorization from logs/crash SDKs, TLS/cert-pinning sibling risk).
- Engine HIGH-RISK flags in ADR-0003 (verify at implementation): current
  Android secure-storage successor library; iOS accessibility class.
- ADR-0003 registry stances APPENDED (interface ISecureTokenStore; 3
  forbidden patterns: PlayerPrefs-for-secrets, mock-in-shipped-build,
  logging-Authorization; 3 api_decisions; open_item auth_token_revocation).
- ADR-0004 "Currency System Atomic Credit/Debit" — WRITTEN
  (docs/architecture/adr-0004-currency-atomic-credit-debit.md). SERVER-SIDE
  (Node/TS), LOW engine risk. Decision: single multi-leg `mutateWallet`
  chokepoint (N legs in ONE transaction → no partial rewards); two-path API
  builds legs (creditMultiplied ×(1+coinValueLevel) vs creditFlat raw, never
  combined = locks GDD Rule 4 double-dip fix; gems never multiplied);
  store-level atomicity (conditional UPDATE ... WHERE coins>=amt RETURNING,
  never in-process mutex/read-modify-write); operation-level idempotency via
  INSERT currency_op ON CONFLICT(idem_key) DO NOTHING (replay ⇒ rollback +
  fresh full-wallet SELECT); new-player upsert (INSERT ... ON CONFLICT
  player_id DO UPDATE); balance column + append-only currency_ledger; analytics
  via transactional OUTBOX (at-least-once, not fire-and-forget). READ COMMITTED
  sufficient (row-lock re-eval), no SERIALIZABLE needed. Independently reviewed
  (backend/distributed-systems agent a324cc976a21a6da6): APPROVE WITH
  CONDITIONS; all 4 blockers folded in (B1 idempotency ON CONFLICT not
  bare-unique-violation; B2 guarded UPDATE-before-ledger ordering + RETURNING;
  B3 new-player upsert; B4 multi-leg single-txn) + 3 minors (READ COMMITTED
  note, full-wallet replay read, outbox).
- HARD COUPLING recorded: ADR-0004 needs the persistence-migration ADR to
  supply real store transactions; the single-instance JSON-file prototype
  satisfies the contract ONLY until a 2nd instance is deployed → multi-instance
  rollout is gated on that migration.
- ADR-0004 registry stances APPENDED (state_ownership wallet_balance;
  interface mutateWallet+two-path; 3 forbidden patterns; 5 api_decisions;
  2 open_items incl. persistence_backend_gates_multi_instance).
- ADR-0005 "Server-Side Persistence Migration to PostgreSQL" — WRITTEN
  (docs/architecture/adr-0005-server-persistence-postgres.md). SERVER-SIDE,
  LOW engine risk. Scoped to SERVER half only (client cache/offline-queue =
  separate future ADR). Decision: Postgres HYBRID schema — relational
  wallet/currency_ledger/currency_op/analytics_outbox + player table (money +
  cross-player queries), JSONB player_state.data for evolving per-player-only
  state (progress/quests/mascots) with schema_version + zod validation;
  leaderboard sort key promoted to an INDEXED real column (player.leaderboard_
  score), never JSONB. TWO write paths on one store: generic updatePlayer =
  LOCKED read-modify-write (SELECT..FOR UPDATE + mutate + UPDATE, pure/fast
  mutators, no I/O under lock) for non-money state; money EXCEPTED → ADR-0004
  mutateWallet. Data layer: query-builder/raw parameterized SQL (Knex/pg), no
  heavy ORM. Versioned SQL migrations; fresh schema (no prod players);
  optional dev-only JSON import. RECONCILES the latent GDD-Rule-3-vs-ADR-0004
  contradiction (locked vs unlocked RMW). Independently reviewed (DB-architect
  agent ac5ffb09e246c7f04): APPROVE WITH CONDITIONS; all 3 blockers folded in
  — (1) canonical lock order player_state-before-wallet to prevent ABBA
  deadlock in composed ops; (2) composed-op whole-operation idempotency gated
  on shared idem_key so retried JSONB mutator doesn't double-apply; (3) top-N
  leaderboard indexed column not JSONB scan — plus minors (tightened locked-
  vs-unlocked RMW wording, no-I/O-in-mutator, zod+schema_version).
- ADR-0005 RESOLVES ADR-0004's open_item persistence_backend_gates_multi_
  instance — the transactional store now exists (mark resolved in registry).
- Registry: pending user approval to append ADR-0005 stances.
- DONE so far (5/11): 0001 RNG, 0002 physics, 0003 token storage, 0004
  currency, 0005 persistence. REMAINING 6: analytics buffer/flush, replay
  re-sim service (server perf budget), shared-hub nav, boss-AI damage events,
  UI-Toolkit-vs-UGUI, client cache/offline-queue.
- ADR-0005 registry stances APPENDED (state_ownership player_state; 4
  forbidden patterns; 6 api_decisions; multi_instance open-item RESOLVED).
- ADR-0006 "Analytics Event Buffer & Flush (Client-Side)" — WRITTEN
  (docs/architecture/adr-0006-analytics-buffer-flush.md). CLIENT-side (iOS/
  Android) + Node ingest. Decisions: FIRST-PARTY transport (dumb durable
  client buffer → our /events batch endpoint, NO third-party SDK on device;
  server may fan out to Amplitude/BigQuery later); MONEY/reward events
  (currency_earned/spent, purchase_completed, quest_claimed, mascot_acquired)
  are SERVER-emitted via ADR-0004 outbox, NOT the client buffer — reconciles
  GDD catalog, prevents double-count + spoof. Durable buffer cap 500 FIFO-by-
  eventId; batch 20 / 30s / on-background; non-blocking emit; at-least-once +
  server dedup by client eventId; device-id pre-auth. Independently reviewed
  (Unity+telemetry agent a8fccc9ecf3f51c7c): APPROVE WITH CONDITIONS; all 3
  blockers folded — (B1) durability boundary = SYNCHRONOUS pause-persist to
  durable log on OnApplicationPause, on-background is persist-not-send +
  next-launch reflush; (B2) per-event poison quarantine (400/422 return bad
  eventId, resend rest; 401 refresh+retry; 408/429/5xx/network backoff), not
  drop-whole-batch; (B3) single-flight flush + id-keyed removal/eviction —
  plus minors (seq/schemaVersion/appVersion on event; sessionId=client UUID +
  server-side inactivity session close since session_end unreliable on kill;
  server received_at for cohorting, don't trust device clock; FIFO excludes
  in-flight batch).
- Registry: pending user approval to append ADR-0006 stances.
- DONE so far (6/11): 0001 RNG, 0002 physics, 0003 token storage, 0004
  currency, 0005 persistence, 0006 analytics. REMAINING 5: replay re-sim
  service (server perf budget), shared-hub nav, boss-AI damage events,
  UI-Toolkit-vs-UGUI, client cache/offline-queue.
- ADR-0006 registry stances APPENDED (interface Analytics.Emit+/events; 5
  forbidden patterns; 5 api_decisions; client_durable_local_storage open-item).
- ADR-0007 "Server-Side Headless Replay Re-Simulation Architecture" — WRITTEN
  (docs/architecture/adr-0007-replay-resimulation-service.md). SERVER-side.
  Closes the perf/latency budget ADR-0001 + ADR-0002 deferred; answers anti-
  cheat GDD Open Q1. KEY RECONCILIATION: reward model is FLAG-ONLY per the
  anti-cheat GDD (Tier-1 clamp governs reward at submission; Tier-2 replay is
  async + only raises a fraud flag → 3-flags/7-days human review; NO
  clawback) — this SUPERSEDES ADR-0001's "clawback" wording, which I annotated
  in ADR-0001 (Decision pt 4 note + related-decisions), not silently rewrote.
  Decisions: synchronous Tier-1 credit + async flag-only Tier-2; RISK-BASED
  coverage (100% high-value/leaderboard/flagged + sampled remainder, anomaly-p-
  bump for sub-threshold farmers) = tunable CPU dial not 100%; warm .NET
  SharedSimCore worker pool consuming Postgres queue (SELECT..FOR UPDATE SKIP
  LOCKED) — refines ADR-0001's spawn-per-check, no networked service, no new
  infra; resource caps (≤720 frames×balls, CPU/time) → over-cap = Tier-1+
  degraded flag; fail-open + risk-based capacity shedding → saturated =
  Tier-1-only+ALERT; run-ID idempotency end-to-end; escalation → human review
  (never auto-ban). Independently reviewed (distributed-systems/anti-cheat
  agent a3779925a2ffd0fbb): APPROVE WITH CONDITIONS; all 4 blockers folded —
  (B1) atomic claim+reclaim query owns expired-lease recovery (not SKIP LOCKED
  alone); (B2) lease_epoch FENCING + UNIQUE(run_id) flag + analytics dedup so a
  slow-job double-process can't double-flag/emit; (B3) reward credit + job
  enqueue in ONE transaction (outbox) — no rewarded-but-unverified run; (B4)
  poison-job max_attempts → dead_letter, no infinite crash loop — plus minors
  (sub-threshold blind-spot mitigation, leaderboard-integrity lag called out,
  cached queue-depth gauge not per-enqueue COUNT, review-queue backpressure).
- Registry: pending user approval to append ADR-0007 stances (incl. marking
  the reward-model flag-only supersession of ADR-0001).
- DONE so far (7/11): 0001 RNG, 0002 physics, 0003 token storage, 0004
  currency, 0005 persistence, 0006 analytics, 0007 replay-resim. REMAINING 4:
  shared-hub nav, boss-AI damage events, UI-Toolkit-vs-UGUI, client cache/
  offline-queue.
- ADR-0007 registry stances APPENDED (interface verification_job queue; 4
  forbidden patterns; 4 api_decisions; reward_model_flag_only open-item RESOLVED
  + ADR-0001 annotated).
- ADR-0008 "UI System — UGUI Primary, UI Toolkit Deferred" — WRITTEN
  (docs/architecture/adr-0008-ui-system-ugui.md). CLIENT-side Unity 6.3,
  MEDIUM risk. Decision: UGUI (Canvas) primary for BOTH Hub menus and in-game
  HUD now; UI Toolkit NOT used now but allowed later for ONE whole data-heavy
  screen via an explicit gate (data-heavy AND whole-screen-not-mixed AND cost
  consciously accepted). Rationale: one mental model, mature, best fit for
  juicy gameplay-coupled HUD, lowest risk with no UI specialist; coexistence-
  later preserved. Mobile specifics locked: EventSystem MUST use
  InputSystemUIInputModule (+EnhancedTouch) — new Input System default in
  Unity 6, legacy StandaloneInputModule gives ZERO touches; Canvas Scaler
  Scale-With-Screen-Size (height-biased match, anchoring does real work,
  tablet max-width clamp); SafeArea component recomputes on
  safeArea/orientation/resolution change (not Start-only); perf checklist to
  hold ≤150 draw calls (per-canvas rebuild → static/dynamic canvas split but
  don't over-split; Raycast Target OFF on non-interactive; RectMask2D not
  Mask; no Layout Groups/ContentSizeFitter on dynamic; watch overdraw;
  atlases; event-driven HUD). TMP bundled in com.unity.ugui in 6.3.
  Independently reviewed (Unity mobile UI agent a4a6f1565d0bf7e1e): APPROVE
  WITH CONDITIONS; all 3 blockers folded — (B1) EventSystem/
  InputSystemUIInputModule, (B2) full perf checklist not just atlases+split,
  (B3) safe-area recompute timing — plus minors (match tradeoff, over-split
  caveat, Device Simulator, TMP-bundled confirmation).
- Registry: pending user approval to append ADR-0008 stances.
- DONE so far (8/11): 0001 RNG, 0002 physics, 0003 token storage, 0004
  currency, 0005 persistence, 0006 analytics, 0007 replay-resim, 0008 UI-system.
  REMAINING 3: shared-hub nav, boss-AI damage events, client cache/offline-queue.
- ADR-0008 registry stances APPENDED (6 forbidden patterns; 3 api_decisions;
  ui_toolkit_gate_trigger open-item).
- ADR-0009 "Shared Hub Navigation Architecture" — WRITTEN
  (docs/architecture/adr-0009-shared-hub-navigation.md). CLIENT-side Unity 6.3.
  Builds on ADR-0008 (UGUI). Decision: single HubNavigator current-screen
  authority; TWO GameObject roots — never-deactivated persistent-authority root
  (HubNavigator/auth-observer/coroutines) + deactivatable Hub-VIEW root
  (camera/EventSystem/AudioListener/UI); persistent Hub scene + mini-games
  loaded via Addressables ADDITIVE scene (local Addressables for MVP, remote =
  later config change), ONE resident, released on exit; modals = in-Hub UGUI
  panels (Hub underneath); cache-first non-blocking read-through aggregation
  (bg-fetch-fail → last-known cached); 300ms debounce (last-wins pre-commit) +
  nav-lock (ignore post-commit); auth-expiry → close modal+unload+route to
  login. Independently reviewed (Unity mobile agent a6f3469ac1c175338):
  APPROVE WITH CONDITIONS; all 3 blockers folded — (B1) additive-scene
  component conflicts: on mini-game activate DISABLE Hub camera/EventSystem/
  AudioListener + SetActiveScene(minigame), reverse on return; (B2) persistent-
  authority vs deactivatable-view two-root split so SetActive(false) doesn't
  kill HubNavigator coroutines; (B3) unload via Addressables.UnloadSceneAsync
  (stored handle) NOT SceneManager.UnloadScene (leaks) + release asset handles
  + Resources.UnloadUnusedAssets/GC settle (memory-freed claim was overstated)
  — plus minors (debounce-vs-lock phase split, mask activation frame via
  allowSceneActivation, backgrounding-mid-load, modal blocks mini-game tap).
- Registry: pending user approval to append ADR-0009 stances.
- DONE so far (9/11): 0001 RNG, 0002 physics, 0003 token, 0004 currency, 0005
  persistence, 0006 analytics, 0007 replay-resim, 0008 UI-system, 0009 hub-nav.
  REMAINING 2: boss-AI damage events, client cache/offline-queue.
- ADR-0009 registry stances APPENDED (interface HubNavigator; 4 forbidden
  patterns; 2 api_decisions).
- ADR-0010 "Client Cache & Offline Action Queue" — WRITTEN
  (docs/architecture/adr-0010-client-cache-offline-queue.md). CLIENT-side.
  RESOLVES the shared client_durable_local_storage_api open-item: a SINGLE
  embedded SQLite DB (maintained plugin) is THE client durable store for
  cached_state + analytics_buffer (ADR-0006) + offline_queue. Cache =
  last-known-good, read-first-then-reconcile, SERVER-WINS full overwrite,
  display-only (never authoritative for rewards). Offline queue = queue-if-
  idempotent(run-ID)-else-block, NEVER optimistic to cache, remove-AFTER-ack,
  server re-validates on reconnect (stale→reject not force), 24h + size cap,
  playerId-scoped. Tokens NOT here (Keychain/Keystore, ADR-0003). Independently
  reviewed (Unity+offline-sync agent a520b508c6db2f125): APPROVE WITH
  CONDITIONS; all 4 blockers folded — (B1) remove-after-ack exactly-once
  ordering; (B2) SQLite WAL + single serialized owned-connection access layer
  (analytics off-thread + cache/queue main → SQLITE_BUSY otherwise); (B3)
  guest→link PRESERVES progress (same playerId per account-auth.md, NOT
  discard — I'd wrongly lumped link with logout; corrected to link-preserve vs
  switch-discard); (B4) PRAGMA integrity_check on open → recreate corrupt DB —
  plus minors (inter-action independence assumption stated, queue size cap,
  result-shows-but-currency-pending UX).
- ADR-0010 registry stances APPENDED (interface sqlite_store shared with
  ADR-0006; client_durable_local_storage_api open-item RESOLVED).
- ADR-0011 "Boss Damage / Defeat Event Architecture" — WRITTEN
  (docs/architecture/adr-0011-boss-damage-events.md). Decision: BossDamageModel
  lives INSIDE SharedSimCore (not a MonoBehaviour) — deterministic, bit-
  reproducible, unspoofable boss HP/defeat state machine consuming
  ConsumeHitEvents() count (1 hit = 1 damage, decoupled from brick HP per
  boss-ai GDD Core Rule 1); win checked at frame boundary BEFORE danger-line
  loss (win-priority preserved); client renders HP bar/VFX from the same event
  queue, never authoritative; boss_defeated/reward events are server-emitted
  only (ADR-0006/0007 + ADR-0004 creditFlat), never client. Registry stances
  APPENDED (interface BossDamageModel; 4 forbidden patterns: client-
  authoritative boss HP, reading brick HP for damage, per-substep loss
  preempting frame-end win-check, client emitting boss_defeated).
- (GraphKG note, 2026-07-11: onboarded graphify on quack-studio via
  /graphify — 128 nodes/198 edges/10 communities from the 43 design+arch
  docs. While building it, caught that MY OWN tracking had gone stale after
  a context compaction: I believed ADR-0008-0011 were still pending when
  they were in fact already fully written AND registered before the
  compaction. Corrected here — do not re-litigate 0008-0011, they are done.)
- ALL 11 ADRs COMPLETE (11/11): 0001 RNG, 0002 physics, 0003 token storage,
  0004 currency, 0005 persistence, 0006 analytics, 0007 replay-resim,
  0008 UI-system (UGUI), 0009 hub-nav, 0010 client-cache, 0011 boss-damage.
  Every ADR independently reviewed by a fresh general-purpose subagent before
  finalizing; every registry stance appended with explicit user approval.
- /architecture-review still requires a FRESH session (independence rule) —
  not yet run, cannot run here.
- /test-setup COMPLETE (2026-07-11): tests/{unit,integration,smoke,evidence,
  EditMode,PlayMode}/ + .github/workflows/tests.yml (Unity 6.3.0f1, game-ci/
  unity-test-runner, UNITY_LICENSE secret needed manually before first CI
  run). Smoke list seeded with anti-cheat/determinism-specific checks
  (Tier-1-governs-reward, replay-within-tolerance). Gate still needs ONE
  example test file written before /gate-check technical-setup passes.
- GAP FOUND + FIXED (2026-07-11): /test-setup's "write one example test" step
  assumed a Unity Editor project already existed. It didn't — zero .cs files,
  no Assets/, no ProjectSettings/, no .asmdef anywhere. /setup-engine had only
  pinned the version + written engine-reference docs, never scaffolded the
  actual project. Flagged this honestly rather than writing an inert
  placeholder .cs file with nothing to compile it. User approved scaffolding
  the minimal project now.
- Scaffolded: ProjectSettings/ProjectVersion.txt (6000.3.0f1), Packages/
  manifest.json (test-framework, textmeshpro, 2d.sprite, inputsystem).
  FIRST REAL CODE WRITTEN: Assets/Scripts/SharedSimCore/{IDeterministicRng.cs,
  Pcg32Rng.cs, SharedSimCore.asmdef} — a real, compilable implementation of
  ADR-0001's interface (canonical PCG32 XSH-RR, all arithmetic unchecked).
  SharedSimCore.asmdef sets "noEngineReferences": true — this COMPILE-TIME
  excludes UnityEngine.dll, upgrading the UnityEngine.Random forbidden-pattern
  enforcement in the registry from "grep-check, not yet implemented" to
  "structurally unreachable" (registry updated to reflect this).
  tests/EditMode/{EditModeTests.asmdef, SharedSimCore/Pcg32RngTests.cs} — 5
  tests: same-seed-twice determinism, different-seeds sanity, reseed-
  reproducibility, float-range, non-degeneracy. DELIBERATELY does NOT
  hardcode a cross-platform "golden vector" (Unity-IL2CPP-build ==
  replay-verifier.exe) yet — I cannot execute C# in this session, and
  guessing a magic expected uint32 risks a test that's wrong for the wrong
  reason. Documented exactly what real vector to capture and where, once a
  real Editor + the CLI verifier both exist. This is honest partial progress
  on ADR-0001's test-vector requirement, not the requirement fully met.
- /test-setup gate requirement ("at least one example test file") now
  genuinely satisfied — not cosmetically.
- /ux-design COMPLETE (2026-07-11): design/ux/shared-hub.md — all sections
  written and approved. Key content: Shared Hub is a NON-VISUAL orchestration
  layer (GDD explicit: not the visual implementation, that's Hub UI) — so
  Layout Specification documents a render-stack/state-machine model instead
  of pixel zones/wireframe (deliberate deviation from the template, flagged
  as such rather than forcing a fake wireframe). New decisions/gaps surfaced
  during authoring (not in the GDD, confirmed as working defaults or flagged
  for follow-up, not silently assumed):
    - Android hardware/gesture back button: close modal if open, else
      standard OS behavior — confirmed default, needs a small GDD addendum.
    - Debounce vs. VoiceOver/TalkBack double-tap: flagged implementation
      risk, not resolved (AT activation could double-fire the 300ms debounce
      logic and hit the wrong target).
    - Proposed NEW analytics event `hub_navigation` (client-emitted per
      ADR-0006 ownership split) — no event for hub taps exists in
      analytics-event-tracking.md's catalog. Routed to Analytics GDD owner,
      not unilaterally added to the catalog.
    - No reduced-motion policy carried over from quack-blaster into this
      native pivot — flagged.
    - Cross-reference check: 2 new interaction patterns identified for a
      future interaction-patterns.md (debounced-navigation-tap,
      modal-open-pause-underneath) — no library exists yet to add them to.
  6 total Open Questions recorded in the spec itself.
- /ux-review design/ux/shared-hub.md — VERDICT: APPROVED (2026-07-11). 0
  blocking issues; 3 advisory (missing header Platform Target field, no
  resolution criterion — both FIXED; unresolved cross-team follow-ups
  [debounce-vs-AT-double-tap, unapproved hub_navigation event] correctly
  flagged not resolved, tracked in Open Questions not blocking this spec).
  Spec is ready for /team-ui Phase 2 handoff.
- /ux-design STARTED (2026-07-11): design/ux/hub-ui.md skeleton created.
  Task: Hub UI (home screen content) UX spec. Current section: Purpose &
  Player Need (about to author). This spec DOES have real visual layout
  (unlike Shared Hub) — hub-ui.md GDD gives header/games-section/stat-grid/
  quests-card/level-select/leaderboard/mascot-preview content, and art-bible
  gives the actual palette (Marquee Orange/Duck-Pond Teal/Bill Gold/Brick
  Red/Egg Cream + NEW Fern Green/Amethyst Purple for rarity) + typography
  (Bungee/Manrope/Space Mono) + a concrete colorblind-safety requirement
  (Brick Red vs Fern Green red-green confusion pair, must never be color-
  alone). GDD's own Open Q1 (2→5 hero-card layout: grid/scroll/paginated)
  is explicitly this spec's job to resolve.
- /ux-design COMPLETE (2026-07-11): design/ux/hub-ui.md — all sections
  written and approved. RESOLVED both of hub-ui.md GDD's own Open Questions:
  (1) mascot preview placement = right after Games section (early prominence
  for the new headline pillar); (2) games-section 2→5 scaling = horizontal-
  scroll strip (not grid/pagination — avoids orphaned rows, zero layout
  change per card count, natural touch gesture). Reused shared-hub.md's
  proposed hub_navigation event consistently rather than inventing a
  second one. 10 new component patterns flagged for the future pattern
  library. Real accessibility content this time (unlike Shared Hub's mostly
  N/A pass): 44x44pt/48x48dp touch-target minimum, 2 concrete color-
  independence cases (rarity thumbnails per art-bible's Brick-Red/Fern-Green
  confusion risk; locked tiles), screen-reader labels for icon-dense UI.
  Confirmed 1 new UX default not in the GDD: Logout requires confirmation
  before firing (destructive-adjacent action). Surfaced 2 real data-
  ownership gaps Shared Hub's spec didn't need to catch: level/progress
  summary and stat grid have no named owning system yet. 9 total Open
  Questions recorded.
- /ux-review design/ux/hub-ui.md — first pass NEEDS REVISION: 1 BLOCKING
  (locked hero-card interaction undefined — wireframe showed one, but
  Component Inventory + Interaction Map only covered unlocked cards,
  meaning as-written a player could tap into a game they hadn't unlocked;
  a real inconsistency, not a nitpick) + 1 ADVISORY (no safe-area/notch
  acceptance criterion). BOTH FIXED: locked-hero-card variant added
  (grayed art + unlock-requirement text, no-op tap w/ shake feedback,
  mirrors the locked-level-tile treatment) + safe-area criterion added.
  Verdict after fixes: APPROVED, 0 blocking, 0 advisory outstanding.
- DONE (2/many): shared-hub.md APPROVED, hub-ui.md APPROVED. Both ready
  for /team-ui handoff.
- /ux-design STARTED (2026-07-11): design/ux/account-auth.md skeleton
  created. Task: Account/Auth (login/register/guest/social/session-expiry)
  UX spec. Current section: Purpose & Player Need. This is a FLOW spec
  (multiple screens: Login/Register, Guest entry, social buttons, session-
  expired interstitial, account-linking moment), not a single screen — GDD
  explicitly punts several real UX decisions here: tabs-vs-toggle for
  Login/Register, Guest prominence, account-linking prompt trigger moment.
  Must match shared-hub.md/hub-ui.md's existing entry/exit points exactly
  (both already name Account/Auth as their entry source + logout/session-
  expiry destination).
- /ux-design COMPLETE (2026-07-11): design/ux/account-auth.md — all
  sections written and approved. This is a FLOW spec covering 3 moments
  (main entry, session-expiry interstitial, account-linking prompt), first
  spec that genuinely WRITES data (password/token/player-record) rather
  than read-only. Key decisions confirmed: Guest is the PRIMARY CTA (not
  equal-weight or de-emphasized) — toggle (not tabs) for Login/Register to
  keep it visibly secondary. 2 new analytics events proposed (auth_completed,
  account_linked) flagged as HIGHER priority than usual since they'd measure
  the GDD's own named data-loss risk (guest never links). 10 new component
  patterns flagged. Real accessibility content unique to this spec:
  password-manager/autofill semantic input types, screen-reader error
  announcement, show-password toggle. SURFACED A GENUINE GDD GAP not
  previously caught: NO password-reset/forgot-password flow exists anywhere
  in account-auth.md — routed back to the GDD owner as a real product/
  security gap, not invented here. 10 total Open Questions recorded.
- DONE (3/3 attempted): shared-hub.md, hub-ui.md, account-auth.md all
  written. shared-hub.md + hub-ui.md both /ux-review APPROVED (hub-ui.md
  needed 1 real fix first). account-auth.md not yet reviewed.
- /ux-review design/ux/account-auth.md — first pass NEEDS REVISION: 1
  BLOCKING (on-screen keyboard avoidance never addressed anywhere — the
  first spec with real text-entry fields, and a static layout risks the
  keyboard covering the password field/submit button, a very common real
  mobile-form bug; nothing in Layout/States/Interaction Map/Acceptance
  Criteria mentioned it) + 1 ADVISORY (no minimum touch-target size stated,
  inconsistent with hub-ui.md's own 44x44pt/48x48dp precedent). BOTH FIXED:
  keyboard-avoidance requirement added to Layout Zones + a new "Keyboard
  visible" state + a new acceptance criterion; touch-target minimum added
  to Accessibility referencing hub-ui.md's standard. Verdict after fixes:
  APPROVED, 0 blocking, 0 advisory outstanding.
- DONE (3/3): shared-hub.md, hub-ui.md, account-auth.md — ALL THREE
  /ux-review APPROVED. Every single one needed at least one real fix on
  first pass (Hub UI: locked-hero-card interaction gap; Account/Auth:
  keyboard avoidance) — the review step has genuinely earned its keep every
  time it's run this session, not just a formality.
- /ux-design STARTED (2026-07-11): design/ux/ricochet-hud.md skeleton
  created. Task: Ricochet HUD (real-time gameplay overlay for Super
  Ricochet) UX spec. Unusually complete/low-ambiguity GDD — exact layout
  (top bar/boss bar/playfield/footer), exact tween duration (0.12s),
  dirty-check behavior all pre-specified; GDD's own Open Questions = "None."
  Scope boundary: the playfield canvas itself is Super Ricochet's, out of
  scope here — only the HUD chrome around it. Must reuse hub-ui.md's
  currency-chip and level-pill component styling per the GDD's explicit
  instruction, not invent new ones.
- /ux-design COMPLETE (2026-07-11): design/ux/ricochet-hud.md — all
  sections written and approved. Real-time gameplay overlay (first non-
  menu spec) — different accessibility/data/animation concerns than the
  prior three: dirty-check performance constraint flagged as architectural
  not just UX; screen-reader continuous-narration-vs-on-demand-query
  tension caught (would make VoiceOver unusable during fast action);
  exit-confirmation-only-during-Firing decision (not during Aiming/Over);
  boss HP tween explicitly GDD-mandated so reduced-motion needs "shorter,
  not removed." Proposed 1 new event (run_abandoned). 7 new component
  patterns flagged.
  ⚠️ SELF-CAUGHT IP-RISK NEAR-MISS: while drafting the ASCII wireframe I
  used "Honktyson" (the flagged HAOPLAY-copied boss name) as example
  content — the FIRST time that name was ever casually written into
  quack-studio as if real (vs. prior deliberate citations of the risk in
  art-bible.md/adr-0011). Caught + fixed same turn (placeholder swapped to
  "[Boss Name]"), grep-verified clean, memory file project_quack_ip_risk.md
  updated with a "near-miss" entry + lesson (don't reuse quack-blaster
  proper nouns as filler/example content in quack-studio).
- ALL 4 UX SPECS DONE (shared-hub, hub-ui, account-auth, ricochet-hud) —
  3 of 4 already /ux-review APPROVED. ricochet-hud.md not yet reviewed.
- /ux-review design/ux/ricochet-hud.md — first pass NEEDS REVISION: 1
  BLOCKING (whether gameplay pauses during the "Quit run?" mid-Firing
  confirmation was never specified — genuinely consequential since Super
  Ricochet's physics runs on ADR-0002's fixed-timestep accumulator; if the
  sim kept running while the player decided, the boss could die or all
  balls could resolve before they answer, making the confirmation stale/
  misleading) + 1 ADVISORY (no safe-area/device-edge criterion, same class
  as Hub UI's but arguably more relevant since this HUD sits at the screen
  edges for the WHOLE play session). BOTH FIXED: pause decision stated
  explicitly (balls freeze, boss bar stops; Cancel resumes exactly, Confirm
  abandons) in Entry & Exit Points + States & Variants + 2 new acceptance
  criteria; Data Requirements gained an implementation cross-reference
  noting ADR-0002's existing MaxCatchUp spiral-of-death clamp is the
  natural mechanism to gate for this pause (not a new one to invent).
  VERIFIED after fixes: re-read full doc, confirmed no contradictions, ADR-
  0002 cross-reference is factually accurate. Verdict: APPROVED, 0 blocking,
  0 advisory outstanding.
- ALL 4 UX SPECS DONE AND APPROVED (2026-07-11): shared-hub.md, hub-ui.md,
  account-auth.md, ricochet-hud.md. Every single one needed at least one
  real BLOCKING fix on first /ux-review pass (locked-hero-card gap,
  keyboard-avoidance gap, sim-pause-during-confirmation gap) — 4 for 4, not
  a formality. Also this session: self-caught + fixed an IP-risk near-miss
  ("Honktyson" placeholder in ricochet-hud.md's wireframe) before it landed,
  logged to memory.
- design/ux/interaction-patterns.md COMPLETE (2026-07-11): consolidated all
  30 patterns flagged across the 4 approved specs (5 Navigation, 4 Input, 5
  Feedback, 11 Data Display incl. the pre-existing GDD-named currency-chip,
  5 CTA/Branding). Confirmed via Glob: Shop/Mascot Database/Quack Runner
  GDDs don't exist yet, so those pattern gaps are correctly blocked, not
  invented speculatively. No cross-pattern inconsistencies found — reuse
  (header-icon-action, text-cta-button, live-counter, progress-summary,
  interstitial-message) was already handled correctly during original
  authoring.
- docs/architecture/control-manifest.md COMPLETE (2026-07-11), via
  /create-control-manifest. Marked ⚠️ PROVISIONAL (prominent in header +
  Cross-Cutting Constraints) since all 11 ADRs are Status:Proposed, not
  Accepted — user explicitly chose to build it now anyway rather than wait
  for /architecture-review, with the caveat clearly stated (not silently
  pretending Accepted). Consolidated: 4 layers (Foundation/Core/Feature/
  Presentation) from ADR-0001–0011 + registry.yaml's 42 entries + technical-
  preferences.md (naming/perf budgets) + deprecated-apis.md (10 Unity 6.3
  forbidden APIs). Flagged real gap: technical-preferences.md's "Allowed
  Libraries" is still [TO BE CONFIGURED] — not silently filled in.
  Independently reviewed (agent a0d22c9aea0acb483, "full" review-mode gate):
  verified all 42 registry entries present + spot-checked actual ADR text
  (not just registry summaries) for ADR-0002/0007 + grep-confirmed the
  Proposed-status claim. APPROVE WITH CONDITIONS: 2 missing entries found
  and BOTH FIXED — (1) ADR-0003's ban on androidx.security
  EncryptedSharedPreferences (deprecated, and the ADR's own Risks section
  names this as the exact mistake a coding agent would make from stale
  training data); (2) ADR-0006's sessionId/session_end best-effort-only
  rule (load-bearing for KPI correctness). No fabricated rules, no
  dangerous omissions otherwise.
- /gate-check technical-setup-to-pre-production RUN (2026-07-11), full
  review mode, 4 independent director subagents spawned in parallel.
  Verdict: CONCERNS (not PASS) — 10/13 required artifacts present. 2
  missing artifacts are structurally blocked (requirements-traceability.md
  + /architecture-review itself both need a FRESH session, independence
  rule). 1 missing artifact is fully actionable now, no excuse:
  design/accessibility-requirements.md. NEW gap surfaced: design/player-
  journey.md doesn't exist — all 4 completed UX specs independently
  flagged this same absence in their own Open Questions (consistent
  signal). Director panel: Creative READY, Technical READY, Art READY,
  **Producer CONCERNS** — the one dissent, and it's substantive: "11
  mostly-backend ADRs + 4 UX specs + a 25KB control manifest authored for
  a core loop that has never been prototyped or playtested... premature
  architecture." (Producer also flagged a false "duplicate ADR-0002"
  collision — corrected in the written report: that's the REJECTED/
  superseded first draft, explicitly marked as such, not an unresolved
  collision.) Chain-of-verification checked whether this should be FAIL
  instead — confirmed CONCERNS is accurate since a vertical slice/prototype
  is NOT a required artifact for THIS specific gate (only for the next one,
  Pre-Production→Production, and even there it's "recommended not
  blocking"). Report written to production/gate-checks/technical-setup-to-
  pre-production-2026-07-11.md.
  USER DECISION: update stage.txt to "Pre-Production" anyway (gaps are
  structural/actionable, not fundamental — 3 of 4 directors said READY),
  then go straight to /vertical-slice next, directly answering the
  Producer's core concern rather than deferring it. stage.txt UPDATED.
- Next: /vertical-slice (in progress). Also still pending, not yet done:
  design/accessibility-requirements.md, design/player-journey.md. Separately,
  in a FRESH session (independence rule): /architecture-review, individual
  /design-review per GDD. Still-open: replay-verifier.exe CLI doesn't exist
  yet; PlayMode/ has no .asmdef yet; password-reset flow gap in account-
  auth.md; 4 proposed analytics events unapproved by an Analytics GDD owner;
  project-wide reduced-motion policy undecided; technical-preferences.md's
  Allowed Libraries unconfigured.
- /vertical-slice STARTED (2026-07-11). review-mode=full (from
  production/review-mode.txt) — Phase 7 Creative Director review WILL run
  after the report, unlike the lean-mode ADR/UX work so far.
  Concept: super-ricochet. Validation question: does a player feel the
  "Ready, Aim, Fire!" fantasy (precision+chaos+escalating tension) within
  2-3 min with no guidance, and can SharedSimCore's fixed-point physics
  design (ADR-0002) actually be built + feel good at representative
  quality in a bounded session?
  REAL TENSION SURFACED + RESOLVED (not silently worked around): the
  control manifest states, sourced from ADR-0002, "Do not begin Super
  Ricochet gameplay implementation before this spike passes" (the on-device
  ARM64-IL2CPP==x86-CLI byte-identical proof — impossible in this session,
  no physical device/build pipeline). Resolution, confirmed by user via
  AskUserQuestion: that rule targets PRODUCTION implementation; this
  skill's own constraints already forbid ever refactoring slice code into
  production (reference-only, written from scratch later). So the slice
  builds ADR-0002's fixed-point design faithfully (tests whether the
  design is implementable + fun) but explicitly does NOT attempt or claim
  to satisfy the spike gate — that stays open, still blocks ADR-0002
  reaching Accepted, still blocks real production Ricochet code.
  Scope confirmed (user picked "Confirm as proposed"): Super Ricochet core
  loop only — grid/aim-fire/Q16.16 sub-stepped collision (ADR-0002),
  Boss AI 1-hit-1-damage + win-before-loss-priority (ADR-0011/boss-ai-
  damage-model.md), Pcg32Rng-seeded brick HP/spawn rolls (ADR-0001), ONE
  hardcoded level-1 config (bossHp=800, initialRows=4, maxBrickHp=6,
  spawnDensity=0.45, startingBalls=3 — level-difficulty-config-ricochet.md).
  Placeholder HUD (ball count, boss HP bar/name, win/loss banner) and
  placeholder art (primitive shapes). OUT: Currency/Anti-Cheat/Analytics/
  Save (no server round-trip, reward shown not persisted), Hub/Account/
  Auth (boots straight into the Ricochet scene), multi-level progression,
  mascots/quests/shop.
  Success criteria: full Ready->Aiming->Firing->Over cycle playable no-
  crash in Editor; no tunnelling (matches super-ricochet.md AC1); boss HP
  -1/hit exactly, defeat instant + wins priority over same-frame danger-
  line loss (matches boss-ai-damage-model.md's 5 ACs); same seed replayed
  twice in-Editor produces identical brick layout/RNG outcomes (spot-check
  determinism, NOT the full cross-platform spike); playtester (user)
  reports whether the fantasy lands.
  Budget: session-based build-iteration checkpoints (this environment has
  no literal calendar days) — reassess at iteration 3 if the loop isn't
  demonstrable yet, matching the skill's day-3 sunk-cost rule in spirit.
  Current phase: Phase 4 — Implement. FIRST BUILD WRITTEN (2026-07-11):
  Assets/Scripts/SharedSimCore/{Fix32.cs, IDeterministicPhysics2D.cs,
  BossDamageModel.cs, RicochetSim.cs} (engine-free scored sim, implements
  IDeterministicPhysics2D faithfully, Q16.16 fixed-point per ADR-0002
  incl. distance-substepping + IntSqrt-for-aim-normalization-only,
  BossDamageModel per ADR-0011 win-before-loss-priority) +
  Assets/_VerticalSlice/Scripts/RicochetSliceController.cs (Unity view/
  input, plain UGUI Image rects - no sprite/Rigidbody2D/prefab deps,
  legacy Input.* for drag-to-aim, builds its entire UI tree at runtime) +
  Assets/_VerticalSlice/Scenes/SuperRicochetSlice.unity (hand-authored
  scene YAML - Main Camera + one bootstrap GameObject; flagged to user as
  the single highest-risk artifact since no Unity Editor was available to
  verify it, with a documented 30-second fallback if it fails to open) +
  prototypes/super-ricochet-vertical-slice/README.md (documents the
  Assets/-vs-prototypes/ structural split this Unity project needs vs.
  the skill's generic engine-agnostic template, + all deliberate scope
  cuts). NO .NET SDK available in this session (checked both dotnet CLI
  and csc) - none of this C# has been compiled. Told the user first-round
  Console compile errors are expected, to paste them back for fixing.
  Waiting on user's first Editor playtest round (skill's Phase 4
  multi-turn loop: user runs -> reports errors/observations -> fix ->
  repeat until the full loop cycle is demonstrable).
- User asked me to install/run Unity myself (2026-07-11). Installed Unity
  Hub via winget (succeeded) + Unity 6000.3.19f1 via Hub CLI (this
  machine's Hub only listed 6000.3.19f1 as available, not the project's
  exact 6000.3.0f1 pin - same LTS line, flagged not silently substituted).
  Ran the project through `Unity.exe -batchmode -nographics -quit` to
  check compilation. BLOCKED: not a license issue - Editor bootstrap
  fails because Data/Resources/PackageManager/Server/
  UnityPackageManager.exe is entirely missing (whole Server/ folder
  empty) even though the rest of the ~3.7GB install looks otherwise
  intact. Reproduced identically after a reinstall attempt. Hypothesis
  (not confirmed): CrowdStrike Falcon EDR is present on this machine and
  quarantined the freshly-written helper exe - a common pattern for
  small internal executables from a fresh install. Did NOT attempt to
  probe/adjust antivirus/EDR settings - out of scope to do unilaterally
  on what may be a managed machine. User chose to install/run Unity
  themselves via the Hub GUI instead (may behave differently than the
  CLI path that hit this) rather than have me keep fighting it
  autonomously. Still waiting on the user's first Editor playtest round.
- FOUND + FIXED (2026-07-11): user couldn't find SuperRicochetSlice.unity
  in Unity Hub's project browser / Explorer despite the file genuinely
  existing on disk. Root cause confirmed via PowerShell: the whole
  project lives under OneDrive with Files On-Demand active, and every
  file in it (127 total) had the ReparsePoint attribute (OneDrive cloud
  placeholder) without the Pinned attribute - meaning some tooling that
  enumerates without triggering hydration (Hub's folder picker, a stale
  Explorer view) could see files as absent/empty even though a full read
  (Get-Content) successfully hydrated and returned real content. This
  would have kept causing intermittent issues throughout Editor use, not
  just this one scene, since Unity reads/writes across the whole Assets
  tree. FIXED: ran `attrib -U +P <project> /S /D` to pin the entire
  project folder; re-verified recursively across all 127 files - 0
  remain cloud-only/unpinned. User should retry adding the project to
  Unity Hub now.
- Editor install saga continued (2026-07-11): hit "already installed"
  blocking a reinstall to a user-owned path; wrong "Scenes" folder got
  opened as its own Unity project twice, contaminating Assets/
  _VerticalSlice/Scenes/ with Temp/Library/Logs/Assets - cleaned up both
  times, scene + meta remade fresh from source on request, re-verified
  (byte-identical sizes, GUID cross-ref intact, now fully Pinned with no
  ReparsePoint flag at all). Tried 3 independent install mechanisms total:
  Hub CLI -> Program Files (partial install, missing PackageManager
  Server), Hub CLI -> user path (blocked, "already installed"), standalone
  offline NSIS installer (found real changeset 4e8d7afad3cd via the
  release page, downloaded 3.76GB directly, ran silently) -> user path
  (installed 2.3GB, STILL missing the exact same file, confirmed via full
  recursive search). Identical failure across 3 totally different
  installer code paths for the SAME specific file
  (Data/Resources/PackageManager/Server/UnityPackageManager.exe) is
  strong evidence of a targeted block (likely EDR signature match on that
  file specifically, given its job as a local IPC server that opens ports
  + spawns as a child process) rather than a generic permissions/download
  problem. Diagnostic tool run twice by user confirmed network/connectivity
  is fully healthy (15/20 checks pass, 148Mbps) - rules out a bad
  download. USER DECISION (2026-07-11): disregard the EDR angle entirely,
  move on. Vertical slice Phase 4 (user playtest loop) marked PAUSED, not
  abandoned, not marked done - task #34 stays in_progress. Written code
  (Assets/Scripts/SharedSimCore/*, Assets/_VerticalSlice/*) is complete,
  self-verified (brace-balanced, byte-exact, GUID cross-referenced) and
  ready to test whenever Unity can actually launch on this machine.
  PIVOTING to design/accessibility-requirements.md next - the gate-check's
  only OTHER fully-actionable-now gap (unlike requirements-traceability.md
  /architecture-review, which need a fresh session). Does not depend on
  Unity being runnable.
- design/accessibility-requirements.md COMPLETE (2026-07-11). Grep-first
  approach: pulled every existing accessibility/colorblind/reduced-motion/
  screen-reader/touch-target mention across all 4 approved UX specs +
  art-bible.md + interaction-patterns.md BEFORE drafting, so this doc
  consolidates and formalizes what's already consistent practice rather
  than inventing fresh rules that could contradict already-approved specs.
  Committed tier: "AA-equivalent for mobile touch games" (not a downgrade
  to token "Basic" - the 4 specs already wrote AA-level content as their
  working default; formalized what was already true instead of
  contradicting approved work). Consolidated: 4.5:1/3:1 contrast, never-
  color-alone (traces to art-bible's Brick Red/Fern Green warning), 44x44/
  48x48 touch targets (already the de facto standard across all 4 specs),
  screen-reader labels + transition announcements, the on-demand-query
  (not continuous narration) pattern for real-time HUD stats first
  reasoned through in ricochet-hud.md - promoted here to the general
  project rule. ACTUALLY RESOLVED the one gap all 4 specs flagged and none
  closed: reduced-motion policy (OS-signal + in-app toggle; decorative
  animations fully removed, GDD-mandated feedback like the boss-HP tween
  shortened not removed, per ricochet-hud.md's own reasoning generalized).
  Explicit Non-Goals section: keyboard/gamepad N/A (touch-only, already
  decided in account-auth.md), full WCAG 2.2 certification audit deferred
  to Polish. Genuinely unresolved item carried forward, not papered over:
  AT double-tap vs. HubNavigator's 300ms debounce (needs real
  engineering validation once input-handling code exists). No dedicated
  review skill exists for this doc type (unlike /ux-review for UX specs) -
  self-verified via the grep-first cross-check already embedded in the
  doc's own Consistency Check section rather than forcing an artificial
  subagent review.
- Next candidate: design/player-journey.md - the other gap the gate-check
  flagged, independently raised in all 4 UX specs' own Open Questions.
  Also does not depend on Unity.
- design/player-journey.md COMPLETE (2026-07-11). CAUGHT + FLAGGED a small
  provenance error before writing: shared-hub.md claimed a template
  existed at .claude/docs/templates/player-journey.md - checked via Glob,
  it doesn't exist and never did. Noted honestly in the doc header rather
  than silently pretending to follow a template that isn't real. Authored
  6 journey phases grounded in the 4 approved specs' actual entry/exit
  points + GDD signals rather than invented content: First Launch (Guest-
  primary per account-auth.md, warmer first-time transition), First Core
  Loop (Super Ricochet lvl 1, ties to currency-system.md's deliberate 0/0
  starting balance so the first reward is EARNED not GIVEN), Early
  Retention (daily quests/login streak formulas from game-concept.md,
  surfaces the coin/gem sink-exhaustion gap at the exact phase a
  consistent player would feel it), Building the Collection (BLOCKED -
  Mascot DB has no GDD), Habitual Engagement (BLOCKED - Runner/3 games
  have no GDDs, surfaced as the single largest gap: the "5-game
  collection" pillar currently has real design behind exactly 1 of 5
  games), and a cross-phase Churn Risk section (guest-never-links
  compounding risk, currency sink exhaustion, single-mini-game ceiling,
  no password-reset flow - all traced to gaps already flagged elsewhere,
  not newly invented, but never before collected side-by-side). Cross-
  Spec Consistency Check section verified this map's assumptions against
  all 4 specs' own Player-Context-on-Arrival claims - 0 contradictions
  found; one genuine NEW connection surfaced (hub-ui.md's inviting-empty-
  state requirement and currency-system.md's deliberate 0/0 balance were
  never cross-referenced to each other before, despite both describing
  the same first-launch moment).
- BOTH gate-check-flagged actionable gaps now closed (accessibility-
  requirements.md + player-journey.md). Remaining pending work is either
  structurally blocked (requirements-traceability.md/architecture-review
  need a FRESH session) or blocked on Unity actually running (vertical
  slice Phase 4 playtest) or blocked on undesigned systems (Mascot DB,
  Shop, Runner, 3 new mini-games - all surfaced again independently by
  player-journey.md's own Phases 4-5).
- /design-system mascot-database STARTED (2026-07-11), review-mode=full,
  picked over Runner as the next system since it's an explicit pillar
  blocking TWO already-approved artifacts (hub-ui.md's mascot preview,
  player-journey.md Phase 4). Skeleton at design/gdd/mascot-database.md.
  Sections WRITTEN + approved so far: Overview (data layer = shape/logic
  of JSONB player_state per ADR-0005, not a new table), Player Fantasy
  (creative-director consult: "Crew" reveal-moment tone + "Scrapbook"
  gallery framing combined; rarity = flair not power, hard line vs.
  no-pay-to-win pillar), Detailed Design (economy-designer + systems-
  designer consults: 3 tiers Common/Rare/Epic matching art-bible's
  existing 2-color scheme exactly; 16-mascot MVP roster across 5
  milestone categories - First Contact/Progression Gates/Mastery Repeats/
  Consistency Streaks/Capstone; 100% DETERMINISTIC acquisition, zero
  randomness, explicit rejection of gacha mechanics per Player Fantasy;
  grants ride the EXISTING Tier-1/ADR-0007 pipeline + compose into the
  SAME transaction as mutateWallet sharing its idemKey - no new anti-
  cheat mechanism; N:1 unlockConditions w/ intentional:true collision
  guard), Formulas (collection_completion_percent trivial formula;
  progression_gate_level as a lookup table pegged to ricochet's existing
  difficulty bands, not invented ones; mastery_repeat_gap = +12 levels
  guaranteeing 2 boss cycles; explicitly states NO probability formula
  exists, contrasting the system's own "Rarity Logic" name), Edge Cases
  (fresh systems-designer consult targeted at the FINAL written text, not
  general architecture - caught a REAL CONTRADICTION between Core Rule 6
  "never revoked" and the Anti-Cheat Interactions text implying Tier-2
  fraud-review revocation authority; FIXED by patching Core Rule 6 itself
  with an explicit confirmed-fraud-only exception, not just noting the
  tension - also resolved roster-change edge cases: deprecated mascots
  grandfathered but excluded from completion% denominator/numerator both,
  new mascots surfaced not silently dropping completion%, simultaneous
  multi-milestone grants all fire in one transaction, Capstone evaluated
  in a second pass so same-run completion works, Consistency Streak
  explicitly inherits Login Streak's future UTC-day definition as a
  forward contract). Registry (design/registry/entities.yaml) does not
  exist yet - this will be the FIRST GDD to seed it, in Phase 5b.
  Remaining sections: Dependencies, Tuning Knobs, Visual/Audio (REQUIRED
  - Character systems category), UI Requirements, Acceptance Criteria,
  Open Questions.
- design/gdd/mascot-database.md COMPLETE (2026-07-11), all 12 sections
  written + approved. Remaining sections done: Dependencies (hard: Save/
  Persistence, Anti-Cheat; soft: Super Ricochet/Boss AI, Currency; soft-
  provisional: Daily Quests/Login Streak, no GDDs yet), Tuning Knobs (5
  knobs, all explicitly placeholder pending /balance-check), Visual/Audio
  (art-director consult: reveal beat fires on HUB RETURN not mid-run,
  greeting-not-jackpot framing, identical structural beat across all 3
  tiers - explicitly rejected any tier getting a longer runway/delay/
  countdown since that's where gacha tension would creep back in;
  locked-slot silhouette shows rarity border, NOT the art bible's
  desaturated loss treatment; 16 mascots fit the art bible's existing
  2-material-slot budget via shared rig + swappable outfit), UI
  Requirements (Mascot Collection screen scoped, UX Flag issued for
  future /ux-design), Acceptance Criteria (qa-lead consult, 16 GIVEN/
  WHEN/THEN criteria incl. one directly testing "flair not power" is
  literally true not just stated; qa-lead HONESTLY flagged 2 coverage
  gaps - Consistency Streak criteria blocked on undesigned Daily Quests/
  Login Streak, "surfaced in-product" half of the roster-growth edge case
  blocked on a future UX spec - both carried into Open Questions rather
  than forced), Open Questions (5 items, all real: 2 blocked-on-other-
  systems, 1 blocked-on-asset-spec, numeric-placeholders restated as the
  single most consequential item, roster-exhaustion retention risk
  named as mitigated-not-solved).
  CD-GDD-ALIGN final review (full review-mode, fresh independent pass
  against the COMPLETE doc, not a rubber-stamp of the earlier Player-
  Fantasy-shaping consult): APPROVED WITH CONDITIONS - caught a real
  ambiguity where Player Fantasy's "mascots will eventually be gem/real-
  money-priced" could be read as contradicting Core Rule 3's
  deterministic-only acquisition; game-concept.md's actual Shop scope is
  cosmetic SKINS, not mascots themselves. FIXED same pass (one-line edit
  clarifying "mascot cosmetic skins"). Status header updated to Designed
  + records the verdict.
  REGISTRY CORRECTION (important, transparently caught + fixed): earlier
  Glob check falsely reported design/registry/entities.yaml doesn't
  exist. It DOES exist with 28 real prior entries (account-auth, save-
  persistence, analytics, currency-system, anti-cheat, shared-hub, level-
  difficulty-config-ricochet, super-ricochet, ricochet-hud) - genuine
  earlier-session work my Glob check missed for unclear reasons. Told
  the user directly rather than silently overwriting it. Correctly
  APPENDED (not replaced) 6 new entries matching the file's EXISTING
  schema exactly (flat entities: list, type:constant|formula, source:
  kebab-case-system-name, referenced_by: []) instead of the different
  schema I'd initially drafted before discovering the real file:
  collection_completion_percent, progression_gate_level,
  mastery_repeat_gap, mvp_mascot_roster_size, mascot_rarity_tiers,
  mascot_milestone_taxonomy.
  systems-index.md updated: Mascot Database -> "Designed... pending
  independent /design-review in a fresh session."
  Task #37 marked completed.
- User picked "Design Quack Runner next" (2026-07-12). Skill's own
  dependency check surfaced Quack Runner depends on undesigned "Obstacle
  Spawn/Difficulty Ramp (Runner)" - same situation Super Ricochet was in
  before Level/Difficulty Config (Ricochet) got designed first. User
  chose to mirror that precedent: design the ramp system FIRST (task
  #39), Quack Runner itself after (task #38, still pending).
- design/gdd/obstacle-spawn-difficulty-ramp-runner.md COMPLETE
  (2026-07-12), all 12 sections, review-mode=full. KEY MOVE: read the
  ACTUAL prototype source (quack-blaster/client/src/game/runner.ts)
  directly rather than trust game-concept.md's paraphrase - confirmed
  the stated obstacle mix/speed/interval formulas exactly, but also
  surfaced 2 things the paraphrase missed: (1) ALL THREE non-coin types
  (bomb/bird/cloud) deal damage, not just bombs; (2) there's a SECOND RNG
  roll for lateral spawn position the systems-designer had flagged as
  unverified - confirmed real from source.
  Player Fantasy (creative-director consult): "the game raising the bar
  because you're clearing it" - deliberately distinct from Ricochet's
  own escalating-pressure fantasy so the two mini-games don't deliver
  the same tension two ways; "dancing to a song that keeps speeding up"
  borrowed as tone/audio flavor language without committing to literal
  beat-synced mechanics.
  Core Rules (systems-designer consult): VERIFIED via direct grep of
  anti-cheat-replay-verification.md that Runner's deterministic-replay
  requirement was ALREADY a binding commitment from an earlier GDD (line
  118 + an explicit Tier-1 placeholder formula deferred to this GDD) -
  not a fresh decision. Formulas carried over exactly from runner.ts
  (obstacleSpeed, spawnInterval as pure functions, not incremental
  mutation - matching what the systems-designer independently
  recommended before I'd even shown them the source). Two seeded
  Pcg32Rng rolls per spawn (type + position).
  Formulas (focused systems-designer consult): derived + independently
  VERIFIED BY HAND (re-did the calculus myself, confirmed the integral,
  the t=27 phase-boundary continuity, and the t=30 worked example) a
  rigorous Tier-1 maxPlausibleScore(t) ceiling - uses the coin-maximizing
  upper bound (never false-positive-rejects a lucky legitimate run) since
  Tier-1 doesn't replay the actual RNG sequence, correctly deferring
  "did this specific sequence happen" to Tier-2.
  Edge Cases (fresh systems-designer consult against final text): found
  + FIXED two real problems in already-written sections, not just
  documented them - (1) Core Rule 4 didn't pin the RNG roll ORDER
  (type-then-position vs position-then-type), a genuine determinism bug
  since an unordered spec lets client/server each validly diverge and
  silently desync EVERY run's replay, not just malformed ones - patched
  Core Rule 4 directly; (2) Formulas declared t as "int" without
  clarifying real vs integer division, a classic C# bug (t/30 truncates
  to 0 for t<30 under integer division, silently breaking the whole
  ramp) - added an explicit Implementation Note. Also wrote 4 real edge
  cases (malformed-t clamping, extreme-t DoS/CPU protection via
  closed-form summation instead of a per-claimed-t loop, coin-before-
  death same-tick collision ordering, verified t=27 boundary continuity).
  Acceptance Criteria (qa-lead consult + 3 self-added criteria for all 3
  gaps qa-lead honestly flagged - t>=75 branch, distribution-tolerance
  test, edge-clamping test): 17 total GIVEN/WHEN/THEN criteria.
  CD-GDD-ALIGN final review: APPROVED WITH CONDITIONS - both conditions
  folded directly into Open Questions #1 and #3 as binding
  commitments/gates (Pillar 4's "elevated" claim stays unearned until
  Quack Runner's own future GDD delivers progression/currency/
  leaderboard parity; native-port feel of the unbounded speed curve is a
  required gate, not optional, before that future GDD locks).
  Registry: appended 5 new entries (obstacleSpeed, spawnInterval,
  maxPlausibleScore, runner_obstacle_type_mix, runner_health) to the
  real entities.yaml using its correct existing schema.
  systems-index.md updated: Obstacle Spawn/Difficulty Ramp (Runner) ->
  Designed. Task #39 marked completed.
- Next: task #38, Quack Runner itself (the actual gameplay GDD), now
  unblocked since its dependency is designed. Two items explicitly
  routed to it from the ramp GDD's Open Questions: progression/currency
  interaction decision, leaderboard scope decision.
- design/gdd/quack-runner.md COMPLETE (2026-07-12). Full review-mode, all
  12 sections. Resolved both items routed from the ramp GDD: (1) coins
  share Ricochet's Coin Value upgrade via the existing creditMultiplied
  leg rather than a new progression track (economy-designer: excluding
  Runner would make it feel second-class exactly when the pillar wants
  elevation); (2) Runner gets its own separate runnerBestScore
  leaderboard, not a unified cross-game score (the two score formulas
  are structurally incomparable). 9 Core Rules incl. touch/drag-only
  movement (no keyboard, unlike the prototype), sim-tick-authoritative
  collision inside SharedSimCore, coin vs non-coin collision handling,
  dodge bonus, strict score/currency separation (dodge bonus never
  credited as currency), gems deferred (coins-only for MVP). Formulas:
  coinCredit (1:1 into creditMultiplied), dodgeBonus(t)=10+floor(t/5),
  runnerLeaderboardScore (leaderboard-only, distinct from currency
  value). Reused ADR-0010's "client cache is display-only, never
  authoritative for rewards" pattern directly for the live-coin-tally-
  vs-server-verified-count reconciliation rather than inventing a new
  mechanism. Resolved a real cross-GDD coordinate-unit mismatch: the
  ramp GDD's obstacleSpeed(t) is raw pixels/sec from the prototype's
  600px canvas, but Runner's own Core Rules mandate normalized 0-1
  coordinates — added normalizedObstacleSpeed(t) = obstacleSpeed(t)/600
  to Runner's own Edge Cases as the actual consumer, without changing
  the sibling GDD's curve.
  CD-GDD-ALIGN review: APPROVED WITH CONDITIONS, 3 conditions, ALL FIXED
  same pass — C1: Open Question 5 had mislabeled the ramp GDD's inherited
  binding readability-gate condition (obstacle traversal time at t≈120s
  drops to ~0.4s, below the 0.6s spawn floor — an unreadable-spike risk)
  as if it were about the coin-vs-dodge value ratio; rewritten to state
  the real condition and split the ratio question into its own item.
  C2: Edge Cases mis-cited "Core Rule 5" (Successful Dodge) for
  normalized coordinates; corrected to Core Rules 1+2 (movement/
  collision). C3: added new Open Question 6 stating explicitly that the
  prototype's 100-coin/run cap is removed entirely and Runner becomes a
  second uncapped, Coin-Value-multiplied coin faucet — worsening
  game-concept.md's already-open coin-sink gap, not a new problem but
  now explicitly named.
  Acceptance Criteria: 19 GIVEN/WHEN/THEN, qa-lead-validated full
  coverage. Registry: 3 entries appended to entities.yaml (dodge_bonus,
  runner_leaderboard_score, runner_coin_credit) — registry now has 42
  total entries. systems-index.md updated: Quack Runner -> Designed —
  CD-GDD-ALIGN APPROVED WITH CONDITIONS (3 conditions folded in same
  pass), pending independent /design-review in a fresh session. Task
  #38 marked completed.
  Two AskUserQuestion approval prompts were dismissed mid-flow
  (Acceptance Criteria approval, systems-index update approval) — both
  times work paused immediately per instruction; user then invoked
  /anthropic-skills:tech-lead-orchestrator (treated as sufficient signal
  to proceed with my own top recommendation per the "Do As Recommended"
  standing preference) and later typed "1" to authorize the systems-
  index update specifically.
- ALL THREE queued /design-system GDDs now complete and CD-approved:
  mascot-database.md, obstacle-spawn-difficulty-ramp-runner.md,
  quack-runner.md. Registry stands at 42 entries. Next recommended step
  (per the tech-lead-orchestrator response): /consistency-check — the
  cheapest moment to catch cross-GDD drift, since all three just added
  registry entries in the same session. After that, continue through
  the remaining Vertical-Slice-tier undesigned systems (Currency Ledger/
  Transaction Log, Daily Quests, Login Streak, Mascot Gallery/Equip UI,
  Runner HUD, Leaderboard) in dependency order. Structurally blocked
  here (need a fresh session, independence rule): /architecture-review,
  individual /design-review per GDD (now including all three of
  mascot-database.md, obstacle-spawn-difficulty-ramp-runner.md,
  quack-runner.md). Vertical slice Phase 4 user-playtest loop stays
  PAUSED (Unity/EDR install blocker, task #34 in_progress, not
  abandoned) per explicit user instruction to disregard that issue.
<!-- CONSISTENCY-CHECK: 2026-07-12 | GDDs checked: 14 | Conflicts found: 0 | Report: docs/consistency-report-2026-07-12.md -->
- Fixed 2 stale "(future GDD)" prose references to Quack Runner in
  obstacle-spawn-difficulty-ramp-runner.md (Interactions + Depended-on-by
  sections) now that quack-runner.md is designed. Text-only, no value
  changes, flagged by the consistency-check report but not a registry
  conflict.
- /design-system Currency Ledger/Transaction Log STARTED (2026-07-12,
  task #40, review-mode=full). Skeleton created at
  design/gdd/currency-ledger-transaction-log.md. Key context gathered
  before drafting: ADR-0004 already locked the `currency_ledger`
  table schema (op_id, currency, delta, resulting_balance,
  multiplier_applied, created_at) + `currency_op` idempotency table,
  written transactionally inside Currency System's `mutateWallet` —
  confirmed via ADR-0004/ADR-0005/control-manifest.md. This GDD's job
  is the design/product layer on top (audit trust, dispute resolution,
  anti-cheat cross-reference, possible player-facing history) — NOT
  re-deriving the persistence schema, which is already Accepted-track
  architecture. Currency System's own GDD already lists "Depended on
  by (hard): ... Currency Ledger" — reciprocal dependency confirmed.
  Anti-Cheat/Replay Verification's GDD does NOT yet list Currency
  Ledger in its Dependencies (systems-index's "pairs with anti-cheat
  hardening" note is aspirational, not yet a formal bidirectional
  link) — flagged as a live design question for Core Rules, not
  assumed either way. No player-facing "transaction history" UI
  exists anywhere yet — also an open design question, not assumed.
- design/gdd/currency-ledger-transaction-log.md COMPLETE (2026-07-12).
  Full review-mode, all 12 sections + Visual/Audio and UI Requirements
  correctly marked N/A (pure infrastructure, no player-facing surface —
  confirmed by creative-director's "Witness" Player Fantasy framing).
  5 Core Rules: (1) 3 enumerated read-only reader classes, players never
  get direct access; (2) Anti-Cheat gains a formal hard read-dependency
  via `getPlayerLedger(playerId, since, limit)`, only on human-reviewer
  escalation, not every mismatch; (3) retention forever, no pruning;
  (4) account deletion anonymizes the player link on BOTH `currency_op`
  and the new denormalized `currency_ledger.player_id` together — a
  real self-contradiction was caught and fixed mid-session (an earlier
  draft only anonymized `currency_op`, which would have left an
  un-anonymized, queryable link on `currency_ledger`, defeating the
  GDPR erasure the rule exists to satisfy); (5) 3 narrow, named query
  functions only — no raw table access for any reader, including
  Anti-Cheat.
  Real architecture gap surfaced, not silently assumed: `currency_ledger`
  as ADR-0004 currently specifies it has no `player_id` column (only
  `op_id`, requiring a join through `currency_op`) — this GDD flags a
  required ADR-0004 schema addendum (denormalize `player_id` onto
  `currency_ledger` + a `(player_id, created_at)` composite index,
  written in the same `mutateWallet` transaction) as Open Question 2, a
  concrete next action, not yet authored.
  One process note: a systems-designer subagent I'd spawned wrote its
  Core Rules draft directly to the file instead of returning it for the
  approval step this skill requires (it had Edit-tool access and used
  it) — flagged to the user transparently rather than silently treating
  it as final; the content was then revised (folding in a backend
  reviewer's `player_id`/narrow-API findings) and put through the normal
  approval gate before being kept.
  qa-lead pass found 3 real gaps in the first Acceptance-Criteria draft
  (tuning-knob enforcement behavior unspecified, backfill-completion
  signal not concretely testable, Anti-Cheat's 3-flags/7-days threshold
  hardcoded here instead of phrased as inherited) — all 3 fixed before
  the section was approved (clamp-vs-reject behavior now specified per
  knob; backfill-complete redefined as a concrete `COUNT(*) WHERE
  player_id IS NULL = 0` check; threshold phrasing corrected).
  CD-GDD-ALIGN final review: APPROVED WITH CONDITIONS, 2 conditions,
  BOTH FIXED same pass — C1: `getPlayerLedger`'s signature was
  inconsistent between Core Rule 5(a) (`playerId, dateRange`) and Core
  Rule 2/Acceptance Criteria (`playerId, since, limit`) — unified to the
  latter, since Tuning Knobs' pagination only makes sense with an
  explicit `limit`; C2: clarified in Open Question 2 that the GDD's
  index suggestion is a requirement for the future ADR addendum to
  satisfy, not a physical-schema spec to copy verbatim.
  Fixed the one-directional dependency gap Core Rule 2 creates: also
  updated `anti-cheat-replay-verification.md`'s own Dependencies section
  to list Currency Ledger reciprocally, in the same pass (project
  convention).
  Registry: appended 3 new entries (currency_ledger_retention,
  ledger_query_page_size, ledger_reconciliation_date_range_cap) —
  registry now at 46 total entries. systems-index.md updated: Currency
  Ledger/Transaction Log -> Designed — CD-GDD-ALIGN APPROVED WITH
  CONDITIONS (2 conditions folded in same pass), pending independent
  `/design-review` in a fresh session. Task #40 marked completed.
- Undesigned systems remaining in the Vertical-Slice tier (priority
  order per systems-index.md): Daily Quests, Login Streak, Mascot
  Gallery/Equip UI, Runner HUD (Per-Mini-Game HUD, Runner instance),
  Leaderboard. Structurally blocked here (fresh-session independence
  rule): /architecture-review, individual /design-review per GDD (now
  including currency-ledger-transaction-log.md). Vertical slice Phase 4
  user-playtest loop stays PAUSED (Unity/EDR blocker, task #34
  in_progress, not abandoned).
- design/gdd/daily-quests.md COMPLETE (2026-07-12). Full review-mode, all
  12 sections. User asked mid-session for options to progress faster;
  after presenting the lean-vs-full tradeoff (fewer agent spawns/no
  CD-GDD-ALIGN vs. catching self-contradictions like this session already
  found twice), user chose to KEEP full mode — recorded so this isn't
  re-litigated on the next system.
  Resolved the systems-index-flagged shared-mini-game-result-schema risk
  WITHIN this GDD rather than blocking on a separate ADR: defined a
  narrow abstracted event set (bricksDestroyed, coinsCollected,
  bossDefeated, runCompleted) that any mini-game's own result shape
  (RunResult, RunnerRunPayload, or a future type) maps into — Daily
  Quests never inspects a mini-game-specific shape directly.
  7 Core Rules (game-designer + economy-designer consults): UTC-midnight
  generation with a deterministic non-client-predictable seed;
  progress increments ONLY from Anti-Cheat-validated results, atomic
  with the run's own currency credit, distinct from the quest's own
  reward credit which only happens on claim; "runs completed" = any of
  the 5 mini-games, win/loss-agnostic; completion and claim are distinct
  states, client sends quest ID only, server derives the amount;
  unclaimed-at-rollover quests are forfeited (softened by a 2hr UI nudge,
  never a grace period); one free reroll/day; target counts + ALL-FLAT
  reward routing (economy-designer's call, specifically to avoid the
  Coins-quest stacking with Coin Value's up-to-5x multiplier and blowing
  past Login Streak's income parity) — bricks 50->50 coins, coins
  20->80 coins, runs 3->45 coins, bosses 1->100 coins+1 gem.
  Edge-case review caught + FIXED a real self-contradiction in Rule 6:
  the original reroll wording ("re-runs the same draw excluding the
  current type") could duplicate a type already occupying another active
  slot, violating the no-repeat invariant — fixed to swap in the single
  type omitted from that day's original draw, the only substitution that
  preserves no-repeat.
  qa-lead flagged QA-harness/observability gaps (forcing an Anti-Cheat
  rejection on demand, analytics-outbox visibility, simulating an async
  Tier-2 flag) — not a design gap in the GDD itself, carried to Open
  Questions as future /qa-plan scope rather than silently dropped.
  CD-GDD-ALIGN final review: APPROVED WITH CONDITIONS, 2 conditions —
  C1 FIXED: Core Rule 4 and Dependencies had misattributed the
  quest_claimed server-authoritative outbox to ADR-0006; corrected to
  ADR-0004 (ADR-0006's own text confirms server-authoritative events
  "come from ADR-0004's outbox, NOT this client path" — ADR-0006 is the
  CLIENT buffer ADR). C2 investigated and found NOT a real defect: CD
  flagged creditFlat/creditMultiplied as GDD-invented API names since
  currency-system.md's own prose only says "flat path"/"multiplied
  path" in prose — but verified these ARE real ADR-0004/
  control-manifest.md-sourced leg names already used identically in
  quack-runner.md, so no fix was needed (an instance of verifying an
  agent's finding rather than blindly applying it).
  Fixed the one-directional dependency gap Core Rule 2 creates: also
  updated anti-cheat-replay-verification.md's own Dependencies section
  to list Daily Quests reciprocally, same pass (project convention).
  Registry: appended 4 new entries (daily_quest_targets_rewards,
  daily_quest_count, daily_quest_reroll_count,
  daily_quest_reset_cadence) — registry now at 50 total entries.
  systems-index.md updated: Daily Quests -> Designed — CD-GDD-ALIGN
  APPROVED WITH CONDITIONS (2 conditions folded in same pass), pending
  independent /design-review in a fresh session. Task #41 marked
  completed.
- Undesigned systems remaining in the Vertical-Slice tier: Login Streak,
  Mascot Gallery/Equip UI, Runner HUD (Per-Mini-Game HUD, Runner
  instance), Leaderboard. Structurally blocked here (fresh-session
  independence rule): /architecture-review, individual /design-review
  per GDD (now including currency-ledger-transaction-log.md and
  daily-quests.md). Vertical slice Phase 4 user-playtest loop stays
  PAUSED (Unity/EDR blocker, task #34 in_progress, not abandoned).
- design/gdd/login-streak.md COMPLETE (2026-07-12). Full review-mode, all
  12 sections. Real ambiguities in the one-line carried-over prototype
  summary were resolved as fresh design decisions in Core Rules, same
  approach as Daily Quests: streak counter is uncapped (only the coin
  formula's scaling caps at day 10=190) so the 7-day gem bonus keeps
  repeating indefinitely; "active" = a server-recorded Account/Auth
  login only, deliberately NOT reusing Daily Quests' validated-gameplay
  definition since the two systems are driven by different upstream
  signals on purpose; eligibility auto-detects on login but the reward
  still requires a manual claim tap (mirrors Daily Quests' completion-
  vs-claim split); a missed day resets to 0 then immediately to 1 on the
  triggering login; all-flat Currency System routing; a NEW
  streak_claimed analytics event proposed (no existing ADR-0004 entry,
  unlike quest_claimed).
  economy-designer revised the carried-over gem bonus 5->2/7 days after
  finding 5 would nearly match Daily Quests' active-play gem income for
  zero effort and be 5x its richest single reward — kept the coin cap at
  190 (confirmed reasonable, stays under Daily Quests' ~206/day avg).
  Edge-case review caught 2 real gaps in Core Rules (not self-
  contradictions this time, genuine missing specification) and fixed
  both directly: Rule 8 amended for brand-new accounts' first-ever login
  (no prior lastQualifyingLoginDate to diff against — now auto-
  qualifies); Rule 7 amended to state explicitly that streak LENGTH and
  actual CLAIM INCOME can fully diverge (a player who never taps claim
  keeps growing streakCount while forfeiting every reward) — correctly
  implied by the rules as written but not previously stated plainly.
  Also caught + fixed a real doc-mechanics defect: my own earlier Core
  Rules edit had left a duplicate empty "### States and Transitions"
  placeholder heading in the file (the real table had been embedded
  inside the Core Rules edit instead of replacing the dedicated
  placeholder) — deleted the leftover duplicate.
  Separately, verified and corrected an overstatement carried in the
  ALREADY-APPROVED daily-quests.md: it claimed Hub UI had an
  "already-reserved 'quests-card' slot" — checked hub-ui.md directly and
  found no such named component exists, only a generic mention that
  Daily Quests/Login Streak data renders via Shared Hub's aggregation.
  Fixed the wording in 3 places across daily-quests.md (Dependencies,
  Visual/Audio, UI Requirements) and wrote Login Streak's own UI
  Requirements accurately from the start.
  CD-GDD-ALIGN final review: APPROVED WITH CONDITIONS, 2 conditions,
  BOTH FIXED same pass — C1: Open Question 5 (UTC-midnight fairness)
  was scoped as Login-Streak-specific when Daily Quests shares the
  identical exposure with no equivalent open question — cross-
  referenced both ways (added as daily-quests.md's own new Open
  Question #6). C2: Core Rule 11/Tuning Knobs cited "(Formulas)" for a
  design goal the Formulas section never actually stated in prose —
  added an explicit sentence there so the citation is accurate.
  Registry: appended 2 new entries (streak_reward_formula,
  streak_reset_condition) — registry now at 52 total entries.
  systems-index.md updated: Login Streak -> Designed — CD-GDD-ALIGN
  APPROVED WITH CONDITIONS (2 conditions folded in same pass), pending
  independent /design-review in a fresh session. Task #42 marked
  completed.
- Undesigned systems remaining in the Vertical-Slice tier: Mascot
  Gallery/Equip UI, Runner HUD (Per-Mini-Game HUD, Runner instance),
  Leaderboard. Structurally blocked here (fresh-session independence
  rule): /architecture-review, individual /design-review per GDD (now
  including currency-ledger-transaction-log.md, daily-quests.md, and
  login-streak.md). Vertical slice Phase 4 user-playtest loop stays
  PAUSED (Unity/EDR blocker, task #34 in_progress, not abandoned).
- Aside: user asked mid-session to consult /skill-creator to find/create
  a proper specialist subagent (game-designer, systems-designer, etc.)
  since every consult so far has used general-purpose pretending to be
  a specialist. Flagged to the user that skill-creator builds triggered
  Skills (.claude/skills/*/SKILL.md), not registered Task-tool
  subagent_types (.claude/agents/*.md) — the actual mechanism that
  would fix the "pretend to be X" fragility. User did not redirect to
  the agents/*.md path yet; resumed GDD work instead per "any ways
  proceed with the last step." Still an open opportunity if raised
  again.
- design/gdd/mascot-gallery-equip-ui.md COMPLETE (2026-07-12). Full
  review-mode, all 12 sections. Two agent spawns failed mid-session on
  a Claude session-limit API error (reset 1:20am Asia/Riyadh) — paused
  cleanly (Overview + Player Fantasy already written/approved) rather
  than degrading review depth, then retried successfully once resumed.
  Resolved a real naming mismatch before drafting: mascot-database.md's
  own Dependencies section calls this system "Mascot Gallery/Equip UI"
  (matching systems-index.md) but hub-ui.md's UI Requirements calls the
  same referent "Mascot Collection" — reconciled explicitly in Overview
  rather than treating them as two different things. Also resolved a
  real gap nothing else defined: what "equip" actually DOES mechanically
  (Hub header avatar only) vs. the separate, undesigned "cosmetic mascot
  skins" Shop monetization layer mentioned in game-concept.md —
  explicitly scoped out.
  8 Core Rules (game-designer + UI-feasibility consults): fixed
  placeholder avatar until first unlock, then auto-equip (every unlock
  after stays manual); Locked slots have NO equip control at all
  (absent, not disabled) but stay tappable for Mascot Database's own
  unlock-condition display; persistence via
  player_state.data.equippedMascotId through the locked updatePlayer
  mutator, never mutateWallet; instant-apply equip, no confirm;
  sprite-atlased + virtualized/pooled ScrollRect committed at MVP
  (UI-feasibility: retrofitting later means rewriting equip/selection
  callbacks against recycled views, not just the rendering) with a
  drag-vs-tap threshold; a NEW mascot_equipped analytics event
  proposed; filter/sort explicitly deferred.
  Edge-case review caught 2 real gaps and fixed both directly (not
  self-contradictions this time, genuine missing spec): Core Rule 2
  amended for simultaneous multi-mascot grants (no defined winner
  existed); Core Rule 6 amended so the pooled grid's "equipped"
  highlight re-evaluates on every cell rebind AND on any
  equippedMascotId change, since it's derived state on recycled cells,
  not per-slot stored data.
  Also caught + fixed the SAME duplicate-placeholder-heading artifact
  as Login Streak (embedding a States table inside a Core Rules edit
  instead of replacing the dedicated placeholder) — now watching for
  this pattern specifically after two occurrences.
  Fixed 3 one-directional dependency gaps this GDD's own Dependencies
  section creates: save-persistence.md, account-auth.md, and (the more
  substantive one) hub-ui.md, whose header Core Rule 1 was updated to
  actually say the avatar sources from equippedMascotId now, not a
  hardcoded prototype duck — not just a dependency-list edit.
  CD-GDD-ALIGN final review: APPROVED WITH CONDITIONS, 2 REAL
  conditions, BOTH FIXED same pass — C1: Core Rule 2's tiebreak
  ("lowest canonical roster ID") assumed a field that doesn't exist
  anywhere in mascot-database.md; corrected to
  highest-rarity-then-alphabetical-name, both real defined fields. C2:
  Core Rule 5's optimistic instant-apply had no stated behavior for a
  failed updatePlayer write; added explicit client-rollback-to-
  last-confirmed-state. A third item the reviewer flagged (an inline
  date-tag mismatch in hub-ui.md) was checked directly against the file
  and found to be a FALSE POSITIVE — both dates already said
  2026-07-12 correctly; hub-ui.md's frontmatter Last Updated WAS
  genuinely stale at 2026-07-09 despite two same-day edits, and that
  real (if minor) issue was fixed instead of the non-issue the reviewer
  named.
  Registry: appended 2 new entries (mascot_equip_default,
  mascot_equip_multi_grant_tiebreak) — registry now at 54 total
  entries. systems-index.md updated: Mascot Gallery/Equip UI ->
  Designed — CD-GDD-ALIGN APPROVED WITH CONDITIONS (2 conditions folded
  in same pass), pending independent /design-review in a fresh session.
  Task #43 marked completed.
- Undesigned systems remaining in the Vertical-Slice tier: Runner HUD
  (Per-Mini-Game HUD, Runner instance), Leaderboard. Structurally
  blocked here (fresh-session independence rule): /architecture-review,
  individual /design-review per GDD (now including
  currency-ledger-transaction-log.md, daily-quests.md, login-streak.md,
  and mascot-gallery-equip-ui.md). Vertical slice Phase 4 user-playtest
  loop stays PAUSED (Unity/EDR blocker, task #34 in_progress, not
  abandoned).
- design/gdd/runner-hud.md COMPLETE (2026-07-12). Full review-mode, all
  12 sections. Unlike Ricochet HUD's pure carryover, real prototype
  source (RunnerCanvas.tsx, RunnerScreen.tsx from the completed "DOM
  overlay" task) was read directly as ground truth, same practice used
  for the original Quack Runner GDD — not paraphrased. This surfaced a
  genuine, deliberate elevation over the prototype: quack-runner.md's
  own UI Requirements calls for a live coinsCollected chip during play
  that the prototype never had.
  8 Core Rules (game-designer consult): chip-row layout with the new
  coin chip; health simplified to a single binary icon (not the
  prototype's literal repeated-heart pattern, since runner_health caps
  at 1); coin chip updates instantly per-grab, per-field dirty-check
  (not whole-second batched, to match "coins climbing with every grab"
  in Player Fantasy); accessibility parity via Unity's native
  Accessibility APIs (explicitly flagged for engineering verification
  since exact API names are past this project's Unity 6.3 knowledge-
  cutoff risk, carrying forward the prototype's proven aria-hidden/
  aria-live design INTENT, not its DOM-specific mechanism); reduced-
  motion respected via platform settings.
  RESOLVED both real gaps quack-runner.md's own Open Questions 1 and 2
  explicitly routed here rather than solving on paper: (1) app-
  backgrounding/pause — OnApplicationPause(true) only, no manual
  button, freezes SharedSimCore's tick advancement which closes the
  anti-cheat "can't freeze the ramp" concern by construction since
  obstacleSpeed(t)/spawnInterval(t) are pure functions of sim-tick t,
  120s wall-clock-measured cap before auto-forfeit; (2) death-shake
  magnitude — 5px default (down from the prototype's 14px), explicitly
  flagged as an unplaytested Tuning Knob pending /balance-check, not a
  locked value. Also fixed quack-runner.md itself: added the new Paused
  state to its own States table and marked both Open Questions resolved
  with pointers back to this GDD.
  Edge-case review resolved 2 genuine technical gaps (not self-
  contradictions this time): the 120s cap must be a wall-clock
  timestamp diff evaluated once at resume, not a running countdown,
  since Unity doesn't tick Update() while backgrounded; and GameOver
  vs. Paused in the same frame is a strict ordering (GameOver always
  wins, OnApplicationPause delivers between frames not mid-frame), not
  a true race needing a tiebreak.
  Self-check caught the SAME duplicate-placeholder-heading artifact a
  THIRD time (Login Streak, Mascot Gallery, now this) from embedding a
  States table inside a Core Rules edit instead of replacing the
  dedicated placeholder separately — fixed again, and also caught +
  fixed a real drift within this GDD itself: Overview still described
  health as "repeated heart icons" after Core Rule 2 later simplified
  it to a single binary icon, a residual inconsistency from mid-design
  refinement that self-check exists to catch.
  CD-GDD-ALIGN final review: APPROVED WITH CONDITIONS, 2 conditions,
  BOTH FIXED same pass — C1: Dependencies listed only Quack Runner as a
  hard dependency despite Core Rule 7's pause mechanism directly
  depending on SharedSimCore's tick-freeze behavior; added SharedSimCore
  as a named hard dependency, matching Ricochet HUD's own precedent of
  listing Boss AI/Damage Model alongside Super Ricochet whenever a Core
  Rule is governed by a specific system. C2: the header truncated the
  pillar text, dropping ", not a side loop" from game-concept.md's
  canonical wording; restored.
  Registry: appended 2 new entries (runner_death_shake_magnitude,
  runner_max_backgrounded_duration) — registry now at 56 total entries.
  systems-index.md updated: Per-Mini-Game HUD's Runner instance ->
  Designed, CD-GDD-ALIGN APPROVED WITH CONDITIONS (2 conditions folded
  in same pass), pending independent /design-review in a fresh session.
  Task #44 marked completed.
- Only ONE undesigned system remains in the entire Vertical-Slice tier:
  Leaderboard. Structurally blocked here (fresh-session independence
  rule): /architecture-review, individual /design-review per GDD (now
  including currency-ledger-transaction-log.md, daily-quests.md,
  login-streak.md, mascot-gallery-equip-ui.md, and runner-hud.md, plus
  the quack-runner.md re-fix). Vertical slice Phase 4 user-playtest
  loop stays PAUSED (Unity/EDR blocker, task #34 in_progress, not
  abandoned).
- /design-system leaderboard.md COMPLETE (2026-07-12), full review-mode.
  Formalized a decision Quack Runner's own GDD had already made
  implicitly ("unify across games?" from systems-index.md) — separate
  per-game leaderboards, not one unified score, since
  runnerLeaderboardScore and Ricochet's raw bestScore/level/
  bossesDefeated fields are structurally incomparable. Derived
  ricochetBestScore = (levelReached x 100,000) + min(bossesDefeated,
  99,999) from scratch (no formula existed anywhere for Ricochet before
  this), with a hand-verified overflow-safety proof (99,999 clamp <
  100,000 level multiplier, checked against an extreme-case worked
  example: level 30 + 150,000 bosses = 3,099,999, still ranks below
  level 31/0 bosses = 3,100,000).
  REAL ARCHITECTURE CONFLICT FOUND: ADR-0005 locked a SINGULAR
  player.leaderboard_score indexed column, written before the
  per-game-separate decision existed. Two specialist consultants
  disagreed on the fix — game-designer proposed dedicated per-game
  columns, backend/database consultant proposed a generic
  leaderboard_scores(player_id, game_id, score, achieved_at) table
  (data-only event for future mini-games, never a schema migration).
  Surfaced the disagreement to the user via AskUserQuestion; user
  approved the table-based recommendation. Flagged as a required
  ADR-0005 addendum this GDD cannot itself author (same pattern as
  Currency Ledger's own ADR-0004 addendum requirement).
  Account deletion diverges deliberately from Currency Ledger's
  anonymize-never-delete precedent: leaderboard rows are DELETED
  entirely, reasoned explicitly (no audit/legal retention obligation for
  a public ranking, unlike financial records; a tombstoned "Deleted
  Player" in a public top-20 is confusing broken UX with zero
  compensating audit value).
  Self-check caught the SAME duplicate-placeholder-heading artifact a
  FOURTH time (Login Streak, Mascot Gallery, Runner HUD, now this) —
  fixed again. Standing rule adopted going forward: States and
  Transitions gets its own separate Edit call, always, never embedded
  inside a Core Rules edit's new_string.
  CD-GDD-ALIGN final review: APPROVED WITH CONDITIONS, 4 conditions, ALL
  FIXED same pass — C1: leaderboard_scores schema had no column for Rule
  3's per-game display stats (Ricochet's level/bossesDefeated, Runner's
  coinsCollected/survival time); unrecoverable from score alone for
  Runner's non-invertible sum (coinsCollected x 25 + Sum dodgeBonus(t_i)
  can't be decoded back into its parts) — added a display_stats JSONB
  column, updated atomically with score/achieved_at in the same upsert
  SET clause. C2: quack-runner.md's own Interactions section still read
  "Leaderboard (not yet designed)" with obsolete field names
  (runnerBestScore, Ricochet's bestScore) despite its Dependencies
  section already being correctly updated — fixed to match. C3:
  ricochetBestScore was authored inside leaderboard.md itself rather
  than owned by super-ricochet.md, breaking this GDD's own Rule 2
  ownership precedent (Runner's score is owned by quack-runner.md,
  Leaderboard only consumes it) — backported the full formula/
  overflow-proof/examples into super-ricochet.md's own Formulas section,
  reduced leaderboard.md's Core Rule 1 and Formulas section to a pure
  consumer, matching Rule 2's pattern exactly. C4: the required ADR-0005
  addendum named only the table shape, missing that Core Rule 4 puts
  this table's write in the same transaction as reward crediting —
  making it a THIRD leg alongside ADR-0005's existing two
  (player_state FOR UPDATE, then the guarded wallet update); extended
  Core Rule 9 and Open Question 2 to require the addendum also extend
  ADR-0005's canonical lock order and whole-operation idempotency
  gating to this leg, closing an ABBA-deadlock-class risk the CD review
  caught that no earlier specialist pass had surfaced.
  Registry: appended 4 new entries — ricochet_leaderboard_score
  (source corrected to super-ricochet per C3), leaderboard_top_n,
  leaderboard_tiebreak, leaderboard_scores_schema — registry now at 60
  total entries.
  systems-index.md updated: Leaderboard -> Designed, CD-GDD-ALIGN
  APPROVED WITH CONDITIONS (4 conditions folded in same pass), pending
  independent /design-review in a fresh session. Both the summary-table
  row and the Progress Tracker row updated.
  Task #45 marked completed.
- **THE ENTIRE VERTICAL-SLICE TIER IS NOW FULLY DESIGNED.** Leaderboard
  was the last undesigned system in that priority group (per
  systems-index.md's own numbered list, items 12-20). No system in the
  Vertical Slice tier remains at "Not Started." Every one of the 9
  Vertical-Slice-tier GDDs carries a CD-GDD-ALIGN verdict (either
  APPROVED WITH CONDITIONS with fixes folded in, or a self-reviewed
  APPROVED from earlier in the project) and is pending only the
  structurally-blocked, fresh-session-only /design-review pass.
  Remaining structurally blocked work (fresh-session independence
  rule): /architecture-review, individual /design-review per GDD (full
  queue: mascot-database.md, obstacle-spawn-difficulty-ramp-runner.md,
  quack-runner.md, currency-ledger-transaction-log.md, daily-quests.md,
  login-streak.md, mascot-gallery-equip-ui.md, runner-hud.md,
  leaderboard.md). The required ADR-0005 addendum (schema + composed-op
  lock-order/idempotency extension) is not yet formally authored — a
  real next step, not yet scheduled. Vertical slice Phase 4
  user-playtest loop stays PAUSED (Unity/EDR blocker, task #34
  in_progress, not abandoned). No explicit next-tier instruction given
  yet for after Leaderboard's completion.

## Session Extract — /architecture-review 2026-07-18
- Verdict: CONCERNS
- Requirements: 24 systems — 19 covered, 1 partial (Runner HUD), 4 gaps (all Not-Started: Live-Ops Flag Config, 3 Undesigned Mini-Games, IAP/Receipt Validation, Shop/Cosmetics/Battle Pass)
- New TR-IDs registered: None (no tr-registry.yaml exists; coverage assessed system→ADR)
- GDD revision flags: obstacle-spawn-difficulty-ramp-runner.md (cap obstacleSpeed(t)), level-difficulty-config-ricochet.md (level validation + max_brick_hp) — all pre-existing ADR-routed items, no new engine-reality flags
- Top ADR gaps: IAP/receipt-credit ADR, Shop/coin-sink ADR, Live-Ops flag-config ADR (all Not-Started, expected)
- Cross-ADR items: CI-1 ADR-0001 clawback→flag-only one-line correction; CI-2 tolerance_units GDD-owner call; duplicate ADR-0002 number (hygiene)
- Linchpin: all 14 ADRs Proposed; ADR-0002 determinism spike is BLOCKING and gates 0007/0011/0013/0014
- Docs refreshed: architecture.md (ADR audit + required-ADRs), control-manifest.md (flagged missing 0012-0014, fixed leaderboard_score→per-game table)
- Report: docs/architecture/architecture-review-2026-07-18.md

## Session Extract — /architecture-review re-verification 2026-07-18 (post task #27)
- Verdict: CONCERNS (UNCHANGED). Independent re-verification, not a full re-derivation — the same-day full review above is current; this pass verified its open findings against actual file state after the analytics-catalog annotations (task #27) landed.
- Confirmed still-accurate: CI-1 (ADR-0001 §4 clawback wording still present, one-line fix outstanding); CI-2 (tolerance_units still open, GDD-owner call, not silently changed); duplicate ADR-0002 (super-ricochet-physics-api = REJECTED/superseded, deterministic-fixedpoint-physics = Proposed — clean split); architecture.md layer map still 11-MVP-only; control-manifest.md still PROVISIONAL + missing ADR-0012–0014; all 14 ADRs still Proposed.
- Task #27 annotations validated consistent: two-transaction outbox split (streak_claimed→mutateWallet ADR-0004; mascot_equipped→updatePlayer ADR-0005) is coherent across ADR-0004/0005/0006 + registry. No new cross-ADR conflict.
- Known derived-doc lag (not a new defect): control-manifest.md line 118 client-emission-ban does not yet list streak_claimed/mascot_equipped — will resolve when the manifest is regenerated.
- No files edited in this review session (author/review independence preserved). Fixes handed off to a fresh session.
- Path to PASS unchanged: (1) ADR-0002 determinism spike [critical path]; (2) refresh architecture.md layer map + regenerate control-manifest.md (fold 0012–0014); (3) close CI-1 one-liner + CI-2 GDD-owner call.
