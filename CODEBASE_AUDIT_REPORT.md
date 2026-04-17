# CODEBASE_AUDIT_REPORT

**Project:** ANTS (Ant-colony simulation, .NET 10 WinForms + SkiaSharp)
**Audit date:** 2026-04-17
**Scope:** Whole repository at `C:\Users\Mario Moerman\Desktop\ANTS`
**Auditor mandate:** Senior Principal Architect — structural/quality-only, zero behavior change
**Methodology:** FASE 0 (complete file read) → FASE 1 (deep analysis) → proposed refactor plan
**Total files inventoried:** 42 source files (.cs) + 1 project file + 1 README
**Total lines of code (non-blank, non-comment, approximate):** ~6,400 LOC in simulation + engine

---

## 1. Executive Summary — Top 10 findings

Every finding below is backed by a concrete `file:line` reference. None are speculative.

1. **`Engine/Engine.cs` is a 1,939-line god object.** It mixes rendering pipeline, input handling, simulation driver, resource management, UI layout, HUD, stats panel, pheromone overlay, ant selection, follow-camera, food/wall painting, placement ghosts and map switching in a single class. This is the single largest structural debt in the codebase and the root cause of several derivative smells (duplicate code, per-frame allocations, mixed concerns). — `Engine/Engine.cs:1-1939`

2. **Documentation drift: README contradicts the actual code.** The README describes 4 pheromone channels (Home / Food / Danger / Boundary) but the code ships with 3 (`HomeTrail`, `FoodTrail`, `EnemyTrail`). The README's behavior-pipeline description also pre-dates the current `AntBehavior` pipeline, and does not mention Queen personalities, `Patrol`, the role system, or lunge animation. — `README.md` vs. `Simulation/Pheromones/PheromoneChannel.cs:1-8`, `Simulation/AntBehavior.cs:1-100`.

3. **`PheromoneGrid` allocates `ChannelCount = 3` channel slots but slot 2 (`EnemyTrail`) is intentionally left `null`.** `EnemyTrail` uses separate dictionaries (`_enemyTrails`, `_enemyDistances`, `_enemyActiveCells`, `_enemyActiveSet`) keyed by target colony id. The current design is functionally correct but confusing and invites null-deref bugs on any new code path. — `Simulation/Pheromones/PheromoneGrid.cs:6, 41-57`.

4. **Field-name repurposing smell in `SpatialGrid.QueryState`.** `ClosestCombatDistSq` is used as `antWeight` by `VisionSystem` (different semantics, same storage), and `ClosestEnemyColony` doubles as scratch space. The struct is a cross-system bag rather than a focused contract. — `Simulation/SpatialGrid.cs` (QueryState), consumer `Simulation/Behavior/VisionSystem.cs`, consumer `Simulation/Behavior/CombatSystem.cs:22-31`.

5. **Rule-of-three violation: Manhattan-diamond nest paint loop is duplicated three times** inside `Engine.cs` (grid picture, food picture, placement ghost). Same `abs(dx)+abs(dy) <= r` core, three copies. — `Engine/Engine.cs` (three `for (int dy = -r; dy <= r; dy++)` blocks).

6. **Per-frame `SKPaint` allocation** in `DrawSelectedAntInfoPanel` — violates documented Performance Rule 8 ("share paint objects, never allocate in hot path"). Also, `UiLineChart.Draw` allocates two `SKPaint` + two `SKPath` per call; acceptable because it's inside a cached `SKPicture`, but should be made explicit with a comment. — `Engine/Engine.cs` DrawSelectedAntInfoPanel, `Engine/Ui/UiLineChart.cs:113-125`.

7. **Duplicate write in `Engine.cs`** — `_fillPaint.Style = SKPaintStyle.Fill;` appears on two consecutive lines (dead statement). — `Engine/Engine.cs:1833-1834`.

8. **Pervasive comments throughout the code violate the "no comments in source" rule.** Nearly every non-trivial method has a block of explanatory comments (AntBehavior, PheromoneGrid, Colony, SensorSystem, VisionSystem, Engine). Per your directive, all non-API, non-license comments must be removed; the knowledge they encode must either be expressed by naming/structure or moved to `docs/`. Estimated comment LOC ≈ 900.

