# Quack Studio — Art Bible

> **Status**: Complete (all 9 sections)
> **Author**: Abdulrahman Alenazan + Claude
> **Last Updated**: 2026-07-09
> **Art Director Sign-Off (AD-ART-BIBLE)**: APPROVED 2026-07-09 (self-reviewed
> — no `creative-director`/`art-director` subagent registered in this
> environment). Checked against the game's pillars (shared hub/economy,
> server-authoritative economy, collectible mascots, every mini-game is a
> real pillar) and the master prompt's "family-friendly, energetic" and
> "avoid copying existing titles" requirements — no conflicts found; the
> Honktyson naming issue (unrelated to this document, found earlier the same
> session) is tracked separately in [[project_quack_ip_risk]].
> **Genre reference**: painterly/chunky casual-mobile-collection convention
> (see Section 9 for specific reference sources and what to take/avoid from
> each) — **not** a reskin of any single reference title. Character designs,
> poses, compositions, and names must be original throughout.

## 1. Visual Identity Statement

**One-line rule**: Every duck is a painterly, fully-rendered adventurer with
a distinct silhouette and personality, shown in expressive motion — never a
flat icon or a static portrait.

**Supporting principles:**

1. **Chunky tactility over flat minimalism** — UI elements read as physical,
   pressable objects (thick borders, layered drop shadows, dimensional
   depth), not printed flat shapes. *Design test: when a UI element's style
   is ambiguous, choose the version that looks touchable over the version
   that looks printed.*
2. **Character-first moments, not icon-first moments** — every mini-game
   entry point, reward, and milestone gets a fully-rendered character
   illustration reacting to it, not an abstract icon standing in for it.
   *Design test: when deciding how to represent an event, default to "show
   a duck reacting" over "show a symbol representing."*
3. **Warm and saturated, never muddy or grim** — the palette leans into
   warm oranges/golds/greens/teals at high saturation, even in loss/danger
   states (no desaturated or gritty color shifts). *Design test: when two
   color choices both fit semantically, pick the warmer, more saturated
   one.*

**Pillar connection**: Chunky tactility + character-first serve "every
mini-game is a real pillar" — each game needs to feel like its own
confident place, not a reskin of the others. Warm saturation serves "shared
hub/economy" (one cohesive, inviting world) and the master prompt's
explicit "family-friendly, energetic" direction.

## 2. Mood & Atmosphere

| Game State | Primary Emotion | Lighting Character | Atmosphere (3–5 adjectives) | Energy |
|---|---|---|---|---|
| **Hub (home base)** | Welcoming ownership — "this is my place" | Warm midday sun, soft golden highlights, no harsh shadows | Cozy, sunlit, inviting, collected, alive | Measured |
| **Super Ricochet — Aiming** | Focused anticipation | Slightly cooler daylight, clear high contrast on the trajectory line/UI | Tense, precise, held-breath, clean | Measured, building |
| **Super Ricochet — Firing/Volley** | Chaotic delight | Bright flashes on impact, warm sparks, brief screen-wide glow on big hits | Kinetic, bouncy, percussive, bright | Frenetic |
| **Boss Victory** | Triumphant celebration | Golden hour burst, radial light rays behind the winning duck | Jubilant, sparkling, larger-than-life, warm | Frenetic (brief), then settling |
| **Run Loss** | Light disappointment, quick to recover | Dimmed but never dark — muted warm tones, not cold or gray | Gentle, apologetic, still warm, brief | Measured, low |
| **Reward/Quest Claim** | Delight, small treat | Punchy highlight burst on the reward itself, rest of screen stays warm-neutral | Sparkly, satisfying, bite-sized, bright | Quick spike |

Every state stays in the warm end of the spectrum (Principle 3) — even the
Loss state is "gentle disappointment," never grim, matching the
family-friendly pillar. Only *energy level* and *contrast* shift between
states, not color temperature.

## 3. Shape Language

- **Character silhouette philosophy**: every duck reads at thumbnail size
  via a single distinguishing prop/outfit silhouette (a hat shape, a
  weapon, a cape line) — never relying on face detail to differentiate.
  Bodies are rounded and soft (large head-to-body ratio, no sharp
  anatomical angles) to stay approachable and family-friendly; the
  *outfit* carries all the personality and role information, not the body
  shape itself.
