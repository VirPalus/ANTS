namespace ANTS;

public static class CombatSystem
{

    public static void Tick(Ant ant, Colony colony, World world)
    {
        if (!ant.Role.IsCombatant)
        {
            return;
        }

        QueryState state = new QueryState();
        state.ClosestEnemy = null;
        state.ClosestEnemyColony = null;
        state.ScratchScalar = CombatTuning.AttackRangeSq;
        state.Colonies = world.Colonies;
        state.World = world;
        state.QueryCenterX = ant.X;
        state.QueryCenterY = ant.Y;

        world.SpatialGrid.QueryRadius(ant.X, ant.Y, CombatTuning.AttackRange, colony.Id, CombatFindClosestCallback, ref state);

        if (state.ClosestEnemy == null)
        {
            return;
        }

        ant.EngagementTimer = CombatTuning.EngagementHoldSeconds;
        ant.LastCombatTargetColonyId = state.ClosestEnemyColony!.Id;
        ant.DistanceFromEnemy = 0f;
        Ant closestEnemy = state.ClosestEnemy;
        Colony closestColony = state.ClosestEnemyColony;

        if (ant.AttackCooldown > 0f)
        {
            return;
        }

        DamageTarget(closestEnemy, closestColony, world);
        ant.AttackCooldown = CombatTuning.AttackCooldownSeconds;
        ant.InternalClock = 0f;

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
        ant.LungeTimer = CombatTuning.LungeDuration;
        colony.PheromoneGrid.DepositEnemy(closestColony.Id, (int)ant.X, (int)ant.Y, 1.0f, 0f);
    }

    private static void DamageTarget(Ant target, Colony targetColony, World world)
    {
        target.Health -= CombatTuning.AttackDamage;
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
        if (target.IsDead)
        {
            return;
        }
        if (!state.World!.HasLineOfSight(state.QueryCenterX, state.QueryCenterY, target.X, target.Y))
        {
            return;
        }
        if (distSq < state.ScratchScalar)
        {
            state.ScratchScalar = distSq;
            state.ClosestEnemy = target;
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
