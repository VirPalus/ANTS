namespace ANTS;

using System.Globalization;
using System.IO;

internal static class HarnessWriter
{
    public static void WriteHeader(StreamWriter w, int seed, string mapName, int seconds, MapDefinition def)
    {
        CultureInfo ic = CultureInfo.InvariantCulture;
        w.WriteLine("# ANTS characterization harness");
        w.WriteLine("# seed=" + seed.ToString(ic));
        w.WriteLine("# map=" + mapName);
        w.WriteLine("# seconds=" + seconds.ToString(ic));
        w.WriteLine("# world=" + def.Width.ToString(ic) + "x" + def.Height.ToString(ic));
        w.WriteLine("# colonies=" + def.ColonySeeds.Count.ToString(ic));
        w.WriteLine("#");
    }

    public static void CaptureAndWrite(World world, int tick, StreamWriter actualWriter, StreamWriter detailWriter)
    {
        string snapshot = HarnessSnapshot.BuildSnapshot(world, tick);
        string digest = HarnessDigest.Sha256Hex(snapshot);
        string line = "t=" + tick.ToString(CultureInfo.InvariantCulture) + " sha256=" + digest;
        actualWriter.WriteLine(line);
        detailWriter.WriteLine(line);
        detailWriter.WriteLine(snapshot);
        detailWriter.WriteLine();
    }
}
