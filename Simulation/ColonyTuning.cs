namespace ANTS;

internal static class ColonyTuning
{
    internal const int StartingFood = 50;
    internal const float DefenseDecayPerSecond = 0.08f;
    internal const float CombatDeathDefenseWeight = 0.35f;
    internal const float CombatDeathOffenseWeight = 0.20f;
    internal const float EnemyContactDefenseWeight = 0.15f;
    internal const float DefenseScaleNormalizer = 3.0f;
    internal const float FoundingPeriodSeconds = 60f;
    internal const float OffenseDecayPerSecond = 0.02f;
    internal const float OffenseScaleNormalizer = 5.0f;
    internal const float CombatDeathDefenseRadius = 20f;
    internal const float CombatDeathDefenseRadiusSq = CombatDeathDefenseRadius * CombatDeathDefenseRadius;
    internal const float ProtectedRadiusBuffer = 4f;
    internal const float ProtectedRadiusMin = 8f;
    internal const float ProtectedRadiusMax = 80f;
    internal const float ProtectedRadiusSmoothing = 0.08f;

    internal const float NestMaxHealth = 100f;
    internal const float NestDamagePerEnemyPerSec = 2.0f;
    internal const float NestRegenPerSec = 2.0f;
    internal const float NestRegenDelaySeconds = 5f;
    internal const float NestAttackRadius = 4f;
    internal const float NestAttackRadiusSq = NestAttackRadius * NestAttackRadius;
    internal const int MaxEnemyCountForDanger = 25;
}
