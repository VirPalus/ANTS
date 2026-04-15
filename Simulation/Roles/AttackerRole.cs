namespace ANTS;

public class AttackerRole : AntRole
{
    public static readonly AttackerRole Instance = new AttackerRole();

    public override string RoleName
    {
        get { return "Attacker"; }
    }

    private AttackerRole()
    {
        MaxSpeed = 5.5f;
        TurnRate = 6.5f;
        SensorDistance = 4.0f;
        SensorAngleRad = 0.78539816f;
        DepositInterval = 0.2f;
        SensorInterval = 0.20f;
        AutonomyMax = 350f;
        ExplorationRate = 0.015f;
        GradientThreshold = 0.003f;
        DensityPenalty = 0.030f;
        ActiveDegradeChance = 0.15f;
        ActiveDegradeFactor = 0.98f;
        VisualScale = 2.0f;
        IsCombatant = true;
        VisionRange = 14.0f;
        VisionInterval = 0.15f;
        SpawnFoodCost = 3;
        LeashRange = 0f;
    }

    public override float GetVisualAttraction(VisualTargetType type, Ant ant)
    {
        if (type == VisualTargetType.EnemyAnt)
        {
            return 4.0f;
        }
        if (type == VisualTargetType.EnemyNest)
        {
            return 3.5f;
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
    }

    public override void OnLostTrail(Ant ant, Colony colony, World world)
    {
    }
}
