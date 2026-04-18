# REFACTOR_PLAN

**Companion document to** `CODEBASE_AUDIT_REPORT.md`.
**Hard rule:** every phase is behavior-preserving. A characterization-test harness (FASE 0 deliverable) must stay byte-identical green across every commit. No big-bang rewrite, no feature work, no dependency changes, no value changes to simulation constants.

**Convention:** every phase = one PR / one pushable branch = multiple atomic commits. Each commit independently builds and runs. Each commit lists in its message: goal, files, risk (L/M/H), test strategy, rollback.

---

## FASE 0 — Tooling & characterization harness

**Status.** ✅ DONE (sub-phases 0.1–0.7 + 0.A–0.D + 0.A.1 all landed).

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

**Status.** ✅ DONE (1.1 duplicate `_fillPaint.Style` removed, 1.2 redundant using removed, 1.3–1.4 prose-comment strip across all directories).

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

## FASE 1.5 — Pre-existing CA/IDE warnings cleanup (retrospective)

**Status.** ✅ DONE (2026-04-18). Tussenfase tussen FASE 1 en FASE 3, ontstaan nadat strictere `.editorconfig` uit FASE 0 9 pre-existing analyzer warnings zichtbaar maakte die de build niet blokkeerden maar de 0-warning gate (verlangd voor latere FASEs) wel vuil hielden.

**Goal.** Ruim 9 pre-existing CA/IDE warnings op zonder gedragsverandering, zodat `dotnet build -c Debug` naar 0 warnings gaat en blijft.

**Scope (per warning).**

- CA1805, CA1816, CA1304 in `Engine/Ui/UiStartOverlay.cs` (default-value init, `Dispose` SuppressFinalize, `char.ToUpperInvariant`).
- CA1305 × 2 in `Engine/Ui/UiTopBar.cs` (`CultureInfo.InvariantCulture` op twee `ToString`-calls).
- IDE0040 × 3 in `Program.cs` (`internal` op class, `private` op `Main` en `RunGui`).
- CA1822 in `Simulation/Roles/RoleQuota.cs` (`PickByDeficit` → `static`).

**Files touched.** 4 bestanden; alles aanpassingen binnen bestaande methodes, geen API-wijziging.

**Risk.** L. Pure compiler/analyzer-clean-up; IL-effect beperkt tot `internal`/`private` toegankelijkheid en `static` invocation; harness-gedekt.

**Test strategy.** `dotnet build -c Debug` = 0 warnings / 0 errors. Harness digest identiek aan baselines.

**Rollback.** Per-commit revert.

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

**Status.** ✅ DONE (3.1, 3.2, 3.4 landed; 3.3 satisfied via FASE 8.3 inline-collapse).

**Goal.** Tighten the contract surface without moving behavior.

**Commits (each atomic).**

1. ✅ `Simulation/Colony.cs`: downgrade implementation-detail constants to `private const` (`MaxEnemyCountForDanger`, defense/offense thresholds, nest-regen rates). Any external read becomes a method or computed property (`IsInDangerPosture()`, …). Keep behavior.
2. ✅ `Simulation/SpatialGrid.cs`: rename `QueryState.ClosestCombatDistSq` → `ScratchScalar`; update every consumer (`CombatSystem`, `VisionSystem`).
3. ✅ (via FASE 8.3) `Simulation/Goals/AntGoal.cs`: resolved via **Option A (collapse)** — `AntGoal` struct deleted, `Ant.Goal` is now a bare `GoalType`. Two options were considered:
   - **Option A (collapse):** inline `AntGoal` into `Ant.Goal` of type `GoalType`. ← chosen
   - **Option B (enrich):** add real fields (`TargetX`, `TargetY`, `Deadline`) used by at least two roles. Only if we agree it will be used in a later, non-refactor phase.
