namespace ANTS;

public class ForagerRole : AntRole
{
    public static readonly ForagerRole Instance = new ForagerRole();

    public override string RoleName
    {
        get { return "Forager"; }
    }

    private ForagerRole()
    {
        MaxSpeed = 5.5f;
        TurnRate = 6.5f;
        SensorDistance = 4.5f;
        SensorAngleRad = 0.61086524f;
        DepositInterval = 0.15f;
        SensorInterval = 0.20f;
        AutonomyMax = 300f;
        ExplorationRate = 0.015f;
        DensityPenalty = 0.015f;
        ActiveDegradeChance = 0.25f;
        ActiveDegradeFactor = 0.97f;
        VisualScale = 1.0f;
        IsCombatant = false;
        VisionInterval = 0.20f;
        SpawnFoodCost = 1;
        LeashRange = 0f;
    }

    public override float GetVisualAttraction(VisualTargetType type, Ant ant)
    {
        if (type == VisualTargetType.FoodCell && ant.CarryingFood == 0)
        {
            return 2.0f;
        }
        if (type == VisualTargetType.EnemyAnt)
        {
            return -2.0f;
        }
        if (type == VisualTargetType.EnemyNest)
        {
            return -0.8f;
        }
        return 0f;
    }

    public override void UpdateGoal(Ant ant, Colony colony, World world)
    {
        if (ant.CarryingFood > 0 && ant.Goal != GoalType.ReturnHome)
        {
            ant.Goal = GoalType.ReturnHome;
            return;
        }

        if (ant.CarryingFood == 0 && ant.Goal == GoalType.ReturnHome)
        {
            ant.Goal = GoalType.SeekFood;
        }
    }

    public override PheromoneChannel GetFollowChannel(Ant ant)
    {
        if (ant.Goal == GoalType.ReturnHome)
        {
            return PheromoneChannel.HomeTrail;
        }
        return PheromoneChannel.FoodTrail;
    }

    public override PheromoneChannel GetDepositChannel(Ant ant)
    {
        if (ant.Goal == GoalType.ReturnHome)
        {
            return PheromoneChannel.FoodTrail;
        }
        return PheromoneChannel.HomeTrail;
    }

    public override void OnReachedFoodCell(Ant ant, Colony colony, World world)
    {
        if (ant.CarryingFood > 0)
        {
            return;
        }
        int cx = (int)ant.X;
        int cy = (int)ant.Y;
        if (world.TakeFood(cx, cy))
        {
            ant.CarryingFood = 1;
            ant.DistanceFromFoodSource = 0f;
            ant.InternalClock = 0f;
            ant.Heading += (float)Math.PI;
            ant.Goal = GoalType.ReturnHome;
        }
    }

    public override void OnReachedOwnNest(Ant ant, Colony colony, World world)
    {
        if (ant.CarryingFood > 0)
        {
            colony.DepositFood();
            ant.CarryingFood = 0;
            ant.InternalClock = 0f;
            ant.Heading += (float)Math.PI;
            ant.Goal = GoalType.SeekFood;
        }
    }

    public override void OnReachedEnemyNest(Ant ant, Colony colony, World world, Colony enemyColony)
    {
        if (ant.CarryingFood > 0)
        {
            return;
        }
        if (enemyColony.StealFood())
        {
            ant.CarryingFood = 1;
            ant.DistanceFromFoodSource = 0f;
            ant.InternalClock = 0f;
            ant.Heading += (float)Math.PI;
            ant.Goal = GoalType.ReturnHome;
        }
    }

    public override void OnLostTrail(Ant ant, Colony colony, World world)
    {
        if (ant.CarryingFood == 0 && ant.Goal == GoalType.SeekFood)
        {
            ant.Role = ScoutRole.Instance;
            ant.Goal = GoalType.Explore;
        }
    }
}