# Mascot Gallery/Equip UI

> **Status**: Designed
> **Author**: Abdulrahman Alenazan + Claude
> **Last Updated**: 2026-07-12
> **Implements Pillar**: Collectible mascots
> **Creative Director Review (CD-GDD-ALIGN)**: APPROVED WITH CONDITIONS
> 2026-07-12 (2 real conditions, both fixed same pass — C1: Core Rule
> 2's multi-grant auto-equip tiebreak assumed an undefined "canonical
> roster ID" field that doesn't exist anywhere in `mascot-database.md`;
> corrected to highest-rarity-then-alphabetical-name, both real, already
> -defined fields — fixed in Core Rule 2, its Edge Case, and its
> Acceptance Criterion. C2: Core Rule 5's optimistic instant-apply had
> no stated behavior for a failed `updatePlayer` write; added an
> explicit client-rollback-to-last-confirmed-state rule, edge case, and
> AC. A third flagged item — an inline date-tag mismatch between Core
> Rule 1 and the Interactions section in `hub-ui.md` — was checked
> directly against the file and found to be a false positive, both
> already said 2026-07-12; `hub-ui.md`'s frontmatter `Last Updated` WAS
> genuinely stale at 2026-07-09 despite two same-day edits, and that
> real (if minor) issue was fixed instead.)
> **`/design-review` (2026-07-17)**: NEEDS REVISION — 2 blocking items
> (Core Rule 2's tiebreak leans on mascot *name* uniqueness, but
> mascot-database.md never validates that names are unique and Core Rule
> 3 there explicitly allows renames — the same "assumed field that isn't
> actually enforced" class of bug as the already-fixed C1 canonical-ID
> issue, one level more subtle; `equippedMascotId` had no defined
> behavior for the actual sole revocation path — confirmed Tier-2 fraud,
> per mascot-database.md Core Rule 6 — leaving it possibly pointing at a
> mascot the player no longer owns) plus 1 recommended and 1
> nice-to-have item. All folded in below; re-review pending.

## Overview

Mascot Gallery/Equip UI is the screen where players view their collected
mascot roster (owned + locked, per Mascot Database's ownership/rarity
model) and select one owned mascot as **equipped** — the mascot that
displays as the player's active avatar across Hub surfaces, starting with
the header avatar slot `hub-ui.md` already reserves (currently hardcoded
to a single prototype duck). This GDD reconciles a naming mismatch:
`hub-ui.md` calls the viewing screen "Mascot Collection" and
`mascot-database.md`'s Dependencies section calls this system "Mascot
Gallery/Equip UI" (matching `systems-index.md`) — both refer to the same
system; this document uses the canonical systems-index name throughout.
Equipping has no gameplay or stat effect, consistent with mascots being
purely collectible per the no-pay-to-win pillar. This GDD explicitly does
**not** design cosmetic mascot skins — a separate, not-yet-designed
Shop-driven monetization layer mentioned in `game-concept.md`. Equip
selects among mascots the player already owns; it does not purchase or
apply a skin.

## Player Fantasy

