# Technical Preferences

## Engine & Language
- **Engine**: Unity 6.3 LTS
- **Language**: C#

## Naming Conventions
- Classes: PascalCase (e.g., `PlayerController`)
- Public fields/properties: PascalCase (e.g., `MoveSpeed`)
- Private fields: _camelCase (e.g., `_moveSpeed`)
- Methods: PascalCase (e.g., `TakeDamage()`)
- Files: PascalCase matching class (e.g., `PlayerController.cs`)
- Constants: PascalCase or UPPER_SNAKE_CASE

## Input & Platform
- **Target Platforms**: Mobile (iOS 14+, Android API 25+ — Android 7.1 minimum
  per Unity 6.3 LTS's own floor, raised from the master prompt's original
  "API 21+" ask; see `docs/engine-reference/unity/breaking-changes.md`)
- **Input Methods**: Touch
- **Primary Input**: Touch
- **Gamepad Support**: None
- **Touch Support**: Full
- **Platform Notes**: Single tap/hold/drag gestures per the master prompt's
  Creative/UX direction. All UI must be thumb-reachable on one-handed portrait
  play; no hover-only interactions (no mouse on target devices).

## Performance Budgets
- **Frame Rate**: 60fps target on mid-range devices, 30fps fallback on low-end
  with dynamic quality scaling (per master prompt)
- **Frame Budget**: 16.6ms
- **Draw Calls**: ≤150 per frame on mid-range mobile (dynamic batching enabled)
- **Memory**: <1.3GB high-end, <700MB mid-range (per master prompt)
- **Initial Download**: <150MB app size before asset-bundle streaming

## Testing
- **Framework**: NUnit (Unity Test Framework) — suggested default for Unity C# projects

## Engine Specialists

> No dedicated Unity specialist subagents are registered in this Claude Code
> environment. This table documents the *intended* routing per the
> claude-code-game-studios convention; until those subagents exist here,
> route this work to `general-purpose` or `Plan` agents using this table as
> guidance, or use the Agent tool with an explicit brief naming the relevant
> Unity subsystem.

- **Primary**: unity-specialist (not registered — fall back to general-purpose)
- **Language/Code Specialist**: unity-specialist (C# review — primary covers it)
- **Shader Specialist**: unity-shader-specialist (Shader Graph, HLSL, URP materials)
- **UI Specialist**: unity-ui-specialist (UI Toolkit UXML/USS, UGUI Canvas, runtime UI)
- **Additional Specialists**: unity-dots-specialist (ECS, Jobs, Burst), unity-addressables-specialist (asset loading, memory, content catalogs)

### File Extension Routing

| File Extension / Type | Specialist to Spawn |
|-----------------------|---------------------|
| Game code (.cs files) | unity-specialist |
| Shader / material files (.shader, .shadergraph, .mat) | unity-shader-specialist |
| UI / screen files (.uxml, .uss, Canvas prefabs) | unity-ui-specialist |
| Scene / prefab / level files (.unity, .prefab) | unity-specialist |
| Native extension / plugin files (.dll, native plugins) | unity-specialist |
| General architecture review | unity-specialist |

## Forbidden Patterns
[TO BE CONFIGURED]

## Allowed Libraries
[TO BE CONFIGURED]
