# REFACTOR_BACKLOG

Notes captured during the staged refactor (see `REFACTOR_PLAN.md`). Append-only.
Each entry is dated and self-contained.

## Lessons Learned

### NUL-byte corruption on large-file Edit-shrinks (FUSE-specific)

The Claude.ai Write/Edit tools, when shrinking large C# files (>60 KB,
e.g. `Engine/Engine.cs`) through the FUSE mount, can leave trailing NUL
bytes equal to the number of shrunk bytes. The logical content is correct;
the physical file is NUL-padded back to the original size.

Mitigation pipeline (applied before every commit):

1. Edit the file.
2. Verify: `wc -c`, `md5sum`, `tail -c 50 | xxd`, `tr -cd '\0' | wc -c`.
3. If NUL > 0: strip trailing NULs via Python
   (`while data.endswith(b'\x00'): data = data[:-1]`).
4. Re-verify: NUL count must be 0.

Manual removal (step 3) is still required; the verification step (2) is
the backstop that catches the issue before it reaches git.

Observed incidents:

- 2026-04-17/18 FASE 0.A — `Tests/Characterization/CharacterizationHarness.cs`
  shrink from 8325 B to 2150 B left 6175 trailing NULs.
- 2026-04-18 FASE 1.1 — `Engine/Engine.cs` shrink from 65491 B to 65445 B
  left 46 trailing NULs.

Python-direct writes (bypass Edit tool, use `open(f, 'wb').write(...)`)
do not trigger this — confirmed cleanly in FASE 1.2 across 11 files.

### FUSE truncation variant (2026-04-18) — 4 incidents observed

Tijdens FASE 8.1 docs-update ontdekt: Edit-tool op
`CODEBASE_AUDIT_REPORT.md` en `REFACTOR_PLAN.md` truncateerde files tot
originele byte-grootte, nieuwe content toegevoegd maar staartregels
kwijtgeraakt (302→297 regels, 283→279 regels). Nieuwe variant van
FUSE-bug: geen NUL-padding, maar trailing truncation.

Detection signal: post-Edit `wc -c` matches BEFORE-edit size exactly
(niet de verwachte new size). Verifieer altijd `wc -c` matches
expected delta. Additional detection: file tail ends mid-word or
without trailing newline.

Scope has escalated through 4 incidents; the failure is not limited
to the main agent's Edit tool.

**Affected write-paths (all truncate on large markdown):**

- Main agent `Edit` / `Write` tool targeting FUSE-mount paths.
- Sub-agent `Edit` / `Write` tool targeting Windows-style paths
  (`C:\\Users\\...\\ANTS`) — those paths route through the same
  FUSE mount; NTFS-safety assumption is false.
- `git checkout -- <file>` — checkout-time write through FUSE can
  also truncate. This was not observed before the 4th incident.

**Safe write-path (proven):**

- Bash shell tool writing via Python `open(f, 'wb').write(data)`.

**Preventive rules (cumulative):**

1. (2026-04-18, after 3rd incident) Voor markdown files >5 KB gebruik
   **Python-direct write VOORAF**, niet als recovery. Edit-tool is
   consistent onbetrouwbaar op grote markdown files op dit FUSE mount.
2. (2026-04-18, after 4th incident) **Sub-agents that write to
   Windows-style paths are NOT safe** for this repo — those paths
   resolve through the FUSE mount. Do not delegate docs-edits on
   large markdown files to sub-agents; do them inline via the bash
   shell tool.
3. (2026-04-18, after 4th incident) **`git checkout` is NOT a safe
   recovery** for FUSE-truncated files — the checkout itself can
   truncate the working-tree write. Use `git cat-file blob HEAD:<file>`
   to read from the object store (bypasses FUSE write-path), apply
   edits in memory, and write back via Python-direct.

**Recovery workflow (canonical):**

```
1. git cat-file blob HEAD:<file> > /tmp/<file>.head
2. Apply edits in Python (string-replace on known anchors)
3. open(f, 'wb').write(data.encode('utf-8'))   # via bash shell tool
   (NOT Edit/Write, NOT sub-agents, NOT git checkout)
4. 5-point verify: size delta matches expected, NUL count == 0,
   tail byte-dump reasonable, grep anchors present, git diff --stat
```

**Observed incidents:**

