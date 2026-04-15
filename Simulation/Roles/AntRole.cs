namespace ANTS;

public abstract class AntRole
{
    public float MaxSpeed { get; protected set; }
    public float TurnRate { get; protected set; }
    public float SensorDistance { get; protected set; }
    public float SensorAngleRad { get; protected set; }
    public float DepositInterval { get; protected set; }
    public float SensorInterval { get; protected set; }
    public float AutonomyMax { get; protected set; }
    public float ExplorationRate { get; protected set; }
    public float GradientThreshold { get; protected set; }
    public float DensityPenalty { get; protected set; }
    public float ActiveDegradeChance { get; protected set; }
    public float ActiveDegradeFactor { get; protected set; }
    public float VisualScale { get; protected set; }
    public bool IsCombatant { get; protected set; }

    public float VisionRange { get; protected set; }
    public float VisionInterval { get; protected set; }
    public int SpawnFoodCost { get; protected set; }
    public float LeashRange { get; protected set; }

    public abstract string RoleName { get; }

    public virtual float GetEffectiveLeash(Ant ant, Colony colony)
    {
        return LeashRange;
    }

    public abstract void UpdateGoal(Ant ant, Colony colony, World world);
    public abstract PheromoneChannel GetFollowChannel(Ant ant);
    public abstract PheromoneChannel GetDepositChannel(Ant ant);
    public abstract void OnReachedFoodCell(Ant ant, Colony colony, World world);
    public abstract void OnReachedOwnNest(Ant ant, Colony colony, World world);
    public abstract void OnReachedEnemyNest(Ant ant, Colony colony, World world, Colony enemyColony);
    public abstract void OnLostTrail(Ant ant, Colony colony, World world);
    public abstract float GetVisualAttraction(VisualTargetType type, Ant ant);
}
