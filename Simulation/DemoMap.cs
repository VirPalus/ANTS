namespace ANTS;

public static class DemoMap
{
    public const int Width = 256;
    public const int Height = 160;

    public static MapDefinition Build()
    {
        MapDefinition def = new MapDefinition("Demo Arena", Width, Height);

        for (int x = 0; x < Width; x++)
        {
            def.Cells[x, 0] = CellType.Wall;
            def.Cells[x, Height - 1] = CellType.Wall;
        }
        for (int y = 0; y < Height; y++)
        {
            def.Cells[0, y] = CellType.Wall;
            def.Cells[Width - 1, y] = CellType.Wall;
        }

        AddWallRect(def, 70, 40, 90, 80);
        AddWallRect(def, 166, 40, 186, 80);
        AddWallRect(def, 108, 100, 148, 120);

        def.ColonySeeds.Add(new ColonySeed(28, Height / 2, MapLoader.RedTeamColor));
        def.ColonySeeds.Add(new ColonySeed(Width - 28, Height / 2, MapLoader.BlueTeamColor));

        AddFoodRect(def, 118, 18, 138, 32);
        AddFoodRect(def, 118, Height - 32, 138, Height - 18);
        AddFoodRect(def, 50, 110, 62, 122);
        AddFoodRect(def, 194, 30, 206, 42);

        return def;
    }

    private static void AddWallRect(MapDefinition def, int x0, int y0, int x1, int y1)
    {
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                if (x < 0 || x >= def.Width) continue;
                if (y < 0 || y >= def.Height) continue;
                def.Cells[x, y] = CellType.Wall;
            }
        }
    }

    private static void AddFoodRect(MapDefinition def, int x0, int y0, int x1, int y1)
    {
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                if (x < 0 || x >= def.Width) continue;
                if (y < 0 || y >= def.Height) continue;
                if (def.Cells[x, y] == CellType.Wall) continue;
                def.Cells[x, y] = CellType.Food;
                def.FoodCells.Add(new FoodSeed(x, y, 1f));
            }
        }
    }
}
