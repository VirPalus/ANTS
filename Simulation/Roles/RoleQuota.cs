namespace ANTS;

public class RoleQuota
{
    private const float FoundingScoutShare = 0.10f;
    private const float FoundingForagerShare = 0.90f;

    private const float PeaceScoutShare = 0.10f;
    private const float PeaceForagerShare = 0.80f;
    private const float PeaceDefenderShare = 0.05f;
    private const float PeaceAttackerShare = 0.05f;

    private const float DefenseScoutShare = 0.10f;
    private const float DefenseForagerShare = 0.40f;
    private const float DefenseDefenderShare = 0.30f;
    private const float DefenseAttackerShare = 0.20f;

    private const float OffenseScoutShare = 0.10f;
    private const float OffenseForagerShare = 0.40f;
    private const float OffenseDefenderShare = 0.20f;
    private const float OffenseAttackerShare = 0.30f;

    private const float DefenseBoostOnThreshold = 0.5f;
    private const float DefenseBoostOffThreshold = 0.2f;
    private const float OffenseBoostOnThreshold = 0.4f;
    private const float OffenseBoostOffThreshold = 0.15f;

    private List<AntRole> _roles;
    private bool _defenseBoostActive;
    private bool _offenseBoostActive;

    public RoleQuota()
    {
        _roles = new List<AntRole>();
        _defenseBoostActive = false;
        _offenseBoostActive = false;
    }

    public void Register(AntRole role)
    {
        _roles.Add(role);
    }

    public AntRole PickRoleForSpawn(Colony colony)
    {
        if (_roles.Count == 0)
        {
            return ScoutRole.Instance;
        }
        ComputeTargets(colony, out float scoutTarget, out float foragerTarget, out float defenderTarget, out float attackerTarget);
        return PickByDeficit(colony, scoutTarget, foragerTarget, defenderTarget, attackerTarget);
    }

    public QueenIntent GetCurrentIntent(Colony colony)
    {
        ComputeTargets(colony, out float scoutTarget, out float foragerTarget, out float defenderTarget, out float attackerTarget);
        AntRole next = PickByDeficit(colony, scoutTarget, foragerTarget, defenderTarget, attackerTarget);
        string plan = DeterminePlan(colony, next);
        return new QueenIntent(plan, scoutTarget, foragerTarget, defenderTarget, attackerTarget);
    }

    private void ComputeTargets(Colony colony, out float scoutTarget, out float foragerTarget, out float defenderTarget, out float attackerTarget)
    {
        if (colony.IsFounding)
        {
            scoutTarget = FoundingScoutShare;
            foragerTarget = FoundingForagerShare;
            defenderTarget = 0f;
            attackerTarget = 0f;
            return;
        }

        UpdateDefenseHysteresis(colony);
        UpdateOffenseHysteresis(colony);

        if (_defenseBoostActive && _offenseBoostActive)
        {
            scoutTarget = DefenseScoutShare;
            foragerTarget = DefenseForagerShare;
            defenderTarget = (DefenseDefenderShare + OffenseDefenderShare) * 0.5f;
            attackerTarget = (DefenseAttackerShare + OffenseAttackerShare) * 0.5f;
        }
        else if (_defenseBoostActive)
        {
            scoutTarget = DefenseScoutShare;
            foragerTarget = DefenseForagerShare;
            defenderTarget = DefenseDefenderShare;
            attackerTarget = DefenseAttackerShare;
        }
        else if (_offenseBoostActive)
        {
            scoutTarget = OffenseScoutShare;
            foragerTarget = OffenseForagerShare;
            defenderTarget = OffenseDefenderShare;
            attackerTarget = OffenseAttackerShare;
        }
        else
        {
            scoutTarget = PeaceScoutShare;
            foragerTarget = PeaceForagerShare;
            defenderTarget = PeaceDefenderShare;
            attackerTarget = PeaceAttackerShare;
        }

        ApplyWorkerBiasNudge(colony, ref scoutTarget, ref foragerTarget);
        ApplyCombatantBiasNudge(colony, ref defenderTarget, ref attackerTarget);
    }