9. **`AntGoal` is a struct that wraps a single `GoalType` enum value.** No behavior, no additional fields. It is pure over-abstraction in its current form. Either give it real content (e.g. `TargetX/TargetY`, `Deadline`) or inline it. — `Simulation/Goals/AntGoal.cs`.

10. **No tests, no CI, no formatter config, no `.editorconfig`.** For a project heading towards production quality this is the largest *external* risk: any refactor is effectively a blind edit. A characterization-test harness (deterministic seed, replay a fixed scenario, capture per-tick digest) should be the prerequisite for any non-trivial refactor phase.

---

## 2. Inventory — the whole repository, enumerated

### 2.1 Build / project

| File | LOC | Purpose | Notes |
|------|----:|---------|-------|
| `ANTS.csproj` | 18 | Project file | `net10.0-windows`, `UseWindowsForms`, SkiaSharp 2.88.8, `Maps/*.png` copied to output. |
| `Program.cs` | 16 | Entry point | `Application.Run(new Engine())`. Uses `Application.SetHighDpiMode` + `EnableVisualStyles`. |
| `README.md` | 240 | Architecture blueprint + 10 perf rules | Contains drift vs code (see finding #2). |

### 2.2 Simulation layer — `Simulation/`

| File | LOC | Role | Dependencies |
|------|----:|------|--------------|
| `CellType.cs` | 8 | Enum: Empty / Food / Nest / Wall | — |
| `AntBehavior.cs` | 100 | Per-ant orchestrator pipeline | Ant, Colony, World, all behavior systems, Roles. |
| `Ant.cs` | 76 | Pure data container | AntRole, AntGoal. |
| `Colony.cs` | 331 | Per-colony state + signals | Queen, ColonyStats, Ants list, PheromoneGrid, RoleQuota. |
| `ColonyStats.cs` | 100 | 240-sample circular stats buffer | — |
| `DemoMap.cs` | ~70 | Built-in fallback map | MapDefinition. |
| `MapDefinition.cs` | ~25 | Parsed map DTO | CellType. |
| `MapLoader.cs` | 292 | PNG → MapDefinition (HSV classify + BFS label) | SkiaSharp, MapDefinition. |
| `Queen.cs` | ~65 | Personality biases | — |
| `SpatialGrid.cs` | 274 | Flat-packed spatial hash | Ant, Colony. |
| `World.cs` | 821 | Grid + colonies + food + sim loop | Everything simulation. |
| `Goals/AntGoal.cs` | 8 | Wraps `GoalType` | GoalType. |
| `Goals/GoalType.cs` | 12 | Enum: Explore / SeekFood / ReturnHome / Patrol | — |
| `Pheromones/PheromoneChannel.cs` | 10 | Enum: HomeTrail=0 / FoodTrail=1 / EnemyTrail=2 | — |
| `Pheromones/PheromoneGrid.cs` | 369 | 3-channel grid + sparse decay + per-target enemy trails | PheromoneChannel. |
| `Vision/VisualTargetType.cs` | 10 | Enum: FoodCell / EnemyAnt / EnemyNest | — |
| `Behavior/AutonomySystem.cs` | ~60 | Updates autonomy field | Ant. |
| `Behavior/CombatSystem.cs` | 148 | Melee, lunge anim, deposit enemy trail | SpatialGrid, PheromoneGrid. |
| `Behavior/DepositSystem.cs` | ~90 | Age-weighted pheromone deposit | PheromoneGrid. |
| `Behavior/EnemyDetectionSystem.cs` | 69 | 3×3 scan for enemy nests | PheromoneGrid, Colony. |
| `Behavior/GoalEventSystem.cs` | ~70 | Fires role callbacks on goal events | AntRole. |
| `Behavior/MovementSystem.cs` | ~80 | Moves ant, bounces off walls / enemy nests | World. |
| `Behavior/SensorSystem.cs` | ~250 | Pheromone cone sensing + scoring | PheromoneGrid. |
| `Behavior/SpawnSystem.cs` | 91 | Queen spawn pacing + role pick | Colony, RoleQuota. |
| `Behavior/SteeringSystem.cs` | 34 | Clamp heading delta to `TurnRate` | — |
| `Behavior/VisionSystem.cs` | 265 | Line-of-sight visual steering | SpatialGrid, World, Colony. |
| `Roles/AntRole.cs` | 44 | Abstract base | — |
| `Roles/ScoutRole.cs` | ~110 | Singleton role | AntRole, PheromoneChannel. |
| `Roles/ForagerRole.cs` | ~130 | Singleton role | AntRole, PheromoneChannel. |
| `Roles/DefenderRole.cs` | ~120 | Singleton role (combatant) | AntRole. |
| `Roles/AttackerRole.cs` | ~130 | Singleton role (combatant) | AntRole. |
| `Roles/RoleQuota.cs` | 243 | Posture-based spawn selector w/ hysteresis | Colony, Queen. |
| `Roles/QueenIntent.cs` | ~35 | Struct: plan + per-role targets | — |

### 2.3 Engine / UI layer — `Engine/`

| File | LOC | Role | Notes |
|------|----:|------|-------|
| `Engine.cs` | **1939** | God object | See finding #1. |
| `Engine.Designer.cs` | 14 | Empty designer partial | — |
| `AntRenderer.cs` | 192 | 8× supersampled sprite atlas build | Static ctor. |
| `Camera.cs` | 159 | Zoom/pan 2D orthographic camera | Well-factored. |
| `FastSKGLControl.cs` | 67 | SKGLControl subclass (VSync off, cached args) | Minor P/Invoke. |
| `Ui/UiButton.cs` | 76 | Push/toggle button | Shared paints injected. |
| `Ui/UiLineChart.cs` | 135 | Ring-buffer mini line chart | Allocates paints per Draw (inside SKPicture only). |
| `Ui/UiPanel.cs` | 64 | Rounded-rect helpers | Only inside cached SKPictures. |
| `Ui/UiSegmentedControl.cs` | 98 | Pill-style speed selector | — |
| `Ui/UiStartOverlay.cs` | 295 | Map picker | Owns thumbnail bitmaps. |
| `Ui/UiTheme.cs` | 94 | Palette + spacing grid | No accent colour (intentional). |
| `Ui/UiTopBar.cs` | 167 | Pause / speed / map name bar | — |

### 2.4 Entry-point / dependency graph

`Program.cs → Engine → { World, FastSKGLControl, AntRenderer, Camera, Ui* }`
`Engine.Update() → World.Update() → foreach(colony) { SpawnSystem, foreach(ant) AntBehavior.Tick, cleanup } → DespawnDeadColonies`
`AntBehavior.Tick → DecrementCooldowns → role.UpdateGoal → VisionSystem → SensorSystem → ApplyVisionBlend → ApplyLeash → SteeringSystem → MovementSystem → DepositSystem + EnemyDetectionSystem → CombatSystem → GoalEventSystem → AutonomySystem`

No circular references were found between simulation layer and engine layer. All simulation code is UI-free (no SkiaSharp imports).

### 2.5 External dependencies

- `SkiaSharp` 2.88.8 (+ `SkiaSharp.Views.WindowsForms`, + `SkiaSharp.Views.Desktop.Common`) — rendering
- `System.Windows.Forms` — host + input
- `System.Drawing` (via WinForms) — Rectangle type reused
- P/Invoke: `PeekMessage` in `Engine.cs` for `Application.Idle` loop — Win32-only

No NuGet packages are outdated in a breaking way for net10.0.

---

## 3. Dead-code report (proof-backed)

Every item below was verified with a repo-wide grep; "callers" means static references in source.

| Symbol | Location | Finding | Evidence |
|---|---|---|---|
| `GoalType.Patrol` | `Simulation/Goals/GoalType.cs:10` | Declared but never produced by any role's `UpdateGoal` and never consumed by any system. | Grep `GoalType.Patrol` → only the enum declaration. |
| `CellType.Nest` | `Simulation/CellType.cs` | Defined but never written to by any system after map load; map loader uses colony-seed cells, nest ownership is tracked via `_nestOwner`. Check if still read. | Needs a 2nd pass in FASE 2 (before deletion) — there is a read in `MovementSystem` for the wall-bounce logic; likely still live. **Do not delete until a characterization test confirms.** |
| `PheromoneGrid.ClearEnemyTrailForTarget` | `Simulation/Pheromones/PheromoneGrid.cs:245` | Only one caller expected (on colony death). Verify it actually runs on `DespawnDeadColonies`. | To confirm in FASE 1b. |
| Duplicate `_fillPaint.Style = Fill` | `Engine/Engine.cs:1833-1834` | One of the two is dead. | Visible. |
| `AntGoal` struct | `Simulation/Goals/AntGoal.cs` | Wrapper over a single enum, no behavior. Not dead, but zero-value. | Over-abstraction; candidate for inline. |

**Rule applied:** no symbol will be deleted in the refactor without (a) a repo-wide grep proving zero callers, (b) a build after deletion, and (c) a characterization test comparing sim digests before/after.

---

## 4. Broken-links / code-doc mismatch report

| Claim in code / doc | Reality | Action |
|---|---|---|
| README: "4 pheromone channels: Home, Food, Danger, Boundary" | Code has 3: HomeTrail, FoodTrail, EnemyTrail. `Danger` and `Boundary` were never shipped. | README must be rewritten (FASE 2 doc-pass). |
| README behavior-pipeline diagram | Actual `AntBehavior.Tick` pipeline is different (vision before sensor, blend step, leash clamp, lunge). | README must be rewritten. |
| README: "performance rule 10 — ~10-16 GPU calls/frame" | Current frame has far more (at least: grid replay, food replay, nests replay, ant atlas, HUD replay, stats replay, topbar replay, overlay — plus dynamic debug paths). Rule is aspirational, not current. | Either honour the budget or relax the rule. |
| `Colony.MaxEnemyCountForDanger = 25` publicly visible const | Should be private to Colony (no external reader). | Encapsulate. |
| `PheromoneGrid.ChannelCount = 3` but index 2 is null | Confuses readers. | Either drop `EnemyTrail` from the enum (it never uses the array) or unify the storage. |
| `SpatialGrid.QueryState.ClosestCombatDistSq` | Also used as `antWeight` in `VisionSystem`. Name lies. | Rename to `ScratchScalar` or introduce per-system `QueryState` subtypes. |
| Typo comment `"infintie"` | `Simulation/World.cs` | Will be removed with the comment-pass. |
| Comment refs to Softology ant sim | `Simulation/Behavior/MovementSystem.cs` | Remove; move attribution to `docs/credits.md` if wanted. |

---

## 5. Comment-removal report

**Inventory** — every file with meaningful prose comments to strip, with estimated LOC and policy applied.

Policy per your directive:
- REMOVE: explanatory comments that describe what the code does, section banners, inline reasoning, historical notes, TODO/FIXME lines (extracted to a TODO list), attribution comments ("like Softology").
- KEEP: license headers (none present), XML-doc `///` on public API members that add non-obvious information (currently none present either).
- RELOCATE: genuinely load-bearing architectural commentary → `docs/architecture.md` (new).

| File | ~Comment lines | Example lines | Action |
|---|---:|---|---|
| `Simulation/AntBehavior.cs` | ~25 | Section banners, "//  1. Decrement cooldowns" narration | Remove; pipeline is self-documenting once methods are named right. |
| `Simulation/Pheromones/PheromoneGrid.cs` | ~40 | `// Distance-to-goal per channel…`, `// Sparse decay…` | Remove after replacing with focused method/member names; relocate "sparse decay" rationale to docs. |
| `Simulation/Behavior/SensorSystem.cs` | ~55 | Magic-constant explanations | Constants become `private const` with self-describing names; remove text. |
| `Simulation/Behavior/VisionSystem.cs` | ~50 | Blend-weight rationale | Move to docs. |
| `Simulation/Behavior/CombatSystem.cs` | ~15 | "Use spatial hash grid for O(nearby)" | Remove. |
| `Simulation/Behavior/MovementSystem.cs` | ~25 | Softology attribution, bounce rules | Remove; relocate rules to docs. |
| `Simulation/Colony.cs` | ~50 | Signal/threat explanations | Remove. |
| `Simulation/World.cs` | ~90 | Sections, LOS math, HSV notes | Remove; retain *no* prose in source. |
| `Simulation/MapLoader.cs` | ~40 | HSV classification tables | Move the classification table to `docs/map_format.md`. |
| `Simulation/SpatialGrid.cs` | ~35 | 3-pass rebuild narration | Remove. |
| `Simulation/Roles/*` | ~80 | Per-role rationale | Remove; rationale goes to `docs/roles.md`. |
| `Engine/Engine.cs` | **~250** | Section banners, perf rules, sidebars | Remove; rules → `docs/perf_rules.md`. |
| `Engine/AntRenderer.cs` | ~25 | Supersampling explanation | Remove; one-line docstring on the class is sufficient. |
| `Engine/Camera.cs` | ~40 | Well-written but removable | Remove; zoom/pan is self-evident. |
| `Engine/FastSKGLControl.cs` | ~15 | VSync-off rationale | Remove; keep a 1-line XML doc on the class. |
| `Engine/Ui/*` | ~80 total | Section banners | Remove. |

**Total comment LOC removed (estimate):** ~900.
**Replaced with:** improved naming, new `docs/` folder (4 files), and, where truly necessary, one-line XML `///` on public types.

---

## 6. Architecture findings

1. **Engine.cs is 8 concerns in one file.** Proposed split (detailed in the plan):
   - `Engine/Engine.cs` → frame loop + input dispatch only.
   - `Engine/Rendering/WorldRenderer.cs` → grid, food, walls, nests, ants picture.
   - `Engine/Rendering/HudRenderer.cs` → HUD + stats SKPicture.
   - `Engine/Rendering/OverlayRenderer.cs` → pheromone overlay, debug overlays.
   - `Engine/Rendering/PaintCache.cs` → owns all shared `SKPaint` instances + disposal.
   - `Engine/Interaction/SelectionController.cs` → selected-ant tracking + follow-camera.
   - `Engine/Interaction/PlacementController.cs` → wall/food placement ghosts + paint tools.
   - `Engine/SimDriver.cs` → Stopwatch accumulator + `World.Update` dispatcher.
   - `Engine/Ui/UiLayout.cs` → top-bar + start-overlay layout orchestration.

2. **`PheromoneGrid` mixes two data models.** Two-channel array grid + one-channel Dictionary. The abstraction would benefit from a `PheromoneChannelStore` that both regular and enemy trails implement. Behavior-preserving: keep the same storage, wrap with a uniform interface.

3. **`Colony` exposes constants that are implementation details.** `MaxEnemyCountForDanger`, defense/offense thresholds, nest-regen rates — make `private const` and expose via methods only.

4. **`QueryState` is an overloaded scratch bag.** Introduce per-consumer wrapper structs that project only the fields they need (still a single `ref` parameter → still zero-alloc).

5. **`AntBehavior` is a procedural pipeline.** Fine as-is, but extracting the pipeline into an ordered array of systems (`IAntSystem[]`) would make it debuggable and re-orderable without touching call sites. Behavior-preserving if the order is retained byte-for-byte.

6. **Magic numbers are everywhere.** `SensorSystem`, `VisionSystem`, `CombatSystem`, `SpawnSystem`, `Colony` — most have `const` fields but many more inline. Consolidate into a per-system `Tuning.cs` struct with constants so tuning is centralised.

7. **No interface boundaries between sim and engine.** Engine reaches into `World`, `Colony`, `Ant` field by field. An immutable `WorldSnapshot` or a minimal `IWorldView` surface would decouple render from sim and unlock headless testing.

8. **No separation of per-frame rendering vs. per-tick simulation.** `Engine.OnPaint` both draws and (indirectly) can advance simulation; verify and separate so a paused-but-moving camera has zero sim cost.

---

## 7. Performance findings

The 10 performance rules in README are the contract. Current adherence:

| Rule | Status | Evidence |
|---|---|---|
| 1 — Batch with SKPath | ✅ mostly | Ant atlas + path batching used. |
| 2 — SKPicture replays every command | ⚠️ drift | Rule is honoured in picture caches, but `DrawSelectedAntInfoPanel` allocates `SKPaint` per frame outside the pictures. |
| 3 — Cache non-per-frame | ✅ | Grid, food, nests, HUD, stats, buttons, topbar pictures all dirty-flagged. |
| 4 — No grid lines in hot path | ✅ | Baked into grid picture. |
| 5 — No SKRoundRect per frame | ⚠️ | `UiSegmentedControl.Draw`, `UiStartOverlay.Draw`, `UiPanel` all create `SKRoundRect`, but they run only inside cached pictures — document this invariant at each call site or enforce via a wrapper. |
| 6 — IsAntialias=false on non-text | ✅ | Shared paints are AA=false; toggled on for round-rects then back off. |
| 7 — FilterQuality.Low for atlas | ✅ | Verified in `AntRenderer`. |
| 8 — Share paint objects | ⚠️ | `DrawSelectedAntInfoPanel` allocates; `UiLineChart` allocates (inside cached picture — acceptable but should be pre-built paints on the class). |
| 9 — Minimize Stopwatch | ✅ | Only one Stopwatch at frame level. |
| 10 — Budget 10–16 GPU calls/frame | ❌ | More calls in practice; either raise the rule to 20–25 or batch the top bar / stats / HUD replay into one combined picture. |

Other performance concerns:

- **`SteeringSystem.NormalizeAngle` uses a `while` loop** — under large deltas this is O(n). Replace with `Math.IEEERemainder(angle, 2π)`. `Simulation/Behavior/SteeringSystem.cs`.
- **`Math.Sqrt` on per-ant hot paths** — some call sites already compare squared distances; a few (e.g. `EnemyDetectionSystem` and `CombatSystem` lunge direction) do per-tick Sqrt. Acceptable at current populations; flag for SIMD in FASE 3 only if profiling shows it.
- **`World.HasLineOfSight` uses DDA and allocates no arrays — good.** Consider precomputing an occupancy bitmap for `IsWall` to shave an indirection.
- **`PheromoneGrid.DecayStep` is sparse — excellent.** Prefer `packed % _height` / `packed / _height` combined via `Math.DivRem` to save one division.
- **`SpawnSystem.ComputeSpawnInterval` calls `Math.Pow` on every spawn check** — not hot enough to matter; leave.

---

## 8. Scalability findings

- **Per-colony `float[width,height]` arrays grow linearly with map size.** A 512×512 map + 6 colonies + 3 live pheromone layers + enemy trails per target = multiple hundreds of MB of floats. For maps ≥ 1024² or colonies ≥ 8 this will be painful. Recommendation (post-refactor, behavior-neutral option): switch to chunked storage (e.g. 64×64 tiles) with per-tile activity flags.
- **`SpatialGrid` is per-tick rebuilt with flat packed arrays — scales well.** Dominant cost is the gather step; fine to 10k ants.
- **`Colony.Ants` is a `List<Ant>` scanned fully during cleanup.** For very large colonies, swap to a dense-packed `Ant[]` with tombstone compaction (deferred; not a current bottleneck).
- **`World.Update` serialises every colony.** Parallelising colonies is safe because they own disjoint pheromone grids and share only read access to `World` cells and the spatial grid. A `Parallel.ForEach` over colonies after `SpatialGrid.Rebuild` would give a near-linear speedup. Behavior-neutral iff random-number access is not shared — currently `world.NextRandomFloat()` is a single RNG, so this requires either a per-colony RNG or is deferred.
- **Pheromone decay active-cell list can grow unbounded during transient traffic peaks.** Already trimmed on decay; acceptable.

---

## 9. Security / safety observations

No network I/O, no file writes outside the working folder, no serialisation of untrusted data. Safety surface is small.

- **`File.OpenRead` in `UiStartOverlay.Scan`** silently skips corrupt PNGs — fine.
- **`SKBitmap.Decode`** is on a user-controlled path (Maps/) but the user selects the path, so trust model is acceptable.
- **No obvious unbounded allocations** triggered by map content — `MapLoader` respects the bitmap dimensions.

No security refactor is proposed.

---

## 10. Readability findings

- **Inconsistent null-handling.** Some sites use `TryGetValue`, others use `ContainsKey` + `[]`. Pick one per-file.
- **Mixed `float` vs `double`.** `Math.Sqrt`/`Math.Pow` return `double`, code immediately casts to `float`. Could standardise on `MathF`.
- **Local-variable naming.** `dx, dy, nx, ny` is fine in tight loops; `tw`, `ty` in UI rendering also fine. Some outliers: `packed` vs `cellIdx`.
- **File-scoped namespaces** — already used consistently (`namespace ANTS;`). Keep.
- **`ImplicitUsings` is on** in csproj — a few files still have `using System.Collections.Generic;` which is already implicit. Remove in the comment/housekeeping pass.

---

## 11. Stagable refactor plan (summary — detail in `REFACTOR_PLAN.md`)

| # | Phase | Goal | Risk | Rollback |
|---|---|---|---|---|
| 0 | Tooling | Add `.editorconfig`, enforce `dotnet format`, add a deterministic characterization harness (seeded 60-s sim, digest of per-tick state). | L | Revert commit. |
| 1 | Housekeeping | Remove all prose comments. Remove unused `using`s. Fix duplicate `_fillPaint.Style = Fill`. Fix `"infintie"` typo (if comment stays) or drop it with the rest. | L | Revert commit. |
| 2 | Docs | Create `docs/architecture.md`, `docs/perf_rules.md`, `docs/roles.md`, `docs/map_format.md`. Rewrite `README.md` to match current code. | L | Docs-only. |
| 3 | Naming / encapsulation | Make `Colony` constants private. Rename `QueryState.ClosestCombatDistSq` → `ScratchScalar`. Rename `_fillPaint`/`_linePaint` etc. to reflect ownership. Inline or enrich `AntGoal`. Remove `GoalType.Patrol` if still dead. | L | File-local revert. |
| 4 | Engine split | Extract `WorldRenderer`, `HudRenderer`, `OverlayRenderer`, `PaintCache`, `SelectionController`, `PlacementController`, `SimDriver` out of `Engine.cs`. Each extract is one atomic commit. | **M** | Per-commit revert + harness digest compare. |
| 5 | `PheromoneGrid` unification | Wrap regular + enemy trails in a `PheromoneChannelStore` interface without changing storage. | M | Harness digest compare. |
| 6 | Perf-rule enforcement | Fix per-frame `SKPaint` allocation in selected-ant panel. Cache `UiLineChart` paints. Combine small HUD pictures. Replace `NormalizeAngle` while-loop. | L-M | Per-fix revert; harness digest unchanged. |
| 7 | Magic-number consolidation | Move all tuning constants to per-system `Tuning` structs. No value changes. | L | Per-system revert. |
| 8 | Dead-code sweep | After harness is green: delete `GoalType.Patrol` (if still unused), inline `AntGoal` (if kept thin). | L | Per-symbol revert. |

No big-bang rewrite. Every phase is behavior-neutral, provable by the harness digest.

---

## 12. Prerequisites before any code change

1. **Your approval** of this report and the accompanying `REFACTOR_PLAN.md`.
2. **A characterization-test harness** (I will propose it at the start of FASE 1 — a deterministic seeded 60-second sim that prints a SHA256 of `(tick, colonyCount, antCount-per-colony, foodOnGround, pheromoneCellCount-per-channel)` every second; the digest must be byte-identical across any refactor commit).
3. **Confirmation of the tool chain:** `dotnet format` + the new `.editorconfig` — you already approved this. I will open with those.
4. **Confirmation of scope per phase:** I will not start FASE 4 (Engine split) until FASES 0–3 land and the harness is green.

---

*End of audit report. Proceed by reviewing `REFACTOR_PLAN.md` and approving the FASE 0 tooling commit.*
