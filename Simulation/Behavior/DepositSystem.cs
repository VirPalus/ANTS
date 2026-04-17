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
                colony.PheromoneGrid.DepositEnemy(targetId, cellX, cellY, intensity);
            }
        }
        else
        {
            colony.PheromoneGrid.Deposit(channel, cellX, cellY, intensity);
        }

        float r = world.NextRandomFloat();
        if (r < ant.Role.ActiveDegradeChance)
        {
            PheromoneChannel followChannel = ant.Role.GetFollowChannel(ant);
            colony.PheromoneGrid.DegradeInPlace(followChannel, cellX, cellY, ant.Role.ActiveDegradeFactor);
        }
    }
}
