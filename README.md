# ANTS

A real-time ant colony simulator written in C# .NET 10 for Windows. Multiple colonies compete for food on a 2D grid, communicating indirectly through multi-channel pheromone trails (stigmergy). Rendered with SkiaSharp OpenGL for GPU-accelerated batched drawing.

---

## Architecture at a glance

```
ANTS/
├── Program.cs                           Application entry point
├── Engine/                              Rendering and input (SkiaSharp)
│   ├── Engine.cs                        Main form, simulation driver, draw pipeline
│   ├── Engine.Designer.cs               WinForms designer partial
│   ├── FastSKGLControl.cs               Low-overhead SKGLControl wrapper
│   ├── AntRenderer.cs                   Sprite atlas builder (GPU batched drawing)
│   ├── UiButton.cs                      UI control data
│   └── PlacingMode.cs                   Enum for mouse placement state
└── Simulation/
    ├── Ant.cs                           Ant entity data only
    ├── AntBehavior.cs                   Per-ant pipeline orchestrator
    ├── CellType.cs                      Cell types: Empty, Food, Nest, Wall
    ├── Colony.cs                        Colony data, food store, quota, pheromone grid
    ├── World.cs                         Grid, food, colonies, density, tick loop
    ├── Goals/
    │   ├── AntGoal.cs                   Goal struct (orthogonal to role)
    │   └── GoalType.cs                  Enum: Explore, SeekFood, ReturnHome
    ├── Pheromones/
    │   ├── PheromoneChannel.cs          Enum: HomeTrail, FoodTrail, EnemyTrail, DangerTrail
    │   └── PheromoneGrid.cs             Multi-channel grid with max-op deposit and decay
    ├── Roles/
    │   ├── AntRole.cs                   Abstract flyweight base class
    │   ├── ScoutRole.cs                 Default role, explores and detects food trails
    │   ├── ForagerRole.cs               Dual goal (SeekFood / ReturnHome)
    │   └── RoleQuota.cs                 Deficit-based spawn role selection
    └── Behavior/
        ├── SensorSystem.cs              Fixed 3-cone plus stochastic fallback
        ├── SteeringSystem.cs            Smooth heading interpolation with turn clamp
        ├── MovementSystem.cs            Position update, wall and enemy-nest bouncing
        ├── DepositSystem.cs             Age-weighted exponential pheromone deposit
        ├── GoalEventSystem.cs           Detects nest arrival, enemy nest, food cell
        ├── AutonomySystem.cs            Death after max lifetime
        └── SpawnSystem.cs               Food-gated spawn with quota-driven role selection
```

---

## Core design principles

**Stigmergy only.** No ant accesses another ant directly. All inter-ant coordination flows through the pheromone grid and the ant density field on `World`.

**Role-based extensibility.** Every role is a flyweight singleton inheriting from `AntRole`. Adding a new role (soldier, nurse, queen) means one new file in `Simulation/Roles/`, registered with the colony's `RoleQuota`, without modifying any other file.

**Goal orthogonal to role.** An `AntGoal` struct carries the current intent (`Explore`, `SeekFood`, `ReturnHome`). Roles decide their own goal transitions in `UpdateGoal`, keeping the role interface simple and scalable.

**Stateless systems.** Behavior logic lives in `public static` classes under `Simulation/Behavior/`. Each system is a pure function on `(Ant, Colony, World)`; no hidden state, trivially testable.

**Soft crowd handling.** There is no hard ant-to-ant blocking. Ants can overlap. Crowding is discouraged through a density-field penalty applied inside `SensorSystem` only. This eliminates the spin-on-cell deadlock that plagues hard-block schemes.

---

## Simulation pipeline

`World.Update()` runs at a fixed 60 Hz through the Engine's accumulator. Per tick:

