namespace ANTS;
using SkiaSharp;

public class Colony
{
    public int Id { get; }
    public int NestX { get; }
    public int NestY { get; }
    public Color Color { get; }
    public SKColor CachedSkColor { get; }

    private List<Ant> _ants;
    public int NestFood;
    public float SpawnTimer;

    public PheromoneGrid PheromoneGrid { get; }
    public RoleQuota RoleQuota { get; }
    public ColonyStats Stats { get; }
    public Queen Queen { get; }

    public int ScoutCount { get; private set; }
    public int ForagerCount { get; private set; }
    public int DefenderCount { get; private set; }
    public int AttackerCount { get; private set; }

    public float Defense { get; private set; }
    public float Offense { get; private set; }
    public float TimeSinceDefenseSignal { get; private set; }
    public float ProtectedRadius { get; private set; }
    public float NestHealth { get; private set; }
    public string DeathReason { get; private set; }
    public bool IsAlive { get; private set; }
    public float DeathTime { get; private set; }
    public bool IsNestDead
    {
        get { return NestHealth <= 0f; }
    }
    public float NestHealthFraction
    {
        get { return NestHealth / ColonyTuning.NestMaxHealth; }
    }
    private float _timeSinceNestAttack;
    public float Age { get; private set; }
    public bool IsFounding
    {
        get { return Age < ColonyTuning.FoundingPeriodSeconds; }
    }
    private float _defenseRaw;
    private float _offenseRaw;

    public IReadOnlyList<Ant> Ants
    {
        get { return _ants; }
    }

    internal List<Ant> AntsList
    {
        get { return _ants; }
    }

    public Colony(int id, int nestX, int nestY, Color color, int worldWidth, int worldHeight, Random rng)
    {
        Id = id;
        NestX = nestX;
        NestY = nestY;
        Color = color;
        CachedSkColor = new SKColor(color.R, color.G, color.B, color.A);
        _ants = new List<Ant>();
        NestFood = ColonyTuning.StartingFood;
        SpawnTimer = 0f;
        PheromoneGrid = new PheromoneGrid(worldWidth, worldHeight);
        Queen = new Queen(rng);
        RoleQuota = new RoleQuota();
        RoleQuota.Register(ScoutRole.Instance);
        RoleQuota.Register(ForagerRole.Instance);
        RoleQuota.Register(DefenderRole.Instance);
        RoleQuota.Register(AttackerRole.Instance);
        Stats = new ColonyStats();
        _defenseRaw = 0f;
        Defense = 0f;
        _offenseRaw = 0f;
        Offense = 0f;
        TimeSinceDefenseSignal = 1000f;
        Age = 0f;
        ProtectedRadius = ColonyTuning.ProtectedRadiusMin;
        NestHealth = ColonyTuning.NestMaxHealth;
        _timeSinceNestAttack = ColonyTuning.NestRegenDelaySeconds;
        DeathReason = "";
        IsAlive = true;
        DeathTime = 0f;
    }

    public void MarkDead(string reason, float simulationTime)
    {
        if (!IsAlive)
        {
            return;
        }
        IsAlive = false;
        DeathReason = reason;
        DeathTime = simulationTime;
    }

    public void UpdateNestHealth(World world, float dt)
    {
        int attackers = CountEnemiesNearNest(world);
        if (attackers > 0)
        {
            NestHealth -= attackers * ColonyTuning.NestDamagePerEnemyPerSec * dt;
            if (NestHealth < 0f)
            {
                NestHealth = 0f;
            }
            _timeSinceNestAttack = 0f;
            return;
        }

        _timeSinceNestAttack += dt;
        if (_timeSinceNestAttack < ColonyTuning.NestRegenDelaySeconds)
        {
            return;
        }
        if (NestHealth >= ColonyTuning.NestMaxHealth)
        {
            return;
        }
        NestHealth += ColonyTuning.NestRegenPerSec * dt;
        if (NestHealth > ColonyTuning.NestMaxHealth)
        {
            NestHealth = ColonyTuning.NestMaxHealth;
        }
    }

    private int CountEnemiesNearNest(World world)
    {
        float nestCx = NestX + 0.5f;
        float nestCy = NestY + 0.5f;
        return world.SpatialGrid.CountInRadius(nestCx, nestCy, ColonyTuning.NestAttackRadius, Id, ColonyTuning.MaxEnemyCountForDanger);
    }