- **Environment/prop geometry**: two deliberately distinct registers, not
  one uniform style — (1) **rounded, soft geometry** for characters, hub
  environments, and UI (matching Principle 1's tactility), and (2)
  **blocky, faceted geometry** specifically for Super Ricochet's brick
  targets and constructed enemies, echoing that mini-game's
  physics-destruction heritage. This contrast is intentional: blocky
  targets instantly signal "this is the physics-destruction game," distinct
  from every other screen's rounded softness.
- **UI shape grammar**: UI echoes the world's rounded-and-chunky language
  directly (not a distinct flat HUD style) — thick rounded-rectangle
  panels, pill-shaped buttons, circular currency chips. Same vocabulary as
  characters, just simplified, reinforcing "one cohesive world" rather than
  a game world with a bolted-on separate interface layer.
- **Hero shapes vs. supporting shapes**: the player's active duck and any
  boss are always the largest, most detailed, most saturated shapes on
  screen; supporting elements (background props, non-boss obstacles,
  secondary UI) are simplified, smaller, and slightly desaturated to
  recede. Reward pickups (coins, gems, mascots) are an exception — high
  saturation and a subtle idle bounce/glint — so they read as "wanted"
  even while small.

## 4. Color System

| Color | Role | Semantic Meaning |
|---|---|---|
| **Marquee Orange** (carried from prototype) | Primary/brand hero color | Energy, the "signature" color of the whole app — CTAs, hero character accents |
| **Duck-Pond Teal** (carried from prototype) | Secondary/counter-accent | Calm, "safe," used for secondary mini-games and cool UI states |
| **Bill Gold** (carried from prototype) | Currency & reward | Treasure, achievement — coins, highlight glints, victory bursts |
| **Brick Red** (carried from prototype) | Danger/loss, kept warm not grim | Physical danger/urgency (low boss HP, danger line) — never used for "evil" moral coding, always paired with an icon, never color-alone |
| **Egg Cream** (carried from prototype) | Light surfaces | Card backgrounds, light UI panels, character highlight tones |
| **Fern Green** *(NEW)* | Nature/common-tier | Environment foliage, the "Common" mascot rarity tier |
| **Amethyst Purple** *(NEW)* | Rare/premium | "Rare"/"Epic" mascot rarity tiers, premium-adjacent gem accents |

Four of seven colors carry forward from the prototype's already-validated
system — this keeps our own identity anchored and distinct from the
reference material's specific palette (which leans much more heavily
orange/yellow-dominant with tropical greens) rather than converging toward
it. The two new colors (Fern Green, Amethyst Purple) exist specifically to
support the new mascot-rarity system, which the prototype never needed.

**Colorblind safety**: Brick Red (danger) vs. Fern Green (Common rarity) is
a classic red-green confusion pair. Both are **always paired with a
non-color cue**: danger states get an icon (exclamation/skull), never color
alone; rarity tiers are always shown with a star-count and border-thickness
system, never color alone.

