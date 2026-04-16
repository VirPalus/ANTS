namespace ANTS;

public class Ant
{
    public const int DefaultHealth = 3;

    public float X;
    public float Y;
    public float Heading;
    public float StridePhase;
    public float Age;
    public float InternalClock;
    public float CachedSteerAngle;
    public float SensorCooldown;
    public float DepositCooldown;
    public float AttackCooldown;
    public float EngagementTimer;
    public float VisionCooldown;
    public float VisionSteerAngle;
    public float VisionStrength;
    public int CarryingFood;
    public int Health;
    public bool IsDead;
    // 0 = no enemy detected this scan. Otherwise the Colony.Id whose ant/nest
    // was seen. Carries the target id from VisionSystem through to
    // EnemyDetectionSystem's per-target EnemyTrail deposit.
    public int DetectedEnemyColonyId;
    // 0 = not engaged. Otherwise the Colony.Id of the enemy most recently
    // attacked or reached. Used by DepositSystem to reinforce the correct
    // per-target EnemyTrail layer while EngagementTimer is running.
    public int LastCombatTargetColonyId;

    public AntRole Role;
    public AntGoal Goal;

    public Ant(float x, float y, float heading, AntRole role)
    {
        X = x;
        Y = y;
        Heading = heading;
        StridePhase = 0f;
        Age = 0f;
        InternalClock = 0f;
        CachedSteerAngle = heading;
        SensorCooldown = 0f;
        DepositCooldown = 0f;
        AttackCooldown = 0f;
        EngagementTimer = 0f;
        VisionCooldown = 0f;
        VisionSteerAngle = heading;
        VisionStrength = 0f;
        CarryingFood = 0;
        Health = DefaultHealth;
        IsDead = false;
        DetectedEnemyColonyId = 0;
        LastCombatTargetColonyId = 0;
        Role = role;
        Goal = new AntGoal(GoalType.Explore);
    }
}