1. Advance `SimulationTime`.
2. For each colony, step pheromone decay via `PheromoneGrid.DecayStep`.
3. For each colony, call `SpawnSystem.Tick` (food-gated, quota-driven).
4. For each ant, call `AntBehavior.Update` which runs:

    1. `Role.UpdateGoal` — may change role or goal.
    2. `SensorSystem.Sense` — when its cooldown expires (0.25s default). Sets `CachedSteerAngle`.
    3. `SteeringSystem.UpdateHeading` — smoothly interpolates `Heading` toward `CachedSteerAngle`, clamped to `Role.TurnRate × dt`.
    4. `MovementSystem.Move` — position update, wall and enemy-nest bouncing, density updates.
    5. `DepositSystem.Drop` — when its cooldown expires (0.15s default). Age-weighted exponential intensity, max-op deposit, per-role active trail degradation.
    6. `GoalEventSystem.Check` — triggers `OnReachedOwnNest`, `OnReachedEnemyNest`, or `OnReachedFoodCell` on the role.
    7. `AutonomySystem.Check` — marks the ant dead if `Age > Role.AutonomyMax`.

5. Remove dead ants.

---

## Pheromone model

Four channels per colony grid: `HomeTrail`, `FoodTrail`, `EnemyTrail`, `DangerTrail`. Each cell stores a float intensity in `[0, 1]` per channel.

**Deposit.** `intensity = BaseIntensity × exp(-DecayCoef × ant.InternalClock)`. An ant that recently started a goal phase deposits near `1.0`; an ant that has been wandering long deposits near `0`. The deposit uses a max-operation so strong recent trails are never weakened by stale deposits crossing them.

**Decay.** Each tick, every cell's intensity decreases by `channelRate × dt`. Home fades slowest, food fastest (so fresh finds dominate stale ones).

**Active trail degradation.** When an ant deposits, it has a per-role chance to multiply the cell of the channel it is currently following by `ActiveDegradeFactor` (e.g. 0.99). Over time this erodes circular trails that ants keep revisiting without fresh reinforcement.

**Permanent home marker.** Nest cells are flagged permanent and their `HomeTrail` intensity is pinned to `1.0` forever. This guarantees foragers can always find their way home even when trails elsewhere evaporate.

---

## Role system

`AntRole` (abstract, flyweight) exposes tunable stats (`MaxSpeed`, `TurnRate`, `SensorDistance`, `SensorAngleRad`, `DepositInterval`, `SensorInterval`, `AutonomyMax`, `ExplorationRate`, `GradientThreshold`, `DensityPenalty`, `ActiveDegradeChance`, `ActiveDegradeFactor`) and abstract methods (`UpdateGoal`, `GetFollowChannel`, `GetDepositChannel`, `OnReachedFoodCell`, `OnReachedOwnNest`, `OnReachedEnemyNest`, `OnLostTrail`).

`ScoutRole` is the default on spawn. It follows `FoodTrail` and deposits `HomeTrail`. It scans its 3×3 neighborhood each tick for `FoodTrail` above a threshold — on detection, promotes itself to `ForagerRole` with goal `SeekFood`. On food pickup, switches to `ForagerRole` with goal `ReturnHome`.

`ForagerRole` has two sub-states through `AntGoal`:

- Goal `SeekFood`: follows `FoodTrail`, deposits `HomeTrail`. On losing the gradient entirely, demotes back to `ScoutRole`.
- Goal `ReturnHome`: follows `HomeTrail`, deposits `FoodTrail`. On nest arrival, drops food into the colony store and switches goal back to `SeekFood`.

`RoleQuota` registers desired role fractions. On each spawn, the colony selects the role with the largest deficit versus current composition. The colony is seeded with `Scout: 1.0` by default and new roles are added by extending the quota.

---

## Sensor algorithm

Hybrid fixed plus stochastic fallback:

