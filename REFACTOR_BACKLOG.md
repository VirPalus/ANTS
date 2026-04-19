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

## EMA reset on map-switch (cosmetic)

Location: `Engine/HudRenderer.cs` (fase-4.9) — `_simStageMs` and
`_antStageMs` persist across map reloads.

After a map-switch, HUD still shows decaying EMA values from the
previous map for ~20-40 frames before converging to the new map's
timing. Cosmetic only — no functional impact.

Fix direction: expose `HudRenderer.ResetStageEmas()` that zeroes both
EMAs, and call it from the MapLoader reload path in Engine. Also
consider resetting `_framesThisSecond` + `_fps` so the FPS display
also snaps cleanly.

Scope: low-priority cosmetic polish. Candidate to bundle with the
fase-4.10 StatsPanelRenderer extract (StatsRenderer has its own
per-colony graph buffers that also cross-contaminate on map-switch).
Discovered: 2026-04-19 during fase-4.9 HudRenderer extract.

## HUD rebuild frequency observation (fase-4.10 context)

Location: `Engine/HudRenderer.cs` — `HudUpdateIntervalMs = 50` (20 Hz).

Observation: HUD-rebuild itself is cheap (~30-50μs per call: one
SKPictureRecorder + 8 DrawText + 1 DrawWithBorder). At 20 Hz that
is ~0.6-1.0 ms/sec = 0.06-0.1% CPU — negligible.

The real cost driver is the COUPLED stats rebuild in Engine.Tick:
`if (_hudRenderer.MaybeRebuild()) { RecordStatsPicture(); }`.
`RecordStatsPicture()` is significantly heavier (per-colony graph
rendering, many more DrawText calls) and currently rebuilds at the
same 20 Hz cadence as HUD.

Fix direction: fase-4.10 StatsPanelRenderer extract should give
stats-rebuild its own throttle stopwatch with a slower cadence (e.g.
4-5 Hz = 200-250ms interval). Stats visuals change on a slower scale
than HUD (colony counts, role distribution) — no user-visible loss.

Expected win: 70-85% reduction in stats-rebuild CPU during steady
state (from ~20 Hz down to ~4-5 Hz).

Scope: planned for fase-4.10 StatsPanelRenderer extract as a natural
byproduct of the decoupling.
Discovered: 2026-04-19 during fase-4.9 HudRenderer extract.

---

## FASE 5.5: Stats Panel Performance Sprint (PLANNED POST-FASE 4)

Goal: address 3 stats-panel perf observations identified during
fase-4.10 StatsPanelRenderer extract. Each sub-phase is
characterization-gated (byte-identical digest must be preserved) and
measured against the current stats-rebuild budget at its now-throttled
4 Hz cadence.

Execution follows the FASE 5 template: baseline profile → targeted
change → harness byte-identity → before/after microbenchmark. These
observations were deferred from fase-4.10 (which explicitly limited
itself to "extract + rebuild-cadence throttle" — no paint-path
surgery).

### FASE 5.5-F: Obs-F — SKPath per-graph allocation in DrawPopulationGraph

Observation: `DrawPopulationGraph()` allocates a fresh `SKPath` per
colony per stats-rebuild. At 240 samples the path receives one
`MoveTo` + up to 239 `LineTo` calls. Inside SKPictureRecorder the
SKPath itself is cheap, but the allocation + disposal churn is
avoidable.

Approach: hoist a reusable `SKPath` into StatsPanelRenderer as a
field; `Reset()` between colony draws. Alternatively, consider
building a `float[]` x-stride + `float[]` y-stride pair and issuing
`DrawPoints(SKPointMode.Polygon, ...)` — one allocation-free call
per graph.

Risk: byte-identity — DrawPoints and Path-stroke rasterization rules
are equivalent only when stroke width ≥ 1 and cap/join settings
match. Need digest verification.

Expected win: 5-20μs per stats-rebuild (once per 250ms cadence, so
20-80μs/sec of wall time — small but mechanically clean).

### FASE 5.5-G: Obs-G — String allocations in DrawStatsCard

