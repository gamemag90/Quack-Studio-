# Unity — Breaking Changes (2023.x / 6000.0 → 6000.3 LTS)

Scope: changes between roughly the Unity 2023.x/6000.0 era (the likely edge of LLM training data) and **Unity 6.3 LTS (6000.3)**, the version this project is pinned to. Focused on what matters for a mobile 2D/3D hybrid mini-game collection.

Primary sources: `docs.unity3d.com/6000.3/Documentation/Manual/UpgradeGuideUnity63.html`, `docs.unity3d.com/6000.3/Documentation/Manual/WhatsNewUnity63.html`, `unity.com/blog/unity-6-3-lts-is-now-available`, Unity Discussions "Planned breaking changes" threads.

---

## Rendering pipeline (URP/HDRP) — HIGH IMPACT

### URP Compatibility Mode is gone
- URP's **Render Graph API** became the default rendering path starting in Unity 6.0. A "Compatibility Mode" existed as a bridge for projects with custom `ScriptableRenderPass` code written against the pre-Render-Graph API.
- Compatibility Mode was **deprecated in 6.0** and is **fully removed in 6.3**. `RenderGraphSettings.enableRenderCompatibilityMode` is now read-only and always returns `false`.
- **Why it matters here:** any custom render pass code (e.g. custom outline shaders, post-processing effects for mini-games) must be written against the **Render Graph API** (`RenderGraphContext`, `RecordRenderGraph`), not the older `Execute(ScriptableRenderContext, ref RenderingData)` pattern that dominates pre-2023 tutorials and likely dominates the LLM's training data. Code generated from stale training data for custom `ScriptableRendererFeature`/`ScriptableRenderPass` classes will likely be wrong or won't compile.
- A `URP_COMPATIBILITY_MODE` scripting define exists as a stopgap but is itself deprecated as of 6.4 — do not rely on it for new code.

### URP + HDRP share a Render Graph compiler now
- As of 6.3, URP and HDRP "use the same underlying Render Graph compiler and API," per Unity's own blog. This project uses URP (mobile-first), but be aware any HDRP-flavored sample code found online may now be more directly portable/relevant than in older Unity versions — and vice versa, meaning some render-graph APIs are shared rather than pipeline-specific.

### GPU Lightmapper is now default
- The GPU (OpenCL-based) Lightmapper is now the default baking backend in new projects, replacing CPU-based baking as the default. Combined with the new **xAtlas** lightmap packing algorithm (also default for new scenes, up to ~27% lightmap memory savings but slower packing time). Relevant if any mini-game uses baked lighting for 3D scenes.

### Mobile-specific rendering additions
- URP **Bloom** now has Kawase filtering (optimized for small/mobile resolutions) and Dual filtering (larger resolutions) — new mobile-friendly post-processing option that didn't exist pre-6.3.
- Render Graph Viewer can now connect to **player builds running on real devices**, including mobile — useful for on-device profiling of render passes, not available in older versions.

---

## 2D Physics — HIGH IMPACT for a 2D-heavy mini-game collection

- Unity 6.3 introduces a **new low-level 2D physics API built on Box2D v3**, living in the `UnityEngine.LowLevelPhysics2D` namespace. It runs **alongside** (not replacing) the existing `Rigidbody2D`/`Collider2D` high-level API for now, but Unity has signaled it will eventually replace the current 2D physics backend.
- Multi-threaded performance improvements, better determinism, and visual debugging come with it.
- **Why it matters:** if this project's mini-games use 2D physics (likely, given the "mini-game collection" scope), existing high-level `Rigidbody2D` code still works, but any code an agent writes referencing "Box2D," low-level physics bodies, or expecting the old single-threaded 2D physics behavior should account for the new option. Training data will know nothing about `UnityEngine.LowLevelPhysics2D`.
- **2D Renderer** (URP 2D Renderer) now supports rendering 3D mesh components alongside sprites, with 2D lights affecting 3D meshes and 3D meshes participating in 2D sorting groups. Genuinely new capability for hybrid 2D/3D mini-games — didn't exist before 6.3.
- Physics SDK can now be **stripped from builds** entirely if a game doesn't use physics — relevant for build-size-conscious mobile mini-games that are pure 2D/UI without physics.
- `Physics.autoSyncTransforms` (3D physics) is deprecated → use `Physics.SyncTransforms`.

---

## Input System

