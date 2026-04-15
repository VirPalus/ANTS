namespace ANTS;

public static class SteeringSystem
{
    public static void UpdateHeading(Ant ant, float dt)
    {
        float diff = NormalizeAngle(ant.CachedSteerAngle - ant.Heading);
        float maxTurn = ant.Role.TurnRate * dt;
        if (diff > maxTurn)
        {
            diff = maxTurn;
        }
        else if (diff < -maxTurn)
        {
            diff = -maxTurn;
        }
        ant.Heading += diff;
        ant.Heading = NormalizeAngle(ant.Heading);
    }

    private static float NormalizeAngle(float a)
    {
        float twoPi = (float)(Math.PI * 2);
        while (a > Math.PI)
        {
            a -= twoPi;
        }
        while (a < -Math.PI)
        {
            a += twoPi;
        }
        return a;
    }
}
