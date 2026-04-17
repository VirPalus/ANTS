namespace ANTS;

using System.Globalization;
using System.IO;

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
            HarnessRunner.RunInternal(seed, mapName, seconds, outPath, detailPath);
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
}