- 2026-04-18 FASE 8.1 docs: `CODEBASE_AUDIT_REPORT.md` +
  `REFACTOR_PLAN.md` truncated during Edit-tool call.
- 2026-04-18 plan-update commit: `REFACTOR_PLAN.md` truncated
  (recovered via `.git/index` backup + `git read-tree`).
- 2026-04-18 FASE 6.2 skip docs-commit: `REFACTOR_PLAN.md` +
  `REFACTOR_BACKLOG.md` **both files truncated simultaneously** in
  the same Edit-tool call sequence. Recovery: `git show HEAD:<file>`
  → Python-direct re-apply.
- 2026-04-18 FASE 6.3 docs-commit preparation (double failure mode):
  (i) sub-agent via `Edit` / `Write` tool targeting Windows-style
  paths truncated `REFACTOR_PLAN.md` + `REFACTOR_BACKLOG.md`
  simultaneously; the sub-agent assumed NTFS but the paths route
  through the FUSE mount. (ii) follow-up `git checkout -- <file>`
  *also* truncated the working-tree write — first time this
  failure mode was observed. Recovery:
  `git cat-file blob HEAD:<file>` (object store, bypasses FUSE
  write) → Python-direct re-apply via bash.

## Deferred Decisions

### Patrol enum value removal (2026-04-18)

`GoalType.Patrol` appears dead at first glance but serves as UI state-tag
for Attacker/Defender roles. The original audit (CODEBASE_AUDIT_REPORT.md
§3) claimed "only the enum declaration" — this was stale; repo-wide grep
finds 4 live consumers.

Removing it requires either:

(a) UI change accepting `"Explore"` label for non-foragers in the
    info-panel (Engine.cs:1903 renders `ant.Goal.Type.ToString()`), or
(b) refactor to separate logical-state (Goal) from UI-display-state
    (role-label), so attacker/defender ants can keep a distinct
    on-screen string without the enum value.

Deferred until an explicit UI-modernization task. FASE 8.1 is marked
SKIPPED in REFACTOR_PLAN.md.

References:

- `Simulation/Roles/AttackerRole.cs:48,50` — check-and-set.
- `Simulation/Roles/DefenderRole.cs:53,55` — check-and-set.
- `Engine/Engine.cs:1903` — `ant.Goal.Type.ToString()` info-panel display.

### ClearEnemyTrailForTarget removal (2026-04-18)

`PheromoneGrid.ClearEnemyTrailForTarget(int)` appears unused at first
glance but is called from `World.cs:372` inside
`ForgetEnemyTrailAboutDeadColony(Colony deadColony)`, which itself runs
from `DespawnDeadColonies` on every tick with dead colonies.

The method removes the dead colony's keys from 4 per-enemy-colony
dictionaries:

- `_enemyTrails`
- `_enemyDistances`
- `_enemyActiveCells`
- `_enemyActiveSet`

Deleting the method would cause:

(a) compile break at `World.cs:372` (CS1061), and if the caller is also
    removed,
(b) memory leak in the 4 dictionaries (grow-only over colony deaths),
    and
(c) harness-digest drift, because lingering enemy-trail data affects
    `pheromoneCellCount-per-channel` in the per-second digest and
    influences `SensorSystem` indirectly.

Not deferred for later — this is live code. FASE 8.2 is marked SKIPPED
in REFACTOR_PLAN.md and CODEBASE_AUDIT_REPORT.md §3 has been corrected.

References:

- `Simulation/Pheromones/PheromoneGrid.cs:233-239` — definition.
- `Simulation/World.cs:372` — only call site, inside
  `ForgetEnemyTrailAboutDeadColony`.
- `Simulation/World.cs:335-360` — `DespawnDeadColonies` call chain.

### FASE 6.3 skip — NormalizeAngle rewrite (2026-04-18)

Pre-flight comparison of 3 variants over 10020 inputs in range
`[-3π, 3π]`:

| Variant                       | Identical | Drift   |
|-------------------------------|-----------|---------|
| (a) while-loop (baseline)     | —         | —       |
| (b) `Math.IEEERemainder`      | 33.65%    | 66.35%  |
| (c) float-only `MathF.Round`  | 99.92%    | 0.08%   |

Drift sources:

