namespace ANTS;

public static class SensorSystem
{
    private const int StochasticSampleCount = 12;
    private const float StochasticAngleRange = 1.2f;
    private const float StochasticMinDistance = 2.0f;
    private const float StochasticMaxDistance = 10.0f; // was 25 — too far, caused ants to chase distant signals
    private const float ForwardBias = 1.35f;
    private const float TurnMargin = 1.15f;
    private const float WallPenalty = 0.5f;
    private const float WanderNoiseRange = 0.8f;
    private const int SampleRadius = 1; // 3x3 area sampling for reliable trail detection
    private const float LostHomingWeight = 0.15f; // gentle nest nudge ONLY when trail is completely lost
    private const float WallRepulsionStrength = 0.4f; // how strongly nearby walls push the ant away
    // HomingBias REMOVED — every reference implementation relies purely
    // on pheromone trails to guide returning ants home. A nest-direction
    // bias overrode the sensor and pushed ants through walls.
    // DangerTrail REMOVED — it was a workaround for HomingBias pushing
    // ants into walls. Without HomingBias, ants follow pheromone trails
    // and naturally avoid walls via -WallPenalty.
    // wallFactor REMOVED — it was zeroing out pheromone values near walls,
    // making trails along walls invisible. Wall cells already return
    // -WallPenalty, which is sufficient to steer ants away.

    public static void Sense(Ant ant, Colony colony, World world)
    {
        AntRole role = ant.Role;
        PheromoneChannel followChannel = role.GetFollowChannel(ant);

        // Returning ants carrying food should never randomly explore —
        // they need to follow their HomeTrail back reliably.
        bool suppressExploration = ant.CarryingFood > 0 && ant.Goal.Type == GoalType.ReturnHome;
        if (!suppressExploration)
        {
            float explorationRoll = world.NextRandomFloat();
            if (explorationRoll < role.ExplorationRate)
            {
                float randomTurn = (world.NextRandomFloat() - 0.5f) * (float)Math.PI;
                ant.CachedSteerAngle = ApplyWallRepulsion(ant, world, ant.Heading + randomTurn);
                return;
            }
        }

        // Get the pheromone at the ant's CURRENT position. By subtracting
        // this baseline from sampled values, we turn absolute pheromone
        // into a GRADIENT. Positive = going uphill (toward the trail's
        // source), negative = going downhill (wrong direction on the
        // trail). This prevents ants from following trails backwards —
        // HomeTrail increases toward nest, FoodTrail increases toward food.
        float baseline = colony.PheromoneGrid.Get(followChannel, (int)ant.X, (int)ant.Y);

        float centerAngle = ant.Heading;
        float leftAngle = ant.Heading - role.SensorAngleRad;
        float rightAngle = ant.Heading + role.SensorAngleRad;

        float centerValue = SampleCell(ant, world, colony, followChannel, centerAngle, role.SensorDistance, role.DensityPenalty, baseline);
        float leftValue = SampleCell(ant, world, colony, followChannel, leftAngle, role.SensorDistance, role.DensityPenalty, baseline);
        float rightValue = SampleCell(ant, world, colony, followChannel, rightAngle, role.SensorDistance, role.DensityPenalty, baseline);

        float stochasticBestValue;
        float stochasticBestAngle;
        RunStochasticSweep(ant, world, colony, followChannel, role, baseline, out stochasticBestValue, out stochasticBestAngle);

        float fixedBestValue;
        float fixedBestAngle;
        PickFixedBest(centerValue, leftValue, rightValue, centerAngle, leftAngle, rightAngle, out fixedBestValue, out fixedBestAngle);

        float chosenValue = fixedBestValue;
        float chosenAngle = fixedBestAngle;
        if (stochasticBestValue > fixedBestValue * TurnMargin)
        {
            chosenValue = stochasticBestValue;
            chosenAngle = stochasticBestAngle;
        }

        if (chosenValue > role.GradientThreshold)
        {
            ant.CachedSteerAngle = ApplyWallRepulsion(ant, world, chosenAngle);
            return;
        }

        float wanderNoise = (world.NextRandomFloat() - 0.5f) * WanderNoiseRange;
        float fallbackAngle = ant.Heading + wanderNoise;

        // Weak nest nudge ONLY when truly lost (no trail detected at all)
        // and carrying food. This is NOT a constant bias — it only fires
        // when the ant has zero pheromone guidance. Without this, lost ants
        // wander forever and starve. With trail present, this never runs.
        if (ant.CarryingFood > 0 && ant.Goal.Type == GoalType.ReturnHome)
        {
            float dxNest = (colony.NestX + 0.5f) - ant.X;
            float dyNest = (colony.NestY + 0.5f) - ant.Y;
            float nestAngle = (float)Math.Atan2(dyNest, dxNest);

            float fc = (float)Math.Cos(fallbackAngle);
            float fs = (float)Math.Sin(fallbackAngle);
            float nc = (float)Math.Cos(nestAngle);
            float ns = (float)Math.Sin(nestAngle);
            float bc = fc * (1f - LostHomingWeight) + nc * LostHomingWeight;
            float bs = fs * (1f - LostHomingWeight) + ns * LostHomingWeight;
            fallbackAngle = (float)Math.Atan2(bs, bc);
        }

        ant.CachedSteerAngle = ApplyWallRepulsion(ant, world, fallbackAngle);
        ant.Role.OnLostTrail(ant, colony, world);
    }

