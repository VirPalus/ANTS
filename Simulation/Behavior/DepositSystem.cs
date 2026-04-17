namespace ANTS;

public static class DepositSystem
{
    private const float BaseIntensity = 1.0f;
    private const float DecayCoef = 0.05f;

    public static void Drop(Ant ant, Colony colony, World world)
    {
        int cellX = (int)ant.X;
        int cellY = (int)ant.Y;

        if (!ant.Role.ShouldDeposit(ant))
        {
            return;
        }

        if (world.IsWall(cellX, cellY))
        {
            return;
        }

        PheromoneChannel channel = ant.Role.GetDepositChannel(ant);

        float intensity = BaseIntensity * (float)Math.Exp(-DecayCoef * ant.InternalClock);
        if (intensity <= 0.001f)
        {
            return;
        }

        if (channel == PheromoneChannel.EnemyTrail)
        {
            int targetId = ant.LastCombatTargetColonyId;
            if (targetId != 0)
            {
                colony.PheromoneGrid.DepositEnemy(targetId, cellX, cellY, intensity, ant.DistanceFromEnemy);
            }
        }
        else
        {
            float goalDistance;
            if (channel == PheromoneChannel.HomeTrail)
            {
                float dx = ant.X - (colony.NestX + 0.5f);
                float dy = ant.Y - (colony.NestY + 0.5f);
                goalDistance = (float)Math.Sqrt(dx * dx + dy * dy);
            }
            else
            {
                goalDistance = ant.DistanceFromFoodSource;
            }
            colony.PheromoneGrid.Deposit(channel, cellX, cellY, intensity, goalDistance);
        }

        float r = world.NextRandomFloat();
        if (r < ant.Role.ActiveDegradeChance)
        {
            PheromoneChannel followChannel = ant.Role.GetFollowChannel(ant);
            colony.PheromoneGrid.DegradeInPlace(followChannel, cellX, cellY, ant.Role.ActiveDegradeFactor);
        }
    }
}
