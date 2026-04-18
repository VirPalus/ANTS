namespace ANTS;

internal static class SensorTuning
{
    internal const float ForwardBias = 1.35f;
    internal const float TurnMargin = 1.15f;
    internal const int SampleRadius = 1;
    internal const float LostHomingWeight = 0.15f;
    internal const float StrengthWeight = 0.35f;
    internal const float DistanceWeight = 0.65f;
}
