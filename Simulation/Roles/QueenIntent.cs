namespace ANTS;

public struct QueenIntent
{
    public string Plan;
    public float ScoutTarget;
    public float ForagerTarget;
    public float DefenderTarget;
    public float AttackerTarget;

    public QueenIntent(string plan, float scoutTarget, float foragerTarget, float defenderTarget, float attackerTarget)
    {
        Plan = plan;
        ScoutTarget = scoutTarget;
        ForagerTarget = foragerTarget;
        DefenderTarget = defenderTarget;
        AttackerTarget = attackerTarget;
    }
}