- Float→double→float roundtrip (Bron A, ~1 ULP per wrap).
- Banker's rounding at `±π` boundary (Bron B, ~2π flip).
- Baseline asymmetry at `-π` (pre-existing bug, see next entry).

Decision: SKIP per Mario's zero-drift policy. No bit-exact O(1)
alternative exists that matches current baseline.

Reopen conditions:

- If baseline update becomes acceptable (non-refactor phase).
- If a bit-exact alternative is found.
- If perf profiling proves `NormalizeAngle` is hot path (currently
  0-1 iterations per call, not hot).

## Algorithmic precision notes

### Pre-existing -π asymmetry in NormalizeAngle (2026-04-18)

Current `NormalizeAngle` implementation has asymmetric behavior at
the `-π` boundary:

- `NormalizeAngle(+π_float)` → `-π_float` (expected wrap).
- `NormalizeAngle(-π_float)` → `+π_float` (UNEXPECTED — should stay
  at `-π`).

Root cause: the while-loop uses `Math.PI` (double) in the `<`
comparison. When input is `-(float)π`, implicit float→double
promotion gives `-3.14159274...`, which is strictly less than
`-Math.PI` (`-3.14159265...`), triggering an `a += 2π` iteration
and producing `+π_float` instead of staying at `-π_float`.

This means `NormalizeAngle`'s output range is effectively `(-π, +π]`
rather than `[-π, +π]` as documented.

Baseline includes this asymmetry in its digest. Any fix (use
`MathF.PI` everywhere, or use `<=` instead of `<`) would drift the
baseline.

Deferred to FASE FUTURE (non-refactor bug-fix phase where baseline
update is acceptable). Not urgent — asymmetry is invisible to
gameplay (ants don't care whether their heading says `-π` or `+π`,
they both point in the same direction).

Discovered: 2026-04-18 during FASE 6.3 pre-flight analysis.

## Tech-debt identified during refactor (for FASE 4 Engine split)

### Lunge rendering leaks CombatTuning into Engine.cs

During FASE 7.3 CombatTuning extraction, it was noted that `Engine.cs`
directly reads `CombatTuning.LungeDuration` and `CombatTuning.LungeDistance`
for lunge animation rendering (lines 1136, 1138).

This violates "Engine = only engine concerns" principle — combat tuning
values should not be read by rendering code directly.

Resolution: FASE 4 Engine split will move lunge-animation rendering
from `Engine.cs` to a dedicated renderer (`WorldRenderer.cs` or
`OverlayRenderer.cs`). That renderer takes the `CombatTuning` reference
instead of `Engine.cs`.

Impact: removes last `CombatTuning` dependency from `Engine.cs`.
Risk: L (part of FASE 4 extraction work anyway).
Discovered: 2026-04-18 during FASE 7.3 scope-report.

## Dead code candidates (for FASE 8 follow-up)

### UiLineChart (Engine/Ui/UiLineChart.cs)

Status: declared, never instantiated. Full grep-audit shows 0 external
references (no `new UiLineChart(`, no field declarations, no method
params of this type).

Historical: added 2026-04-17 in commit 5975c2a "new ui" along with
the Engine/Ui/ folder. Never wired up to any caller since.

Resolution options (decision needed before FASE 8 sweep):

(a) Delete UiLineChart.cs entirely — clean up unused code
(b) Wire up a caller (fps graph, colony stats overlay, performance
    overlay) - then FASE 6.2 becomes relevant again for paints
    pre-build

Discovered: 2026-04-18 during FASE 6.2 scope-report.

## Post-refactor feature requests

### Combat: rotate-to-face before attacking

Current behavior: ants attack regardless of facing direction
(can hit enemies at side/back).
Desired behavior: ant must rotate to face enemy (enemy in vision cone)
before committing to attack animation.
Impact: harness digest changes (this is a gameplay tweak, not refactor).
Effort: small feature, requires vision-check gate before CombatSystem
attack trigger.
Discovered: 2026-04-18 during FASE 7.3 visual testing.

### Pheromone overlay performance (tracked as FASE 6.6)

Current behavior: rendering with pheromone overlay enabled drops fps
from 2000+ to ~70 (96% drop).
Desired behavior: same visual result, significantly better fps.
Impact: harness digest unchanged (simulation logic identical);
rendering-only optimization.
Tracked as: `REFACTOR_PLAN.md` FASE 6.6 (added 2026-04-18).
Effort: profiling-driven; candidate approaches include SKPicture
caching, dirty-flag partial updates, tile-based rendering,
lower-resolution overlay texture.
Discovered: 2026-04-18 during FASE 7.1 visual testing.

