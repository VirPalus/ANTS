namespace ANTS;

public static class AntBehavior
{
    private const float VisionHardOverride = 0.7f;

    public static void Update(Ant ant, Colony colony, World world, float dt)
    {
        if (ant.IsDead)
        {
            return;
        }

        ant.Age += dt;
        ant.InternalClock += dt;
        ant.SensorCooldown -= dt;
        ant.DepositCooldown -= dt;
        ant.VisionCooldown -= dt;
        CombatSystem.DecrementCooldown(ant, dt);

        ant.Role.UpdateGoal(ant, colony, world);

        if (ant.VisionCooldown <= 0f)
        {
            VisionSystem.Scan(ant, colony, world);
            ant.VisionCooldown = ant.Role.VisionInterval;
        }

        if (ant.SensorCooldown <= 0f)
        {
            SensorSystem.Sense(ant, colony, world);
            ant.SensorCooldown = ant.Role.SensorInterval;
        }

        ApplyVisionBlend(ant);
        ApplyLeash(ant, colony);

        SteeringSystem.UpdateHeading(ant, dt);
        MovementSystem.Move(ant, colony, world, dt);

        if (ant.DepositCooldown <= 0f)
        {
            DepositSystem.Drop(ant, colony, world);
            EnemyDetectionSystem.Tick(ant, colony, world);
            ant.DepositCooldown = ant.Role.DepositInterval;
        }

        CombatSystem.Tick(ant, colony, world);
        GoalEventSystem.Check(ant, colony, world);
        AutonomySystem.Check(ant, colony, world);
    }

    private static void ApplyLeash(Ant ant, Colony colony)
    {
        float leash = ant.Role.GetEffectiveLeash(ant, colony);
        if (leash <= 0f)
        {
            return;
        }
        if (ant.VisionStrength > 0.3f)
        {
            return;
        }

        float dx = ant.X - (colony.NestX + 0.5f);
        float dy = ant.Y - (colony.NestY + 0.5f);
        float distSq = dx * dx + dy * dy;
        float leashSq = leash * leash;
        if (distSq <= leashSq)
        {
            return;
        }

        float toNestAngle = (float)Math.Atan2(-dy, -dx);
        ant.CachedSteerAngle = toNestAngle;
    }

    private static void ApplyVisionBlend(Ant ant)
    {
        if (ant.VisionStrength <= 0f)
        {
            return;
        }

        if (ant.VisionStrength >= VisionHardOverride)
        {
            ant.CachedSteerAngle = ant.VisionSteerAngle;
            return;
        }

        float w = ant.VisionStrength;
        float pheroCos = (float)Math.Cos(ant.CachedSteerAngle);
        float pheroSin = (float)Math.Sin(ant.CachedSteerAngle);
        float visionCos = (float)Math.Cos(ant.VisionSteerAngle);
        float visionSin = (float)Math.Sin(ant.VisionSteerAngle);
        float blendedCos = pheroCos * (1f - w) + visionCos * w;
        float blendedSin = pheroSin * (1f - w) + visionSin * w;
        ant.CachedSteerAngle = (float)Math.Atan2(blendedSin, blendedCos);
    }
}