Equipping isn't loadout optimization — no stats ride on it — it's a
small daily act of self-expression: picking which mascot best matches
your mood or the moment, the way you'd pick a profile picture. The
gallery becomes a wardrobe of identities rather than a trophy count,
making the collection itself the payoff mechanism for Pillar 3
(collectible mascots) — mascots exist to be worn, not just owned.
Distinct from Mascot Database's own Player Fantasy (the thrill of a
milestone unlock), this is about the quieter, recurring choice of who
represents you today. A mostly-locked early roster stays aspirational
rather than discouraging because Mascot Database's own labeled-silhouette
treatment already converts locked slots into a visible checklist ("16
things I know are coming") rather than a wall of no — this GDD inherits
that pattern, not reinvents it.

## Detailed Design

### Core Rules

1. **Default equipped avatar (0 mascots owned)**: the Hub header avatar
   slot shows a fixed, non-mascot placeholder icon (a generic bird mark,
   distinct from any real mascot) — not a random or "first unlockable"
   substitute, since that would misrepresent ownership. No
   `equippedMascotId` is written until the player has something
   equippable.
2. **Auto-equip on first unlock**: the player's *first*
   `mascot_unlocked` event also writes `equippedMascotId` to that
   mascot, so the header avatar upgrades automatically without
   requiring a manual Gallery visit. Every unlock after the first does
   **not** auto-equip — equip becomes a deliberate choice from then on.
   **[Amended during Edge Cases review, then corrected during
   CD-GDD-ALIGN review, then corrected again 2026-07-17]**: if a single
   transaction grants more than one mascot at once (a run satisfying two
   unlock conditions simultaneously, per Mascot Database's multi-grant
   handling), the auto-equip target is the **highest-rarity** granted
   mascot, tie-broken by **ascending mascot ID** (lexicographic on the
   stable `mascotId` string) if still tied. An earlier draft used "lowest
   canonical roster ID," found to be a nonexistent field and corrected to
   "alphabetical mascot name" during CD-GDD-ALIGN review — but
   `/design-review` found that fix traded one unenforced assumption for
   another: mascot-database.md never validates name uniqueness, and its
   own Core Rule 3 explicitly allows renames ("a future rename never
   orphans a grant") with no collision check against existing names. A
   mascot's `mascotId` (the same stable, content-addressable field Core
   Rule 3 already uses for `unlockCondition` references, specifically
   *because* it's immutable and unique) is the only field this GDD can
   safely assume is both unique and stable — using it here needs no new
   field, no Mascot Database addendum, and closes the gap for good
   rather than trading it for a different unvalidated assumption. All
   granted mascots still queue their own reveal card per Mascot
   Database's existing behavior; only the equip target needed
   disambiguating.
3. **Locked mascots are not equippable, and the equip control is absent
   on Locked slots — not merely disabled.** Tapping a Locked slot
   surfaces Mascot Database's own existing unlock-condition affordance
   (the labeled silhouette + condition text); the slot remains tappable
   for that purpose, but no equip button renders on it at all, since
   there is nothing valid to equip.
4. **Persistence**: equip state is one field,
   `player_state.data.equippedMascotId` (nullable string, mascot ID),
   written exclusively through Save/Persistence's locked
   `updatePlayer(id, mutate)` mutator — never `mutateWallet`, since this
   is not currency. This matches Mascot Database's own
   `player_state.data.mascots` write path through the same chokepoint.
5. **Instant-apply, no confirmation — optimistic client update, with an
   explicit rollback on write failure.** Tapping an owned, unequipped
   mascot immediately updates the header avatar client-side and sends
   the `updatePlayer` write; no confirm dialog, since equip is fully
   reversible with zero gameplay or economic stakes, and a confirm step
   would contradict the "pick a profile picture" framing in Player
   Fantasy. **[Added during CD-GDD-ALIGN review]**: if the write fails
   (network loss, server error), the client reverts the header avatar
   to the last server-confirmed `equippedMascotId` and the highlighted
   grid slot reverts to match — an earlier draft left this case
   unstated, which would have let the UI silently desync from the
   server on a failed write.
6. **Grid rendering: sprite-atlased, virtualized/pooled scroll,
   committed now rather than deferred.** Mascot portraits pack into one
   Sprite Atlas, rarity borders/star icons into a second shared atlas,
   and Locked slots reuse a single silhouette sprite — collapsing the
   whole grid toward a handful of draw calls under ADR-0008's ≤150
   budget. A pooled/recycled `ScrollRect` (not flat instantiation, not
   pagination) is committed at MVP even though 16 slots alone wouldn't
   strictly require it: the roster is explicitly open-ended via future
   content, and retrofitting virtualization later would mean rewriting
   equip/selection/rarity-binding logic against recycled (non-fixed-
   index) views — the expensive part, not the rendering itself. Slot
   tap hit-areas meet the project's 44×44pt/48×48dp minimum touch-target
   rule (`accessibility-requirements.md`), including inter-slot spacing,
   and use a drag-vs-tap threshold so scrolling the grid cannot misfire
   an equip action. **[Amended during Edge Cases review]**: because
   cells are pooled and recycled, the "equipped" highlight is *derived*
   state, not per-slot stored data — it must be re-evaluated on every
   cell rebind (`cell.data.mascotId == equippedMascotId`) as the player
   scrolls, AND every currently-bound visible cell must be forced to
   re-evaluate whenever `equippedMascotId` changes, or a recycled cell
   can surface a stale highlight, or a highlight can persist on a slot
   that's no longer equipped after a new equip lands off-screen.
7. **A new `mascot_equipped` analytics event is proposed here** —
   payload: mascot ID, rarity tier, `source` (`gallery_tap` |
   `auto_equip_first_unlock`). Equip frequency and *which* mascots get
   worn is the only telemetry that validates this GDD's own Player
   Fantasy (self-expression) — without it there is no signal on whether
   that fantasy is actually landing. Flagged explicitly as new, not
   silently assumed to already exist — see Open Questions.
8. **Filter/sort is explicitly deferred, not designed here.** 16 slots
   is small enough for an unfiltered scroll to stay legible; a
   filter-by-rarity or owned/locked toggle is worth revisiting once
   roster growth makes the flat grid unwieldy, not before.
9. **[NEW 2026-07-17] If the currently-equipped mascot is revoked** (the
   sole revocation path per Mascot Database's Core Rule 6: a
   human-confirmed Tier-2 fraud finding), **the same auto-equip selection
   logic as Rule 2 re-runs immediately against the player's remaining
   owned mascots** (highest-rarity, then ascending `mascotId`), and
   `equippedMascotId` updates to that result. If zero mascots remain
   owned, `equippedMascotId` clears back to null/unset and the header
   reverts to Rule 1's placeholder. This was previously unaddressed: the
   only "mascot disappears" case this GDD covered was admin removal from
   the live roster (grandfathering — avatar unchanged, since the player
   still owns it), never actual ownership revocation, which would
   otherwise leave `equippedMascotId` pointing at a mascot the player no
   longer owns — directly violating Core Rule 3's "Locked mascots are not
   equippable" (a revoked mascot is, from this player's perspective,
   equivalent to Locked).

### States and Transitions

No full state-machine table is needed here — equip state is a single
persistent field, not a multi-state lifecycle. Two states: **Unset**
(pre-first-unlock, Rule 1) → **Set** (Rule 2). One system-driven
transition (auto-equip on the first `mascot_unlocked` event), plus
unlimited player-driven **Set → Set** reassignment among owned mascots
thereafter (Rule 5). **[Corrected 2026-07-17]** A reverse **Set → Unset**
transition *does* exist, previously overlooked: a confirmed Tier-2 fraud
revocation (Rule 9) that removes the player's last owned mascot returns
`equippedMascotId` to null and the header to the placeholder. Under
ordinary play (no revocation), the header is never un-equipped back to
the placeholder once a player owns at least one mascot — that framing is
still correct for the common case, just not an absolute guarantee.

### Interactions with Other Systems

- **Mascot Database + Rarity Logic** (reads) — sole source of truth for
  ownership (`player_state.data.mascots`), rarity tiers, and the
  Locked-state visual treatment; this GDD adds no unlock logic of its
  own. **[NEW 2026-07-17]** Also the trigger source for Rule 9's
  re-equip/clear logic: a confirmed Tier-2 fraud revocation (that GDD's
  Core Rule 6) is the one event, besides a fresh unlock, that changes
  which mascots are available to equip from outside this GDD's own
  player-driven taps.
- **Save/Persistence** (writes) — owns the `updatePlayer(id, mutate)`
  locked mutator this GDD writes `equippedMascotId` through (Rule 4).
- **Hub UI** (writes to, read by) — the header avatar slot reads
  `equippedMascotId`, replacing the hardcoded prototype duck.
- **Analytics/Event Tracking** (writes) — receives the new
  `mascot_equipped` event (Rule 7).
- **ADR-0008 (UGUI)** — governs the grid's draw-call budget via atlas
  discipline and virtualization (Rule 6).

## Formulas

None. This system computes nothing — equip is a selection among a
player's already-owned mascot IDs (Mascot Database's own ownership
model), not a derived or calculated value. There is no reward curve,
cost formula, or tunable output range for this system to own.