    private const float PersonalityNudgeScale = 0.0833f;

    private static void ApplyWorkerBiasNudge(Colony colony, ref float scoutTarget, ref float foragerTarget)
    {
        float lean = colony.Queen.ExplorationBias - colony.Queen.GrowthBias;
        float nudge = lean * PersonalityNudgeScale;
        if (nudge > foragerTarget) nudge = foragerTarget;
        if (-nudge > scoutTarget) nudge = -scoutTarget;
        scoutTarget += nudge;
        foragerTarget -= nudge;
    }

    private static void ApplyCombatantBiasNudge(Colony colony, ref float defenderTarget, ref float attackerTarget)
    {
        float lean = colony.Queen.AggressionBias - colony.Queen.ThreatSensitivity;
        float nudge = lean * PersonalityNudgeScale;
        if (nudge > defenderTarget) nudge = defenderTarget;
        if (-nudge > attackerTarget) nudge = -attackerTarget;
        attackerTarget += nudge;
        defenderTarget -= nudge;
    }

    private void UpdateDefenseHysteresis(Colony colony)
    {
        float onT = DefenseBoostOnThreshold / colony.Queen.ThreatSensitivity;
        float offT = DefenseBoostOffThreshold / colony.Queen.ThreatSensitivity;
        if (colony.Defense > onT)
        {
            _defenseBoostActive = true;
        }
        else if (colony.Defense < offT)
        {
            _defenseBoostActive = false;
        }
    }

    private void UpdateOffenseHysteresis(Colony colony)
    {
        float onT = OffenseBoostOnThreshold / colony.Queen.AggressionBias;
        float offT = OffenseBoostOffThreshold / colony.Queen.AggressionBias;
        if (colony.Offense > onT)
        {
            _offenseBoostActive = true;
        }
        else if (colony.Offense < offT)
        {
            _offenseBoostActive = false;
        }
    }

    private AntRole PickByDeficit(Colony colony, float scoutTarget, float foragerTarget, float defenderTarget, float attackerTarget)
    {
        int total = colony.Ants.Count;
        if (total == 0)
        {
            return ScoutRole.Instance;
        }

        float actualScout = (float)colony.ScoutCount / (float)total;
        float actualForager = (float)colony.ForagerCount / (float)total;
        float actualDefender = (float)colony.DefenderCount / (float)total;
        float actualAttacker = (float)colony.AttackerCount / (float)total;

        float scoutDeficit = scoutTarget - actualScout;
        float foragerDeficit = foragerTarget - actualForager;
        float defenderDeficit = defenderTarget - actualDefender;
        float attackerDeficit = attackerTarget - actualAttacker;

        AntRole best = ScoutRole.Instance;
        float bestDeficit = scoutDeficit;

        if (foragerDeficit > bestDeficit)
        {
            best = ForagerRole.Instance;
            bestDeficit = foragerDeficit;
        }
        if (defenderDeficit > bestDeficit)
        {
            best = DefenderRole.Instance;
            bestDeficit = defenderDeficit;
        }
        if (attackerDeficit > bestDeficit)
        {
            best = AttackerRole.Instance;
            bestDeficit = attackerDeficit;
        }

        return best;
    }

    private string DeterminePlan(Colony colony, AntRole chosen)
    {
        if (colony.IsFounding)
        {
            return "founding nest";
        }
        if (ReferenceEquals(chosen, DefenderRole.Instance))
        {
            if (_defenseBoostActive)
            {
                return "defend nest";
            }
            return "reinforce guard";
        }
        if (ReferenceEquals(chosen, AttackerRole.Instance))
        {
            return "hunt enemy";
        }
        if (ReferenceEquals(chosen, ForagerRole.Instance))
        {
            return "gather food";
        }
        if (ReferenceEquals(chosen, ScoutRole.Instance))
        {
            return "explore";
        }
        return "balance roles";
    }
}
