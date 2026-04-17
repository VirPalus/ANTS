namespace ANTS;

public class ScoutRole : AntRole
{
    public static readonly ScoutRole Instance = new ScoutRole();

    private const float FoodTrailDetectThreshold = 0.05f;
    private const int FoodTrailScanRadius = 1;

    public override string RoleName
    {
        get { return "Scout"; }
    }

    private ScoutRole()
    {
        MaxSpeed = 5.5f;
        TurnRate = 7.0f;
        SensorDistance = 3.0f;
        SensorAngleRad = 0.78539816f;
        DepositInterval = 0.15f;
        SensorInterval = 0.20f;
        AutonomyMax = 300f;
        ExplorationRate = 0.03f;

        DensityPenalty = 0.015f;
        ActiveDegradeChance = 0.05f;
        ActiveDegradeFactor = 0.99f;
        VisualScale = 1.0f;
        IsCombatant = false;
        VisionInterval = 0.20f;
        SpawnFoodCost = 1;
        LeashRange = 0f;
    }

    public override float GetVisualAttraction(VisualTargetType type, Ant ant)
    {
        if (type == VisualTargetType.FoodCell)
        {
            return 1.5f;
        }
        if (type == VisualTargetType.EnemyAnt)
        {
            return -1.8f;
        }
        if (type == VisualTargetType.EnemyNest)
        {
            return -0.8f;
        }
        return 0f;
    }

    public override void UpdateGoal(Ant ant, Colony colony, World world)
    {
        if (ant.CarryingFood > 0)
        {
            ant.Role = ForagerRole.Instance;
            ant.Goal = new AntGoal(GoalType.ReturnHome);
            return;
        }

        int centerX = (int)ant.X;
        int centerY = (int)ant.Y;
        PheromoneGrid grid = colony.PheromoneGrid;

        for (int dy = -FoodTrailScanRadius; dy <= FoodTrailScanRadius; dy++)
        {
            for (int dx = -FoodTrailScanRadius; dx <= FoodTrailScanRadius; dx++)
            {
                float v = grid.Get(PheromoneChannel.FoodTrail, centerX + dx, centerY + dy);
                if (v > FoodTrailDetectThreshold)
                {
                    ant.Role = ForagerRole.Instance;
                    ant.Goal = new AntGoal(GoalType.SeekFood);
                    return;
                }
            }
        }
    }

    public override PheromoneChannel GetFollowChannel(Ant ant)
    {
        return PheromoneChannel.FoodTrail;
    }

    public override PheromoneChannel GetDepositChannel(Ant ant)
    {
        return PheromoneChannel.HomeTrail;
    }

    public override void OnReachedFoodCell(Ant ant, Colony colony, World world)
    {
        int cx = (int)ant.X;
        int cy = (int)ant.Y;
        if (world.TakeFood(cx, cy))
        {
            ant.CarryingFood = 1;
            ant.DistanceFromFoodSource = 0f;
            ant.InternalClock = 0f;
            ant.Heading += (float)Math.PI;
            ant.Role = ForagerRole.Instance;
            ant.Goal = new AntGoal(GoalType.ReturnHome);
        }
    }

    public override void OnReachedOwnNest(Ant ant, Colony colony, World world)
    {
        ant.InternalClock = 0f;
    }

    public override void OnReachedEnemyNest(Ant ant, Colony colony, World world, Colony enemyColony)
    {
        if (enemyColony.StealFood())
        {
            ant.CarryingFood = 1;
            ant.DistanceFromFoodSource = 0f;
            ant.InternalClock = 0f;
            ant.Heading += (float)Math.PI;
            ant.Role = ForagerRole.Instance;
            ant.Goal = new AntGoal(GoalType.ReturnHome);
        }
    }

    public override void OnLostTrail(Ant ant, Colony colony, World world)
    {
    }
}