Observation: `DrawStatsCard()` and the signal/role sub-draws call
`count.ToString(CultureInfo.InvariantCulture)` and string
interpolation per stats-rebuild for counts, pressures, and role
breakdowns. ~30-50 small string allocations per card × N colonies.
At the new 4 Hz cadence this is 120-200 string allocs/sec just from
stats.

Approach: either (a) use stackalloc + `Utf8Formatter` with an
`SKPaint.DrawText(ReadOnlySpan<byte>...)` path, or (b) cache last-
drawn values and re-use the formatted string when the underlying
value is unchanged, or (c) adopt a pooled `char[]` with
`TryFormat` + `DrawText(string, int, int, ...)` overloads.

Risk: low — formatting output must be bit-identical to the current
culture-invariant ToString. `TryFormat` with InvariantCulture is a
direct swap-in.

Expected win: measurable GC reduction; ~1-2 μs/card × N colonies
per rebuild, but mostly GC-pressure relief (Gen0 churn).

### FASE 5.5-H: Obs-H — Stats cullRect is too generous

Observation: `RecordStatsPicture()` uses
`SKRect.Create(0, 0, StatsPanelWidth + 20, clientHeight + 20)` as
the picture's cull rect. This bound is conservative — actual draw
content only extends from `UiTopBar.BarHeight` down to the last
colony card bottom. SKPicture playback with a tight cull rect
enables SkiaSharp to skip clipped operations faster.

Approach: compute the actual content height as
`colonies.Count * (StatsCardHeight + StatsCardSpacing) +
UiTopBar.BarHeight + slackPadding` and use that as the cullRect
height. Width similarly: `StatsPanelWidth + 2*slackPadding`.

Risk: if new draw content is added below the last card without
updating the cullRect formula, it will be silently clipped.
Mitigation: add a runtime assert in DEBUG builds when any draw
operation's bounds exceed the cull rect.

Expected win: marginal (~2-5% picture-playback speedup).

Scope: planned for FASE 5 sprint after FASE 4 completion.
Discovered: 2026-04-19 during fase-4.10 StatsPanelRenderer extract.

---

## Stats panel scroll UX (feature request)

Location: `Engine/StatsPanelRenderer.cs` (post-fase-4.10).

Observation: the stats panel renders one card per colony stacked
vertically. With 4+ colonies at typical window heights the bottom
cards overflow below the window viewport and are not visible. There
is currently no scroll state — fase-4.10 grep confirmed zero
existing `_statsScrollOffset` or `_statsScrollbarDragging` fields
(i.e. scrolling was never implemented).

Fix direction: add scroll state inside StatsPanelRenderer
(`_scrollOffset` float, `_scrollbarDragging` bool, `_dragStartY`,
`_dragStartOffset`). Wire mouse-wheel events (WndProc WM_MOUSEWHEEL)
and a scrollbar track on the right edge of the panel. RebuildNow
remains cadence-throttled; scroll is applied as a canvas translate
in Draw() so no rebuild is needed on scroll.

Scope: user-facing feature, not a performance refactor. Candidate
for FASE 7+ UX polish sprint.
Discovered: 2026-04-19 during fase-4.10 StatsPanelRenderer extract
scope-report (user asked about existing scroll state; none found).

---

## StatsPanelRenderer.RebuildNow placement risk

Location: `Engine/StatsPanelRenderer.cs` (fase-4.10 public API).

Observation: StatsPanelRenderer exposes both `MaybeRebuild(width,
height)` (throttled, called every tick) and `RebuildNow(width,
height)` (unconditional rebuild, currently unused). The intent of
RebuildNow is to give external callers (Engine map-reload path, or
a debug "invalidate" button) a way to force a fresh picture
immediately rather than waiting up to 250ms.

Risk: if a future caller invokes `RebuildNow` on every tick (bug or
bad coupling), the 4 Hz throttle is silently defeated and the stats
panel reverts to its pre-fase-4.10 cadence. There is no runtime
guard against this misuse.

Fix direction: either (a) remove `RebuildNow` until a concrete need
surfaces (YAGNI) — simplest option; or (b) add a DEBUG-only assert
inside RebuildNow that tracks call-count-per-second and logs a
warning if invoked more than once per second.

Scope: low priority — no current misuse, but API surface risk worth
flagging.
Discovered: 2026-04-19 during fase-4.10 StatsPanelRenderer extract.

