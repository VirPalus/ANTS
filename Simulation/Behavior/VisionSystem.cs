namespace ANTS;
using System.Collections.Generic;

public static class VisionSystem
{
    private const float StrengthNormalizer = 4.0f;

    public static void Scan(Ant ant, Colony colony, World world)
    {
        AntRole role = ant.Role;
        if (role.VisionRange <= 0f)
        {
            ant.VisionStrength = 0f;
            return;
        }

        float range = role.VisionRange;
        float rangeSq = range * range;

        float sumX = 0f;
        float sumY = 0f;

        float foodWeight = role.GetVisualAttraction(VisualTargetType.FoodCell, ant);
        if (foodWeight != 0f)
        {
            AccumulateFood(ant, world, range, rangeSq, foodWeight, ref sumX, ref sumY);
        }

        float enemyAntWeight = role.GetVisualAttraction(VisualTargetType.EnemyAnt, ant);
        float enemyNestWeight = role.GetVisualAttraction(VisualTargetType.EnemyNest, ant);
        if (enemyAntWeight != 0f || enemyNestWeight != 0f)
        {
            bool sawEnemy = AccumulateEnemies(ant, colony, world, range, rangeSq, enemyAntWeight, enemyNestWeight, ref sumX, ref sumY);
            if (sawEnemy)
            {
                ant.DetectedEnemy = true;
            }
        }

        float magnitude = (float)Math.Sqrt(sumX * sumX + sumY * sumY);
        if (magnitude < 0.0001f)
        {
            ant.VisionStrength = 0f;
            return;
        }

        ant.VisionSteerAngle = (float)Math.Atan2(sumY, sumX);
        float strength = magnitude / StrengthNormalizer;
        if (strength > 1f)
        {
            strength = 1f;
        }
        ant.VisionStrength = strength;
    }

    private static void AccumulateFood(Ant ant, World world, float range, float rangeSq, float weight, ref float sumX, ref float sumY)
    {
        Point[] foodCells = world.FoodCells;
        int foodCount = world.FoodCount;
        for (int i = 0; i < foodCount; i++)
        {
            Point fc = foodCells[i];
            float cellCenterX = fc.X + 0.5f;
            float cellCenterY = fc.Y + 0.5f;
            float dx = cellCenterX - ant.X;
            float dy = cellCenterY - ant.Y;
            float distSq = dx * dx + dy * dy;
            if (distSq > rangeSq)
            {
                continue;
            }
            float dist = (float)Math.Sqrt(distSq);
            if (dist < 0.001f)
            {
                continue;
            }
            float falloff = weight / (1f + dist);
            sumX += (dx / dist) * falloff;
            sumY += (dy / dist) * falloff;
        }
    }

    private static bool AccumulateEnemies(Ant ant, Colony colony, World world, float range, float rangeSq, float antWeight, float nestWeight, ref float sumX, ref float sumY)
    {
        bool sawAny = false;
        IReadOnlyList<Colony> colonies = world.Colonies;
        int colonyCount = colonies.Count;
        for (int c = 0; c < colonyCount; c++)
        {
            Colony other = colonies[c];
            if (other.Id == colony.Id)
            {
                continue;
            }

            if (nestWeight != 0f)
            {
                float nestCenterX = other.NestX + 0.5f;
                float nestCenterY = other.NestY + 0.5f;
                float ndx = nestCenterX - ant.X;
                float ndy = nestCenterY - ant.Y;
                float nDistSq = ndx * ndx + ndy * ndy;
                if (nDistSq <= rangeSq)
                {
                    float nDist = (float)Math.Sqrt(nDistSq);
                    if (nDist > 0.001f)
                    {
                        float falloff = nestWeight / (1f + nDist);
                        sumX += (ndx / nDist) * falloff;
                        sumY += (ndy / nDist) * falloff;
                        sawAny = true;
                    }
                }
            }

            if (antWeight != 0f)
            {
                IReadOnlyList<Ant> enemies = other.Ants;
                int enemyCount = enemies.Count;
                for (int e = 0; e < enemyCount; e++)
                {
                    Ant target = enemies[e];
                    if (target.IsDead)
                    {
                        continue;
                    }
                    float dx = target.X - ant.X;
                    float dy = target.Y - ant.Y;
                    float distSq = dx * dx + dy * dy;
                    if (distSq > rangeSq)
                    {
                        continue;
                    }
                    float dist = (float)Math.Sqrt(distSq);
                    if (dist < 0.001f)
                    {
                        continue;
                    }
                    float falloff = antWeight / (1f + dist);
                    sumX += (dx / dist) * falloff;
                    sumY += (dy / dist) * falloff;
                    sawAny = true;
                }
            }
        }
        return sawAny;
    }
}
