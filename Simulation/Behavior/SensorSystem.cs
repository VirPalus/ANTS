namespace ANTS;

public static class SensorSystem
{
    private const int StochasticSampleCount = 12;
    private const float StochasticAngleRange = 1.2f;
    private const float StochasticMinDistance = 2.0f;
    private const float StochasticMaxDistance = 10.0f;
    private const float ForwardBias = 1.35f;
    private const float TurnMargin = 1.15f;
    private const float WanderNoiseRange = 0.8f;
    private const int SampleRadius = 1;
    private const float LostHomingWeight = 0.15f;

    public static void Sense(Ant ant, Colony colony, World world)
    {
        AntRole role = ant.Role;
        PheromoneChannel followChannel = role.GetFollowChannel(ant);

        bool suppressExploration = ant.CarryingFood > 0 && ant.Goal.Type == GoalType.ReturnHome;
        if (!suppressExploration)
        {
            float explorationRoll = world.NextRandomFloat();
            if (explorationRoll < role.ExplorationRate)
            {
                float randomTurn = (world.NextRandomFloat() - 0.5f) * (float)Math.PI;
                ant.CachedSteerAngle = ant.Heading + randomTurn;
                return;
            }
        }

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
            ant.CachedSteerAngle = chosenAngle;
            return;
        }

        float wanderNoise = (world.NextRandomFloat() - 0.5f) * WanderNoiseRange;
        float fallbackAngle = ant.Heading + wanderNoise;

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

        ant.CachedSteerAngle = fallbackAngle;
        ant.Role.OnLostTrail(ant, colony, world);
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
            return 0f;
        }

        if (world.IsWall(cx, cy))
        {
            return 0f;
        }

        if (world.IsEnemyNest(cx, cy, colony.Id))
        {
            return 0f;
        }

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
