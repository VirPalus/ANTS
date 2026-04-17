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

    public PheromoneGrid(int width, int height)
    {
        _width = width;
        _height = height;
        _intensity = new float[ChannelCount][,];
        for (int c = 0; c < ChannelCount; c++)
        {
            if (c == (int)PheromoneChannel.EnemyTrail)
            {
                continue;
            }
            _intensity[c] = new float[width, height];
        }
        _permanentHome = new bool[width, height];
        _enemyTrails = new Dictionary<int, float[,]>();
    }

    public void MarkPermanentHome(int x, int y)
    {
        if (!InBounds(x, y))
        {
            return;
        }
        _permanentHome[x, y] = true;
        _intensity[(int)PheromoneChannel.HomeTrail][x, y] = PermanentHomeIntensity;
    }

    public void Deposit(PheromoneChannel channel, int x, int y, float intensity)
    {
        if (channel == PheromoneChannel.EnemyTrail)
        {
            // EnemyTrail deposits require a target colony id; use DepositEnemy.
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
        if (intensity > existing)
        {
            // First ant or stronger deposit — set directly.
            grid[x, y] = intensity > MaxIntensity ? MaxIntensity : intensity;
        }
        else
        {
            // Established Route: trail already exists. Add 10% of deposit
            // on top so paths used by multiple ants persist longer.
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

    // Wipe every EnemyTrail cell that was deposited because of this specific
    // target colony. Trails about other still-alive enemies are untouched.
    public void ClearEnemyTrailForTarget(int targetColonyId)
    {
        _enemyTrails.Remove(targetColonyId);
    }

    public void DecayStep(float dt)
    {
        DecayChannel(PheromoneChannel.HomeTrail, DecayPerSecond * dt);
        DecayChannel(PheromoneChannel.FoodTrail, DecayPerSecond * dt);
        DecayEnemyTrails(DecayPerSecond * dt);
    }

    private void DecayChannel(PheromoneChannel channel, float amount)
    {
        int c = (int)channel;
        float[,] grid = _intensity[c];
        bool isHome = channel == PheromoneChannel.HomeTrail;
        for (int x = 0; x < _width; x++)
        {
            for (int y = 0; y < _height; y++)
            {
                if (isHome && _permanentHome[x, y])
                {
                    grid[x, y] = PermanentHomeIntensity;
                    continue;
                }
                float v = grid[x, y];
                if (v <= 0f)
                {
                    continue;
                }
                v -= amount;
                if (v < 0f)
                {
                    v = 0f;
                }
                grid[x, y] = v;
            }
        }
    }

    private void DecayEnemyTrails(float amount)
    {
        foreach (KeyValuePair<int, float[,]> kv in _enemyTrails)
        {
            float[,] grid = kv.Value;
            for (int x = 0; x < _width; x++)
            {
                for (int y = 0; y < _height; y++)
                {
                    float v = grid[x, y];
                    if (v <= 0f)
                    {
                        continue;
                    }
                    v -= amount;
                    if (v < 0f)
                    {
                        v = 0f;
                    }
                    grid[x, y] = v;
                }
            }
        }
    }

    private bool InBounds(int x, int y)
    {
        return x >= 0 && x < _width && y >= 0 && y < _height;
    }
}