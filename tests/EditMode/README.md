# Edit Mode Tests

Unit tests that run without entering Play Mode.
Use for pure logic: formulas, state machines, data validation — and
critically, `SharedSimCore`'s RNG + fixed-point physics test vectors
(ADR-0001, ADR-0002), which must not depend on a live scene.

Assembly definition required: `tests/EditMode/EditModeTests.asmdef`
(reference `SharedSimCore.dll` per its `.asmdef` — ADR-0001/0002 — plus
`UnityEngine.TestRunner` and `UnityEditor.TestRunner`).
