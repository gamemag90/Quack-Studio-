# Unity — Version Reference

| Field | Value |
|-------|-------|
| **Engine Version** | Unity 6.3 LTS (build `6000.3.0f1`) |
| **Project Pinned** | 2026-07-09 |
| **LLM Knowledge Cutoff** | May 2025 |
| **Risk Level** | HIGH — version is beyond LLM training data |

## What "Unity 6" versioning means

Starting with Unity 6, Unity dropped the old `YYYY.x` calendar-year naming (e.g. `2022.3`, `2023.1`) in favor of a `6000.x` internal build number with a marketing name of "Unity 6.x". Two release tracks now exist per year:

- **Update releases** — shipped multiple times a year, shorter support window (e.g. 6.1, 6.2).
- **LTS releases** — shipped roughly once a year, get 2 years of support (3 years for Unity Enterprise/Industry subscribers). 6.0 and 6.3 are both LTS releases.

This project is pinned to **6000.3 (Unity 6.3 LTS)**, the first LTS release since Unity 6.0 LTS.

## Post-Cutoff Version Timeline

Everything below shipped after the assumed LLM training cutoff (~May 2025) and should be treated as "new" relative to the model's built-in knowledge of Unity. Dates are as verified via web research; treat any not explicitly sourced as approximate.

| Version | Type | Approx. Release | Headline changes relevant to this project |
|---|---|---|---|
| **6000.0 (Unity 6.0 LTS)** | LTS | October 2024 | Baseline "Unity 6" — Render Graph API becomes default in URP/HDRP, Compatibility Mode introduced as a deprecated bridge, GPU Resident Drawer, new Input System is default for new projects. (This is likely at or just past the LLM's real knowledge edge — treat 6.0-era APIs as *plausibly* known but unverified.) |
| **6000.1 (Unity 6.1)** | Update | ~March 2025 | Deferred+ rendering path in URP, DirectX 12 ray tracing performance improvements, Android XR / Meta Quest support, Project Auditor (static analysis) tool, Web builds get WebGPU support and "Publish to Play." |
| **6000.2 (Unity 6.2)** | Update | ~August 2025 | Unity AI (generative AI tooling in-editor), new built-in Diagnostics experience (incl. Android ANR monitoring), automatic LOD generation at import, Android XR package reaches "verified" status. |
| **6000.3 (Unity 6.3 LTS)** | LTS | **December 5, 2025** | **This project's pinned version.** Low-level 2D physics on Box2D v3, unified URP/HDRP Render Graph compiler, xAtlas lightmap packing (default), Platform Toolkit (cross-platform accounts/achievements/saves API), URP Compatibility Mode fully removed, Android minimum raised to API 25 (Android 7.1), Gradle 9.1.0 / AGP 9.0.0, UnityWebRequest defaults to HTTP/2, UI Toolkit gets CSS-style filters + native SVG + Shader Graph integration, GPU Lightmapper is now the default baking backend. Supported until **December 2027**. |

Anything versioned 6000.4 or higher (if it exists at the time of a coding session) is **later than this project's pin** and out of scope — do not upgrade APIs to match it without an explicit re-pin decision.

## Verification notes / gaps

- Exact patch-level release notes for 6000.1 and 6000.2 were not exhaustively fetched (only headline features) — if a coding session needs 6.1/6.2-specific API detail not covered in `breaking-changes.md` or `deprecated-apis.md`, treat it as unverified and re-check `docs.unity3d.com/6000.3/...` before writing code against it.
- Some secondary sources (SEO aggregator blogs) made claims about "Unity 2026.1" and "DOTS/Burst 3.2" mobile performance numbers; these were **not** corroborated by official Unity sources and are deliberately excluded from this reference set as unverifiable/likely speculative.

## Last verified: 2026-07-09
