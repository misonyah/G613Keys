using System.Diagnostics;
using System.Runtime.InteropServices;

namespace G613Keys.Core.Profiles;

/// <summary>
/// Polls the foreground window (250 ms) and raises an event with the owning
/// process's executable name when it changes. A poll is simpler than a WinEvent
/// hook and more than fast enough for picking the active profile.
/// </summary>
public sealed class ForegroundWatcher : IDisposable
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    private readonly System.Threading.Timer _timer;
    private string _current = "";

    /// <summary>Raised with the new foreground executable name (e.g. "notepad.exe").</summary>
    public event Action<string>? ForegroundChanged;

    public ForegroundWatcher(int pollMs = 250)
    {
        _timer = new System.Threading.Timer(_ => Poll(), null, 0, pollMs);
    }

    public string Current => _current;

    private void Poll()
    {
        var exe = CurrentForegroundExe();
        if (exe == _current) return;
        _current = exe;
        ForegroundChanged?.Invoke(exe);
    }

    public static string CurrentForegroundExe()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return "";
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return "";
            using var p = Process.GetProcessById((int)pid);
            return (p.MainModule?.ModuleName) ?? $"{p.ProcessName}.exe";
        }
        catch
        {
            return "";
        }
    }

    public void Dispose() => _timer.Dispose();
}
