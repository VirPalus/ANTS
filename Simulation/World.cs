namespace ANTS;
using System.Collections.Generic;

public class World
{
    private CellType[,] _cells;
    public int Width { get; }
    public int Height { get; }

    private List<Colony> _colonies;

    public IReadOnlyList<Colony> Colonies
    {
        get { return _colonies; }
    }

    public World(int width, int height)
    {
        Width = width;
        Height = height;
        _cells = new CellType[Width, Height];
        _colonies = new List<Colony>();
    }

    public CellType GetCell(int x, int y)
    {
        if (x < 0 || x >= Width)
        {
            return CellType.Empty;
        }
        if (y < 0 || y >= Height)
        {
            return CellType.Empty;
        }
        return _cells[x, y];
    }

    public void SetCell(int x, int y, CellType type)
    {
        if (x < 0 || x >= Width)
        {
            return;
        }
        if (y < 0 || y >= Height)
        {
            return;
        }
        _cells[x, y] = type;
    }

    public void AddColony(int x, int y, Color color)
    {
        if (x < 0 || x >= Width)
        {
            return;
        }
        if (y < 0 || y >= Height)
        {
            return;
        }

        Colony newColony = new Colony(x, y, color);
        _colonies.Add(newColony);
    }
}