## Edge Cases

- **If a player's currently-equipped mascot is administratively removed
  from the live roster** (per Mascot Database's grandfathering rule for
  a pre-ship data-error case): the Hub header avatar keeps showing it
  unchanged; `equippedMascotId` is not cleared or reassigned.
  Grandfathering keeps the mascot (and its asset) in the owning
  player's collection — deprecation only excludes it from
  `total_roster_count`/completion percentage, never from rendering.
- **If a guest account links to a permanent account** (same `playerId`):
  `equippedMascotId` carries over automatically — it's one field inside
  the same `player_state.data` blob that migrates wholesale via the
  existing guest-link flow, exactly like `mascots`. No mascot-specific
  migration logic is required.
- **If a single transaction grants more than one mascot at once**
  (a run satisfying two unlock conditions simultaneously): see Core
  Rule 2's amendment — auto-equip targets the highest-rarity granted
  mascot, tie-broken by alphabetical mascot name if still tied.
- **If two owned mascots are tapped in rapid succession**: last-write-
  wins suffices, no atomicity concern. Equip is a `set`, not a
  `mutateWallet`-style delta — writing the same or a different
  `mascotId` twice is naturally idempotent and order-safe, unlike
  Currency System's additive mutations, so no locking or idempotency
  key is needed.
