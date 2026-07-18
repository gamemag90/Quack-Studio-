# Graph Report - .  (2026-07-11)

## Corpus Check
- 43 files · ~63,673 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 128 nodes · 198 edges · 10 communities (8 shown, 2 thin omitted)
- Extraction: 90% EXTRACTED · 9% INFERRED · 1% AMBIGUOUS · INFERRED: 18 edges (avg confidence: 0.78)
- Token cost: 357,604 input · 0 output

## Community Hubs (Navigation)
- GDD System Definitions & Core Rules
- Architecture Decisions & Engine Reference
- Balance Bug Fixes & Cross-Review
- Deterministic Replay & Anti-Cheat Core
- Game Concept & Art Direction Pillars
- Cross-System Interface Conflicts
- Project & Engine Configuration
- Production Stage Tracking
- IP Risk Note (HAOPLAY)
- Review Mode Config

## God Nodes (most connected - your core abstractions)
1. `Anti-Cheat/Replay Verification system` - 12 edges
2. `Quack Studio Master Architecture` - 12 edges
3. `Architecture Registry (architecture.yaml)` - 12 edges
4. `Account/Auth system` - 11 edges
5. `Super Ricochet system` - 11 edges
6. `Quack Studio — Game Concept` - 10 edges
7. `Active Session State: Master Architecture Document` - 10 edges
8. `Currency System` - 9 edges
9. `Cross-GDD Review Report (original, FAIL verdict)` - 8 edges
10. `ADR-0002: Deterministic Fixed-Point Physics Engine` - 8 edges

## Surprising Connections (you probably didn't know these)
- `Risk & Staffing Budget spike gate (blocking for Accepted status)` --semantically_similar_to--> `boss_hp hard cap at level 30 (11,100) — structural fix`  [INFERRED] [semantically similar]
  docs/architecture/adr-0002-deterministic-fixedpoint-physics.md → design/gdd/level-difficulty-config-ricochet.md
- `PCG32 PRNG algorithm choice` --semantically_similar_to--> `brick_hp_roll formula (pow(random,1.6))`  [INFERRED] [semantically similar]
  docs/architecture/adr-0001-deterministic-rng-replay-strategy.md → design/gdd/super-ricochet.md
- `ADR-0002: Deterministic Fixed-Point Physics Engine` --references--> `Boss AI/Damage Model system`  [EXTRACTED]
  docs/architecture/adr-0002-deterministic-fixedpoint-physics.md → design/gdd/boss-ai-damage-model.md
- `ADR-0002: Deterministic Fixed-Point Physics Engine` --references--> `Sub-stepped collision (half ball radius) — tunnelling-proof`  [EXTRACTED]
  docs/architecture/adr-0002-deterministic-fixedpoint-physics.md → design/gdd/super-ricochet.md
- `ADR-0001: Deterministic RNG Strategy` --references--> `Determinism requirement (fixed sub-stepping, seeded RNG)`  [EXTRACTED]
  docs/architecture/adr-0001-deterministic-rng-replay-strategy.md → design/gdd/super-ricochet.md

## Hyperedges (group relationships)
- **Reward-crediting pipeline (Super Ricochet -> Boss AI -> Anti-Cheat -> Currency System)** — design_gdd_super_ricochet_system, design_gdd_boss_ai_damage_model_system, design_gdd_anti_cheat_replay_verification_system, design_gdd_currency_system_system [EXTRACTED 1.00]
- **Foundation-layer systems (zero dependencies)** — design_gdd_account_auth_system, design_gdd_save_persistence_system, design_gdd_analytics_event_tracking_system [EXTRACTED 1.00]
- **Deterministic replay chain: GDD requirement to ADR implementation** — anti_cheat_tier2_replay_verification, design_gdd_super_ricochet_determinism_requirement, adr_0001_deterministic_rng, adr_0002_fixedpoint_physics [INFERRED 0.85]
- **Victorious-Run Reward Chain** — docs_architecture_adr_0011_boss_damage_events_bossdamagemodel, docs_architecture_adr_0007_replay_resimulation_service_verification_job_queue, docs_architecture_adr_0004_currency_atomic_credit_debit_mutatewallet, docs_architecture_adr_0006_analytics_buffer_flush_analytics_emit [INFERRED 0.85]
- **Shared Client Durable Storage Resolution** — docs_architecture_adr_0006_analytics_buffer_flush_analytics_emit, docs_architecture_adr_0010_client_cache_offline_queue_sqlite_store, docs_architecture_adr_0009_shared_hub_navigation_hubnavigator [EXTRACTED 1.00]
- **SharedSimCore Deterministic Simulation Family** — docs_registry_architecture_yaml_ideterministicrng, docs_registry_architecture_yaml_ideterministicphysics2d, docs_registry_architecture_yaml_fix32, docs_architecture_adr_0011_boss_damage_events_bossdamagemodel [EXTRACTED 1.00]

## Communities (10 total, 2 thin omitted)

### Community 0 - "GDD System Definitions & Core Rules"
Cohesion: 0.11
Nodes (31): Standard event catalog (app_open, run_complete, currency_earned, etc.), Analytics must never block gameplay, Tier 1 — Plausibility clamping, Decision: boss damage decoupled from brick HP (1 hit = 1 boss HP), Bcrypt cost factor 10, Guest accounts [NEW], JWT Token TTL (7 days), Social/platform login (OAuth2) [NEW] (+23 more)

