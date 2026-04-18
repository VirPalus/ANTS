namespace ANTS;

public class SpatialGrid
{
    public const int CellSize = 8;

    private readonly int _gridW;
    private readonly int _gridH;
    private readonly int _bucketCount;

    private int[] _bucketStart;
    private int[] _bucketCountArr;

    private float[] _entryX;
    private float[] _entryY;
    private int[] _entryColonyId;
    private Ant[] _entryAnt;
    private int _entryCount;

    private int[] _tempCounts;

    public SpatialGrid(int worldWidth, int worldHeight)
    {
        _gridW = (worldWidth + CellSize - 1) / CellSize;
        _gridH = (worldHeight + CellSize - 1) / CellSize;
        _bucketCount = _gridW * _gridH;

        _bucketStart = new int[_bucketCount];
        _bucketCountArr = new int[_bucketCount];
        _tempCounts = new int[_bucketCount];

        int initialCap = 512;
        _entryX = new float[initialCap];
        _entryY = new float[initialCap];
        _entryColonyId = new int[initialCap];
        _entryAnt = new Ant[initialCap];
        _entryCount = 0;
    }

    public void Rebuild(IReadOnlyList<Colony> colonies)
    {
        Array.Clear(_tempCounts, 0, _bucketCount);

        int totalAnts = 0;
        int colonyCount = colonies.Count;
        for (int c = 0; c < colonyCount; c++)
        {
            IReadOnlyList<Ant> ants = colonies[c].Ants;
            int antCount = ants.Count;
            for (int a = 0; a < antCount; a++)
            {
                Ant ant = ants[a];
                if (ant.IsDead)
                {
                    continue;
                }
                int bx = (int)ant.X / CellSize;
                int by = (int)ant.Y / CellSize;
                if (bx < 0) bx = 0;
                if (bx >= _gridW) bx = _gridW - 1;
                if (by < 0) by = 0;
                if (by >= _gridH) by = _gridH - 1;
                _tempCounts[by * _gridW + bx]++;
                totalAnts++;
            }
        }

        if (totalAnts > _entryX.Length)
        {
            int newCap = totalAnts * 2;
            _entryX = new float[newCap];
            _entryY = new float[newCap];
            _entryColonyId = new int[newCap];
            _entryAnt = new Ant[newCap];
        }
        _entryCount = totalAnts;

        int offset = 0;
        for (int b = 0; b < _bucketCount; b++)
        {
            _bucketStart[b] = offset;
            _bucketCountArr[b] = _tempCounts[b];
            offset += _tempCounts[b];
            _tempCounts[b] = 0;
        }

        for (int c = 0; c < colonyCount; c++)
        {
            Colony colony = colonies[c];
            int colonyId = colony.Id;
            IReadOnlyList<Ant> ants = colony.Ants;
            int antCount = ants.Count;
            for (int a = 0; a < antCount; a++)
            {
                Ant ant = ants[a];
                if (ant.IsDead)
                {
                    continue;
                }
                int bx = (int)ant.X / CellSize;
                int by = (int)ant.Y / CellSize;
                if (bx < 0) bx = 0;
                if (bx >= _gridW) bx = _gridW - 1;
                if (by < 0) by = 0;
                if (by >= _gridH) by = _gridH - 1;
                int bucket = by * _gridW + bx;
                int idx = _bucketStart[bucket] + _tempCounts[bucket];
                _tempCounts[bucket]++;

                _entryX[idx] = ant.X;
                _entryY[idx] = ant.Y;
                _entryColonyId[idx] = colonyId;
                _entryAnt[idx] = ant;
            }
        }
    }

    public void QueryRadius(float cx, float cy, float radius, int excludeColonyId, QueryCallback callback, ref QueryState state)
    {
        float radiusSq = radius * radius;
        int minBx = ((int)(cx - radius)) / CellSize;
        int maxBx = ((int)(cx + radius)) / CellSize;
        int minBy = ((int)(cy - radius)) / CellSize;
        int maxBy = ((int)(cy + radius)) / CellSize;

        if (minBx < 0) minBx = 0;
        if (maxBx >= _gridW) maxBx = _gridW - 1;
        if (minBy < 0) minBy = 0;
        if (maxBy >= _gridH) maxBy = _gridH - 1;

        for (int by = minBy; by <= maxBy; by++)
        {
            int rowOffset = by * _gridW;
            for (int bx = minBx; bx <= maxBx; bx++)
            {
                int bucket = rowOffset + bx;
                int start = _bucketStart[bucket];
                int count = _bucketCountArr[bucket];
                int end = start + count;
                for (int i = start; i < end; i++)
                {
                    if (_entryColonyId[i] == excludeColonyId)
                    {
                        continue;
                    }
                    float dx = _entryX[i] - cx;
                    float dy = _entryY[i] - cy;
                    float distSq = dx * dx + dy * dy;
                    if (distSq <= radiusSq)
                    {
                        callback(ref state, _entryAnt[i], _entryColonyId[i], dx, dy, distSq);
                    }
                }
            }
        }
    }
    public int CountInRadius(float cx, float cy, float radius, int excludeColonyId, int maxCount)
    {
        float radiusSq = radius * radius;
        int minBx = ((int)(cx - radius)) / CellSize;
        int maxBx = ((int)(cx + radius)) / CellSize;
        int minBy = ((int)(cy - radius)) / CellSize;
        int maxBy = ((int)(cy + radius)) / CellSize;

        if (minBx < 0) minBx = 0;
        if (maxBx >= _gridW) maxBx = _gridW - 1;
        if (minBy < 0) minBy = 0;
        if (maxBy >= _gridH) maxBy = _gridH - 1;

        int count = 0;
        for (int by = minBy; by <= maxBy; by++)
        {
            int rowOffset = by * _gridW;
            for (int bx = minBx; bx <= maxBx; bx++)
            {
                int bucket = rowOffset + bx;
                int start = _bucketStart[bucket];
                int bCount = _bucketCountArr[bucket];
                int end = start + bCount;
                for (int i = start; i < end; i++)
                {
                    if (_entryColonyId[i] == excludeColonyId)
                    {
                        continue;
                    }
                    if (_entryAnt[i].IsDead)
                    {
                        continue;
                    }
                    float dx = _entryX[i] - cx;
                    float dy = _entryY[i] - cy;
                    float distSq = dx * dx + dy * dy;
                    if (distSq <= radiusSq)
                    {
                        count++;
                        if (count >= maxCount)
                        {
                            return count;
                        }
                    }
                }
            }
        }
        return count;
    }

    public delegate void QueryCallback(ref QueryState state, Ant ant, int colonyId, float dx, float dy, float distSq);
}

public struct QueryState
{
    public float SumX;
    public float SumY;
    public int ClosestEnemyColonyId;
    public float ClosestDistSq;

    public Ant? ClosestEnemy;
    public Colony? ClosestEnemyColony;
    public float ScratchScalar;

    public IReadOnlyList<Colony>? Colonies;

    public World? World;

    public float QueryCenterX;
    public float QueryCenterY;

    public float CosHeading;
    public float SinHeading;
    public float CosHalfAngle;
}
