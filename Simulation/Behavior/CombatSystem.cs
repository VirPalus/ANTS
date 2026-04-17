namespace ANTS;
using System.Collections.Generic;

public static class CombatSystem
{
    private const float AttackRange = 1.6f;
    private const float AttackRangeSq = AttackRange * AttackRange;
    private const float AttackCooldownSeconds = 0.3f;
    private const float EngagementHoldSeconds = 0.4f;
    private const int AttackDamage = 1;
    public const float LungeDuration = 0.15f;  // total lunge animation time
    public const float LungeDistance = 0.5f;    // max offset in cells

    public static void Tick(Ant ant, Colony colony, World world)
    {
        if (!ant.Role.IsCombatant)
        {
            return;
        }

        // Use spatial hash grid for O(nearby) enemy lookup.
        QueryState state = new QueryState();
        state.ClosestEnemy = null;
        state.ClosestEnemyColony = null;
        state.ClosestCombatDistSq = AttackRangeSq;
        state.Colonies = world.Colonies;
        state.World = world;
        state.QueryCenterX = ant.X;
        state.QueryCenterY = ant.Y;

        world.SpatialGrid.QueryRadius(ant.X, ant.Y, AttackRange, colony.Id, CombatFindClosestCallback, ref state);

        if (state.ClosestEnemy == null)
        {
            return;
        }

        ant.EngagementTimer = EngagementHoldSeconds;
        ant.LastCombatTargetColonyId = state.ClosestEnemyColony!.Id;
        Ant closestEnemy = state.ClosestEnemy;
        Colony closestColony = state.ClosestEnemyColony;

        if (ant.AttackCooldown > 0f)
        {
            return;
        }

        DamageTarget(closestEnemy, closestColony, world);
        ant.AttackCooldown = AttackCooldownSeconds;
        ant.InternalClock = 0f;

        // Start lunge animation toward the target.
        float ldx = closestEnemy.X - ant.X;
        float ldy = closestEnemy.Y - ant.Y;
        float ldist = (float)Math.Sqrt(ldx * ldx + ldy * ldy);
        if (ldist > 0.001f)
        {
            ant.LungeDirX = ldx / ldist;
            ant.LungeDirY = ldy / ldist;
        }
        else
        {
            ant.LungeDirX = (float)Math.Cos(ant.Heading);
            ant.LungeDirY = (float)Math.Sin(ant.Heading);
        }
        ant.LungeTimer = LungeDuration;
        colony.PheromoneGrid.DepositEnemy(closestColony.Id, (int)ant.X, (int)ant.Y, 1.0f);
    }

    private static void DamageTarget(Ant target, Colony targetColony, World world)
    {
        target.Health -= AttackDamage;
        if (target.Health > 0)
        {
            return;
        }

        target.IsDead = true;
        int cx = (int)target.X;
        int cy = (int)target.Y;
        world.DecrementDensity(cx, cy);
        world.DropFoodFromDeadAnt(cx, cy, target.CarryingFood);
        targetColony.RegisterCombatDeath(target.X, target.Y);
    }

    private static readonly SpatialGrid.QueryCallback CombatFindClosestCallback = OnCombatFindClosest;

    private static void OnCombatFindClosest(ref QueryState state, Ant target, int colonyId, float dx, float dy, float distSq)
    {
        // Ant may have died during this tick (grid is built at tick start).
        if (target.IsDead)
        {
            return;
        }
        // Wall occlusion: can't attack through walls.
        if (!state.World!.HasLineOfSight(state.QueryCenterX, state.QueryCenterY, target.X, target.Y))
        {
            return;
        }
        if (distSq < state.ClosestCombatDistSq)
        {
            state.ClosestCombatDistSq = distSq;
            state.ClosestEnemy = target;
            // Resolve Colony from colonyId via the colonies list.
            IReadOnlyList<Colony> colonies = state.Colonies!;
            int count = colonies.Count;
            for (int i = 0; i < count; i++)
            {
                if (colonies[i].Id == colonyId)
                {
                    state.ClosestEnemyColony = colonies[i];
                    break;
                }
            }
        }
    }

    public static void DecrementCooldown(Ant ant, float dt)
    {
        if (ant.AttackCooldown > 0f)
        {
            ant.AttackCooldown -= dt;
            if (ant.AttackCooldown < 0f)
            {
                ant.AttackCooldown = 0f;
            }
        }
        if (ant.LungeTimer > 0f)
        {
            ant.LungeTimer -= dt;
            if (ant.LungeTimer < 0f)
            {
                ant.LungeTimer = 0f;
            }
        }
        if (ant.EngagementTimer > 0f)
        {
            ant.EngagementTimer -= dt;
            if (ant.EngagementTimer <= 0f)
            {
                ant.EngagementTimer = 0f;
                ant.LastCombatTargetColonyId = 0;
            }
        }
    }
}
