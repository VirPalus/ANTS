namespace ANTS;
using System.Collections.Generic;

public class World
{
    public const int NestRadius = 2;
    public const int SimHz = 60;
    public const float TickSeconds = 1f / SimHz;
    public const float FoodPickupAmount = 0.1f;

    private const int InitialFoodCapacity = 256;
    private int _foodVersion;

    private CellType[,] _cells;
    private float[,] _foodAmount;
    private int[,] _nestOwnerCells;
    private int[,] _antDensity;
    private int[,] _wallDistance;

    public int Width { get; }
    public int Height { get; }
    public float SimulationTime { get; private set; }
    public SpatialGrid SpatialGrid { get; private set; }

    private List<Colony> _colonies;
    private List<Colony> _deadColonies;

    private Point[] _foodCells;
    private int _foodCount;

    private Random _random;

    public IReadOnlyList<Colony> Colonies
    {
        get { return _colonies; }
    }

    public IReadOnlyList<Colony> DeadColonies
    {
        get { return _deadColonies; }
    }

    public int FoodCount
    {
        get { return _foodCount; }
    }

    public int FoodVersion
    {
        get { return _foodVersion; }
    }

    public Point[] FoodCells
    {
        get { return _foodCells; }
    }

    public World(int width, int height, int? seed = null)
    {
        Width = width;
        Height = height;
        _cells = new CellType[Width, Height];
        _foodAmount = new float[Width, Height];
        _nestOwnerCells = new int[Width, Height];
        _antDensity = new int[Width, Height];
        _wallDistance = new int[Width, Height];
        _colonies = new List<Colony>();
        _deadColonies = new List<Colony>();
        _foodCells = new Point[InitialFoodCapacity];
        _foodCount = 0;
        _random = seed.HasValue ? new Random(seed.Value) : new Random();
        SimulationTime = 0f;
        SpatialGrid = new SpatialGrid(Width, Height);
        ComputeWallDistance();
    }

    // Multi-source 4-neighbor BFS: distance from every cell to the nearest
    // blocked tile. "Blocked" means a Wall cell OR the world boundary.
    // Wall cells themselves and the outer border row/column are the sources
    // (distance 0). Open cells get the step count to the nearest source.
    private void ComputeWallDistance()
    {
        Queue<Point> queue = new Queue<Point>(Width * 4);

        for (int x = 0; x < Width; x++)
        {
            for (int y = 0; y < Height; y++)
            {
                if (_cells[x, y] == CellType.Wall)
                {
                    _wallDistance[x, y] = 0;
                    queue.Enqueue(new Point(x, y));
                }
                else
                {
                    _wallDistance[x, y] = int.MaxValue;
                }
            }
        }

        // Outer border row/columns are also sources so the BFS matches the
        // "stay away from edges" behaviour callers expect. Skip cells that
        // are already wall-sources to keep the queue tidy.
        for (int x = 0; x < Width; x++)
        {
            if (_wallDistance[x, 0] != 0)
            {
                _wallDistance[x, 0] = 0;
                queue.Enqueue(new Point(x, 0));
            }
            if (_wallDistance[x, Height - 1] != 0)
            {
                _wallDistance[x, Height - 1] = 0;
                queue.Enqueue(new Point(x, Height - 1));
            }
        }
        for (int y = 1; y < Height - 1; y++)
        {
            if (_wallDistance[0, y] != 0)
            {
                _wallDistance[0, y] = 0;
                queue.Enqueue(new Point(0, y));
            }
            if (_wallDistance[Width - 1, y] != 0)
            {
                _wallDistance[Width - 1, y] = 0;
                queue.Enqueue(new Point(Width - 1, y));
            }
        }

        while (queue.Count > 0)
        {
            Point p = queue.Dequeue();
            int next = _wallDistance[p.X, p.Y] + 1;

            if (p.X > 0 && _wallDistance[p.X - 1, p.Y] > next)
            {
                _wallDistance[p.X - 1, p.Y] = next;
                queue.Enqueue(new Point(p.X - 1, p.Y));
            }
            if (p.X < Width - 1 && _wallDistance[p.X + 1, p.Y] > next)
            {
                _wallDistance[p.X + 1, p.Y] = next;
                queue.Enqueue(new Point(p.X + 1, p.Y));
            }
            if (p.Y > 0 && _wallDistance[p.X, p.Y - 1] > next)
            {
                _wallDistance[p.X, p.Y - 1] = next;
                queue.Enqueue(new Point(p.X, p.Y - 1));
            }
            if (p.Y < Height - 1 && _wallDistance[p.X, p.Y + 1] > next)
            {
                _wallDistance[p.X, p.Y + 1] = next;
                queue.Enqueue(new Point(p.X, p.Y + 1));
            }
        }
    }