### Community 1 - "Architecture Decisions & Engine Reference"
Cohesion: 0.14
Nodes (31): ADR-0002 (REJECTED): Super Ricochet Physics API Choice, SharedSimCore Physics Module (rejected design), ADR-0003: Secure On-Device Auth Token Storage, ISecureTokenStore, ADR-0004: Currency System Atomic Credit/Debit, mutateWallet chokepoint, ADR-0005: Server-Side Persistence Migration to PostgreSQL, updatePlayer locked read-modify-write (+23 more)

### Community 2 - "Balance Bug Fixes & Cross-Review"
Cohesion: 0.12
Nodes (18): Risk & Staffing Budget spike gate (blocking for Accepted status), Reuse of reconnect_backoff formula, Reward-inflation bug fix (separate credit terms, not combined), coin_credit formula (multiplied path / flat path), Two separate credit call paths fix (multiplied vs flat), Original bossHp formula (superseded, unbounded), Super Ricochet (mini-game), reconnect_backoff formula (+10 more)

### Community 3 - "Deterministic Replay & Anti-Cheat Core"
Cohesion: 0.12
Nodes (17): replay-verifier.exe CLI child-process invocation, ADR-0001: Deterministic RNG Strategy, Fail-open failure mode decision (superseded clawback wording), PCG32 PRNG algorithm choice, SharedSimCore.dll (.NET Standard 2.1 shared core), Distance sub-stepping driven by remaining distance, ADR-0002: Deterministic Fixed-Point Physics Engine, Rejection of float+tolerance for scored physics (branch fork risk) (+9 more)

### Community 4 - "Game Concept & Art Direction Pillars"
Cohesion: 0.18
Nodes (13): Character-first moments, not icon-first, Chunky tactility over flat minimalism, Color System (Marquee Orange, Duck-Pond Teal, Bill Gold, Brick Red, Egg Cream, Fern Green, Amethyst Purple), Visual Identity Statement — painterly duck adventurer, Warm and saturated, never muddy or grim, Quack Studio — Game Concept, Gems have no recurring sink [OPEN], Pillar: Collectible mascots [NEW] (+5 more)

### Community 5 - "Cross-System Interface Conflicts"
Cohesion: 0.29
Nodes (7): Run-Result Interface (runId, level, seed, inputSequence, clientReportedFields), bossDefeated authority scoped to client-side engine only, Bottleneck systems (Currency, Account/Auth, Save/Persistence), Systems Index (24 systems), MVP Priority Tier (11 systems), Recommend shared mini-game-result interface during /create-architecture, Undefined bossDefeated authority conflict (Boss AI vs Anti-Cheat)

### Community 6 - "Project & Engine Configuration"
Cohesion: 0.40
Nodes (5): Android API 25+ minimum (raised from API 21), Performance Budgets (60fps/150 draw calls/700MB-1.3GB), Unity 6.3 LTS engine pin, quack-blaster web prototype, Quack Studio (project)

### Community 7 - "Production Stage Tracking"
Cohesion: 1.00
Nodes (3): Gate Check: Systems Design to Technical Setup, Project Stage Analysis Report, Project Stage: Concept (stale)

## Ambiguous Edges - Review These
- `SharedSimCore Physics Module (rejected design)` → `Quack Studio Master Architecture`  [AMBIGUOUS]
  docs/architecture/architecture.md · relation: references

## Knowledge Gaps
- **29 isolated node(s):** `quack-blaster web prototype`, `Color System (Marquee Orange, Duck-Pond Teal, Bill Gold, Brick Red, Egg Cream, Fern Green, Amethyst Purple)`, `HAOPLAY 'Quack Quack Attack' reference`, `JWT Token TTL (7 days)`, `Bcrypt cost factor 10` (+24 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **2 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **What is the exact relationship between `SharedSimCore Physics Module (rejected design)` and `Quack Studio Master Architecture`?**
  _Edge tagged AMBIGUOUS (relation: references) - confidence is low._
- **Why does `Cross-GDD Review Report (original, FAIL verdict)` connect `GDD System Definitions & Core Rules` to `Balance Bug Fixes & Cross-Review`, `Game Concept & Art Direction Pillars`?**
  _High betweenness centrality (0.167) - this node is a cross-community bridge._
- **Why does `Quack Studio — Game Concept` connect `Game Concept & Art Direction Pillars` to `GDD System Definitions & Core Rules`, `Balance Bug Fixes & Cross-Review`, `Cross-System Interface Conflicts`?**
  _High betweenness centrality (0.143) - this node is a cross-community bridge._
- **Why does `Anti-Cheat/Replay Verification system` connect `GDD System Definitions & Core Rules` to `Balance Bug Fixes & Cross-Review`, `Deterministic Replay & Anti-Cheat Core`, `Cross-System Interface Conflicts`?**
  _High betweenness centrality (0.111) - this node is a cross-community bridge._
- **Are the 9 inferred relationships involving `Quack Studio Master Architecture` (e.g. with `ADR-0003: Secure On-Device Auth Token Storage` and `ADR-0004: Currency System Atomic Credit/Debit`) actually correct?**
  _`Quack Studio Master Architecture` has 9 INFERRED edges - model-reasoned connections that need verification._
- **What connects `quack-blaster web prototype`, `Performance Budgets (60fps/150 draw calls/700MB-1.3GB)`, `Android API 25+ minimum (raised from API 21)` to the rest of the system?**
  _41 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `GDD System Definitions & Core Rules` be split into smaller, more focused modules?**
  _Cohesion score 0.1053763440860215 - nodes in this community are weakly interconnected._