### UI draw is now the dominant per-frame cost (~200 μs/frame)

Current behavior: after FASE 6.6 (pheromone overlay optimalisatie) blijkt
de UI-draw consistent ~189-209 μs per frame te kosten — onafhankelijk van
of de overlay AAN of UIT staat. Dat is:

- 65% van frame time bij overlay UIT (FRAME med ~300 μs)
- 37% van frame time bij overlay AAN (FRAME med ~550 μs)

Deze kost zat voorheen verstopt achter de 7 ms overlay. Na STAP B
(overlay 7260 → 540 μs) is UI de nieuwe bottleneck.

Verdachte componenten (gemeten via per-phase profiler V2, subsequently removed):

- `DrawStatsPanel` per colony (4× bij test): panel background + border,
  populatiegrafiek (60 datapoints), role breakdown (4 rows met bars),
  queen intent + defense/offense bars. ~30 SKPaint calls per colony.
- `_topBarPicture` / `_buttonsPicture` / `_hudPicture` draw zijn al
  SKPicture-gecached. Blit-cost van SKPicture moet laag zijn maar kan
  nog steeds meetbaar zijn bij hoge client-resolutie (2560×1369 in meting).

Mogelijke aanpakken:

- SKPicture cache voor `DrawStatsPanel` (alleen rebuild bij stat-changes,
  niet elke frame).
- Conditionele skip: panel alleen tekenen als zichtbaar/relevant.
- Batch meerdere stats panels in één SKPicture.

Impact: harness digest onveranderd (render-only).
Effort: middelgroot — vereist stats-dirty-flag propagation.
Scope: buiten FASE 6.6 (rendering pheromone overlay), candidate voor
FASE 7 of 8.
Discovered: 2026-04-19 tijdens FASE 6.6 Profiler V2 meting (Windows,
4 colonies, 02_corridors, 10× speed, 200k+ frames geanalyseerd).

### Follow-camera mutation in draw path (SelectionController)

Current behavior: `SelectionController.DrawOverlay` mutates
`_camera.OffsetX` / `_camera.OffsetY` inline (follow-selected-ant lerp).
Draw-path methods should be side-effect free; camera state should be
advanced during tick (simulation/UI update), not during rendering.

Impact: visual only (camera-follow keeps same behavior); latent bug
surface if/when render throttling decouples from tick rate.
Effort: small — extract an `UpdateFollowCamera()` method, call it from
`Tick()` before `Invalidate()` instead of from `DrawOverlay`.
Scope: out-of-scope for fase-4.3 (pure move only). Candidate for a
follow-up input/simulation-refactor phase.
Discovered: 2026-04-19 during fase-4.3 scope-report review.

### DrawSelectedAntInfoPanel allocates SKPaint per frame (perf-rule-5/8 violation)

Current behavior: `SelectionController.DrawInfoPanel` allocates two
`SKPaint` instances (`bgPaint`, `textPaint`) every render via
`using SKPaint bgPaint = new SKPaint();`. This violates the project rule
that SKPaints should be cached in `PaintCache` and only mutated, not
allocated per frame.

Impact: micro-GC churn during selection-visible frames. Not a harness
concern (panel not active in headless runs). Measurable only when an
ant is selected.
Effort: small — add dedicated info-panel paints to `PaintCache` or a
renderer-local cache; replace `using new SKPaint()` with paint reuse.
Scope: out-of-scope for fase-4.3 (pure move). Fold into fase-4.10
StatsPanelRenderer context or a dedicated info-panel renderer pass.
Discovered: 2026-04-19 during fase-4.3 scope-report review.

### Nest geometry duplicated between ghost and permanent SKPicture

Current behavior: the Manhattan-diamond nest shape is rendered in two
places with identical geometry but from different code paths:

- `PlacementController.DrawNest` — ghost preview during colony
  placement, uses the controller's own `_ghostNestPath` scratch.
- `Engine.RecordNestsPicture` — permanent per-colony nest rendering
  into `_nestsPicture` (SKPicture cache), uses Engine's `_nestPath`.

