namespace ANTS;

public static class SpawnSystem
{
    private const float BaseSpawnInterval = 1.0f;
    private const float MinSpawnInterval = 0.25f;
    private const float MaxSpawnInterval = 5.0f;
    private const float IdealFoodPerAnt = 3.0f;
    private const float IntervalScaleFloor = 0.5f;

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
        float scale = IntervalScaleFloor + IntervalScaleFloor * ratio;
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
