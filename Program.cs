namespace ANTS;

static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--harness")
        {
            Environment.Exit(CharacterizationHarness.Run(args));
            return;
        }

        RunGui();
    }

    static void RunGui()
    {
        ApplicationConfiguration.Initialize();
        Application.SetColorMode(SystemColorMode.System);
        Application.Run(new Engine());
    }
}