Both loops over `[-NestRadius, +NestRadius]` filtering by
`|dx| + |dy| <= NestRadius` and emit identical cell rects. Any future
change to nest shape (e.g. hexagonal, larger radius, chamfered) must
be kept in sync manually.

Impact: none today (identical output guaranteed by identical code).
Latent risk: divergence on future shape change.
Effort: small — extract a static helper like
`NestGeometry.BuildPath(SKPath path, int cellX, int cellY, int cellSize)`.
Scope: deferred to fase-4.13 cleanup or later.
Discovered: 2026-04-19 during fase-4.4 scope-report review.

---

## _buttons list ref stability assumption (InputRouter)

`InputRouter` receives `_buttons` as `IReadOnlyList<UiButton>` in its
constructor and holds that reference for the lifetime of the router.

Current guarantee: `Engine._buttons` is initialized inline as a fresh
`List<UiButton>()` and is never reassigned. `RecalculateLayout()`
mutates the list in-place via `_buttons.Clear()` followed by
`.Add(...)` calls, so the List instance — and therefore the reference
held by `InputRouter` — stays stable across relayouts.

Impact: none today (reference stable by construction).
Latent risk: if a future refactor ever replaces the List instance
(e.g. `_buttons = new List<UiButton>();` or a swap for a different
collection type), `InputRouter` would hold a stale reference and
button hit-testing / hover-diffing would silently break.

If _buttons list reference ever reassigned in future refactor,
InputRouter would hold stale ref. Current pattern: Clear() + Add()
keeps ref stable. Defensive `Func<IReadOnlyList<UiButton>>` wrapper
deferred until needed.

Impact: none today.
Effort: small (change ctor arg type, add lambda at call site).
Scope: defer until an actual reassignment need arises.
Discovered: 2026-04-19 during fase-4.5 scope-report review.

---

## CellSize duplicated across multiple renderers

`const int CellSize = 16;` is currently declared in:

- `Engine.Engine.cs` (L13) — used for camera/world sizing, ant rendering,
  pheromone overlay, cull math.
- `Engine.PlacementController.cs` (L16) — hard-coded duplicate (fase-4.4).
- `Engine.SelectionController.cs` — inline const (fase-4.3).
- `Engine.WorldRenderer.cs` (L22) — hard-coded duplicate (fase-4.6).

All four currently hold the same value (16). Any future change to the
cell size would require editing all four sites and risk silent
divergence if one is missed.

Impact: none today (identical values).
Latent risk: divergence on future resize.
Effort: small — introduce `Engine/RenderConstants.cs` with
`public const int CellSize = 16;` (and any other render-tuning
constants). All call sites import it. Or, per ctor-arg pattern if
we want true per-instance configurability.

CellSize duplicated across PlacementController, SelectionController,
WorldRenderer. Consolidate in RenderConstants.cs in fase-4.13 cleanup.

Scope: deferred to fase-4.13 cleanup.
Discovered: 2026-04-19 during fase-4.6 WorldRenderer extract.

## Shared paint cross-mutation — AntPaint.ColorFilter

Location: `Engine/AntsRenderer.cs` (mutates `PaintCache.AntPaint.ColorFilter`
inside `ApplyBodyTint`) (fase-4.7).

Shared paint cross-mutation: AntsRenderer exclusively mutates
PaintCache.AntPaint.ColorFilter. If future code adds another mutator,
undefined behavior possible. Consider per-renderer paint instances in
fase-4.13 or later.

Scope: deferred to fase-4.13 or later.
Discovered: 2026-04-19 during fase-4.7 AntsRenderer extract.

## SharedFill mutation without try/finally — DrawAntDots

Location: `Engine/AntsRenderer.cs::DrawAntDots` (fase-4.7).

DrawAntDots mutates SharedFill.StrokeCap/StrokeWidth and resets at end.
If exception thrown mid-function, shared paint stays in modified state.
Defensive try/finally deferred - low-risk path.

Scope: deferred.
Discovered: 2026-04-19 during fase-4.7 AntsRenderer extract.

## Naming ambiguity: AntRenderer vs AntsRenderer

Location: `Engine/AntRenderer.cs` (static, sprite atlas) and
`Engine/AntsRenderer.cs` (instance, rendering logic) (fase-4.7).

