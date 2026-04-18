# REFACTOR_PLAN

**Companion document to** `CODEBASE_AUDIT_REPORT.md`.
**Hard rule:** every phase is behavior-preserving. A characterization-test harness (FASE 0 deliverable) must stay byte-identical green across every commit. No big-bang rewrite, no feature work, no dependency changes, no value changes to simulation constants.

**Convention:** every phase = one PR / one pushable branch = multiple atomic commits. Each commit independently builds and runs. Each commit lists in its message: goal, files, risk (L/M/H), test strategy, rollback.

---

## FASE 0 — Tooling & characterization harness

**Goal.** Make refactor safe. Before a single line of business logic is touched, lock down determinism and style.

**Deliverables.**

1. `.editorconfig` (root) — strict C# style: 4-space indent, `var` where apparent, `this.` discouraged, file-scoped namespaces, expression-bodied members where short, `_camelCase` for private fields, `PascalCase` for everything else.
2. `dotnet format` in a pre-commit expectation (document in README; no CI yet — user has no CI).
3. `Tests/Characterization/CharacterizationHarness.cs` — a `Program.cs`-parallel entry point behind a `#if CHARACTERIZATION` or a separate small `ANTS.Harness.csproj`. Loads a canonical seed, a fixed map (committed to the repo as `Maps/harness_fixture.png`), runs 60 seconds of simulated time at a fixed 60 Hz, emits a SHA256 digest per second of `(tick, colonyCount, antCountPerColonySorted, totalFoodOnGround, pheromoneActiveCellCountPerChannelPerColony)`.
4. Commit the baseline digest output as `Tests/Characterization/expected.txt`.
5. Ensure `World.NextRandomFloat()` is seedable — currently it almost certainly calls a `Random` constructed in `World.ctor`; add a seed parameter with default = current behavior.

**Files touched.** New: `.editorconfig`, `Tests/Characterization/*`, `Maps/harness_fixture.png`. Modified: `Simulation/World.cs` (seed parameter, default preserves current behavior).

**Risk.** L. The only simulation-visible change is an added optional parameter; default value keeps current RNG stream.

**Test strategy.** Run the harness twice in a row; both must produce the same digest. That proves determinism. The committed `expected.txt` is that digest.

**Rollback.** Revert the commit; no downstream phase depends on harness files other than as a CI-style gate.

**Out of scope.** Unit tests (the user confirmed there are none and none are demanded); only the characterization digest.

---

## FASE 1 — Housekeeping (zero-risk)

**Goal.** Remove all prose comments and trivial dead statements. Fix the things that are obviously wrong.

**Commits (each one file or one family).**

1. Remove duplicate `_fillPaint.Style = SKPaintStyle.Fill;` in `Engine/Engine.cs` (currently appears twice at lines 1833-1834).
2. Remove redundant `using System.Collections.Generic;` (already in `ImplicitUsings`).
3. Strip prose comments from every file listed in the audit section §5, one commit per directory:
   - `Simulation/*.cs`
   - `Simulation/Behavior/*.cs`
   - `Simulation/Pheromones/*.cs`
   - `Simulation/Roles/*.cs`
   - `Simulation/Goals/*.cs` + `Simulation/Vision/*.cs`
   - `Engine/*.cs`
   - `Engine/Ui/*.cs`
4. Extract the single in-code TODO/FIXME list to `docs/todo.md` (if any survive after the comment-strip — none noticed so far).

**Files touched.** Every `.cs`. But each commit is small and file-scoped.

**Risk.** L. Comments are not code. The only behavior-relevant risk is that a comment was load-bearing because someone removed commented-out code it referenced (not observed in this codebase).

**Test strategy.** After each commit: (a) `dotnet build` must succeed; (b) the FASE-0 harness digest must match `expected.txt` byte-for-byte; (c) `dotnet format --verify-no-changes` must exit zero.

**Rollback.** Per-commit revert; zero downstream dependencies.

---

## FASE 2 — Documentation (doc-only, zero code change)

**Goal.** Repair the code↔doc drift identified in audit §4.

**Commits.**

1. New folder `docs/`:
   - `docs/architecture.md` — pipeline, layers, threading, world tick, rendering.
   - `docs/perf_rules.md` — the 10 rules, with current honouring status (verbatim from audit §7).
   - `docs/roles.md` — flyweight role design, posture, hysteresis, personality biases.
   - `docs/map_format.md` — HSV classification table, BFS labelling.
2. Rewrite `README.md` to reflect the actual code: 3 pheromone channels, current `AntBehavior` pipeline, Queen personalities, role posture, `Patrol` status (removed or still alive depending on FASE 8).

**Files touched.** `README.md` + `docs/*`. No `.cs` file is touched.

**Risk.** L. Docs only.

**Test strategy.** Build unchanged; harness digest unchanged.

**Rollback.** Revert the single commit.

---

## FASE 3 — Naming & encapsulation

**Goal.** Tighten the contract surface without moving behavior.

**Commits (each atomic).**

