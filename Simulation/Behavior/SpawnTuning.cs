namespace ANTS;

internal static class SpawnTuning
{
    internal const float BaseSpawnInterval = 1.0f;
    internal const float MinSpawnInterval = 0.04f;
    internal const float MaxSpawnInterval = 5.0f;
    internal const float IdealFoodPerAnt = 1.5f;
    internal const float IntervalScaleFloor = 0.3f;
    internal const float SurplusRatioExponent = 1.6f;
    internal const float SurplusScaleGain = 1.5f;
}
