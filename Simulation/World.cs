namespace ANTS;
using System.Collections.Generic;

public class World
{
    public const int NestRadius = 2;
    public const int SimHz = 60;

    private const int InitialFoodCapacity = 256;
    private const float SpawnIntervalSeconds = 1.0f;
    private const int SpawnIntervalTicks = (int)(SpawnIntervalSeconds * SimHz);

    private CellType[,] _cells;
    public int Width { get; }
    public int Height { get; }

    private List<Colony> _colonies;

    private int[,] _nestOwnerCells;
    private bool[,] _antOccupancy;

    private Point[] _foodCells;
    private int _foodCount;

    private Random _random;

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
        _nestOwnerCells = new int[Width, Height];
        _antOccupancy = new bool[Width, Height];
        _foodCells = new Point[InitialFoodCapacity];
        _foodCount = 0;
        _random = new Random();
    }

    public void Update()
    {
        int colonyCount = _colonies.Count;
        for (int i = 0; i < colonyCount; i++)
        {
            Colony colony = _colonies[i];
            UpdateColony(colony);
        }
    }

    private void UpdateColony(Colony colony)
    {
        colony.SpawnCounter++;
        if (colony.SpawnCounter >= SpawnIntervalTicks)
        {
            colony.SpawnCounter = 0;
            if (colony.Ants.Count < colony.MaxAnts)
            {
                float spawnX = colony.NestX + 0.5f;
                float spawnY = colony.NestY + 0.5f;
                float heading = (float)(_random.NextDouble() * Math.PI * 2);
                colony.SpawnAnt(spawnX, spawnY, heading, AntRole.Scout);
            }
        }

        IReadOnlyList<Ant> ants = colony.Ants;
        int antCount = ants.Count;
        for (int j = 0; j < antCount; j++)
        {
            Ant ant = ants[j];
            AntBehavior.Update(ant, colony, this);
        }
    }

    public float NextRandomFloat()
    {
        return (float)_random.NextDouble();
    }

    public bool IsBlocked(float x, float y, int selfCellX, int selfCellY, Colony colony)
    {
        if (x < 0f || x >= Width)
        {
            return true;
        }
        if (y < 0f || y >= Height)
        {
            return true;
        }

        int cellX = (int)x;
        int cellY = (int)y;

        if (cellX == selfCellX && cellY == selfCellY)
        {
            return false;
        }

        int owner = _nestOwnerCells[cellX, cellY];

        if (owner != 0 && owner != colony.Id)
        {
            return true;
        }
        if (owner == 0 && _antOccupancy[cellX, cellY])
        {
            return true;
        }
        return false;
    }

    public void UpdateAntOccupancy(int oldCellX, int oldCellY, int newCellX, int newCellY)
    {
        if (newCellX == oldCellX && newCellY == oldCellY)
        {
            return;
        }
        if (_antOccupancy[oldCellX, oldCellY])
        {
            _antOccupancy[oldCellX, oldCellY] = false;
        }
        if (_nestOwnerCells[newCellX, newCellY] == 0)
        {
            _antOccupancy[newCellX, newCellY] = true;
        }
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

        int newId = _colonies.Count + 1;
        Colony newColony = new Colony(newId, x, y, color);
        _colonies.Add(newColony);
        MarkNestCells(newColony);
    }

    private void MarkNestCells(Colony colony)
    {
        for (int dy = -NestRadius; dy <= NestRadius; dy++)
        {
            for (int dx = -NestRadius; dx <= NestRadius; dx++)
            {
                int manhattan = Math.Abs(dx) + Math.Abs(dy);
                if (manhattan > NestRadius)
                {
                    continue;
                }
                int cellX = colony.NestX + dx;
                int cellY = colony.NestY + dy;
                if (cellX < 0 || cellX >= Width)
                {
                    continue;
                }
                if (cellY < 0 || cellY >= Height)
                {
                    continue;
                }
                _nestOwnerCells[cellX, cellY] = colony.Id;
            }
        }
    }
}