    /// <summary>
    /// Scans the 3x3 neighborhood around the ant's cell for walls.
    /// For each wall found, computes a repulsion vector pointing AWAY
    /// from that wall. The sum is blended into the steer angle so the
    /// ant gently steers away before hitting the wall.
    /// </summary>
    private static float ApplyWallRepulsion(Ant ant, World world, float steerAngle)
    {
        int cx = (int)ant.X;
        int cy = (int)ant.Y;

        float repelX = 0f;
        float repelY = 0f;

        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = cx + dx;
                int ny = cy + dy;
                if (nx < 0 || nx >= world.Width || ny < 0 || ny >= world.Height ||
                    world.IsWall(nx, ny))
                {
                    // Wall or edge found — push AWAY from it.
                    // Diagonal walls push less (1/√2) naturally via dx/dy.
                    repelX -= dx;
                    repelY -= dy;
                }
            }
        }

        // No nearby walls → return angle unchanged.
        if (repelX == 0f && repelY == 0f)
        {
            return steerAngle;
        }

        float repelAngle = (float)Math.Atan2(repelY, repelX);

        float sc = (float)Math.Cos(steerAngle);
        float ss = (float)Math.Sin(steerAngle);
        float rc = (float)Math.Cos(repelAngle);
        float rs = (float)Math.Sin(repelAngle);
        float bc = sc * (1f - WallRepulsionStrength) + rc * WallRepulsionStrength;
        float bs = ss * (1f - WallRepulsionStrength) + rs * WallRepulsionStrength;
        return (float)Math.Atan2(bs, bc);
    }

    private static void PickFixedBest(float center, float left, float right, float centerAngle, float leftAngle, float rightAngle, out float bestValue, out float bestAngle)
    {
        float biasedCenter = center * ForwardBias;
        if (biasedCenter >= left * TurnMargin && biasedCenter >= right * TurnMargin)
        {
            bestValue = center;
            bestAngle = centerAngle;
            return;
        }

        if (left > right)
        {
            bestValue = left;
            bestAngle = leftAngle;
            return;
        }

        bestValue = right;
        bestAngle = rightAngle;
    }

    private static float SampleCell(Ant ant, World world, Colony colony, PheromoneChannel channel, float angle, float distance, float densityPenalty, float baseline)
    {
        float cos = (float)Math.Cos(angle);
        float sin = (float)Math.Sin(angle);
        float sampleX = ant.X + cos * distance;
        float sampleY = ant.Y + sin * distance;

        int cx = (int)sampleX;
        int cy = (int)sampleY;

        if (cx < 0 || cx >= world.Width || cy < 0 || cy >= world.Height)
        {
            return -WallPenalty;
        }

        if (world.IsWall(cx, cy))
        {
            return -WallPenalty;
        }

        if (world.IsEnemyNest(cx, cy, colony.Id))
        {
            return -WallPenalty;
        }

        // Sample a 3x3 area around the target point and take the max
        // pheromone value. Single-cell sampling missed trails that were
        // just 1 cell off from the sample point. Area sampling makes
        // trail detection far more reliable.
        float best = 0f;
        for (int dy = -SampleRadius; dy <= SampleRadius; dy++)
        {
            for (int dx = -SampleRadius; dx <= SampleRadius; dx++)
            {
                int sx = cx + dx;
                int sy = cy + dy;
                if (sx < 0 || sx >= world.Width || sy < 0 || sy >= world.Height)
                {
                    continue;
                }
                if (world.IsWall(sx, sy))
                {
                    continue;
                }
                float v = colony.PheromoneGrid.Get(channel, sx, sy);
                if (v > best)
                {
                    best = v;
                }
            }
        }

        int density = world.GetAntDensity(cx, cy);
        best -= density * densityPenalty;

        // GRADIENT: subtract the pheromone at the ant's current position.
        // This turns the value into a gradient:
        //   positive = pheromone is STRONGER ahead (going uphill → correct)
        //   negative = pheromone is WEAKER ahead (going downhill → wrong way)
        // HomeTrail is strongest near nest → uphill = toward nest.
        // FoodTrail is strongest near food → uphill = toward food.
        // This prevents ants from following a trail in the wrong direction.
        best -= baseline;

        return best;
    }

    private static void RunStochasticSweep(Ant ant, World world, Colony colony, PheromoneChannel channel, AntRole role, float baseline, out float bestValue, out float bestAngle)
    {
        bestValue = float.MinValue;
        bestAngle = ant.Heading;

        for (int i = 0; i < StochasticSampleCount; i++)
        {
            float offset = (world.NextRandomFloat() - 0.5f) * 2f * StochasticAngleRange;
            float dist = StochasticMinDistance + world.NextRandomFloat() * (StochasticMaxDistance - StochasticMinDistance);
            float angle = ant.Heading + offset;
            float value = SampleCell(ant, world, colony, channel, angle, dist, role.DensityPenalty, baseline);

            if (value > bestValue)
            {
                bestValue = value;
                bestAngle = angle;
            }
        }
    }
}
