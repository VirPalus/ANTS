namespace ANTS;

public class ColonyStats
{
    public const int SampleCount = 240;
    public const float SampleIntervalSeconds = 1.0f;

    private readonly int[] _populationHistory;
    private readonly int[] _foodHistory;
    private int _writeIndex;
    private int _validSamples;
    private float _accumulator;

    public int SampleWriteIndex
    {
        get { return _writeIndex; }
    }

    public int ValidSamples
    {
        get { return _validSamples; }
    }

    public ColonyStats()
    {
        _populationHistory = new int[SampleCount];
        _foodHistory = new int[SampleCount];
        _writeIndex = 0;
        _validSamples = 0;
        _accumulator = 0f;
    }

    public void Tick(Colony colony, float dt)
    {
        _accumulator += dt;
        if (_accumulator < SampleIntervalSeconds)
        {
            return;
        }
        _accumulator -= SampleIntervalSeconds;

        _populationHistory[_writeIndex] = colony.Ants.Count;
        _foodHistory[_writeIndex] = colony.NestFood;
        _writeIndex = (_writeIndex + 1) % SampleCount;
        if (_validSamples < SampleCount)
        {
            _validSamples++;
        }
    }

    public int GetPopulationAt(int orderedIndex)
    {
        if (orderedIndex < 0 || orderedIndex >= _validSamples)
        {
            return 0;
        }
        int start = (_writeIndex - _validSamples + SampleCount) % SampleCount;
        int slot = (start + orderedIndex) % SampleCount;
        return _populationHistory[slot];
    }

    public int GetFoodAt(int orderedIndex)
    {
        if (orderedIndex < 0 || orderedIndex >= _validSamples)
        {
            return 0;
        }
        int start = (_writeIndex - _validSamples + SampleCount) % SampleCount;
        int slot = (start + orderedIndex) % SampleCount;
        return _foodHistory[slot];
    }

    public int GetMaxPopulation()
    {
        int max = 1;
        for (int i = 0; i < _validSamples; i++)
        {
            int v = _populationHistory[i];
            if (v > max)
            {
                max = v;
            }
        }
        return max;
    }
}