---

## fase-4.11 → fase-4.12 wire-up (tracking note)

fase-4.11 landed profiling infrastructure as pure addition (4 files
under `Engine/Profiling/`): `ProfileSample.cs`, `RingBuffer.cs`,
`FrameProfiler.cs`, `ProfileWriter.cs`. Engine.cs and all renderers
were untouched (md5 verified ongewijzigd).

The infrastructure is dead code until fase-4.12 wires callers. State
of the classes as landed:
- `FrameProfiler` instantiable but never instantiated by production code
- `ProfileWriter` constructible only via `FrameProfiler.Enable()`
- `RingBuffer<ProfileSample>` only live through `FrameProfiler`
- `ProfilePhase` enum referenced only by `FrameProfiler` internals

Dead-code gate: build with `-warnaserror` passes because the classes
are `public sealed` (CA1812 scope is internal types) and every
private field is touched on some reachable code path.

Pending for fase-4.12 (do not start yet):
1. Construct `FrameProfiler` in Engine ctor via `Own(new FrameProfiler())`.
2. Insert `BeginFrame` / `EndFrame` around the body of `Engine.Tick`
   (must NOT replace the existing HudRenderer timing calls — they
   coexist; HUD = always-on, profiler = opt-in toggle).
3. Insert `BeginPhase(Sim)` / `EndPhase(Sim)` around `_sim.Advance()`.
4. Insert `BeginPhase(PaintTotal)` around the body of `OnPaint`, plus
   the 5 inner phase pairs (WorldDraw, OverlayDraw, AntsDraw,
   StatsDraw, HudDraw).
5. Add a top-bar toggle button + F2 hotkey that calls
   `Enable()` / `Disable()`.
6. Add a HUD status indicator that reads `FrameProfiler.IsEnabled`
   and `FrameProfiler.Ring.DroppedCount`.

Observer-effect note for fase-4.12 design: per-phase timing will then
incur BOTH HUD timing (existing, always-on) AND profiler timing
(new, opt-in) = 4 `Stopwatch.GetTimestamp()` calls per phase instead
of 2 when the profiler is enabled. Overhead doubles from ~0.018% to
~0.036% of a 4.17 ms frame budget at 240 FPS. Acceptable as a first
cut; a future optimization could route HUD timing through the
profiler when enabled. NOT an issue to fix in fase-4.11 or fase-4.12.

Discovered: 2026-04-19 during fase-4.11 infrastructure landing.
---

## fase-4.12 landing note (observer effect accepted)

fase-4.12 wired the profiler exactly as planned in the fase-4.11
tracking note above. Scope delivered:
- `FrameProfiler` constructed in Engine ctor via `Own(new FrameProfiler())`.
- Accumulate pair (`AccumulatePhaseBegin` / `AccumulatePhaseEnd`)
  added to `FrameProfiler` to support the WorldDraw split around
  the optional pheromone OverlayDraw segment.
- `LastError` property added to `FrameProfiler` — Enable() catches
  writer construction/start exceptions and surfaces them instead
  of crashing the UI.
- New `Engine/ProfilerUI.cs` draws a HUD-adjacent status indicator
  (PROFILING label, dropped-sample counter, last-error text) only
  when `_profiler.IsEnabled`.
- Profiler button added to the bottom button row (4 buttons wide
  now: Add Colony / Add Food / Pheromones / Profiler).
- F2 hotkey routed through `InputRouter` (ctor param
  `onProfilerToggled`).
- HUD timing preserved unchanged — `ReportSimStageTicks`,
  `ReportAntStageTicks`, `ReportFrameTicks` still each appear
  exactly 1x in Engine.cs.

The observer-effect doubling (~0.018% → ~0.036% of a 4.17 ms frame
at 240 FPS) is accepted as designed; it applies ONLY when the
profiler is toggled on. With the profiler off the hot path is
zero-cost (single bool check per Begin/End* call before early
return).

Harness 3x byte-identical with digest
`78ad61829002a3194554ac4681feb98e` (profiler never Enabled by
harness, so all Begin/End* calls early-return).

Discovered: 2026-04-19 during fase-4.12 wiring.