- **If a grid cell is recycled while scrolling, or `equippedMascotId`
  changes while the grid is mounted**: see Core Rule 6's amendment —
  the "equipped" highlight is derived state re-evaluated on every cell
  rebind and forced to re-evaluate across all currently-bound visible
  cells on any `equippedMascotId` change, preventing a stale highlight
  from surviving a scroll or a new equip landing off-screen.
- **If the `updatePlayer` write for an equip fails** (network loss,
  server error): see Core Rule 5's amendment — the optimistically
  updated header avatar and grid highlight both revert to the last
  server-confirmed `equippedMascotId`, rather than leaving the client
  showing a mascot the server never actually equipped.
- **[NEW 2026-07-17] If the currently-equipped mascot is revoked via a
  confirmed Tier-2 fraud finding** (Mascot Database Core Rule 6's sole
  revocation path): see Core Rule 9 — the same highest-rarity-then-ID
  selection logic Rule 2 uses for auto-equip re-runs against the
  player's remaining owned mascots, or clears to the placeholder if none
  remain. Unlike admin-removal grandfathering (which never touches
  `equippedMascotId` since the player still owns that mascot), this case
  genuinely removes ownership, so the equipped reference cannot be left
  dangling.

## Dependencies

- **Depends on** (hard): Mascot Database + Rarity Logic (sole source of
  ownership, rarity tiers, and Locked-state visuals); Save/Persistence
  (the `updatePlayer` locked mutator this GDD writes
  `equippedMascotId` through); Account/Auth (`playerId` scoping);
  Analytics/Event Tracking (the new `mascot_equipped` event).
- **Depended on by** (hard): Hub UI (the header avatar slot reads
  `equippedMascotId`, replacing the hardcoded prototype duck).

**Consistency check**: Mascot Database's GDD already says "Depended on
by: ... the future Mascot Gallery/Equip UI screen" — matches.
Save/Persistence, Account/Auth, and Hub UI did not yet list this system —
one-directional gaps this GDD's own dependencies create, fixed in the
same pass (see those files), following the project's established
convention of fixing rather than just flagging reciprocal gaps.

## Tuning Knobs

| Knob | Suggested Value | Too Low | Too High |
|---|---|---|---|
| Grid columns per row | 4 | Excessive scrolling for a small roster | Touch targets shrink below the 44×44pt minimum on typical phone widths |
| Pooled cell count | Visible rows + 1 buffer row | Visible pop-in during fast scroll | Erodes the draw-call savings virtualization (Core Rule 6) exists to capture |
| Drag-vs-tap threshold | ~10px | Legitimate taps misfire as drags and get swallowed | Slow drags near a slot boundary misfire as an unwanted equip |

## Visual/Audio Requirements

**Equip-moment VFX**: a quiet UI feedback beat, not a character
moment — equip isn't among the art bible's three character-first
triggers (mini-game entry, quest complete, mascot unlock), so like
Daily Quests' assign/reroll it stays icon-first. The tapped cell gets a
quick spring-scale pop (tactility principle); the equipped border snaps
onto it as the prior cell's clears; the Hub header avatar pops in on
next view. A short, low-key confirm tick — not a reward chime, since
nothing was earned. Matches Core Rule 5's instant-apply/no-confirm:
felt, not celebrated.

**Equipped-slot highlight**: a thick rounded Marquee Orange border (the
art bible's UI shape grammar — thick rounded-rectangle panels, same
vocabulary as the rest of the app) plus a small checkmark badge, framing
the LOD-simplified thumbnail (the art bible's own LOD rule: simplified
silhouette-only rendering for small mascot-gallery thumbnails, full
detail reserved for the active/hero duck) rather than relying on extra
character detail. Distinct from rarity's border/star language in color
and position so the two never collide. Since Core Rule 6 makes this
derived state, the border must render correctly on every rebind with no
persistent animation — only the transient tap-pop plays once per equip.