- The **new Input System package** (`com.unity.inputsystem`) has been the default/recommended for new Unity 6 projects since 6.0 — legacy `Input.GetKey`/`Input.GetAxis` (the "Input Manager") still works via a compatibility shim (`Active Input Handling` = "Both") but is not the recommended path for new projects. This shift started before 6.3 but is worth stating explicitly since older training data defaults to the legacy `Input` class in nearly all examples.
- No 6.3-specific breaking changes to the Input System package itself were found in official release notes; treat existing Input System knowledge as broadly valid but confirm package version compatibility (Input System package versions are decoupled from editor version via Package Manager).
- **Not independently verified**: specific mobile touch-input API changes in the Input System package for 6.3. If touch/gesture code is written, verify against the current Input System package manual rather than assuming training-data behavior.

---

## Addressables & Asset Bundles

- Unity continues investing in **Addressables** for live-game content delivery. In 6.3, AssetBundles get a smaller in-memory footprint via **TypeTree deduplication**.
- **Addressables for Android** exists as a separate companion package (`com.unity.addressables.android`) supporting Google Play Asset Delivery — relevant for keeping initial APK/AAB download size down on mobile.
- No explicit breaking API removals were found for Addressables in the 6.3 upgrade guide. Treat core Addressables workflow (Groups, `Addressables.LoadAssetAsync`, etc.) as stable, but verify Play Asset Delivery integration specifics against current package docs since this is an area with mobile-specific packaging nuance.

---

## Mobile build pipeline — Android

- **Minimum supported Android version raised to Android 7.1 (API level 25)** as of 6.3.
- **Gradle upgraded 8.13 → 9.1.0**, **Android Gradle Plugin upgraded 8.10.0 → 9.0.0**. Any custom Gradle templates (`mainTemplate.gradle`, `launcherTemplate.gradle`) or third-party native plugins need review for AGP 9.0.0 compatibility — AGP major version bumps routinely break custom Gradle syntax.
- **Android 16 behavior change**: OS no longer force-enforces fixed orientation on large screens; Unity added a new **App Category** Player Setting (replacing `PlayerSettings.Android.androidIsGame`) — should be explicitly set to "Game" for this project.
- Round and legacy (non-adaptive) Android launcher icons are **deprecated** — use adaptive icons only.
- `UnityWebRequest` now defaults to **HTTP/2** on all platforms where the server supports it (early Android tests: ~40% server load reduction, ~15-20% on-device CPU reduction). Shouldn't break existing networking code but changes underlying behavior/performance characteristics.

## Mobile build pipeline — Web/other

- Facebook Instant Games platform target is deprecated (irrelevant unless this project targets it).
- Web builds: IL2CPP metadata now uses variable-size indices, reducing build size; native Apple Silicon support for the Web build toolchain removes Rosetta 2 dependency on Mac build machines.

---

## Editor-level / project-setup changes worth knowing

- **`[SerializeField]` is now restricted to fields only** — applying it to a property, method, or type is now a **compile-time error** (previously just ineffective/warned). Auto-properties need `[field: SerializeField]` instead. This is a real trap for generated code: if an agent writes `[SerializeField] public int Foo { get; set; }` expecting old lenient behavior, the project won't compile.
- `Object.FindObjectOfType<T>()` / `FindObjectsOfType<T>()` were deprecated back in **Unity 2023.1** (not new to 6.3, but likely still present in stale training data as "the normal way") in favor of `FindFirstObjectByType<T>()`, `FindAnyObjectByType<T>()`, and `FindObjectsByType<T>(FindObjectsSortMode)`. See `deprecated-apis.md`.
- USS (UI Toolkit stylesheets) parser was upgraded in 6.3 with stricter validation — USS that "worked" (silently ignored) under looser older parsing may now surface errors.
- Search system backend changed to LMDB; `Window > Search > Index Manager` was removed in favor of `Preferences > Search > Indexing`.

---

## Confirmed NOT relevant / out of scope for this project

- Multiplayer-specific breaking changes (Multiplay Hosting removal, Netcode for GameObjects 1.x `NetworkTransform.Update` override removal) — noted for completeness but skip unless this project adds networked multiplayer later.
- Magic Leap / XR-specific deprecations — skip unless an XR mini-game is added.
- Ray tracing / DLSS4 changes — desktop/console-oriented, low relevance to mobile-first mini-games, but not incompatible with mobile builds either.

## Last verified: 2026-07-09
