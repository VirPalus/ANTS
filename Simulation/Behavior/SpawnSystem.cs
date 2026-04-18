namespace ANTS;

public static class SpawnSystem
{
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

        colony.PheromoneGrid.Deposit(PheromoneChannel.HomeTrail, spawnCellX, spawnCellY, PheromoneGrid.PermanentHomeIntensity, 0f);
    }

    private static float ComputeSpawnInterval(Colony colony)
    {
        int antCount = colony.Ants.Count;
        float foodPerAnt;
        if (antCount == 0)
        {
            foodPerAnt = SpawnTuning.IdealFoodPerAnt;
        }
        else
        {
            foodPerAnt = (float)colony.NestFood / (float)antCount;
        }

        float ratio = foodPerAnt / SpawnTuning.IdealFoodPerAnt;

        float scale = SpawnTuning.IntervalScaleFloor + SpawnTuning.IntervalScaleFloor * ratio;

        if (ratio > 1f)
        {
            float surplus = ratio - 1f;
            scale += SpawnTuning.SurplusScaleGain * (float)Math.Pow(surplus, SpawnTuning.SurplusRatioExponent);
        }

        if (scale <= 0.01f)
        {
            return SpawnTuning.MaxSpawnInterval;
        }

        float interval = SpawnTuning.BaseSpawnInterval / scale;
        if (interval < SpawnTuning.MinSpawnInterval)
        {
            interval = SpawnTuning.MinSpawnInterval;
        }
        if (interval > SpawnTuning.MaxSpawnInterval)
        {
            interval = SpawnTuning.MaxSpawnInterval;
        }
        return interval;
    }
}
