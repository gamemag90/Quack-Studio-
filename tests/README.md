# Test Infrastructure

**Engine**: Unity 6.3 LTS (build `6000.3.0f1`)
**Test Framework**: Unity Test Framework (NUnit)
**CI**: `.github/workflows/tests.yml`
**Setup date**: 2026-07-11

## Directory Layout

```
tests/
  unit/           # Isolated unit tests (formulas, state machines, logic)
  integration/    # Cross-system and save/load tests
  smoke/          # Critical path test list for /smoke-check gate
  evidence/       # Screenshot logs and manual test sign-off records
  EditMode/       # Edit Mode tests — no Play Mode required
  PlayMode/       # Play Mode tests — real scene, physics, coroutines
```

## Running Tests

`Window → General → Test Runner` in the Unity Editor, or headlessly via
`game-ci/unity-test-runner` in CI (see `.github/workflows/tests.yml`).

## Test Naming

- **Files**: `[System][Feature]Tests.cs`
- **Methods**: `[Scenario]_[ExpectedResult]`
- **Example**: `CurrencySystemCreditTests.cs` → `CreditMultiplied_AppliesCoinValueUpgradeOnlyToCollectedTerm()`

## Story Type → Test Evidence

| Story Type | Required Evidence | Location |
|---|---|---|
| Logic | Automated unit test — must pass | `tests/unit/[system]/` |
| Integration | Integration test OR playtest doc | `tests/integration/[system]/` |
| Visual/Feel | Screenshot + lead sign-off | `tests/evidence/` |
| UI | Manual walkthrough OR interaction test | `tests/evidence/` |
| Config/Data | Smoke check pass | `production/qa/smoke-*.md` |

## Determinism-critical systems (special test requirement)

Per ADR-0001 and ADR-0002, `SharedSimCore` (RNG + fixed-point physics) has an
**additional, non-negotiable test requirement**: a CI shared test-vector suite
that asserts byte-identical results between the Unity client (IL2CPP-built)
and the standalone .NET verification CLI, not just Editor Play Mode. See:

- `docs/architecture/adr-0001-deterministic-rng-replay-strategy.md` — RNG test vectors, `algorithm_version`.
- `docs/architecture/adr-0002-deterministic-fixedpoint-physics.md` — the BLOCKING spike gate (Q-format freeze + overflow proof + signed-`unchecked`/`IntSqrt` codegen assertion on ARM64-IL2CPP vs. x86).

These live under `tests/unit/SharedSimCore/` once the C# library exists, but
are called out here because they gate Super Ricochet implementation per the
TD condition recorded in `docs/architecture/architecture.md`.

## CI

Tests run automatically on every push to `main` and on every pull request.
A failed test suite blocks merging.

**Setup required before first CI run**: add a `UNITY_LICENSE` repository
secret (GitHub → Settings → Secrets and variables → Actions). Do not attempt
to automate this — it must be added manually per Unity's licensing terms.
