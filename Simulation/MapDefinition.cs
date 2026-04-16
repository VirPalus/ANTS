namespace ANTS;
using System.Collections.Generic;

// The parsed result of a map image. A MapDefinition is intentionally
// passive: it describes what the world *should* look like on start but
// doesn't touch the World itself. Engine.InitializeWorld applies the
// definition by sizing the world, copying Cells, seeding colonies, and
// placing food.
//
// Coordinate convention: map-pixel (px, py) maps 1:1 to simulation
// cell (cx, cy). There is no sub-cell world-unit layer. A 200x120 PNG
// produces a 200x120-cell simulation.
public class MapDefinition
{
    public int Width { get; }
    public int Height { get; }

    // Base layout. Only Empty / Wall / Food are used here; Nest cells
    // are added later when colonies are seeded from ColonySeeds.
    public CellType[,] Cells { get; }

    // Food cells with their initial amount (0..1). Same cell is also
    // marked as CellType.Food inside Cells, but the amount is tracked
    // separately so the loader can carry per-cell food through to the
    // World.
    public List<FoodSeed> FoodCells { get; }

    // Colonies discovered from red / blue blobs in the map. One entry
    // per connected component; Position is the component's centroid.
    public List<ColonySeed> ColonySeeds { get; }

    // Friendly label for the map, e.g. "Open Field" or the file name
    // without extension. Shown in the start overlay.
    public string Name { get; }

    public MapDefinition(string name, int width, int height)
    {
        Name = name;
        Width = width;
        Height = height;
        Cells = new CellType[width, height];
        FoodCells = new List<FoodSeed>();
        ColonySeeds = new List<ColonySeed>();
    }
}

public struct FoodSeed
{
    public int X;
    public int Y;
    public float Amount;

    public FoodSeed(int x, int y, float amount)
    {
        X = x;
        Y = y;
        Amount = amount;
    }
}

public struct ColonySeed
{
    public int X;
    public int Y;
    public Color Color;

    public ColonySeed(int x, int y, Color color)
    {
        X = x;
        Y = y;
        Color = color;
    }
}
