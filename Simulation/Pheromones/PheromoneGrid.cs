namespace ANTS;
using System.Collections.Generic;

public class PheromoneGrid
{
    public const int ChannelCount = 3;
    public const float MaxIntensity = 1.0f;
    public const float EstablishedRouteMax = 2.0f;
    public const float ReinforceFraction = 0.10f;
    public const float DecayPerSecond = 1f / 60f;
    public const float PermanentHomeIntensity = 1.0f;

    private readonly int _width;
    private readonly int _height;
    private readonly float[][,] _intensity;
    private readonly bool[,] _permanentHome;
    private readonly Dictionary<int, float[,]> _enemyTrails;

    // Sparse decay: track which cells have non-zero pheromone per channel.
    // Packed index = x * _height + y. _activeSet avoids duplicate inserts.
    private readonly List<int>[] _activeCells;
    private readonly bool[][,] _activeSet;

    // Sparse tracking for enemy trails: one list + set per target colony.
    private readonly Dictionary<int, List<int>> _enemyActiveCells;
    private readonly Dictionary<int, bool[,]> _enemyActiveSet;

    public PheromoneGrid(int width, int height)
    {
        _width = width;
        _height = height;
        _intensity = new float[ChannelCount][,];
        _activeCells = new List<int>[ChannelCount];
        _activeSet = new bool[ChannelCount][,];
        for (int c = 0; c < ChannelCount; c++)
        {
            if (c == (int)PheromoneChannel.EnemyTrail)
            {
                continue;
            }
            _intensity[c] = new float[width, height];
            _activeCells[c] = new List<int>(256);
            _activeSet[c] = new bool[width, height];
        }
        _permanentHome = new bool[width, height];
        _enemyTrails = new Dictionary<int, float[,]>();
        _enemyActiveCells = new Dictionary<int, List<int>>();
        _enemyActiveSet = new Dictionary<int, bool[,]>();
    }

    public void MarkPermanentHome(int x, int y)
    {
        if (!InBounds(x, y))
        {
            return;
        }
        _permanentHome[x, y] = true;
        int c = (int)PheromoneChannel.HomeTrail;
        _intensity[c][x, y] = PermanentHomeIntensity;
        // Permanent home cells are always active but never removed.
        if (!_activeSet[c][x, y])
        {
            _activeSet[c][x, y] = true;
            _activeCells[c].Add(x * _height + y);
        }
    }

    public void Deposit(PheromoneChannel channel, int x, int y, float intensity)
    {
        if (channel == PheromoneChannel.EnemyTrail)
        {
            return;
        }
        if (!InBounds(x, y))
        {
            return;
        }
        if (intensity <= 0f)
        {
            return;
        }
        int c = (int)channel;
        float[,] grid = _intensity[c];
        float existing = grid[x, y];

        // Track this cell as active if it wasn't already.
        if (!_activeSet[c][x, y])
        {
            _activeSet[c][x, y] = true;
            _activeCells[c].Add(x * _height + y);
        }

        if (intensity > existing)
        {
            grid[x, y] = intensity > MaxIntensity ? MaxIntensity : intensity;
        }
        else
        {
            float reinforced = existing + intensity * ReinforceFraction;
            if (reinforced > EstablishedRouteMax) reinforced = EstablishedRouteMax;
            grid[x, y] = reinforced;
        }
    }

    public void DepositEnemy(int targetColonyId, int x, int y, float intensity)
    {
        if (targetColonyId <= 0)
        {
            return;
        }
        if (!InBounds(x, y))
        {
            return;
        }
        if (intensity <= 0f)
        {
            return;
        }
        float[,]? grid;
        if (!_enemyTrails.TryGetValue(targetColonyId, out grid))
        {
            grid = new float[_width, _height];
            _enemyTrails[targetColonyId] = grid;
            _enemyActiveCells[targetColonyId] = new List<int>(128);
            _enemyActiveSet[targetColonyId] = new bool[_width, _height];
        }

        // Track active cell.
        bool[,] activeSet = _enemyActiveSet[targetColonyId];
        if (!activeSet[x, y])
        {
            activeSet[x, y] = true;
            _enemyActiveCells[targetColonyId].Add(x * _height + y);
        }

        float existing = grid[x, y];
        if (intensity > existing)
        {
            grid[x, y] = intensity > MaxIntensity ? MaxIntensity : intensity;
        }
        else
        {
            float reinforced = existing + intensity * ReinforceFraction;
            if (reinforced > EstablishedRouteMax) reinforced = EstablishedRouteMax;
            grid[x, y] = reinforced;
        }
    }

