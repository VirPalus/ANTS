namespace ANTS;
using System.Collections.Generic;

public static class CombatSystem
{
    private const float AttackRange = 1.6f;
    private const float AttackRangeSq = AttackRange * AttackRange;
    private const float AttackCooldownSeconds = 0.3f;
    private const float EngagementHoldSeconds = 0.4f;
    private const int AttackDamage = 1;

    public static void Tick(Ant ant, Colony colony, World world)
    {
        if (!ant.Role.IsCombatant)
        {
            return;
        }

        IReadOnlyList<Colony> colonies = world.Colonies;
        int colonyCount = colonies.Count;

        Ant? closestEnemy = null;
        Colony? closestColony = null;
        float closestDistSq = AttackRangeSq;

        for (int c = 0; c < colonyCount; c++)
        {
            Colony other = colonies[c];
            if (other.Id == colony.Id)
            {
                continue;
            }

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
                if (distSq < closestDistSq)
                {
                    closestDistSq = distSq;
                    closestEnemy = target;
                    closestColony = other;
                }
            }
        }

        if (closestEnemy == null)
        {
            return;
        }

        ant.EngagementTimer = EngagementHoldSeconds;

        if (ant.AttackCooldown > 0f)
        {
            return;
        }

        DamageTarget(closestEnemy, closestColony!, world);
        ant.AttackCooldown = AttackCooldownSeconds;
        ant.InternalClock = 0f;
        colony.PheromoneGrid.Deposit(PheromoneChannel.EnemyTrail, (int)ant.X, (int)ant.Y, 1.0f);
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
        targetColony.RegisterCombatDeath();
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
        if (ant.EngagementTimer > 0f)
        {
            ant.EngagementTimer -= dt;
            if (ant.EngagementTimer < 0f)
            {
                ant.EngagementTimer = 0f;
            }
        }
    }
}