    public void UpdateProtectedRadius()
    {
        float maxDistSq = 0f;
        int count = _ants.Count;
        for (int i = 0; i < count; i++)
        {
            Ant a = _ants[i];
            if (a.IsDead)
            {
                continue;
            }
            if (a.Role.IsCombatant)
            {
                continue;
            }
            float dx = a.X - (NestX + 0.5f);
            float dy = a.Y - (NestY + 0.5f);
            float distSq = dx * dx + dy * dy;
            if (distSq > maxDistSq)
            {
                maxDistSq = distSq;
            }
        }

        float targetRadius = (float)Math.Sqrt(maxDistSq) + ColonyTuning.ProtectedRadiusBuffer;
        if (targetRadius < ColonyTuning.ProtectedRadiusMin)
        {
            targetRadius = ColonyTuning.ProtectedRadiusMin;
        }
        if (targetRadius > ColonyTuning.ProtectedRadiusMax)
        {
            targetRadius = ColonyTuning.ProtectedRadiusMax;
        }
        ProtectedRadius = ProtectedRadius * (1f - ColonyTuning.ProtectedRadiusSmoothing) + targetRadius * ColonyTuning.ProtectedRadiusSmoothing;
    }

    public void TickAge(float dt)
    {
        Age += dt;
    }

    public void AddAnt(Ant ant)
    {
        _ants.Add(ant);
    }

    public void RemoveDeadAnts()
    {
        int writeIndex = 0;
        int count = _ants.Count;
        for (int readIndex = 0; readIndex < count; readIndex++)
        {
            Ant ant = _ants[readIndex];
            if (!ant.IsDead)
            {
                if (writeIndex != readIndex)
                {
                    _ants[writeIndex] = ant;
                }
                writeIndex++;
            }
        }
        if (writeIndex < count)
        {
            _ants.RemoveRange(writeIndex, count - writeIndex);
        }
    }

    public void RecountRoles()
    {
        int scouts = 0;
        int foragers = 0;
        int defenders = 0;
        int attackers = 0;
        int count = _ants.Count;
        for (int i = 0; i < count; i++)
        {
            AntRole role = _ants[i].Role;
            if (ReferenceEquals(role, ScoutRole.Instance))
            {
                scouts++;
            }
            else if (ReferenceEquals(role, ForagerRole.Instance))
            {
                foragers++;
            }
            else if (ReferenceEquals(role, DefenderRole.Instance))
            {
                defenders++;
            }
            else if (ReferenceEquals(role, AttackerRole.Instance))
            {
                attackers++;
            }
        }
        ScoutCount = scouts;
        ForagerCount = foragers;
        DefenderCount = defenders;
        AttackerCount = attackers;
    }

    public bool ConsumeFoodForSpawn(int cost)
    {
        if (NestFood < cost)
        {
            return false;
        }
        NestFood -= cost;
        return true;
    }

    public void DepositFood()
    {
        NestFood++;
    }

    public bool StealFood()
    {
        if (NestFood <= 0)
        {
            return false;
        }
        NestFood--;
        return true;
    }

    public void RegisterCombatDeath(float deathX, float deathY)
    {
        float dx = deathX - (NestX + 0.5f);
        float dy = deathY - (NestY + 0.5f);
        float distSq = dx * dx + dy * dy;
        if (distSq < ColonyTuning.CombatDeathDefenseRadiusSq)
        {
            _defenseRaw += ColonyTuning.CombatDeathDefenseWeight;
            TimeSinceDefenseSignal = 0f;
            return;
        }
        _offenseRaw += ColonyTuning.CombatDeathOffenseWeight;
    }

    public void RegisterDefenseSignal(float severity)
    {
        _defenseRaw += ColonyTuning.EnemyContactDefenseWeight * severity;
        TimeSinceDefenseSignal = 0f;
    }

    public void RegisterOffenseSignal(float amount)
    {
        _offenseRaw += amount;
    }

    public void UpdateSignals(float dt)
    {
        _defenseRaw *= (float)Math.Exp(-ColonyTuning.DefenseDecayPerSecond * dt);
        Defense = (float)Math.Tanh(_defenseRaw / ColonyTuning.DefenseScaleNormalizer);

        _offenseRaw *= (float)Math.Exp(-ColonyTuning.OffenseDecayPerSecond * dt);
        Offense = (float)Math.Tanh(_offenseRaw / ColonyTuning.OffenseScaleNormalizer);

        TimeSinceDefenseSignal += dt;
    }
}
