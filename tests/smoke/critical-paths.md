# Smoke Test: Critical Paths

**Purpose**: Run these checks in under 15 minutes before any QA hand-off.
**Run via**: `/smoke-check` (which reads this file)
**Update**: Add new entries when new core systems are implemented.

## Core Stability (always run)

1. Game launches to Hub without crash
2. A player can register/login (guest and password/social path) — Account/Auth
3. Hub responds to all inputs without freezing; navigation debounce holds (shared-hub.md, ADR-0009)

## Core Mechanic (update per sprint)

<!-- Add each mini-game's core loop here as it is implemented -->
4. [Super Ricochet: aim, fire, ball resolves, boss HP decrements on hit — update once implemented]

## Data Integrity

5. Save completes without error (Save/Persistence — updatePlayer chokepoint, ADR-0005)
6. Load restores correct state; local cache reconciles to server on reconnect (ADR-0010)
7. Currency credit/debit never goes negative under rapid repeated taps (Currency System, ADR-0004)

## Determinism / Anti-Cheat (project-specific — do not skip)

8. A recorded Super Ricochet run replays to the same result server-side within `tolerance_units` (ADR-0001/0002/0007)
9. Tier-1 clamp always governs the granted reward; a forced Tier-2 mismatch only flags, never claws back (ADR-0007)

## Performance

10. No visible frame rate drops on target hardware (60fps target, 30fps fallback)
11. No memory growth over 5 minutes of play (once core loop is implemented)
