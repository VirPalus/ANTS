namespace ANTS;

public static class AntBehavior
{
    private const float ScoutSpeedCellsPerSecond = 4.8f;
    private const float ScoutSpeed = ScoutSpeedCellsPerSecond / World.SimHz;

    private const float ScoutWanderRadPerSecond = 12f;
    private const float ScoutWander = ScoutWanderRadPerSecond / World.SimHz;

    private const float BounceJitter = 0.8f;

    private const float StridePhasePerCell = 4f;

    public static void Update(Ant ant, Colony colony, World world)
    {
        if (ant.Role == AntRole.Scout)
        {
            UpdateScout(ant, colony, world);
        }
    }

    private static void UpdateScout(Ant ant, Colony colony, World world)
    {
        float wander = (world.NextRandomFloat() - 0.5f) * 2f * ScoutWander;
        ant.Heading += wander;
        TryMoveForward(ant, colony, world, ScoutSpeed);
    }

    private static void TryMoveForward(Ant ant, Colony colony, World world, float speed)
    {
        float newX = ant.X + (float)Math.Cos(ant.Heading) * speed;
        float newY = ant.Y + (float)Math.Sin(ant.Heading) * speed;

        bool hitsVerticalWall = newX < 0f || newX >= world.Width;
        bool hitsHorizontalWall = newY < 0f || newY >= world.Height;

        if (hitsVerticalWall && hitsHorizontalWall)
        {
            ant.Heading += (float)Math.PI;
            return;
        }
        if (hitsVerticalWall)
        {
            ant.Heading = (float)Math.PI - ant.Heading;
            return;
        }
        if (hitsHorizontalWall)
        {
            ant.Heading = -ant.Heading;
            return;
        }

        int oldCellX = (int)ant.X;
        int oldCellY = (int)ant.Y;

        if (world.IsBlocked(newX, newY, oldCellX, oldCellY, colony))
        {
            BounceRandomly(ant, world);
            return;
        }

        int newCellX = (int)newX;
        int newCellY = (int)newY;

        world.UpdateAntOccupancy(oldCellX, oldCellY, newCellX, newCellY);

        ant.X = newX;
        ant.Y = newY;
        ant.StridePhase += speed * StridePhasePerCell;
    }

    private static void BounceRandomly(Ant ant, World world)
    {
        ant.Heading += (float)Math.PI + (world.NextRandomFloat() - 0.5f) * BounceJitter;
    }
}
