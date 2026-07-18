# Unity — Current Best Practices (post-training-cutoff features relevant to Quack Studio)

Scope: new features/practices introduced in Unity 6.0 → 6.3 that are relevant to a **mobile-first 2D/3D hybrid mini-game collection**. This is guidance for *what to prefer*, distinct from `deprecated-apis.md` (*what to avoid*) and `breaking-changes.md` (*what changed and broke*).

---

## UI Toolkit vs UGUI — which to use where

Official Unity direction (per docs.unity3d.com "Comparison of UI systems") and current community consensus as of 6.3:

- **UGUI is not deprecated** and remains fully supported, but is in "maintenance mode" — it gets bug fixes, not major new capability investment.
- **UI Toolkit is where active investment is happening.** In 6.3 specifically it gained: CSS-style visual filters (opacity, tint, grayscale, sepia, invert, blur), native SVG/vector graphics import, a dedicated UI Test Framework package, UI Shader Graph integration, and a USS `aspect-ratio` property.
- **Practical guidance for this project:**
  - For **menus, settings screens, and non-gameplay-critical UI** (main menu, mini-game select screen, pause/settings overlays): prefer **UI Toolkit**. It batches into fewer draw calls, has no per-element GameObject overhead, and scales better across the many small screens a mini-game collection needs.
  - For **in-game HUD elements tightly coupled to gameplay objects** (e.g. a health bar following a character, UI elements needing Animator-driven tweening, or elements built by an asset/animation pipeline that assumes Canvas/RectTransform), **UGUI remains a reasonable, lower-friction choice** — especially under time pressure, since more tutorials/assets/animation tooling still assume UGUI.
  - **Both can coexist in the same project** — this is explicitly supported and common (e.g. UGUI for HUD, UI Toolkit for menus). Don't force a single system project-wide if it adds friction.
  - Do not assume UI Toolkit is a drop-in replacement for UGUI patterns (Canvas, RectTransform, `Image`, `Button` components) — it uses USS/UXML and a retained-mode visual tree; treat it as a distinct system requiring its own document structure.

## 2D Physics — new low-level Box2D v3 API

- Unity 6.3 ships a new **`UnityEngine.LowLevelPhysics2D`** namespace built on Box2D v3, running alongside (not replacing) the existing high-level `Rigidbody2D`/`Collider2D` API.
- **For this project:** the high-level 2D physics API (`Rigidbody2D`, `Collider2D`, `Physics2D.Raycast`, etc.) remains the default, well-documented, and stable choice for mini-games — keep using it as primary knowledge assumes. The new low-level API is worth reaching for only if a specific mini-game needs fine-grained multi-threaded physics control or has measured a physics bottleneck; it is not yet the default recommended path for typical gameplay code, and its API surface is new enough that a coding agent should pull the current package/manual docs rather than improvising signatures from general Box2D knowledge.
- The 2D Renderer's new ability to mix 3D meshes with 2D sprites (lit by 2D lights, participating in sprite sorting groups) is directly relevant to a "2D/3D hybrid" mini-game collection — this enables techniques like 3D characters/props in an otherwise 2D scene without hacky camera/layer tricks. This is a genuinely new capability (6.3), not available in pre-6.3 knowledge.

## Mobile rendering performance (URP)

- **URP Bloom** now offers Kawase filtering (mobile/small-resolution optimized) and Dual filtering (larger resolutions) — prefer Kawase for phone-class targets when using bloom.
- **GPU Lightmapper is now the default baking backend**, paired with the new **xAtlas** lightmap packing (default for new scenes, reports up to ~27% lightmap memory savings, at the cost of slower bake times). For any 3D scenes in the mini-game collection using baked lighting, this is the new default path — no action needed beyond knowing baking behavior has shifted from the older Progressive CPU/GPU lightmapper defaults.
- **Render Graph Viewer can now attach to real device builds** (including mobile), enabling on-device render-pass profiling that wasn't previously possible — use this over guesswork when diagnosing mobile rendering performance.
- URP and HDRP now share the same underlying Render Graph compiler/API — mostly an internal-architecture note, but means Render Graph API knowledge transfers across pipelines more than it used to.

## Addressables / asset pipeline

- Keep using **Addressables** (not raw AssetBundles) as the default content-delivery/asset-loading system — this remains Unity's actively-invested recommendation, reinforced by 6.3's TypeTree deduplication work reducing AssetBundle memory footprint.
- For Android specifically, use the companion **Addressables for Android** package if/when Play Asset Delivery (splitting install-time vs. on-demand asset packs) becomes relevant for keeping initial download size low — a real concern for a multi-mini-game collection where not every game needs to be in the initial APK/AAB.

## Burst / performance

- Burst-compiled C# remains the recommended path for CPU-heavy, hot-path code (job system + Burst), unchanged in principle from pre-6.3 knowledge.
- Unity 6.3 extended Burst-compiled code into new territory: **scriptable audio processors** (Burst-compiled C# audio units for custom DSP/audio processing) are a genuinely new 6.3 feature — relevant if any mini-game needs custom audio effects processing beyond standard AudioMixer effects.
- **Not independently verified for this document:** claims circulating in secondary/SEO sources about "Unity 2026.1 DOTS" and "Burst 3.2" delivering specific mobile performance percentages (e.g. entity iteration overhead reduction, ARM SIMD codegen numbers). These were not corroborated by official Unity sources during this research pass and should be treated as unverified marketing/speculative content, not a basis for architecture decisions. If DOTS/ECS becomes relevant to this project, verify current DOTS package status directly against `docs.unity3d.com` and Unity's DOTS package pages at that time, since DOTS packages version independently and were historically preview/experimental for long stretches.

## Project setup / new conventions

- **Build Profiles** (Editor feature): lets you configure only the settings relevant to a specific build target via an explicit "Add Settings" workflow, rather than one monolithic Player Settings blob. Worth adopting for a multi-mini-game, multi-platform-leaning project to keep per-target build config clean.
- **Package Manager**: packages can now be **version-pinned** via a `pinnedPackages` manifest property, and package signature verification is available — worth using to keep the project's dependency set reproducible across machines/CI.
- **Project Auditor** (introduced 6.1): built-in static analysis tool that scans scripts, assets, and project settings for performance issues — worth running periodically as part of this project's QA/perf workflow rather than relying solely on manual profiling.
- **Diagnostics / ANR monitoring** (introduced 6.2): built-in diagnostics experience including Android "Application Not Responding" (ANR) monitoring with device/session detail — relevant for catching mobile-specific stability issues (frozen main thread) that don't show up the same way in editor testing.
- Android builds: set the new **App Category** Player Setting explicitly to "Game" (see `deprecated-apis.md` — this supersedes `PlayerSettings.Android.androidIsGame`), and target the updated minimum of **Android 7.1 (API 25)**.

## Explicit gaps — do not guess past these

- Exact current Input System package version/API surface for 6.3 was not pulled from the live package manual in this pass — verify directly before writing non-trivial input code (especially touch/gesture handling for mobile).
- DOTS/ECS/Entities package maturity and API surface as of 6.3 was not researched in depth here — this project appears to be MonoBehaviour/GameObject-oriented per its "mini-game collection" framing, so DOTS was treated as out of scope; revisit explicitly if that assumption changes.
- No official Unity source was found with granular before/after code samples for the Render Graph custom pass migration — when a mini-game actually needs a custom `ScriptableRendererFeature`, pull the live URP package "Render Graph" manual section at implementation time rather than relying on this summary alone.

## Last verified: 2026-07-09