    public float Get(PheromoneChannel channel, int x, int y)
    {
        if (!InBounds(x, y))
        {
            return 0f;
        }
        if (channel == PheromoneChannel.EnemyTrail)
        {
            float max = 0f;
            foreach (float[,] grid in _enemyTrails.Values)
            {
                float v = grid[x, y];
                if (v > max)
                {
                    max = v;
                }
            }
            return max;
        }
        return _intensity[(int)channel][x, y];
    }

    public void DegradeInPlace(PheromoneChannel channel, int x, int y, float factor)
    {
        if (!InBounds(x, y))
        {
            return;
        }
        if (channel == PheromoneChannel.EnemyTrail)
        {
            foreach (float[,] grid in _enemyTrails.Values)
            {
                grid[x, y] *= factor;
            }
            return;
        }
        if (channel == PheromoneChannel.HomeTrail && _permanentHome[x, y])
        {
            return;
        }
        _intensity[(int)channel][x, y] *= factor;
    }

    public void ClearEnemyTrailForTarget(int targetColonyId)
    {
        _enemyTrails.Remove(targetColonyId);
        _enemyActiveCells.Remove(targetColonyId);
        _enemyActiveSet.Remove(targetColonyId);
    }

    public void DecayStep(float dt)
    {
        float amount = DecayPerSecond * dt;
        DecayChannelSparse(PheromoneChannel.HomeTrail, amount);
        DecayChannelSparse(PheromoneChannel.FoodTrail, amount);
        DecayEnemyTrailsSparse(amount);
    }

    private void DecayChannelSparse(PheromoneChannel channel, float amount)
    {
        int c = (int)channel;
        float[,] grid = _intensity[c];
        bool isHome = channel == PheromoneChannel.HomeTrail;
        List<int> active = _activeCells[c];
        bool[,] activeSet = _activeSet[c];

        int writeIdx = 0;
        int count = active.Count;
        for (int i = 0; i < count; i++)
        {
            int packed = active[i];
            int x = packed / _height;
            int y = packed % _height;

            if (isHome && _permanentHome[x, y])
            {
                grid[x, y] = PermanentHomeIntensity;
                active[writeIdx] = packed;
                writeIdx++;
                continue;
            }

            float v = grid[x, y];
            if (v <= 0f)
            {
                // Already zero — remove from active list.
                activeSet[x, y] = false;
                continue;
            }

            v -= amount;
            if (v <= 0f)
            {
                v = 0f;
                grid[x, y] = 0f;
                activeSet[x, y] = false;
                continue;
            }

            grid[x, y] = v;
            active[writeIdx] = packed;
            writeIdx++;
        }

        // Trim the list to only surviving entries.
        if (writeIdx < count)
        {
            active.RemoveRange(writeIdx, count - writeIdx);
        }
    }

    private void DecayEnemyTrailsSparse(float amount)
    {
        foreach (KeyValuePair<int, float[,]> kv in _enemyTrails)
        {
            int targetId = kv.Key;
            float[,] grid = kv.Value;
            List<int> active = _enemyActiveCells[targetId];
            bool[,] activeSet = _enemyActiveSet[targetId];

            int writeIdx = 0;
            int count = active.Count;
            for (int i = 0; i < count; i++)
            {
                int packed = active[i];
                int x = packed / _height;
                int y = packed % _height;

                float v = grid[x, y];
                if (v <= 0f)
                {
                    activeSet[x, y] = false;
                    continue;
                }

                v -= amount;
                if (v <= 0f)
                {
                    v = 0f;
                    grid[x, y] = 0f;
                    activeSet[x, y] = false;
                    continue;
                }

                grid[x, y] = v;
                active[writeIdx] = packed;
                writeIdx++;
            }

            if (writeIdx < count)
            {
                active.RemoveRange(writeIdx, count - writeIdx);
            }
        }
    }

    private bool InBounds(int x, int y)
    {
        return x >= 0 && x < _width && y >= 0 && y < _height;
    }
}
