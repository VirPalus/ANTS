namespace ANTS;

public static class MovementSystem
{
    private const float BounceJitter = 0.9f;
    private const float StridePhasePerCell = 4f;

    public static void Move(Ant ant, Colony colony, World world, float dt)
    {
        if (ant.EngagementTimer > 0f)
        {
            return;
        }

        float speed = ant.Role.MaxSpeed * dt;
        float cos = (float)Math.Cos(ant.Heading);
        float sin = (float)Math.Sin(ant.Heading);
        float newX = ant.X + cos * speed;
        float newY = ant.Y + sin * speed;

        int oldCX = (int)ant.X;
        int oldCY = (int)ant.Y;
        int newCX = (int)newX;
        int newCY = (int)newY;

        if (newX < 0f || newX >= world.Width || newY < 0f || newY >= world.Height)
        {
            BounceOff(ant, world);
            return;
        }

        // Hard-block walls: ants cannot occupy a wall cell. Only bother
        // checking when we'd actually cross into a new cell — within the
        // same cell we already know it isn't a wall (we're standing on it).
        if ((newCX != oldCX || newCY != oldCY) && world.IsWall(newCX, newCY))
        {
            BounceOff(ant, world);
            return;
        }

        if (world.IsEnemyNest(newCX, newCY, colony.Id))
        {
            Colony? enemy = world.GetColonyById(world.GetNestOwner(newCX, newCY));
            bool canEnterToSteal = ant.CarryingFood == 0 && enemy != null && enemy.NestFood > 0;
            if (!canEnterToSteal)
            {
                BounceOff(ant, world);
                return;
            }
        }

        ant.X = newX;
        ant.Y = newY;
        ant.StridePhase += speed * StridePhasePerCell;

        if (newCX != oldCX || newCY != oldCY)
        {
            world.DecrementDensity(oldCX, oldCY);
            world.IncrementDensity(newCX, newCY);
        }
    }

    private static void BounceOff(Ant ant, World world)
    {
        float r = world.NextRandomFloat();
        ant.Heading += (float)Math.PI + (r - 0.5f) * BounceJitter;
        ant.CachedSteerAngle = ant.Heading;
    }
}
