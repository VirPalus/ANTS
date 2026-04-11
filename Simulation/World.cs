namespace ANTS;
using System.Collections.Generic;

public class World
{
    private const int InitialFoodCapacity = 256;

    private CellType[,] _cells;
    public int Width { get; }
    public int Height { get; }

    private List<Colony> _colonies;

    private Point[] _foodCells;
    private int _foodCount;

    public IReadOnlyList<Colony> Colonies
    {
        get { return _colonies; }
    }

    public int FoodCount
    {
        get { return _foodCount; }
    }

    public Point[] FoodCells
    {
        get { return _foodCells; }
    }

    public World(int width, int height)
    {
        Width = width;
        Height = height;
        _cells = new CellType[Width, Height];
        _colonies = new List<Colony>();
        _foodCells = new Point[InitialFoodCapacity];
        _foodCount = 0;
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

        CellType oldType = _cells[x, y];
        if (oldType == type)
        {
            return;
        }

        _cells[x, y] = type;

        if (type == CellType.Food)
        {
            AddFoodCell(x, y);
            return;
        }

        if (oldType == CellType.Food)
        {
            RemoveFoodCell(x, y);
        }
    }

    private void AddFoodCell(int x, int y)
    {
        if (_foodCount == _foodCells.Length)
        {
            int newCapacity = _foodCells.Length * 2;
            Point[] grown = new Point[newCapacity];
            Array.Copy(_foodCells, grown, _foodCount);
            _foodCells = grown;
        }

        _foodCells[_foodCount] = new Point(x, y);
        _foodCount++;
    }

    private void RemoveFoodCell(int x, int y)
    {
        for (int i = 0; i < _foodCount; i++)
        {
            if (_foodCells[i].X == x && _foodCells[i].Y == y)
            {
                _foodCount--;
                _foodCells[i] = _foodCells[_foodCount];
                return;
            }
        }
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
