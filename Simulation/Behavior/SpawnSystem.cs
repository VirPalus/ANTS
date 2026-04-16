namespace ANTS;

public static class SpawnSystem
{
    private const float BaseSpawnInterval = 1.0f;
    private const float MinSpawnInterval = 0.04f;
    private const float MaxSpawnInterval = 5.0f;
    private const float IdealFoodPerAnt = 1.5f;
    private const float IntervalScaleFloor = 0.3f;
    private const float SurplusRatioExponent = 1.6f;
    private const float SurplusScaleGain = 1.5f;

    public static void Tick(Colony colony, World world, float dt)
    {
        colony.SpawnTimer -= dt;
        if (colony.SpawnTimer > 0f)
        {
            return;
        }

        float interval = ComputeSpawnInterval(colony);
        colony.SpawnTimer += interval;

        AntRole role = colony.RoleQuota.PickRoleForSpawn(colony);
        if (colony.NestFood < role.SpawnFoodCost)
        {
            role = ScoutRole.Instance;
        }
        if (!colony.ConsumeFoodForSpawn(role.SpawnFoodCost))
        {
            return;
        }

        float spawnX = colony.NestX + 0.5f;
        float spawnY = colony.NestY + 0.5f;
        float heading = world.NextRandomFloat() * (float)Math.PI * 2f;

        Ant ant = new Ant(spawnX, spawnY, heading, role);
        colony.AddAnt(ant);

        int spawnCellX = (int)spawnX;
        int spawnCellY = (int)spawnY;
        world.IncrementDensity(spawnCellX, spawnCellY);

        colony.PheromoneGrid.Deposit(PheromoneChannel.HomeTrail, spawnCellX, spawnCellY, PheromoneGrid.PermanentHomeIntensity);
    }

    private static float ComputeSpawnInterval(Colony colony)
    {
        int antCount = colony.Ants.Count;
        float foodPerAnt;
        if (antCount == 0)
        {
            foodPerAnt = IdealFoodPerAnt;
        }
        else
        {
            foodPerAnt = (float)colony.NestFood / (float)antCount;
        }

        float ratio = foodPerAnt / IdealFoodPerAnt;

        // Base linear scale (keeps well-fed colonies above the floor).
        float scale = IntervalScaleFloor + IntervalScaleFloor * ratio;

        // Surplus boost: when food-per-ant exceeds the ideal, the queen should
        // ramp up spawning aggressively so the food-to-ants feedback loop
        // stays tight (no more 10x surplus while the queen "sleeps").
        if (ratio > 1f)
        {
            float surplus = ratio - 1f;
            scale += SurplusScaleGain * (float)Math.Pow(surplus, SurplusRatioExponent);
        }

        if (scale <= 0.01f)
        {
            return MaxSpawnInterval;
        }

        float interval = BaseSpawnInterval / scale;
        if (interval < MinSpawnInterval)
        {
            interval = MinSpawnInterval;
        }
        if (interval > MaxSpawnInterval)
        {
            interval = MaxSpawnInterval;
        }
        return interval;
    }
}
