namespace ANTS;

using System.IO;
using System.Text;

internal static class HarnessRunner
{
    public static void RunInternal(int seed, string mapName, int seconds, string outPath, string detailPath)
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

        HarnessWriter.WriteHeader(actualWriter, seed, mapName, seconds, def);
        HarnessWriter.WriteHeader(detailWriter, seed, mapName, seconds, def);

        HarnessWriter.CaptureAndWrite(world, tick: 0, actualWriter, detailWriter);

        for (int tick = 1; tick <= totalTicks; tick++)
        {
            world.Update();
            if (tick % ticksPerSnapshot == 0)
            {
                HarnessWriter.CaptureAndWrite(world, tick, actualWriter, detailWriter);
            }
        }

        actualWriter.Flush();
        detailWriter.Flush();
    }
}