1. With probability `Role.ExplorationRate`, take a uniformly random turn and return. This is the exploration breakout layer.
2. Otherwise sample three cones at `heading − SensorAngleRad`, `heading`, `heading + SensorAngleRad`. Each cone aggregates pheromone intensity over a 3×3 cell window at distance `SensorDistance`, subtracting density penalty and wall penalty.
3. If the best cone value exceeds `GradientThreshold`, steer toward its angle.
4. Otherwise run a 16-sample stochastic sweep within `±π/2` at random distances. If any sample clears the threshold, steer toward it.
5. Otherwise add small random wander noise and invoke `Role.OnLostTrail` so the role can demote itself.

---

## Anti-loop and anti-deadlock layers

| # | Mechanism | Location |
|---|-----------|----------|
| 1 | Global pheromone decay | `PheromoneGrid.DecayStep` |
| 2 | Age-weighted exponential deposit | `DepositSystem.Drop` |
| 3 | Max-operation on deposit (no weakening) | `PheromoneGrid.Deposit` |
| 4 | Active trail degradation on follow | `DepositSystem.Drop` and `DegradeInPlace` |
| 5 | Autonomy timeout (max lifetime) | `AutonomySystem.Check` |
| 6 | Exploration breakout probability | `SensorSystem.Sense` |
| 7 | Permanent home marker | `PheromoneGrid.MarkPermanentHome` |
| 8 | Soft density penalty in sensor | `SensorSystem.SampleCone` |
| 9 | No hard ant-to-ant blocking | `MovementSystem.Move` |
| 10 | Smooth heading interpolation with turn clamp | `SteeringSystem.UpdateHeading` |

---

## Food economy

Each colony starts with `Colony.StartingFood` units in its store. Every `SpawnSystem` tick, the colony consumes one unit to produce an ant — no food, no spawning. Foragers that reach their own nest with food increment the store by one. Scouts that enter an enemy nest cell steal one unit from the enemy's store and carry it home as foragers.

---

## World cell types

`CellType` is an enum with `Empty`, `Food`, `Nest`, and `Wall`. Nest cells are set by `World.MarkNestCells` when a colony is placed; the corresponding `_nestOwnerCells[x, y]` entry records which colony owns them. `World.SetCell` refuses to write over nest cells and refuses to write `CellType.Nest` directly, guaranteeing that nest geometry can only change through `AddColony`. `Wall` is reserved for future obstacle support.

---

## Rendering

`Engine` drives a fixed-step simulation via `Stopwatch` tick accumulator and renders each frame through a `FastSKGLControl`. `AntRenderer` builds a supersampled two-row sprite atlas (row 0 = plain body, row 1 = body with red food marker) containing 16 stride frames. Ants are drawn in one `DrawAtlas` call per colony, colored in a single pass by an `SKColorFilter` matrix that simultaneously tints the white body to the colony color and remaps the red food marker to green. The pheromone overlay renders `HomeTrail` in colony color and `FoodTrail` in green, alpha-weighted by intensity.

---

## Build and run

Requires Windows with the .NET 10 SDK. From the repository root:

```
dotnet build
dotnet run
```

Left-click "Add Colony" then click the grid to place a colony. Left-click "Add Food" then drag to paint food. "Pheromones" toggles the trail overlay. Right-click cancels any placement mode.

---

## Extending the simulator

Add a new role by creating `Simulation/Roles/SoldierRole.cs` extending `AntRole`, setting stats in its private constructor, implementing the abstract methods, exposing `public static readonly SoldierRole Instance`, and registering it in `Colony`'s constructor via `RoleQuota.Register(SoldierRole.Instance, fraction)`. Nothing else changes.

Add a new pheromone channel by appending to `PheromoneChannel`, bumping `PheromoneGrid.ChannelCount`, and adding a decay rate in `PheromoneGrid.DecayStep`. Roles that want to follow or deposit the new channel reference it by name.

Add a new goal by appending a value to `GoalType` and handling it in the relevant role's `UpdateGoal`, `GetFollowChannel`, and `GetDepositChannel`.
