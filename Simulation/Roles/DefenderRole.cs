namespace ANTS;

public class DefenderRole : AntRole
{
    public static readonly DefenderRole Instance = new DefenderRole();

    public override string RoleName
    {
        get { return "Defender"; }
    }

    private DefenderRole()
    {
        MaxSpeed = 5.0f;
        TurnRate = 6.0f;
        SensorDistance = 6.0f;
        SensorAngleRad = 0.78539816f;
        DepositInterval = 0.25f;
        SensorInterval = 0.25f;
        AutonomyMax = 400f;
        ExplorationRate = 0.12f;

        DensityPenalty = 0.060f;
        ActiveDegradeChance = 0.10f;
        ActiveDegradeFactor = 0.98f;
        VisualScale = 2.0f;
        IsCombatant = true;
        VisionInterval = 0.15f;
        SpawnFoodCost = 2;
        LeashRange = 0f;
    }

    public override float GetEffectiveLeash(Ant ant, Colony colony)
    {
        return colony.ProtectedRadius;
    }

    public override float GetVisualAttraction(VisualTargetType type, Ant ant)
    {
        if (type == VisualTargetType.EnemyAnt)
        {
            return 3.5f;
        }
        if (type == VisualTargetType.EnemyNest)
        {
            return 1.5f;
        }
        return 0f;
    }

    public override void UpdateGoal(Ant ant, Colony colony, World world)
    {
        if (ant.Goal.Type != GoalType.Patrol)
        {
            ant.Goal = new AntGoal(GoalType.Patrol);
        }
    }

    public override PheromoneChannel GetFollowChannel(Ant ant)
    {
        return PheromoneChannel.EnemyTrail;
    }

    public override PheromoneChannel GetDepositChannel(Ant ant)
    {
        return PheromoneChannel.EnemyTrail;
    }

    public override bool ShouldDeposit(Ant ant)
    {
        return ant.EngagementTimer > 0f;
    }

    public override void OnReachedFoodCell(Ant ant, Colony colony, World world)
    {
    }

    public override void OnReachedOwnNest(Ant ant, Colony colony, World world)
    {
        ant.InternalClock = 0f;
    }

    public override void OnReachedEnemyNest(Ant ant, Colony colony, World world, Colony enemyColony)
    {
        ant.InternalClock = 0f;
        ant.Heading += (float)Math.PI;
    }

    public override void OnLostTrail(Ant ant, Colony colony, World world)
    {
    }
}