Naming ambiguity: AntRenderer.cs (static, sprite atlas) vs
AntsRenderer.cs (instance, rendering logic). Visually near-identical
names cause confusion in file explorer. Consider renaming in fase-4.13:
- AntRenderer.cs -> AntSpriteAtlas.cs (describes what it contains)
- AntsRenderer.cs -> AntRenderer.cs (single, primary renderer)

Scope: deferred to fase-4.13 cleanup.
Discovered: 2026-04-19 during fase-4.7 AntsRenderer extract.

---

## FASE 5: Pheromone Overlay Performance Sprint (PLANNED POST-FASE 4)

Goal: address 4 pheromone-overlay perf observations identified during
fase-4.8 OverlayRenderer extract. Each sub-phase is characterization-
gated (byte-identical digest must be preserved) and measured against
the current overlay budget (~216μs end-to-end).

These are EXPLICITLY PLANNED, not general-backlog deferred. Execution
follows the fase-6.6 template: baseline profile → targeted change →
harness byte-identity → before/after microbenchmark.

### FASE 5.1: Obs-C — bounds-check elimination in tight pixel loop (HIGH VALUE)

Observation: the `homeArrs[c][x, y]` and `foodArrs[c][x, y]` reads in
the per-pixel inner loop trigger two JIT bounds-checks per access on
every frame. For an 80×80 viewport × 2 channels × N colonies this is
measurable.

Approach: pin the multidim arrays or switch to `float[]` row-major with
manual indexing (`arr[y * width + x]`) so the JIT can hoist bounds
checks. Alternatively, switch to `Span<float>` with aggressive
inlining.

Risk: requires matching row-major layout with `PheromoneGrid` storage.
If PheromoneGrid uses `[x, y]` (x outer, y inner contiguous), the
iteration order in DrawPheromoneOverlay (x outer, y inner) is already
cache-friendly; only bounds-check elimination is at play.

Expected win: 30-60μs per overlay frame (rough estimate pending
microbenchmark).

### FASE 5.2: Obs-B — dirty-rect tracking for overlay buffer

Observation: every overlay frame clears both `homeBuf` and `foodBuf`
in full (`Array.Clear(..., bufSize)`) then walks the viewport rect.
Most pixels never change between consecutive frames (pheromones decay
slowly).

Approach: track per-colony dirty rects in `PheromoneGrid` (the active-
cells set already knows which cells changed); in OverlayRenderer, only
re-render the union dirty rect instead of the full viewport. Also
consider skipping the `Array.Clear` when the buffer is already zero
in untouched regions.

Risk: invalidation correctness. Pheromone decay means cells silently
drop below cutoff without being marked dirty — need to track both
"newly written" and "newly fell below cutoff" regions.

Expected win: 80-150μs per overlay frame when few cells change
(common case mid-game).

### FASE 5.3: Obs-A — loop-order / SIMD opportunity

Observation: the inner pixel loop does byte×byte multiply-divide-round
(SkMulDiv255Round) four times per active pixel, scalar. This is a
perfect SIMD candidate.

Approach: use `System.Numerics.Vector<byte>` or `Vector128` to batch
4 or 8 pixels at a time for the MulDiv255 + premultiply step. Requires
restructuring the inner loop to collect a strip of pixels then vectorize.

Risk: layout change must not break byte-identity digest. SIMD vs
scalar rounding may differ by 1 LSB → must verify digest.

Expected win: 40-80μs per overlay frame in high-pheromone scenarios.

### FASE 5.4: Obs-D — DrawBitmap consolidation (single blit)

Observation: OverlayRenderer issues two `DrawBitmap` calls per frame
(home bitmap + food bitmap). Each is a separate GPU draw call with
texture upload.

Approach: composite home and food into a single RGBA bitmap before
blit. Home uses colony color; food uses fixed green. Both channels
can be packed into one buffer with RGB = max(homeRGB, foodRGB) and
A = max(homeA, foodA), or use a 2-texture shader draw.

Risk: blending order matters. Current code draws home first then food;
the alpha compositing is additive in SrcOver mode. A single-bitmap
version must match the exact per-pixel result.

Expected win: 20-40μs per overlay frame (one fewer draw call, one
fewer texture upload).

Scope: planned for FASE 5 sprint after FASE 4 completion.
Discovered: 2026-04-19 during fase-4.8 OverlayRenderer extract.