1. `Simulation/Colony.cs`: downgrade implementation-detail constants to `private const` (`MaxEnemyCountForDanger`, defense/offense thresholds, nest-regen rates). Any external read becomes a method or computed property (`IsInDangerPosture()`, …). Keep behavior.
2. `Simulation/SpatialGrid.cs`: rename `QueryState.ClosestCombatDistSq` → `ScratchScalar`; update every consumer (`CombatSystem`, `VisionSystem`).
3. `Simulation/Goals/AntGoal.cs`: resolve the over-abstraction. Two options — decide in the PR review:
   - **Option A (collapse):** inline `AntGoal` into `Ant.Goal` of type `GoalType`.
   - **Option B (enrich):** add real fields (`TargetX`, `TargetY`, `Deadline`) used by at least two roles. Only if we agree it will be used in a later, non-refactor phase.
   - **Default: Option A**, since we are behavior-neutral and `AntGoal` currently adds nothing.
4. `Engine/Engine.cs`: rename paints by ownership, e.g. `_sharedFill`, `_sharedStroke`, `_sharedText` — readers cannot currently tell which paints are shared vs per-owner.

**Files touched.** Scoped per commit; no system boundary moves.

**Risk.** L. Pure rename / visibility.

**Test strategy.** Build + harness digest unchanged.

**Rollback.** Per-commit revert.

---

## FASE 4 — Engine split (**the big one**)

**Goal.** Decompose `Engine.cs` (1,939 LOC) into cohesive collaborators. **No behavior change; every extract is a move of code, not a rewrite.**

**Target file layout.**

```
Engine/
  Engine.cs                          # ~300 LOC: frame loop, input dispatch, wiring
  SimDriver.cs                       # Stopwatch accumulator, World.Update dispatch
  Interaction/
    SelectionController.cs           # selected ant, follow-camera, hover hit-test
    PlacementController.cs           # wall/food placement ghosts
  Rendering/
    PaintCache.cs                    # owns all shared SKPaint; IDisposable
    WorldRenderer.cs                 # grid/food/walls/nests/ants SKPictures
    HudRenderer.cs                   # HUD + stats panel SKPictures
    OverlayRenderer.cs               # pheromone overlay, debug overlays
  Ui/                                # unchanged
```

**Commits (strictly in this order — the harness must be green after each).**

1. Extract `PaintCache.cs` — move every shared `SKPaint` into it. `Engine.cs` holds a `PaintCache _paints`. The field names stay the same (via properties) so diff is minimal.
2. Extract `SimDriver.cs` — move the Stopwatch accumulator + `World.Update` call.
3. Extract `SelectionController.cs` — move selected-ant state + hit-testing.
4. Extract `PlacementController.cs` — move wall/food painting + ghost preview.
5. Extract `WorldRenderer.cs` — move grid/food/nests/ants pictures + dirty-flag logic.
6. Extract `HudRenderer.cs` — move HUD, stats, top-bar, start-overlay orchestration. **Important:** `UiTopBar`, `UiStartOverlay`, `UiLineChart` stay where they are; only the orchestration moves.
7. Extract `OverlayRenderer.cs` — move pheromone-overlay code.
8. Clean-up commit: fix paint ownership (every `SKPaint` has exactly one owner that disposes it).

**Files touched.** `Engine/Engine.cs` shrinks to ~300 LOC; new files in `Engine/Rendering/` and `Engine/Interaction/`.

**Risk.** **M**. Large surface. Mitigated by:
- No behavior change — every extract is a move.
- Per-commit harness digest compare.
- Paint ownership tracked explicitly (`PaintCache` owns all shared paints; per-component paints owned by the component).

**Test strategy.** After each commit: build, run the harness, assert digest == `expected.txt`. Additionally, manual smoke check (open the app, verify visible frames are identical to pre-refactor — take a reference screenshot in FASE 0 and compare pixel-for-pixel).

**Rollback.** Per-commit revert. The split is staged specifically so rollback of a single commit still leaves a buildable tree.

---

## FASE 5 — PheromoneGrid unification (interface, not storage)

**Goal.** Remove the two-model smell (array grid for Home/Food, per-colony-id dict for Enemy) without changing storage.

**Approach.**

- Define `internal interface IPheromoneChannelStore { Deposit/Get/GetDistance/DegradeInPlace/DecayStep }`.
- Two implementations: `ArrayChannelStore` (Home/Food) and `PerTargetChannelStore` (Enemy). Current fields/arrays stay byte-identical.
- `PheromoneGrid` becomes a dispatcher: `Get(channel, ...)` routes to the right store.
- The `ChannelCount = 3` constant stops being misleading because the second slot is no longer "null but indexed"; it is simply "not an array-backed channel".

**Files touched.** `Simulation/Pheromones/PheromoneGrid.cs` + one or two new files. Callers unchanged (public API identical).

**Risk.** M. Any reorder or premature caching could change sampling. Harness digest is the line of defense.

**Test strategy.** Harness digest unchanged.

**Rollback.** Single revert.

---

## FASE 6 — Performance-rule enforcement

**Goal.** Make the 10 rules in README actually hold. No behavior change.

**Commits.**

