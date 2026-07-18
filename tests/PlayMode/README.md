# Play Mode Tests

Integration tests that run in a real game scene.
Use for cross-system interactions, physics, coroutines, and UI flows —
e.g. the kinematic `Rigidbody2D` visual sync to `SharedSimCore` (ADR-0002),
the fixed-timestep accumulator's spiral-of-death clamp, or hub navigation
(ADR-0009).

Assembly definition required: `tests/PlayMode/PlayModeTests.asmdef`.
