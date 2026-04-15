namespace ANTS;

public class PheromoneGrid
{
    public const int ChannelCount = 4;
    public const float MaxIntensity = 1.0f;
    public const float HomeDecayPerSecond = 0.010f;
    public const float FoodDecayPerSecond = 0.020f;
    public const float EnemyDecayPerSecond = 0.015f;
    public const float DangerDecayPerSecond = 0.005f;
    public const float PermanentHomeIntensity = 1.0f;

    private readonly int _width;
    private readonly int _height;
    private readonly float[][,] _intensity;
    private readonly bool[,] _permanentHome;

    public int Width
    {
        get { return _width; }
    }

    public int Height
    {
        get { return _height; }
    }

    public PheromoneGrid(int width, int height)
    {
        _width = width;
        _height = height;
        _intensity = new float[ChannelCount][,];
        for (int c = 0; c < ChannelCount; c++)
        {
            _intensity[c] = new float[width, height];
        }
        _permanentHome = new bool[width, height];
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
            grid[x, y] = intensity > MaxIntensity ? MaxIntensity : intensity;
        }
    }

    public float Get(PheromoneChannel channel, int x, int y)
    {
        if (!InBounds(x, y))
        {
            return 0f;
        }
        return _intensity[(int)channel][x, y];
    }

    public void DegradeInPlace(PheromoneChannel channel, int x, int y, float factor)
    {
        if (!InBounds(x, y))
        {
            return;
        }
        if (channel == PheromoneChannel.HomeTrail && _permanentHome[x, y])
        {
            return;
        }
        _intensity[(int)channel][x, y] *= factor;
    }

    public void DecayStep(float dt)
    {
        DecayChannel(PheromoneChannel.HomeTrail, HomeDecayPerSecond * dt);
        DecayChannel(PheromoneChannel.FoodTrail, FoodDecayPerSecond * dt);
        DecayChannel(PheromoneChannel.EnemyTrail, EnemyDecayPerSecond * dt);
        DecayChannel(PheromoneChannel.DangerTrail, DangerDecayPerSecond * dt);
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

    private bool InBounds(int x, int y)
    {
        return x >= 0 && x < _width && y >= 0 && y < _height;
    }
}