1. Move `SKPaint` allocations in `DrawSelectedAntInfoPanel` to `PaintCache`.
2. Pre-build `UiLineChart` paints on the instance (currently allocated per `Draw` call, which lives inside a cached `SKPicture` — acceptable but now made explicit).
3. Replace `SteeringSystem.NormalizeAngle` while-loop with `Math.IEEERemainder(angle, 2π)`. **Verify with harness** — the mathematical result is equivalent; any drift indicates a bug in the while-loop that callers relied on.
4. Combine topbar + HUD + stats replay into one combined `SKPicture` to hit the 10-16 GPU-call budget. **Behavior-identical** because SKPicture replay is order-preserving.
5. Document every remaining deliberate rule-breaker in a one-line code comment at the call site (this is the ONLY place comments survive), e.g. `// perf-rule-5 exempt: inside cached SKPicture`.

**Files touched.** `Engine/*`, `Simulation/Behavior/SteeringSystem.cs`.

**Risk.** L-M. The angle-normalization change is the one with a real semantic surface; covered by the harness digest.

**Test strategy.** Harness digest unchanged.

**Rollback.** Per-commit revert.

---

## FASE 7 — Magic-number consolidation

**Goal.** Every tuning constant lives in one place per system.

**Commits.**

1. `Simulation/Behavior/SensorTuning.cs` — collects `ForwardBias`, `TurnMargin`, `SampleRadius`, `LostHomingWeight`, `StrengthWeight`, `DistanceWeight`.
2. `Simulation/Behavior/VisionTuning.cs` — cone widths, blend weights.
3. `Simulation/Behavior/CombatTuning.cs` — `AttackRange`, `AttackCooldownSeconds`, etc.
4. `Simulation/Behavior/SpawnTuning.cs` — `BaseSpawnInterval`, `IdealFoodPerAnt`, …
5. `Simulation/Colony.Tuning.cs` — signal thresholds, nest-regen rates.

All struct/class fields are `internal const`. No value changes.

**Files touched.** Only the named systems.

**Risk.** L. Mechanical extraction.

**Test strategy.** Harness digest unchanged (obviously — no value has changed).

**Rollback.** Per-commit revert.

---

## FASE 8 — Dead-code sweep

**Goal.** Remove what is proven unused after FASES 0-7 settle.

**Rule.** A symbol may be deleted only if: (a) repo-wide grep (case-sensitive, whole-word) shows zero references; (b) the build stays green after deletion; (c) the harness digest stays byte-identical.

**Candidates.**

- ~~`GoalType.Patrol`~~ **SKIPPED** — Patrol is not dead code (see audit §3, updated 2026-04-18). Used as UI state-tag in `AttackerRole`/`DefenderRole` + displayed via `Engine.cs:1903`. Requires separate UI-modernization decision, out of refactor scope.
- ~~`PheromoneGrid.ClearEnemyTrailForTarget`~~ **SKIPPED** — live cleanup method (see audit §3, updated 2026-04-18). Called on colony death from `World.cs:372` via `DespawnDeadColonies` → `ForgetEnemyTrailAboutDeadColony`. Removing it would leak memory in per-enemy dictionaries and drift the harness digest.
- `AntGoal` — if FASE 3 chose Option A (inline), delete the struct.

**Files touched.** The affected types only.

**Risk.** L — gated by harness and grep.

**Rollback.** Per-commit revert.

---

## FASE 9 — CHANGES.md

**Goal.** A chronological per-phase changelog for the audit reader.

**Content.** One section per phase:
- Goal
- Files touched (list)
- Commits (short SHAs + titles)
- Risk taken
- Harness digest before/after (must match)
- Known regressions (none expected)

---

## Phase-ordering rationale

- **0 before everything:** no safe change without the harness.
- **1 (comments) before 2 (docs):** writing new docs while old comments still exist guarantees drift.
- **3 (naming) before 4 (split):** it is easier to move well-named code than to rename while moving.
- **4 (split) before 5 (pheromone unify):** a smaller `Engine.cs` makes it easier to see pheromone call sites.
- **5 before 6:** performance fixes land after the API shape stabilises.
- **6 before 7:** perf-rule enforcement may surface more magic numbers.
- **7 before 8:** dead-code sweep only makes sense when everything else is consolidated.

## What is explicitly OUT OF SCOPE

- New features (no new roles, no new pheromone channels).
- Dependency updates (SkiaSharp stays 2.88.8, .NET stays net10.0-windows).
- Config changes (no change to `<TargetFramework>`, no new NuGet).
- Tuning changes (no simulation constant changes value).
- Rendering changes (no new visual effects, no palette changes).
- Parallel/multithreading (parallelising colonies is attractive but changes RNG ordering — deferred).

## Approvals requested

Before I touch a single line of code:

1. **Approve** this plan as-is, OR request changes.
2. **Approve** the FASE 0 characterization-harness approach (one seeded run, 60s, per-second digest).
3. **Approve** FASE 3's `AntGoal` decision (Option A = inline; Option B = enrich).
4. **Confirm** you want me to commit per commit with descriptive messages (ok for your workflow?).

Once approved, I start with FASE 0, land it, share the harness digest, wait for your "go" on FASE 1, and iterate.
