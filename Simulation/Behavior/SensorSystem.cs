namespace ANTS;

public static class SensorSystem
{
    private const int StochasticSampleCount = 12;
    private const float StochasticAngleRange = 1.2f;
    private const float StochasticMinDistance = 2.0f;
    private const float StochasticMaxDistance = 25.0f;
    private const float ForwardBias = 1.35f;
    private const float TurnMargin = 1.15f;
    private const float WallPenalty = 0.5f;
    private const float WanderNoiseRange = 0.8f;
    private const float HomingBiasWeight = 0.45f;
    private const float WallDistanceCap = 8f;
    private const float WallDistanceCapSq = WallDistanceCap * WallDistanceCap;

    public static void Sense(Ant ant, Colony colony, World world)
    {
        AntRole role = ant.Role;
        PheromoneChannel followChannel = role.GetFollowChannel(ant);

        float explorationRoll = world.NextRandomFloat();
        if (explorationRoll < role.ExplorationRate)
        {
            float randomTurn = (world.NextRandomFloat() - 0.5f) * (float)Math.PI;
            ant.CachedSteerAngle = ant.Heading + randomTurn;
            return;
        }

        float centerAngle = ant.Heading;
        float leftAngle = ant.Heading - role.SensorAngleRad;
        float rightAngle = ant.Heading + role.SensorAngleRad;

        float centerValue = SampleCell(ant, world, colony, followChannel, centerAngle, role.SensorDistance, role.DensityPenalty);
        float leftValue = SampleCell(ant, world, colony, followChannel, leftAngle, role.SensorDistance, role.DensityPenalty);
        float rightValue = SampleCell(ant, world, colony, followChannel, rightAngle, role.SensorDistance, role.DensityPenalty);

        float stochasticBestValue;
        float stochasticBestAngle;
        RunStochasticSweep(ant, world, colony, followChannel, role, out stochasticBestValue, out stochasticBestAngle);

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
            ant.CachedSteerAngle = ApplyHomingBias(ant, colony, chosenAngle);
            return;
        }

        float wanderNoise = (world.NextRandomFloat() - 0.5f) * WanderNoiseRange;
        float fallbackAngle = ant.Heading + wanderNoise;
        ant.CachedSteerAngle = ApplyHomingBias(ant, colony, fallbackAngle);
        ant.Role.OnLostTrail(ant, colony, world);
    }

    private static float ApplyHomingBias(Ant ant, Colony colony, float steerAngle)
    {
        if (ant.CarryingFood == 0)
        {
            return steerAngle;
        }
        if (ant.Goal.Type != GoalType.ReturnHome)
        {
            return steerAngle;
        }

        float dxNest = (colony.NestX + 0.5f) - ant.X;
        float dyNest = (colony.NestY + 0.5f) - ant.Y;
        float nestAngle = (float)Math.Atan2(dyNest, dxNest);

        float steerCos = (float)Math.Cos(steerAngle);
        float steerSin = (float)Math.Sin(steerAngle);
        float nestCos = (float)Math.Cos(nestAngle);
        float nestSin = (float)Math.Sin(nestAngle);

        float blendedCos = steerCos * (1f - HomingBiasWeight) + nestCos * HomingBiasWeight;
        float blendedSin = steerSin * (1f - HomingBiasWeight) + nestSin * HomingBiasWeight;
        return (float)Math.Atan2(blendedSin, blendedCos);
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

    private static float SampleCell(Ant ant, World world, Colony colony, PheromoneChannel channel, float angle, float distance, float densityPenalty)
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

        if (world.IsEnemyNest(cx, cy, colony.Id))
        {
            return -WallPenalty;
        }

        float value = colony.PheromoneGrid.Get(channel, cx, cy);
        int density = world.GetAntDensity(cx, cy);
        value -= density * densityPenalty;

        int wallDist = world.GetWallDistance(cx, cy);
        float clampedDist = wallDist > WallDistanceCap ? WallDistanceCap : (float)wallDist;
        float wallFactor = (clampedDist * clampedDist) / WallDistanceCapSq;
        value *= wallFactor;
        return value;
    }

    private static void RunStochasticSweep(Ant ant, World world, Colony colony, PheromoneChannel channel, AntRole role, out float bestValue, out float bestAngle)
    {
        bestValue = float.MinValue;
        bestAngle = ant.Heading;

        for (int i = 0; i < StochasticSampleCount; i++)
        {
            float offset = (world.NextRandomFloat() - 0.5f) * 2f * StochasticAngleRange;
            float dist = StochasticMinDistance + world.NextRandomFloat() * (StochasticMaxDistance - StochasticMinDistance);
            float angle = ant.Heading + offset;
            float value = SampleCell(ant, world, colony, channel, angle, dist, role.DensityPenalty);

            if (value > bestValue)
            {
                bestValue = value;
                bestAngle = angle;
            }
        }
    }
}