**Auto-equip-on-first-unlock**: rides Mascot Database's existing reveal
card rather than adding a second beat — a small "Now your avatar" badge
on that card, resolving into the header-avatar pop-in once the card
dismisses. No added runway: Mascot Database's own "no tier gets a
longer beat" rule extends here too.

**Art bible fit**: tactility/pressability, Marquee Orange as
signature/self-expression color, UI shape grammar, spring-based
animation feel, LOD tiering (explicitly anticipates mascot-gallery
thumbnails), colorblind-safe icon+color pairing (generalized from
rarity to this new "current selection" case).

*Honestly flagged*: no mood-table row covers "equip"; the confirm-tick
SFX and the Marquee-Orange-plus-badge selection pattern are both new,
unwritten in the art bible — this is the first system needing a
"currently selected" visual language, worth folding back into the art
bible if reused elsewhere.

## UI Requirements

Mascot Gallery/Equip UI renders as a full screen — `hub-ui.md`'s own UI
Requirements already lists it as "Mascot Collection"; this GDD uses the
canonical `systems-index.md` name throughout, per the naming
reconciliation in Overview. It shows a virtualized grid of all roster
slots — locked silhouettes and unlocked full art per Mascot Database's
spec, rarity border/star indicators on every slot, and an
equipped-highlight on the current selection (Visual/Audio Requirements).
Entry point: a "View Collection" CTA from Hub UI's mascot preview
(`hub-ui.md` Core Rule 2). Detailed layout, spacing, and interaction map
belong in `/ux-design`, not this GDD — this section only establishes the
screen's required content and states.

## Acceptance Criteria

- **GIVEN** 0 owned mascots, **WHEN** the Hub loads, **THEN** the header
  shows the fixed placeholder icon and `equippedMascotId` is
  null/unwritten.
- **GIVEN** 0 owned mascots, **WHEN** the first `mascot_unlocked` fires
  for one mascot, **THEN** `equippedMascotId` equals that mascot's ID
  and the header updates without a Gallery visit.
- **[REVISED 2026-07-17] GIVEN** 0 owned mascots, **WHEN** one transaction
  grants 2+ mascots simultaneously, **THEN** `equippedMascotId` equals the
  highest-rarity granted mascot (ascending `mascotId` if still tied — not
  name, which mascot-database.md never guarantees unique), and every
  granted mascot still gets its own reveal card.
- **GIVEN** `equippedMascotId` is already set, **WHEN** a later
  `mascot_unlocked` fires, **THEN** `equippedMascotId` is unchanged.
- **GIVEN** a Locked slot, **WHEN** viewed, **THEN** no equip button
  renders on it, and tapping it shows the unlock-condition
  silhouette/text, never an equip action.
- **GIVEN** a mascot is equipped, **WHEN** the write completes, **THEN**
  `player_state.data.equippedMascotId` is updated via
  `updatePlayer(id, mutate)`, and `mutateWallet` is never called.
- **GIVEN** an owned, unequipped slot, **WHEN** tapped, **THEN** it
  equips and the header updates immediately with no confirm dialog.
- **GIVEN** a touch starting on a slot, **WHEN** movement exceeds the
  drag-vs-tap threshold (Tuning Knobs) before release, **THEN** no
  equip fires; below threshold, equip fires.
- **GIVEN** the equipped slot is off-screen, **WHEN** scrolled into
  view, **THEN** its highlight renders correctly from
  `cell.data.mascotId == equippedMascotId`, with no stale carryover
  from a previously-bound mascot.