    // Call after bulk-placing walls (e.g. finishing a map load) so the
    // cached wall-distance reflects the new layout. Individual SetCell
    // calls do not auto-recompute; bulk-edit then flip once.
    public void RecomputeWallDistance()
    {
        ComputeWallDistance();
    }

    // Bulk-apply the walls + food from a parsed map. Colonies are NOT
    // added here -- Engine iterates ColonySeeds separately so color/id
    // assignment stays in one place. Wall-distance is recomputed once
    // at the end, which is cheap relative to touching every cell.
    public void ApplyMapLayout(MapDefinition def)
    {
        if (def.Width != Width || def.Height != Height)
        {
            throw new ArgumentException("Map dimensions don't match world dimensions.");
        }

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                if (def.Cells[x, y] == CellType.Wall)
                {
                    _cells[x, y] = CellType.Wall;
                }
            }
        }

        int foodCount = def.FoodCells.Count;
        for (int i = 0; i < foodCount; i++)
        {
            FoodSeed f = def.FoodCells[i];
            if (f.X < 0 || f.X >= Width) continue;
            if (f.Y < 0 || f.Y >= Height) continue;
            // Food-on-wall conflicts (should be rare: the map loader
            // guards against them too) fall out as "wall wins".
            if (_cells[f.X, f.Y] == CellType.Wall) continue;

            _cells[f.X, f.Y] = CellType.Food;
            _foodAmount[f.X, f.Y] = f.Amount;
            AddFoodCell(f.X, f.Y);
        }

        RecomputeWallDistance();
    }

    public int GetWallDistance(int x, int y)
    {
        if (x < 0 || x >= Width)
        {
            return 0;
        }
        if (y < 0 || y >= Height)
        {
            return 0;
        }
        return _wallDistance[x, y];
    }

    public bool IsWall(int x, int y)
    {
        if (x < 0 || x >= Width)
        {
            return true;
        }
        if (y < 0 || y >= Height)
        {
            return true;
        }
        return _cells[x, y] == CellType.Wall;
    }

    /// <summary>
    /// Fast line-of-sight check using DDA ray march through the grid.
    /// Returns true if no wall cell blocks the straight line from (x0,y0) to (x1,y1).
    /// </summary>
    public bool HasLineOfSight(float x0, float y0, float x1, float y1)
    {
        float dx = x1 - x0;
        float dy = y1 - y0;

        int cellX = (int)x0;
        int cellY = (int)y0;
        int endCellX = (int)x1;
        int endCellY = (int)y1;

        // Already in the same cell — trivially visible.
        if (cellX == endCellX && cellY == endCellY)
        {
            return true;
        }

        // Direction of stepping (+1 or -1).
        int stepX = dx > 0 ? 1 : -1;
        int stepY = dy > 0 ? 1 : -1;

        // Distance along the ray to cross one full cell in X or Y.
        float tDeltaX = dx != 0 ? Math.Abs(1f / dx) : float.MaxValue;
        float tDeltaY = dy != 0 ? Math.Abs(1f / dy) : float.MaxValue;

        // Distance from start to the first X or Y cell boundary.
        float tMaxX;
        if (dx > 0)
        {
            tMaxX = (cellX + 1 - x0) * tDeltaX;
        }
        else if (dx < 0)
        {
            tMaxX = (x0 - cellX) * tDeltaX;
        }
        else
        {
            tMaxX = float.MaxValue;
        }

        float tMaxY;
        if (dy > 0)
        {
            tMaxY = (cellY + 1 - y0) * tDeltaY;
        }
        else if (dy < 0)
        {
            tMaxY = (y0 - cellY) * tDeltaY;
        }
        else
        {
            tMaxY = float.MaxValue;
        }

        // March through cells until we reach the target cell or hit a wall.
        // Safety limit to avoid infinite loops on edge cases.
        int maxSteps = Math.Abs(endCellX - cellX) + Math.Abs(endCellY - cellY) + 2;
        for (int step = 0; step < maxSteps; step++)
        {
            if (tMaxX < tMaxY)
            {
                cellX += stepX;
                tMaxX += tDeltaX;
            }
            else
            {
                cellY += stepY;
                tMaxY += tDeltaY;
            }

            if (cellX == endCellX && cellY == endCellY)
            {
                return true; // Reached target without hitting a wall.
            }

            if (IsWall(cellX, cellY))
            {
                return false; // Wall blocks line of sight.
            }
        }

        return true; // Fallback: assume visible.
    }

    public void Update()
    {
        float dt = TickSeconds;
        SimulationTime += dt;

        // Rebuild spatial hash once per tick BEFORE any ant updates.
        SpatialGrid.Rebuild(_colonies);

        int colonyCount = _colonies.Count;
        for (int i = 0; i < colonyCount; i++)
        {
            _colonies[i].PheromoneGrid.DecayStep(dt);
        }

        for (int i = 0; i < colonyCount; i++)
        {
            Colony colony = _colonies[i];
            SpawnSystem.Tick(colony, this, dt);

            IReadOnlyList<Ant> ants = colony.Ants;
            int antCount = ants.Count;
            for (int j = 0; j < antCount; j++)
            {
                Ant ant = ants[j];
                if (ant.IsDead)
                {
                    continue;
                }
                AntBehavior.Update(ant, colony, this, dt);
            }

            colony.RemoveDeadAnts();
            colony.RecountRoles();
            colony.UpdateProtectedRadius();
            colony.UpdateNestHealth(this, dt);
            colony.UpdateSignals(dt);
            colony.TickAge(dt);
            colony.Stats.Tick(colony, dt);
        }

        DespawnDeadColonies();
    }

    private void DespawnDeadColonies()
    {
        for (int i = _colonies.Count - 1; i >= 0; i--)
        {
            Colony colony = _colonies[i];
            if (colony.IsNestDead)
            {
                int remainingFood = colony.NestFood;
                colony.MarkDead("overrun", SimulationTime);
                ForgetEnemyTrailAboutDeadColony(colony);
                ClearNestCells(colony);
                ScatterFoodAtNest(colony, remainingFood);
                _colonies.RemoveAt(i);
                _deadColonies.Add(colony);
                continue;
            }
            if (colony.Ants.Count == 0 && colony.NestFood == 0)
            {
                colony.MarkDead("starved", SimulationTime);
                ForgetEnemyTrailAboutDeadColony(colony);
                ClearNestCells(colony);
                _colonies.RemoveAt(i);
                _deadColonies.Add(colony);
            }
        }
    }

    // The dead colony is no longer a threat to anyone. Every other colony
    // drops exactly the EnemyTrail layer that was tagged with this colony's
    // Id. Trails about other still-living enemies live in their own layers
    // and are untouched. Also clear any ants still referencing this id as
    // their detection/combat target so they don't deposit under a dead key.
    private void ForgetEnemyTrailAboutDeadColony(Colony deadColony)
    {
        int colonyCount = _colonies.Count;
        for (int c = 0; c < colonyCount; c++)
        {
            Colony other = _colonies[c];
            if (other.Id == deadColony.Id)
            {
                continue;
            }
            other.PheromoneGrid.ClearEnemyTrailForTarget(deadColony.Id);

            IReadOnlyList<Ant> ants = other.Ants;
            int antCount = ants.Count;
            for (int a = 0; a < antCount; a++)
            {
                Ant ant = ants[a];
                if (ant.DetectedEnemyColonyId == deadColony.Id)
                {
                    ant.DetectedEnemyColonyId = 0;
                }
                if (ant.LastCombatTargetColonyId == deadColony.Id)
                {
                    ant.LastCombatTargetColonyId = 0;
                }
            }
        }
    }

    private void ScatterFoodAtNest(Colony colony, int totalFood)
    {
        if (totalFood <= 0)
        {
            return;
        }

        int cellCount = 0;
        for (int dy = -NestRadius; dy <= NestRadius; dy++)
        {
            for (int dx = -NestRadius; dx <= NestRadius; dx++)
            {
                int manhattan = Math.Abs(dx) + Math.Abs(dy);
                if (manhattan > NestRadius)
                {
                    continue;
                }
                int cx = colony.NestX + dx;
                int cy = colony.NestY + dy;
                if (cx < 0 || cx >= Width)
                {
                    continue;
                }
                if (cy < 0 || cy >= Height)
                {
                    continue;
                }
                cellCount++;
            }
        }

        if (cellCount == 0)
        {
            return;
        }

        float perCell = ((float)totalFood * FoodPickupAmount) / (float)cellCount;
        for (int dy = -NestRadius; dy <= NestRadius; dy++)
        {
            for (int dx = -NestRadius; dx <= NestRadius; dx++)
            {
                int manhattan = Math.Abs(dx) + Math.Abs(dy);
                if (manhattan > NestRadius)
                {
                    continue;
                }
                int cx = colony.NestX + dx;
                int cy = colony.NestY + dy;
                if (cx < 0 || cx >= Width)
                {
                    continue;
                }
                if (cy < 0 || cy >= Height)
                {
                    continue;
                }

                if (_cells[cx, cy] == CellType.Food)
                {
                    _foodAmount[cx, cy] += perCell;
                }
                else if (_cells[cx, cy] == CellType.Empty)
                {
                    _cells[cx, cy] = CellType.Food;
                    _foodAmount[cx, cy] = perCell;
                    AddFoodCell(cx, cy);
                }
            }
        }

        colony.NestFood = 0;
    }

    private void ClearNestCells(Colony colony)
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
                _nestOwnerCells[cellX, cellY] = 0;
                if (_cells[cellX, cellY] == CellType.Nest)
                {
                    _cells[cellX, cellY] = CellType.Empty;
                }
            }
        }
    }

    public float NextRandomFloat()
    {
        return (float)_random.NextDouble();
    }

    public int GetNestOwner(int cellX, int cellY)
    {
        if (cellX < 0 || cellX >= Width)
        {
            return 0;
        }
        if (cellY < 0 || cellY >= Height)
        {
            return 0;
        }
        return _nestOwnerCells[cellX, cellY];
    }

    public Colony? GetColonyById(int colonyId)
    {
        int count = _colonies.Count;
        for (int i = 0; i < count; i++)
        {
            Colony candidate = _colonies[i];
            if (candidate.Id == colonyId)
            {
                return candidate;
            }
        }
        return null;
    }

    public bool IsEnemyNest(int cellX, int cellY, int colonyId)
    {
        if (cellX < 0 || cellX >= Width)
        {
            return false;
        }
        if (cellY < 0 || cellY >= Height)
        {
            return false;
        }
        int owner = _nestOwnerCells[cellX, cellY];
        return owner != 0 && owner != colonyId;
    }

    public int GetAntDensity(int x, int y)
    {
        if (x < 0 || x >= Width)
        {
            return 0;
        }
        if (y < 0 || y >= Height)
        {
            return 0;
        }
        return _antDensity[x, y];
    }

    public void IncrementDensity(int x, int y)
    {
        if (x < 0 || x >= Width)
        {
            return;
        }
        if (y < 0 || y >= Height)
        {
            return;
        }
        _antDensity[x, y]++;
    }

    public void DecrementDensity(int x, int y)
    {
        if (x < 0 || x >= Width)
        {
            return;
        }
        if (y < 0 || y >= Height)
        {
            return;
        }
        if (_antDensity[x, y] > 0)
        {
            _antDensity[x, y]--;
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

    public float GetFoodAmount(int x, int y)
    {
        if (x < 0 || x >= Width)
        {
            return 0f;
        }
        if (y < 0 || y >= Height)
        {
            return 0f;
        }
        return _foodAmount[x, y];
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
        if (type == CellType.Nest)
        {
            return;
        }

        CellType oldType = _cells[x, y];
        if (oldType == CellType.Nest)
        {
            return;
        }
        // Can't place food on walls — ants can't reach those cells.
        if (type == CellType.Food && oldType == CellType.Wall)
        {
            return;
        }
        if (oldType == type)
        {
            return;
        }

        _cells[x, y] = type;

        if (type == CellType.Food)
        {
            _foodAmount[x, y] = 0.4f;
            AddFoodCell(x, y);
            return;
        }

        if (oldType == CellType.Food)
        {
            _foodAmount[x, y] = 0f;
            RemoveFoodCell(x, y);
        }
    }

    public void DropFoodFromDeadAnt(int x, int y, int carryingFood)
    {
        if (carryingFood <= 0)
        {
            return;
        }
        if (x < 0 || x >= Width)
        {
            return;
        }
        if (y < 0 || y >= Height)
        {
            return;
        }
        if (_cells[x, y] == CellType.Nest)
        {
            return;
        }

        float amount = carryingFood * FoodPickupAmount;
        if (_cells[x, y] == CellType.Food)
        {
            _foodAmount[x, y] += amount;
            return;
        }

        _cells[x, y] = CellType.Food;
        _foodAmount[x, y] = amount;
        AddFoodCell(x, y);
    }

    public bool TakeFood(int x, int y)
    {
        if (x < 0 || x >= Width)
        {
            return false;
        }
        if (y < 0 || y >= Height)
        {
            return false;
        }
        if (_cells[x, y] != CellType.Food)
        {
            return false;
        }

        _foodAmount[x, y] -= FoodPickupAmount;
        _foodVersion++;
        if (_foodAmount[x, y] <= 0f)
        {
            _foodAmount[x, y] = 0f;
            _cells[x, y] = CellType.Empty;
            RemoveFoodCell(x, y);
        }
        return true;
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
        _foodVersion++;
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
        Colony newColony = new Colony(newId, x, y, color, Width, Height, _random);
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
                if (_cells[cellX, cellY] == CellType.Food)
                {
                    _foodAmount[cellX, cellY] = 0f;
                    RemoveFoodCell(cellX, cellY);
                }
                _cells[cellX, cellY] = CellType.Nest;
                _nestOwnerCells[cellX, cellY] = colony.Id;
                colony.PheromoneGrid.MarkPermanentHome(cellX, cellY);
            }
        }
    }
}
