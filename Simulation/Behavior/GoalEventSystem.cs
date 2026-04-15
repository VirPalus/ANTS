namespace ANTS;

public static class GoalEventSystem
{
    private const float NestDetectRadius = World.NestRadius + 1f;
    private const float NestDetectRadiusSq = NestDetectRadius * NestDetectRadius;

    public static void Check(Ant ant, Colony colony, World world)
    {
        if (IsNearOwnNest(ant, colony))
        {
            ant.Role.OnReachedOwnNest(ant, colony, world);
            return;
        }

        int cellX = (int)ant.X;
        int cellY = (int)ant.Y;

        int nestOwner = world.GetNestOwner(cellX, cellY);
        if (nestOwner != 0 && nestOwner != colony.Id)
        {
            Colony? enemy = world.GetColonyById(nestOwner);
            if (enemy != null)
            {
                ant.Role.OnReachedEnemyNest(ant, colony, world, enemy);
                return;
            }
        }

        if (world.GetCell(cellX, cellY) == CellType.Food)
        {
            ant.Role.OnReachedFoodCell(ant, colony, world);
        }
    }

    private static bool IsNearOwnNest(Ant ant, Colony colony)
    {
        float dx = ant.X - (colony.NestX + 0.5f);
        float dy = ant.Y - (colony.NestY + 0.5f);
        float distSq = dx * dx + dy * dy;
        return distSq < NestDetectRadiusSq;
    }
}
