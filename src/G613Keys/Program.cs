namespace G613Keys;

internal static class Program
{
    private static Mutex? _singleInstance;

    [STAThread]
    private static void Main()
    {
        _singleInstance = new Mutex(initiallyOwned: true, "G613Keys.SingleInstance", out bool isNew);
        if (!isNew)
            return; // already running

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApp());

        GC.KeepAlive(_singleInstance);
    }
}
