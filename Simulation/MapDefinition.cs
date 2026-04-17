namespace ANTS;
using System.Collections.Generic;

public class MapDefinition
{
    public int Width { get; }
    public int Height { get; }

    public CellType[,] Cells { get; }

    public List<FoodSeed> FoodCells { get; }

    public List<ColonySeed> ColonySeeds { get; }

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
