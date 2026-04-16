namespace ANTS;

public static class EnemyDetectionSystem
{
    private const int ScanRadius = 1;
    private const float DetectionDepositIntensity = 1.0f;
    private const float NestHomeRadius = 15f;
    private const float NestHomeRadiusSq = NestHomeRadius * NestHomeRadius;
    private const float OffenseSignalAmount = 0.3f;

    public static void Tick(Ant ant, Colony colony, World world)
    {
        if (ant.Role.IsCombatant)
        {
            return;
        }

        int centerX = (int)ant.X;
        int centerY = (int)ant.Y;

        for (int dy = -ScanRadius; dy <= ScanRadius; dy++)
        {
            for (int dx = -ScanRadius; dx <= ScanRadius; dx++)
            {
                if (dx == 0 && dy == 0)
                {
                    continue;
                }
                int nx = centerX + dx;
                int ny = centerY + dy;
                int owner = world.GetNestOwner(nx, ny);
                if (owner != 0 && owner != colony.Id)
                {
                    colony.PheromoneGrid.DepositEnemy(owner, centerX, centerY, DetectionDepositIntensity);
                    colony.RegisterOffenseSignal(OffenseSignalAmount);
                    RegisterThreatIfNearOwnNest(ant, colony);
                    return;
                }
            }
        }

        if (ant.DetectedEnemyColonyId != 0)
        {
            colony.PheromoneGrid.DepositEnemy(ant.DetectedEnemyColonyId, centerX, centerY, DetectionDepositIntensity);
            colony.RegisterOffenseSignal(OffenseSignalAmount);
            RegisterThreatIfNearOwnNest(ant, colony);
            ant.DetectedEnemyColonyId = 0;
        }
    }

    private static void RegisterThreatIfNearOwnNest(Ant ant, Colony colony)
    {
        float dx = ant.X - (colony.NestX + 0.5f);
        float dy = ant.Y - (colony.NestY + 0.5f);
        float distSq = dx * dx + dy * dy;
        if (distSq >= NestHomeRadiusSq)
        {
            return;
        }
        float dist = (float)Math.Sqrt(distSq);
        float severity = (NestHomeRadius - dist) / NestHomeRadius;
        colony.RegisterDefenseSignal(severity);
    }
}
