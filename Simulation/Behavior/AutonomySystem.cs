namespace ANTS;

public static class AutonomySystem
{
    public static void Check(Ant ant, Colony colony, World world)
    {
        if (ant.Age > ant.Role.AutonomyMax)
        {
            int cx = (int)ant.X;
            int cy = (int)ant.Y;
            ant.IsDead = true;
            world.DecrementDensity(cx, cy);
            world.DropFoodFromDeadAnt(cx, cy, ant.CarryingFood);
        }
    }
}
