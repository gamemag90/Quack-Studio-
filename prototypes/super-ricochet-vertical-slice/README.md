# Super Ricochet — Vertical Slice

> VERTICAL SLICE — NOT FOR PRODUCTION. Reference only; production code is
> written from scratch, never refactored from this slice.

**Validation question**: Does a player, starting from nothing, feel Super
Ricochet's "Ready, Aim, Fire!" fantasy (precision + chaos + escalating
tension) within 2–3 minutes with no guidance — and can `SharedSimCore`'s
Q16.16 fixed-point physics design (ADR-0002) actually be built and feel
good at representative quality in a bounded session?

## Where the actual build lives

The `/vertical-slice` skill's generic template assumes an engine-agnostic
`prototypes/[concept]-vertical-slice/` folder holds the runnable build.
Unity doesn't work that way — only code under `Assets/` compiles and only
scenes under `Assets/` can be opened/played. So for this Unity project,
the split is:

- **This folder** (`prototypes/super-ricochet-vertical-slice/`) — this
  README, and later `REPORT.md` / `PIVOT-NOTE.md` per the skill's own
  Phase 6/8 instructions.
- **`Assets/Scripts/SharedSimCore/`** — the actual scored simulation:
  `Fix32.cs`, `IDeterministicPhysics2D.cs`, `BossDamageModel.cs`,
  `RicochetSim.cs`. Engine-free (`.NET Standard 2.1`, `noEngineReferences:
  true`), same assembly as the existing `Pcg32Rng`.
- **`Assets/_VerticalSlice/`** — the Unity-only view/input layer
  (`Scripts/RicochetSliceController.cs`, `Scenes/SuperRicochetSlice.unity`).
  Isolated under this distinctly-named folder so it's unambiguous and easy
  to delete wholesale once the slice's job is done.

## How to run it

1. Open the project in Unity 6.3 LTS (`6000.3.0f1`).
2. Open `Assets/_VerticalSlice/Scenes/SuperRicochetSlice.unity`.
3. Press Play.
4. Drag from the launcher (yellow square, bottom of the playfield) upward
   to aim, release to fire. Complete a full Ready → Aiming → Firing →
   Over (win or loss) cycle.

**If the scene fails to open or shows errors**: the scene file was hand-
authored (no Unity Editor was available to generate/verify it in this
session) and is the single highest-risk artifact in this build. The
fastest fix is to skip debugging the YAML entirely — create a new empty
scene at that same path (File > New Scene, save over it), drag
`RicochetSliceController.cs` onto an empty GameObject, and press Play.
Everything else (the actual simulation and UI) is built entirely from
code in `Awake()`, so a fresh empty scene with just that one script
attached is sufficient.

**Compile errors**: none of this C# was run through an actual compiler in
this session (no .NET SDK was available) — it was hand-written directly
against the existing `Pcg32Rng`/`IDeterministicRng` API and ADR-0002's
documented design. First-round compile errors from the Unity Console are
expected; paste them back and they'll get fixed.

## Known, deliberate scope cuts (not oversights)

- **ADR-0002's blocking on-device spike** (ARM64-IL2CPP == x86-CLI byte-
  identical proof) is not attempted here — it needs a physical device
  build pipeline this session doesn't have. This slice builds the
  *design* faithfully but doesn't and can't satisfy that gate. ADR-0002
  stays Proposed; production Ricochet code stays blocked on it.
- Coin pickups and `+1 ball` power-ups (super-ricochet.md Rule 9) are cut
  — only the core aim/fire/collide/boss/win-loss loop is in scope.
- Currency, Anti-Cheat submission, Analytics, Save/Persistence, Hub
  navigation, Account/Auth, multi-level progression, mascots, quests, and
  shop are all out of scope — single hardcoded level 1, no server
  round-trip, reward is not persisted.
- Rendering is plain UGUI `Image` rects with no sprite/prefab/`Rigidbody2D`
  dependencies — a slice-only simplification. Production should follow
  ADR-0002 Decision 3 (kinematic `Rigidbody2D`, visual-only) and ADR-0008
  (`InputSystemUIInputModule`); this slice uses legacy `Input.*` for
  simplicity and doesn't validate either of those two points.
- The brick-HP roll (`ceil(pow(random,1.6) × maxBrickHp)`) uses
  `System.Math.Pow` (float), matching the GDD's literal formula — unlike
  the ball-physics path, this is *not* claimed to be cross-platform
  bit-exact. Whether it needs to be is a genuine open item outside
  ADR-0002's stated scope (ball physics only), not resolved here.
