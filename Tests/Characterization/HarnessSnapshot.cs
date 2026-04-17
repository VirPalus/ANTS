namespace ANTS;

using System.Globalization;
using System.Text;

internal static class HarnessSnapshot
{
    public static string BuildSnapshot(World world, int tick)
    {
        StringBuilder sb = new StringBuilder(1024);
        CultureInfo ic = CultureInfo.InvariantCulture;

        sb.Append("tick=").Append(tick.ToString(ic));
        sb.Append(" simtime=").Append(((int)(world.SimulationTime * 1000f)).ToString(ic));
        sb.Append(" foodcells=").Append(world.FoodCount.ToString(ic));
        sb.Append(" foodver=").Append(world.FoodVersion.ToString(ic));

        int[] orderedIds = new int[world.Colonies.Count];
        for (int i = 0; i < world.Colonies.Count; i++)
        {
            orderedIds[i] = world.Colonies[i].Id;
        }
        Array.Sort(orderedIds);

        for (int k = 0; k < orderedIds.Length; k++)
        {
            Colony? c = FindColony(world, orderedIds[k]);
            if (c == null) continue;

            int antCount = c.Ants.Count;
            long sumXq = 0;
            long sumYq = 0;
            long sumHeadingQ = 0;
            long sumCarrying = 0;
            int aliveCount = 0;
            for (int i = 0; i < antCount; i++)
            {
                Ant a = c.Ants[i];
                if (a.IsDead) continue;
                aliveCount++;
                sumXq += (long)(a.X * 100.0);
                sumYq += (long)(a.Y * 100.0);
                sumHeadingQ += (long)(a.Heading * 1000.0);
                sumCarrying += a.CarryingFood;
            }

            sb.Append(" | c").Append(c.Id.ToString(ic));
            sb.Append(" ants=").Append(aliveCount.ToString(ic));
            sb.Append(" scout=").Append(c.ScoutCount.ToString(ic));
            sb.Append(" forager=").Append(c.ForagerCount.ToString(ic));
            sb.Append(" def=").Append(c.DefenderCount.ToString(ic));
            sb.Append(" att=").Append(c.AttackerCount.ToString(ic));
            sb.Append(" nestFood=").Append(c.NestFood.ToString(ic));
            sb.Append(" nestHP=").Append(((int)(c.NestHealth * 100.0)).ToString(ic));
            sb.Append(" defense=").Append(((int)(c.Defense * 10000.0)).ToString(ic));
            sb.Append(" offense=").Append(((int)(c.Offense * 10000.0)).ToString(ic));
            sb.Append(" protR=").Append(((int)(c.ProtectedRadius * 100.0)).ToString(ic));
            sb.Append(" carryingSum=").Append(sumCarrying.ToString(ic));
            sb.Append(" sumX=").Append(sumXq.ToString(ic));
            sb.Append(" sumY=").Append(sumYq.ToString(ic));
            sb.Append(" sumH=").Append(sumHeadingQ.ToString(ic));
            sb.Append(" alive=").Append(c.IsAlive ? "1" : "0");
        }

        return sb.ToString();
    }

    private static Colony? FindColony(World world, int id)
    {
        for (int i = 0; i < world.Colonies.Count; i++)
        {
            if (world.Colonies[i].Id == id)
            {
                return world.Colonies[i];
            }
        }
        return null;
    }
}
