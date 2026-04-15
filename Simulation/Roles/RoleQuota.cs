namespace ANTS;
using System.Collections.Generic;

public class RoleQuota
{
    private const float CombatantFloor = 0.10f;
    private const float CombatantRange = 0.35f;
    private const float CombatantSigmoidMid = 50f;
    private const float CombatantSigmoidSlope = 30f;
    private const float DefenseBoostOnThreshold = 0.6f;
    private const float DefenseBoostOffThreshold = 0.3f;
    private const float CombatantTargetCap = 0.65f;
    private const float BaseScoutShare = 0.55f;
    private const float DefenderFloor = 0.15f;

    private List<AntRole> _roles;
    private bool _defenseBoostActive;

    public RoleQuota()
    {
        _roles = new List<AntRole>();
        _defenseBoostActive = false;
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
        int population = colony.Ants.Count;
        Queen queen = colony.Queen;

        float totalCombatant;
        if (colony.IsFounding)
        {
            totalCombatant = 0f;
        }
        else
        {
            float sigmoidInput = -(population - CombatantSigmoidMid) / CombatantSigmoidSlope;
            float sigmoid = 1f / (1f + (float)Math.Exp(sigmoidInput));
            float baseCombatant = CombatantFloor + CombatantRange * sigmoid;
            totalCombatant = baseCombatant * queen.AggressionBias;

            UpdateDefenseHysteresis(colony);
            if (_defenseBoostActive)
            {
                totalCombatant *= 1f + colony.Defense * queen.ThreatSensitivity;
            }

            float enemyPresence = colony.Defense;
            if (colony.Offense > enemyPresence)
            {
                enemyPresence = colony.Offense;
            }
            totalCombatant *= enemyPresence;

            if (totalCombatant > CombatantTargetCap)
            {
                totalCombatant = CombatantTargetCap;
            }
            if (totalCombatant < 0f)
            {
                totalCombatant = 0f;
            }
        }

        float defenderShare;
        if (totalCombatant <= 0f)
        {
            defenderShare = 0f;
            defenderTarget = 0f;
            attackerTarget = 0f;
        }
        else
        {
            defenderShare = DefenderFloor + (1f - DefenderFloor) * colony.Defense;
            if (defenderShare > 1f)
            {
                defenderShare = 1f;
            }
            defenderTarget = totalCombatant * defenderShare;
            attackerTarget = totalCombatant - defenderTarget;
        }

        float remaining = 1f - totalCombatant;
        float exploreWeight = queen.ExplorationBias;
        float foragerWeight = queen.GrowthBias;
        float totalWeight = exploreWeight + foragerWeight;
        float scoutShare = BaseScoutShare * (exploreWeight / (totalWeight * 0.5f + 0.5f));
        if (scoutShare > 0.85f)
        {
            scoutShare = 0.85f;
        }
        if (scoutShare < 0.15f)
        {
            scoutShare = 0.15f;
        }

        scoutTarget = remaining * scoutShare;
        foragerTarget = remaining - scoutTarget;
    }

    private void UpdateDefenseHysteresis(Colony colony)
    {
        if (colony.Defense > DefenseBoostOnThreshold)
        {
            _defenseBoostActive = true;
        }
        else if (colony.Defense < DefenseBoostOffThreshold)
        {
            _defenseBoostActive = false;
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
