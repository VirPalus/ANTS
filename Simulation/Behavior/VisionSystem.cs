namespace ANTS;
using System.Collections.Generic;

public static class VisionSystem
{
    private const float StrengthNormalizer = 2.0f;

    public static void Scan(Ant ant, Colony colony, World world)
    {
        AntRole role = ant.Role;
        if (role.VisionRange <= 0f)
        {
            ant.VisionStrength = 0f;
            return;
        }

        float rangeSq = role.VisionRange * role.VisionRange;
        float halfAngle = role.SensorAngleRad;
        float cosHalfAngle = (float)Math.Cos(halfAngle);
        float cosHeading = (float)Math.Cos(ant.Heading);
        float sinHeading = (float)Math.Sin(ant.Heading);

        float sumX = 0f;
        float sumY = 0f;

        float foodWeight = role.GetVisualAttraction(VisualTargetType.FoodCell, ant);
        if (foodWeight != 0f)
        {
            AccumulateFood(ant, world, rangeSq, foodWeight, cosHeading, sinHeading, cosHalfAngle, ref sumX, ref sumY);
        }

        float enemyAntWeight = role.GetVisualAttraction(VisualTargetType.EnemyAnt, ant);
        float enemyNestWeight = role.GetVisualAttraction(VisualTargetType.EnemyNest, ant);
        if (enemyAntWeight != 0f || enemyNestWeight != 0f)
        {
            int closestEnemyColonyId = AccumulateEnemies(ant, colony, world, rangeSq, enemyAntWeight, enemyNestWeight, cosHeading, sinHeading, cosHalfAngle, ref sumX, ref sumY);
            if (closestEnemyColonyId != 0)
            {
                ant.DetectedEnemyColonyId = closestEnemyColonyId;
            }
        }

        AccumulateWalls(ant, world, role.VisionRange, cosHeading, sinHeading, cosHalfAngle, ref sumX, ref sumY);

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

    private static void AccumulateFood(Ant ant, World world, float rangeSq, float weight,
        float cosHeading, float sinHeading, float cosHalfAngle, ref float sumX, ref float sumY)
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
            float ndx = dx / dist;
            float ndy = dy / dist;
            float dot = ndx * cosHeading + ndy * sinHeading;
            if (dot < cosHalfAngle)
            {
                continue;
            }
            float falloff = weight / (1f + dist);
            sumX += ndx * falloff;
            sumY += ndy * falloff;
        }
    }

    /// <summary>
    /// Scans cells within the ant's forward vision cone for walls and map edges.
    /// Each wall cell adds a repulsion vector (push AWAY from wall), with closer
    /// walls pushing harder. This replaces the old 3x3 wall repulsion that was
    /// in SensorSystem — now ants see walls at their full vision range.
    /// </summary>
    private static void AccumulateWalls(Ant ant, World world, float range,
        float cosHeading, float sinHeading, float cosHalfAngle, ref float sumX, ref float sumY)
    {
        int scanRadius = (int)Math.Ceiling(range);
        int cx = (int)ant.X;
        int cy = (int)ant.Y;
        float rangeSq = range * range;

        const float WallRepulsionWeight = 3.0f;

        for (int dy = -scanRadius; dy <= scanRadius; dy++)
        {
            for (int dx = -scanRadius; dx <= scanRadius; dx++)
            {
                if (dx == 0 && dy == 0) continue;

                int wx = cx + dx;
                int wy = cy + dy;

                bool isWall = wx < 0 || wx >= world.Width || wy < 0 || wy >= world.Height;
                if (!isWall)
                {
                    isWall = world.IsWall(wx, wy);
                }
                if (!isWall) continue;

                float cellDx = (wx + 0.5f) - ant.X;
                float cellDy = (wy + 0.5f) - ant.Y;
                float distSq = cellDx * cellDx + cellDy * cellDy;
                if (distSq > rangeSq) continue;

                float dist = (float)Math.Sqrt(distSq);
                if (dist < 0.001f) continue;

                float ndx = cellDx / dist;
                float ndy = cellDy / dist;
                float dot = ndx * cosHeading + ndy * sinHeading;
                if (dot < cosHalfAngle) continue;

                float falloff = WallRepulsionWeight / (1f + dist * dist);
                sumX -= ndx * falloff;
                sumY -= ndy * falloff;
            }
        }
    }

    private static int AccumulateEnemies(Ant ant, Colony colony, World world, float rangeSq, float antWeight, float nestWeight,
        float cosHeading, float sinHeading, float cosHalfAngle, ref float sumX, ref float sumY)
    {
        int closestId = 0;
        float closestDistSq = float.MaxValue;
        float range = (float)Math.Sqrt(rangeSq);

        if (nestWeight != 0f)
        {
            IReadOnlyList<Colony> colonies = world.Colonies;
            int colonyCount = colonies.Count;
            for (int c = 0; c < colonyCount; c++)
            {
                Colony other = colonies[c];
                if (other.Id == colony.Id)
                {
                    continue;
                }

                float nestCenterX = other.NestX + 0.5f;
                float nestCenterY = other.NestY + 0.5f;
                float ndx = nestCenterX - ant.X;
                float ndy = nestCenterY - ant.Y;
                float nDistSq = ndx * ndx + ndy * ndy;
                if (nDistSq <= rangeSq)
                {
                    float nDist = (float)Math.Sqrt(nDistSq);
                    if (nDist > 0.001f && world.HasLineOfSight(ant.X, ant.Y, nestCenterX, nestCenterY))
                    {
                        float nnx = ndx / nDist;
                        float nny = ndy / nDist;
                        float dot = nnx * cosHeading + nny * sinHeading;
                        if (dot < cosHalfAngle) continue;

                        float falloff = nestWeight / (1f + nDist);
                        sumX += nnx * falloff;
                        sumY += nny * falloff;
                        if (nDistSq < closestDistSq)
                        {
                            closestDistSq = nDistSq;
                            closestId = other.Id;
                        }
                    }
                }
            }
        }

        if (antWeight != 0f)
        {
            QueryState state = new QueryState();
            state.SumX = sumX;
            state.SumY = sumY;
            state.ClosestEnemyColonyId = closestId;
            state.ClosestDistSq = closestDistSq;
            state.ClosestCombatDistSq = antWeight;
            state.World = world;
            state.QueryCenterX = ant.X;
            state.QueryCenterY = ant.Y;
            state.CosHeading = cosHeading;
            state.SinHeading = sinHeading;
            state.CosHalfAngle = cosHalfAngle;

            world.SpatialGrid.QueryRadius(ant.X, ant.Y, range, colony.Id, VisionEnemyAntCallback, ref state);

            sumX = state.SumX;
            sumY = state.SumY;
            closestId = state.ClosestEnemyColonyId;
        }

        return closestId;
    }

    private static readonly SpatialGrid.QueryCallback VisionEnemyAntCallback = OnVisionEnemyAnt;

    private static void OnVisionEnemyAnt(ref QueryState state, Ant target, int colonyId, float dx, float dy, float distSq)
    {
        if (target.IsDead) return;

        float dist = (float)Math.Sqrt(distSq);
        if (dist < 0.001f) return;

        float ndx = dx / dist;
        float ndy = dy / dist;
        float dot = ndx * state.CosHeading + ndy * state.SinHeading;
        if (dot < state.CosHalfAngle) return;

        if (!state.World!.HasLineOfSight(state.QueryCenterX, state.QueryCenterY, target.X, target.Y))
        {
            return;
        }

        float weight = state.ClosestCombatDistSq;
        float falloff = weight / (1f + dist);
        state.SumX += ndx * falloff;
        state.SumY += ndy * falloff;
        if (distSq < state.ClosestDistSq)
        {
            state.ClosestDistSq = distSq;
            state.ClosestEnemyColonyId = colonyId;
        }
    }
}