**UI palette**: mostly matches the world palette directly (per Section 3's
"same vocabulary" rule), with a near-black warm "ink" tone (carried from
the prototype's `--ink-950`) reserved for text and borders to guarantee
contrast against the saturated background colors.

## 5. Character Design Direction

The "player character" is whichever mascot is currently equipped — there's
no separate fixed player-avatar body distinct from the mascot roster.
Distinguishing features: mascots use outfit/prop silhouettes (Section 3)
plus a rarity-tier visual language (border color/star count from Section 4,
not species variation — keeps the roster feeling like one cohesive duck
cast, not disconnected creatures). Bosses get larger scale, more elaborate
props, and a name-plate treatment. Expression/pose style: exaggerated and
instantly readable (big clear emotion poses), not subtle or realistic —
matches the warm family-friendly tone. LOD: full detail for the active/hero
duck on screen; simplified silhouette-only rendering for small
mascot-gallery thumbnails, where detail wouldn't read anyway.

## 6. Environment Design Language

"Arcade boardwalk" as the unifying architectural metaphor — a semi-abstract
seaside/carnival backdrop tying the Hub and every mini-game together
thematically (echoes the duck-pond origin) without requiring deep
world-building lore, since this is a casual arcade collection, not a
narrative RPG. Texture philosophy: painted/stylized, not PBR-realistic —
matches the painterly character direction and is cheaper to produce and
lighter on mobile GPUs. Prop density: sparse-to-moderate in the Hub (screen
space is precious, keep focus on characters/UI), moderate-to-dense in
mini-game backdrops (secondary to foreground gameplay, can carry more
atmosphere). Environmental storytelling: minimal and optional flavor — this
is a lobby/arcade metaphor, not a narrative payload.

## 7. UI/HUD Visual Direction

Screen-space HUD (not diegetic) — standard genre convention, matches the
prototype. Typography carries forward unchanged from the prototype's system
(Bungee display, Manrope body, Space Mono for numbers) — Bungee's bold
rounded letterforms already fit "chunky tactility" perfectly. Iconography:
flat-with-depth (slight bevel/shadow), not fully flat and not
photorealistic. Animation feel: bouncy, spring-based easing (slight
overshoot then settle), never linear/mechanical.

**UX alignment note**: Principle 2 ("character-first, not icon-first")
applies to discrete, meaningful moments (mini-game entry, quest complete,
mascot unlock) — it must **not** extend to high-frequency in-play feedback
(brick hits, mid-run coin pickups), which stays lightweight/iconic for both
clarity and the 60fps performance budget. Super Ricochet's own GDD already
handles this correctly (particles/sound for hits, not character
illustrations); this section makes that boundary explicit for future UI
work.

## 8. Asset Standards

Against `technical-preferences.md`'s budgets (60fps mid-range target, ≤150
draw calls, <1.3GB high-end/<700MB mid-range memory):
- Texture atlasing required for all UI chip/icon sets (one atlas per
  screen, not per-element).
- Character materials capped at 2 material slots per mascot (body +
  outfit) to keep draw calls bounded as the roster grows.
- Texture resolution tiers: 1024×1024 for hero/active characters, 512×512
  for secondary/gallery thumbnails, 256×256 for UI icons.

**Ideal vs. constrained tradeoff**: painterly detail ideally wants
higher-res hand-painted textures, but the mid-range 700MB memory budget
forces the tiered system above rather than uniform high-res — accepted as
the right tradeoff for a mobile-first, multi-mini-game-collection scope.

## 9. Reference Direction

1. **HAOPLAY's "Quack Quack Attack"** (user-provided reference — genre &
   production-quality benchmark) — **Take**: the multi-mini-game hub
   structure, chunky comic-book UI chrome (thick borders, bold outlined
   type), character-first reward-moment framing, and cross-game
   currency-display consistency. **Avoid, explicitly**: specific character
   designs/outfits, specific mascot poses, the exact boss name pattern
   (already flagged and corrected — see [[project_quack_ip_risk]]), logo/
   wordmark treatment, and exact marketing-art compositions. This is the
   closest reference and needs the firmest "inspired by, not copied from"
   discipline.
2. **Peggle (PopCap)** — physics-blaster-specific reference for Super
   Ricochet. **Take**: the satisfying multi-layered impact feedback on a
   successful bounce-chain shot, and trajectory-preview-as-skill-expression.
   **Avoid**: Peggle's specific celebratory audio sting and rainbow/unicorn
   victory imagery — ours needs its own distinct victory treatment per
   Section 2's mood table.
3. **Coin Master / Board Kings-style hub economy games** — daily-engagement
   density reference. **Take**: the "always something small to claim"
   visual density in a hub screen. **Avoid**: their slot-machine/spin
   mechanics entirely — not part of this design, and gambling-adjacent
   mechanics conflict with the family-friendly pillar.
4. **Supercell character art (Clash Royale / Brawl Stars)** — a
   well-known, high-production-bar example of "chunky tactile" character
   rendering distinct from HAOPLAY's specific style. **Take**: bold thick
   outline linework and highly saturated, dimensional-but-clean shading as
   an execution-quality bar. **Avoid**: their PvP/combat framing entirely —
   irrelevant to a casual collection game.

These four are deliberately non-redundant — each contributes a different
lesson (hub structure, physics-game feel, engagement density,
character-rendering execution) rather than four sources all pointing at the
same thing.
