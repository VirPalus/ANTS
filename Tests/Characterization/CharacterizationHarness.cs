namespace ANTS;

using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

public static class CharacterizationHarness
{
    private const int DefaultSeed = 42;
    private const string DefaultMapName = "01_open_field";
    private const int DefaultSeconds = 60;

    public static int Run(string[] args)
    {
        int seed = DefaultSeed;
        string mapName = DefaultMapName;
        int seconds = DefaultSeconds;
        string outDir = Path.Combine(AppContext.BaseDirectory, "CharacterizationOutput");
        string outPath = Path.Combine(outDir, "actual.txt");
        string detailPath = Path.Combine(outDir, "actual_detail.txt");

        for (int i = 1; i < args.Length; i++)
        {
            string a = args[i];
            int eq = a.IndexOf('=');
            if (eq <= 0) continue;
            string key = a.Substring(0, eq);
            string val = a.Substring(eq + 1);
            switch (key)
            {
                case "seed":
                    seed = int.Parse(val, CultureInfo.InvariantCulture);
                    break;
                case "map":
                    mapName = val;
                    break;
                case "seconds":
                    seconds = int.Parse(val, CultureInfo.InvariantCulture);
                    break;
                case "out":
                    outPath = val;
                    detailPath = Path.Combine(
                        Path.GetDirectoryName(val) ?? ".",
                        Path.GetFileNameWithoutExtension(val) + "_detail.txt");
                    break;
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(detailPath)!);

        try
        {
            RunInternal(seed, mapName, seconds, outPath, detailPath);
            return 0;
        }
        catch (Exception ex)
        {
            string err = "HARNESS ERROR: " + ex.GetType().Name + ": " + ex.Message + Environment.NewLine + ex.StackTrace;
            File.WriteAllText(outPath, err);
            File.WriteAllText(detailPath, err);
            return 1;
        }
    }

    private static void RunInternal(int seed, string mapName, int seconds, string outPath, string detailPath)
    {
        string mapPath = Path.Combine(AppContext.BaseDirectory, "Maps", mapName + ".png");
        if (!File.Exists(mapPath))
        {
            throw new FileNotFoundException("Fixture map not found next to executable.", mapPath);
        }

        MapDefinition def = MapLoader.Load(mapPath);

        World world = new World(def.Width, def.Height, seed);
        world.ApplyMapLayout(def);

        for (int i = 0; i < def.ColonySeeds.Count; i++)
        {
            ColonySeed s = def.ColonySeeds[i];
            world.AddColony(s.X, s.Y, s.Color);
        }

        int totalTicks = seconds * World.SimHz;
        int ticksPerSnapshot = World.SimHz;

        using FileStream actualFs = File.Create(outPath);
        using StreamWriter actualWriter = new StreamWriter(actualFs, new UTF8Encoding(false));
        actualWriter.NewLine = "\n";

        using FileStream detailFs = File.Create(detailPath);
        using StreamWriter detailWriter = new StreamWriter(detailFs, new UTF8Encoding(false));
        detailWriter.NewLine = "\n";

        WriteHeader(actualWriter, seed, mapName, seconds, def);
        WriteHeader(detailWriter, seed, mapName, seconds, def);

        CaptureAndWrite(world, tick: 0, actualWriter, detailWriter);

        for (int tick = 1; tick <= totalTicks; tick++)
        {
            world.Update();
            if (tick % ticksPerSnapshot == 0)
            {
                CaptureAndWrite(world, tick, actualWriter, detailWriter);
            }
        }

        actualWriter.Flush();
        detailWriter.Flush();
    }

    private static void WriteHeader(StreamWriter w, int seed, string mapName, int seconds, MapDefinition def)
    {
        w.WriteLine("# ANTS characterization harness");
        w.WriteLine("# seed=" + seed.ToString(CultureInfo.InvariantCulture));
        w.WriteLine("# map=" + mapName);
        w.WriteLine("# seconds=" + seconds.ToString(CultureInfo.InvariantCulture));
        w.WriteLine("# world=" + def.Width.ToString(CultureInfo.InvariantCulture) + "x" + def.Height.ToString(CultureInfo.InvariantCulture));
        w.WriteLine("# colonies=" + def.ColonySeeds.Count.ToString(CultureInfo.InvariantCulture));
        w.WriteLine("#");
    }

    private static void CaptureAndWrite(World world, int tick, StreamWriter actualWriter, StreamWriter detailWriter)
    {
        string snapshot = BuildSnapshot(world, tick);
        string digest = Sha256Hex(snapshot);
        actualWriter.WriteLine("t=" + tick.ToString(CultureInfo.InvariantCulture) + " sha256=" + digest);
        detailWriter.WriteLine("t=" + tick.ToString(CultureInfo.InvariantCulture) + " sha256=" + digest);
        detailWriter.WriteLine(snapshot);
        detailWriter.WriteLine();
    }

    private static string BuildSnapshot(World world, int tick)
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
            Colony c = FindColony(world, orderedIds[k]);
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

    private static string Sha256Hex(string input)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(input);
        byte[] hash = SHA256.HashData(bytes);
        StringBuilder hex = new StringBuilder(hash.Length * 2);
        for (int i = 0; i < hash.Length; i++)
        {
            hex.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
        }
        return hex.ToString();
    }
}
