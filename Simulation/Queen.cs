namespace ANTS;

public class Queen
{
    public const float BiasMin = 0.7f;
    public const float BiasMax = 1.3f;

    public float AggressionBias { get; }
    public float ExplorationBias { get; }
    public float GrowthBias { get; }
    public float ThreatSensitivity { get; }

    public Queen(Random rng)
    {
        AggressionBias = NextBias(rng);
        ExplorationBias = NextBias(rng);
        GrowthBias = NextBias(rng);
        ThreatSensitivity = NextBias(rng);
    }

    private static float NextBias(Random rng)
    {
        double roll = rng.NextDouble();
        return BiasMin + (float)roll * (BiasMax - BiasMin);
    }
}