4. ✅ `Engine/Engine.cs`: rename paints by ownership, e.g. `_sharedFill`, `_sharedStroke`, `_sharedText` — readers cannot currently tell which paints are shared vs per-owner.

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
2. ~~Pre-build `UiLineChart` paints~~ **SKIPPED** — UiLineChart has 0 callers in codebase (discovered 2026-04-18 during FASE 6.2 scope-report). Premature to optimize paints for a class that isn't used. See REFACTOR_BACKLOG.md "Dead code candidates" for FASE 8.5 evaluation: either delete UiLineChart entirely, or wire up a caller first (fps graph, stats overlay) then optimize.
3. ~~Replace `SteeringSystem.NormalizeAngle` while-loop with `Math.IEEERemainder`~~ **SKIPPED** — Pre-flight empirical analysis (2026-04-18) proved both proposed alternatives break bit-exact equivalence with baseline while-loop: `Math.IEEERemainder` 66.35% drift (float→double→float roundtrip + banker's rounding); `MathF`-only alternative 0.08% drift (still non-zero). Plan assumption "mathematically equivalent" held conceptually but not bit-exactly. Harness baselines depend on current bit-pattern output. No performance win in practice (analysis shows 0-1 iterations per call; large-input pathological case never occurs due to bounded input sources: `Atan2` results + small deltas). Keeping the while-loop. Related: pre-existing `-π` asymmetry bug discovered during pre-flight, tracked separately in `REFACTOR_BACKLOG.md` "Algorithmic precision notes".
4. Combine topbar + HUD + stats replay into one combined `SKPicture` to hit the 10-16 GPU-call budget. **Behavior-identical** because SKPicture replay is order-preserving.
5. Document every remaining deliberate rule-breaker in a one-line code comment at the call site (this is the ONLY place comments survive), e.g. `// perf-rule-5 exempt: inside cached SKPicture`.
6. **FASE 6.6 — Pheromone overlay performance optimization (NEW).**
   Context: tijdens testen rond FASE 7.1 ontdekt dat met de pheromone-overlay AAN de fps van ~2000+ naar ~70 zakt (~96 % drop). Dit is een echte bottleneck in de render-path die gefixt moet worden zonder visueel resultaat te wijzigen.
   Scope (mogelijk — profiling-driven; niet alles hoeft te landen):
   - Profiling-based root-cause analyse (welke draw-calls / allocations zijn de hotspot).
   - `SKPicture`-caching van de pheromone-layer (rebuild alleen op dirty tick).
   - Dirty-flag-based partial updates (alleen gewijzigde cellen re-rasteren).
   - Tile-based rendering (screen-space tiles, cache per tile).
   - Lower-resolution overlay texture (bilinear upsample bij display).
   Invariants:
   - Visueel resultaat: **identiek** (zelfde pixels per frame) — check met screenshot-diff.
   - Harness digest: **byte-identiek** (simulation-logic ongewijzigd; dit is puur rendering).
   Test strategy:
   - Before/after fps-metingen met overlay aan én uit (rapporteer getallen in commit).
   - Visuele pixel-diff op minstens één referentie-frame.
   - Harness digest 3× byte-identiek op alle drie de baselines.
   Risk. **M** — rendering-surface, maar strict visueel-identiek criterium + harness-gate beschermen gedrag.
   Rollback. Per-commit revert; geïsoleerd in overlay-rendering.

**Files touched.** `Engine/*`, `Simulation/Behavior/SteeringSystem.cs`.

**Risk.** L-M. The angle-normalization change is the one with a real semantic surface; covered by the harness digest.

**Test strategy.** Harness digest unchanged.

**Rollback.** Per-commit revert.

---

## FASE 7 — Magic-number consolidation

**Goal.** Every tuning constant lives in one place per system.

**Status.** 7.1 ✅ DONE (2026-04-18); 7.2–7.5 pending.

**Commits.**

1. ✅ `Simulation/Behavior/SensorTuning.cs` — collects `ForwardBias`, `TurnMargin`, `SampleRadius`, `LostHomingWeight`, `StrengthWeight`, `DistanceWeight`. Stochastic constants stay in `SensorSystem.cs` (deferred; scope = fixed-sensor scoring weights).
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

**Status.** ✅ DONE (8.1 skipped + documented, 8.2 skipped + documented, 8.3 landed, 8.4 landed).

**Goal.** Remove what is proven unused after FASES 0-7 settle.

**Rule.** A symbol may be deleted only if: (a) repo-wide grep (case-sensitive, whole-word) shows zero references; (b) the build stays green after deletion; (c) the harness digest stays byte-identical.

**Candidates.**

- ~~`GoalType.Patrol`~~ **SKIPPED** — Patrol is not dead code (see audit §3, updated 2026-04-18). Used as UI state-tag in `AttackerRole`/`DefenderRole` + displayed via `Engine.cs:1903`. Requires separate UI-modernization decision, out of refactor scope.
- ~~`PheromoneGrid.ClearEnemyTrailForTarget`~~ **SKIPPED** — live cleanup method (see audit §3, updated 2026-04-18). Called on colony death from `World.cs:372` via `DespawnDeadColonies` → `ForgetEnemyTrailAboutDeadColony`. Removing it would leak memory in per-enemy dictionaries and drift the harness digest.
- ✅ `AntGoal` — FASE 3 koos Option A (inline); struct gesloopt, `Ant.Goal` is nu direct `GoalType`. (FASE 8.3, 2026-04-18.)
- ✅ **FASE 8.4 — `_smallTextPaint` dead field** (2026-04-18, retrospectief gedocumenteerd). `Engine/Engine.cs` bevatte een `SKPaint _smallTextPaint` field dat gedeclareerd, geïnitialiseerd én gedisposed werd, maar nergens gelezen. Ontdekt tijdens de FASE 3.4 scope-audit. Verwijderd (decl + init-blok); geen callers, harness digest byte-identiek, 0 warnings.

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

## Remaining work order (as of 2026-04-18)

Done: FASE 0, 1, 1.5, 3 (3.1/3.2/3.3-via-8.3/3.4), 7.1, 8 (8.1 skip, 8.2 skip, 8.3, 8.4).

Upcoming, in execution order:

1. **FASE 7.2** — `VisionTuning.cs` extraction.
2. **FASE 7.3** — `CombatTuning.cs` extraction.
3. **FASE 7.4** — `SpawnTuning.cs` extraction.
4. **FASE 7.5** — `Colony.Tuning.cs` extraction.
5. **FASE 6.2** — `UiLineChart` paint allocation cleanup.
6. **FASE 6.3** — replace `SteeringSystem.NormalizeAngle` while-loop with `Math.IEEERemainder`.
7. **FASE 6.5** — one-line comments documenting deliberate perf-rule exemptions.
8. **FASE 6.6 (NEW)** — pheromone overlay performance optimization (96 % fps-drop fix; visueel-identiek, harness-identiek).
9. **STOP for evaluation** — herschouwen of FASE 2, 4, 5, 9 nog in huidige vorm nodig zijn of dat de scope moet wijzigen.

FASE 2 (docs), FASE 4 (Engine split), FASE 5 (PheromoneGrid unify) en FASE 9 (CHANGES.md) staan voorlopig op hold tot de bovenstaande evaluatie.

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
