namespace ANTS;

public static class DepositSystem
{
    private const float BaseIntensity = 1.0f;
    private const float DecayCoef = 0.05f;

    public static void Drop(Ant ant, Colony colony, World world)
    {
        int cellX = (int)ant.X;
        int cellY = (int)ant.Y;

        PheromoneChannel channel = ant.Role.GetDepositChannel(ant);

        float intensity = BaseIntensity * (float)Math.Exp(-DecayCoef * ant.InternalClock);
        if (intensity <= 0.001f)
        {
            return;
        }

        colony.PheromoneGrid.Deposit(channel, cellX, cellY, intensity);

        float r = world.NextRandomFloat();
        if (r < ant.Role.ActiveDegradeChance)
        {
            PheromoneChannel followChannel = ant.Role.GetFollowChannel(ant);
            colony.PheromoneGrid.DegradeInPlace(followChannel, cellX, cellY, ant.Role.ActiveDegradeFactor);
        }
    }
}
