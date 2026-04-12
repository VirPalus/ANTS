# ANTS

A 2D ant colony simulator built from scratch in C#, with a custom rendering engine and no external game framework.

## About

ANTS is a learning project focused on building a complete colony simulation without relying on existing game libraries. Every part of the engine — the game loop, GPU-accelerated rendering, world model, entity system, and ant behaviors — is written by hand. The goal is to understand how a simulation works under the hood, and to experiment with emergent behavior, pathfinding, and pheromone-based decision making.

## Tech stack

- **Language:** C#
- **Runtime:** .NET 10
- **UI host:** Windows Forms
- **Rendering:** SkiaSharp (OpenGL-accelerated via SKGLControl)
- **Platform:** Windows
- **Editor:** Visual Studio Code
- **Game framework:** none — custom engine

## Project structure

```
ANTS/
├── Engine/
│   ├── Engine.cs              Game loop, layout, input, draw calls, HUD, dispose
│   ├── Engine.Designer.cs     WinForms designer (component container)
│   ├── FastSKGLControl.cs     Custom SKGLControl that bypasses WinForms paint cycle
│   ├── AntRenderer.cs         Sprite atlas builder (16 animation frames, 8x supersampled)
│   ├── UiButton.cs            Lightweight UI button (bounds, label, click handler)
│   └── PlacingMode.cs         Enum: None, Colony, Food
├── Simulation/
│   ├── World.cs               Grid, cell types, colonies, food tracking, occupancy, sim loop
│   ├── Colony.cs              Nest position, color, ant list, spawn timer
│   ├── Ant.cs                 Position, heading, stride phase, role
│   ├── AntBehavior.cs         Movement, wandering, wall bouncing, collision
│   ├── AntRole.cs             Enum: Scout
│   └── CellType.cs            Enum: Empty, Food
├── Program.cs                 Application entry point
└── ANTS.csproj                Project file (.NET 10, WinForms, SkiaSharp)
```

## Architecture

The project separates the **engine** (rendering, input, game loop) from the **simulation** (world state, entities, behaviors).

### Engine

The engine owns the window, the game loop, and how things are drawn on screen.

**Game loop** uses `Application.Idle` + `PeekMessage` for uncapped frame rate. A configurable frame cap (default 10,000) throttles via high-resolution `Stopwatch` ticks. The simulation runs on a fixed timestep at 60 Hz using an accumulator pattern, decoupled from the render frame rate.

**Rendering** uses SkiaSharp's OpenGL backend through a custom `FastSKGLControl` that calls `MakeCurrent()` and `OnPaint()` directly, bypassing the standard WinForms invalidation cycle. Static geometry (grid lines, border, buttons, HUD text) is pre-recorded into `SKPicture` objects and replayed each frame. The HUD picture is rebuilt every 50 ms to avoid per-frame string allocation overhead.

**Ant rendering** uses a sprite atlas approach. `AntRenderer` pre-renders 16 stride animation frames at 8x supersample into a single `SKImage` atlas. At draw time, all ants in a colony are batched into a single `DrawAtlas` call with per-instance rotation/scale transforms. Colony color tinting is applied via `SKColorFilter.CreateBlendMode`.

**Resource management** uses an `Own<T>()` / `Replace<T>()` pattern that tracks all `IDisposable` objects in a central list, disposed in reverse order on shutdown.

### Simulation

**World** holds a 2D grid of `CellType` values, a nest ownership grid (`_nestOwnerCells`), and an ant occupancy grid (`_antOccupancy`). Food cells are tracked in a separate flat array for fast iteration during rendering. The world is sized to 80% of the window in whole cells.

**Colonies** are placed interactively via the UI. Each colony has a diamond-shaped nest (manhattan distance radius of 2), a color, and a list of ants. Ants spawn one per second from the nest center up to a maximum of 200 per colony.

**Ant behavior** is implemented in `AntBehavior` as a static update function per ant per tick. Currently only the Scout role exists. Scouts wander randomly, bounce off walls with heading reflection, and avoid cells occupied by other ants or foreign nests. Movement speed and wander rate are defined in cells-per-second and converted to per-tick values using the simulation Hz.

## Interaction

- **Add Colony** button places a colony nest on the grid (click to place, right-click to cancel). A ghost preview follows the cursor showing where the nest will land. Up to 6 colony colors cycle automatically.
- **Add Food** button enables food painting mode. Click and drag to paint food cells onto the grid. Right-click to cancel.

## Performance HUD

A live heads-up display tracks key performance metrics, smoothed with an exponential moving average: FPS, frame time (total ms per frame), sim time (simulation update cost), and ant draw time.

## Current status

The core engine, rendering pipeline, world simulation, colony placement, food painting, and scout ant behavior are all functional. Ants spawn from nests, wander the grid, bounce off walls, and avoid collisions with other ants and foreign nests.

## Roadmap

- Additional ant roles beyond Scout (forager, soldier, queen)
- Programmable ant behaviors (food seeking, returning to nest)
- Pheromone trail system with decay
- Food pickup and delivery mechanics
- Pan and zoom controls
- Colony statistics and info panels

## Build and run

Requires the .NET 10 SDK and Windows.

```
dotnet run
```
