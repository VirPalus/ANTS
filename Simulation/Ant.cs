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
    public int DetectedEnemyColonyId;
    public int LastCombatTargetColonyId;

    public float DistanceFromFoodSource;
    public float DistanceFromEnemy;

    public float LungeTimer;
    public float LungeDirX;
    public float LungeDirY;

    public AntRole Role;
    public GoalType Goal;

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
        DistanceFromFoodSource = 0f;
        DistanceFromEnemy = 0f;
        LungeTimer = 0f;
        LungeDirX = 0f;
        LungeDirY = 0f;
        Role = role;
        Goal = GoalType.Explore;
    }
}
