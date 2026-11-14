namespace G613Keys;

internal static class Program
{
    private static Mutex? _singleInstance;

    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        // --config opens just the configuration window (no tray, no device),
        // so a running instance's "Configure…" and a standalone edit both work.
        if (args.Contains("--config", StringComparer.OrdinalIgnoreCase))
        {
            Application.Run(new ConfigForm());
            return;
        }

        _singleInstance = new Mutex(initiallyOwned: true, "G613Keys.SingleInstance", out bool isNew);
        if (!isNew)
            return; // already running

        Application.Run(new TrayApp());

        GC.KeepAlive(_singleInstance);
    }
}