- **GIVEN** a previously-equipped cell stays visible, **WHEN** the
  equip changes via another surface, **THEN** its highlight clears (and
  any now-equipped visible cell's highlight appears) without requiring
  a scroll to trigger the update.
- **GIVEN** any equip action, **WHEN** it completes, **THEN**
  `mascot_equipped` fires with mascot ID, rarity tier, and the correct
  `source` value.
- **GIVEN** the MVP Gallery, **WHEN** viewed, **THEN** no filter/sort
  control is present.
- **GIVEN** the equipped mascot is administratively removed from the
  live roster, **WHEN** the Hub opens, **THEN** the avatar is unchanged
  and `equippedMascotId` is unchanged.
- **[NEW 2026-07-17] GIVEN** the equipped mascot is revoked via a
  confirmed Tier-2 fraud finding, and the player still owns other
  mascots, **WHEN** revocation completes, **THEN** `equippedMascotId`
  reassigns to the player's remaining highest-rarity mascot (ascending
  `mascotId` if tied). **GIVEN** the revoked mascot was the player's
  only one, **WHEN** revocation completes, **THEN** `equippedMascotId`
  clears to null and the header reverts to the Rule 1 placeholder.
- **GIVEN** a guest account with `equippedMascotId` set, **WHEN** it
  links to a permanent account, **THEN** the value carries over
  unchanged.
- **GIVEN** two owned mascots tapped in quick succession, **WHEN** both
  register, **THEN** `equippedMascotId` ends at the later tap's mascot,
  with no corrupted intermediate state.
- **GIVEN** an equip tap whose `updatePlayer` write fails, **WHEN** the
  failure is detected, **THEN** the header avatar and grid highlight
  both revert to the last server-confirmed `equippedMascotId`.
- **[Engineering-verified, not manual QA]** **GIVEN** the grid fully
  populated and scrolled, **WHEN** profiled, **THEN** draw calls stay
  ≤150 per ADR-0008's budget, and slot hit-areas measure ≥44×44pt/
  48×48dp including inter-slot spacing on a representative phone width.

**QA harness note (flagged by qa-lead review, not a design gap in this
GDD):** several criteria above are only exercisable with test-tooling
support that doesn't exist yet — a save-state inspector or debug fixture
to force a 0-mascot / multi-grant / admin-removed state on demand, and
visibility into the analytics outbox to confirm `mascot_equipped` fired
with the correct payload. These are `/qa-plan` / test-harness scope, not
something this GDD can or should resolve — carried to Open Questions so
it isn't silently dropped, matching the same pattern Daily Quests' own
Acceptance Criteria used for its equivalent gap.

## Open Questions

1. **QA test-harness gaps** (qa-lead review, Acceptance Criteria): a
   save-state inspector or debug fixture to force a 0-mascot /
   multi-grant / admin-removed-mascot state on demand, and visibility
   into the analytics outbox to confirm `mascot_equipped` fired with
   the correct payload. None of these tools currently exist — scope for
   a future `/qa-plan` pass, not this GDD.
2. **[RESOLVED 2026-07-18, see ADR-0006 §5 event-ownership split + ADR-0005 §2
   annotation]** `mascot_equipped` (Core Rule 7) is now formally in the
   server-authoritative outbox catalog — **but not via ADR-0004, as this Open
   Question originally assumed.** Equipping changes only
   `player_state.data.equippedMascotId` (Rule 4) with no currency mutation, so it
   never calls `mutateWallet`. Instead its analytics event is written to the shared
   `analytics_outbox` table **inside the `updatePlayer` transaction** (ADR-0005) —
   exactly-once, same-transaction, server-authoritative, never client-emitted
   (registry client-emission-ban updated to include it). This is the first
   non-money event to ride ADR-0005's outbox path rather than ADR-0004's currency
   path; the outbox *table* is shared, the *writing transaction* is whichever
   chokepoint made the change.
3. **The Marquee-Orange-border-plus-checkmark "currently selected"
   visual pattern (Visual/Audio Requirements) has no art-bible
   precedent** — this is the first system needing that language. Worth
   folding back into the art bible as a formal pattern if any future
   system reuses it (e.g. a future settings/preferences screen), rather
   than each new GDD re-deriving it independently.
4. **Cosmetic mascot skins remain explicitly out of scope** (Overview)
   — a separate, undesigned Shop-driven monetization layer mentioned in
   `game-concept.md`. Not assumed to interact with equip selection in
   any particular way until that system is actually designed.
5. **The Tuning Knobs' pooled-cell-count value is sized for the 16-slot
   MVP roster** — as the roster grows via future content updates, this
   knob (and the grid-columns knob) may need re-tuning to stay
   comfortable at a larger scale. Not solved here; flagged as a future
   tuning pass once real roster-growth plans exist, not a launch
   blocker.