### fase-4.12-fixup — Variant A (Profiler HUD + Graph Window)

Resolves the six defects from the rejected fase-4.12 implementation.
Scope delivered:

- **Top-bar Profile button** flush-right on the top bar (88×32),
  red-tinted background when active (`SKColor(140, 40, 40)`), hover
  states. MapName centered between the speed selector and the
  profile button. Bottom-row button count reduced from 4 to 3
  (the old `profilerButton` was moved to the top bar — 0 references
  remain in `Engine.cs`).
- **6-row HUD status panel** 220×196 drawn below the main HUD at
  `y = UiTopBar.BarHeight + HudGap + HudHeight + HudGap` (= 148f):
  REC + rotation index, Frames, AvgFr, AvgRd, AvgOv, File (CSV
  basename, truncated to 20 chars). Red accent `SKColor(220, 70, 70)`
  for the ● dot and the `REC` label.
- **Running EMA averages** (`AvgFrameMs`, `AvgRenderMs`,
  `AvgOverlayMs`) on `FrameProfiler`, `EmaAlpha = 0.05`. New
  `ReportFrameTicks(long)` public entry point. Fed inside
  `EndFrame()` so averages stay in lockstep with the per-frame
  ring-buffer push into `ProfilerSeries`.
- **CSV output relocated to `%TEMP%`** (was
  `AppContext.BaseDirectory + /ProfileLogs/`). Files now named
  `pheromone_profile_{N}.txt` (1-based rotation index, incremented
  BEFORE naming so `RotationIndex` returns the CURRENT file).
  `CurrentFileName` and `RotationIndex` exposed as thread-safe
  public properties (lock-guarded on `_ioLock`). CSV header extended
  with the seven new sub-phase columns (`grid_us`, `food_us`,
  `nests_us`, `clear_us`, `inner_us`, `marshal_us`, `bitmap_us`).
- **ProfilerGraphWindow** — draggable/resizable overlay window
  (min 600×400, remembers position across Show/Hide, auto-centers
  on first open, clamped to client bounds). Three sub-graphs stacked
  vertically: (1) seven paint-stage metrics (frame/sim/world/overlay/
  ants/stats/hud/paint), (2) three world sub-phases (grid/food/nests),
  (3) four overlay internals (clear/inner/marshal/bitmap). Title bar
  with zoom-level label (`1s` / `5s` / `15s` / `30s`), `[-]`, `[+]`
  and `[X]` buttons. Bottom-right resize triangle.
- **ProfilerSeries** — 13 per-metric float[18000] ring buffers
  (~912 KiB budget total). `CopyLast(int metricIndex, Span<float>)`
  yields samples oldest→newest. Feeds the graph window.
- **Variant A instrumentation split**: `WorldRenderer.DrawBase`
  and `DrawFoodNestsAndGridLines` now use `AccumulatePhase*` for
  `GridDraw` and regular `BeginPhase`/`EndPhase` for `FoodDraw` /
  `NestsDraw` — replaces the old single `WorldDraw` phase.
  `OverlayRenderer.Draw` wraps the two `Array.Clear` calls, the
  x/y inner loop, the two `Marshal.Copy` calls, and the two
  `canvas.DrawBitmap` calls in `ArrayClear` / `InnerLoop` /
  `MarshalCopy` / `DrawBitmap` phases respectively.
- **InputRouter top-most capture**: `OnMouseDown` routes to
  `ProfilerGraphWindow.HandleMouseDown(...)` first when
  `IsVisible`, returning early on consume. `OnMouseMove` and
  `OnMouseUp` forward unconditionally so in-flight drag/resize
  gestures continue even when the cursor leaves the window bounds.
- **F2 hotkey** preserved unchanged (1 occurrence in
  `InputRouter.cs`, matches the existing `onProfilerToggled`
  callback which now also shows/hides the graph window).

Zero-cost-when-disabled guarantee preserved. All
`AccumulatePhaseBegin` calls go through `FrameProfiler.IsEnabled`
early-exit, same as the regular `BeginPhase` path.

Harness 3× byte-identical with digest
`78ad61829002a3194554ac4681feb98e` (profiler never Enabled by
harness).

Landed: 2026-04-19.
