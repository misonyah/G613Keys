using G613Keys.Core.GKeys;
using G613Keys.Core.Hidpp;
using G613Keys.Core.Input;
using G613Keys.Core.Macros;
using G613Keys.Core.Profiles;

namespace G613Keys.Core;

public enum ControllerStatus
{
    Stopped,
    Running,
    NoReceiver,   // LIGHTSPEED receiver not found
    NoDevice,     // receiver present but the G613 didn't answer (off / on Bluetooth)
    NoGKeys,      // device present but doesn't expose GKeys (0x8010)
    Error,
}

/// <summary>
/// Owns the live HID++ pipeline: transport → device → GKeys divert → macro
/// execution via the active per-app profile. Thin enough that the tray app only
/// starts/stops it and shows <see cref="Status"/>.
/// </summary>
public sealed class G613Controller : IDisposable
{
    private readonly ProfileStore _store;
    private readonly ForegroundWatcher _foreground;
    private readonly MacroEngine _engine;
    private readonly KeyboardHook _hook;

    private readonly object _gate = new();
    private HidppTransport? _transport;
    private GKeyService? _gkeys;
    private IReadOnlyList<Profile> _profiles;
    private CancellationTokenSource? _runningMacro;

    public ControllerStatus Status { get; private set; } = ControllerStatus.Stopped;
    public string? LastError { get; private set; }

    /// <summary>Raised whenever <see cref="Status"/> changes, for the tray UI.</summary>
    public event Action<ControllerStatus>? StatusChanged;

    /// <summary>
    /// Virtual-key codes to swallow when <see cref="SuppressDefaultOutput"/> is on
    /// (the double-input fallback). Populated once hardware confirms whether divert
    /// silences the default output and, if not, what the keys emit.
    /// </summary>
    public HashSet<int> DefaultOutputVks { get; } = new();

    public bool SuppressDefaultOutput { get; set; }

    public G613Controller(ProfileStore? store = null)
    {
        _store = store ?? new ProfileStore();
        _profiles = _store.Load();
        _store.Changed += () => _profiles = _store.Load();
        _store.StartWatching();
        _foreground = new ForegroundWatcher();
        _engine = new MacroEngine();
        _hook = new KeyboardHook { ShouldSwallow = vk => SuppressDefaultOutput && DefaultOutputVks.Contains(vk) };
    }

    /// <summary>Build and start the pipeline. Safe to call again to retry.</summary>
    public void Start()
    {
      lock (_gate)
      {
        Stop();
        try
        {
            var channels = WindowsHidChannel.OpenAll();
            if (channels.Count == 0) { SetStatus(ControllerStatus.NoReceiver); return; }

            _transport = new HidppTransport(channels);
            _transport.Start();

            var slot = HidppDevice.FindDeviceIndex(_transport);
            if (slot is null) { SetStatus(ControllerStatus.NoDevice); return; }

            var device = new HidppDevice(_transport, slot.Value);
            byte feat = device.GetFeatureIndex(HidppDevice.GKeysFeatureId);
            if (feat == 0) { SetStatus(ControllerStatus.NoGKeys); return; }

            _gkeys = new GKeyService(_transport, device, feat);
            _gkeys.KeyChanged += OnKeyChanged;
            _gkeys.Enable();

            _hook.Install();
            SetStatus(ControllerStatus.Running);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            SetStatus(ControllerStatus.Error);
        }
      }
    }

    public void Stop()
    {
      lock (_gate)
      {
        _runningMacro?.Cancel();
        if (_gkeys is not null) { _gkeys.KeyChanged -= OnKeyChanged; _gkeys.Dispose(); _gkeys = null; }
        _transport?.Dispose();
        _transport = null;
        if (Status == ControllerStatus.Running) SetStatus(ControllerStatus.Stopped);
      }
    }

    /// <summary>
    /// Periodic keep-alive while running: silently re-assert divert so the pipeline
    /// survives the G613 clearing divert on idle sleep/wake (which fires no device
    /// event and leaves <see cref="Status"/> at Running, so nothing else recovers it).
    /// Cheap and safe to call every few seconds; a transient failure is ignored and
    /// the not-running health retry rebuilds if the device actually went away.
    /// </summary>
    public void RefreshDivertIfRunning()
    {
        lock (_gate)
        {
            if (Status == ControllerStatus.Running && _gkeys is not null)
            {
                try { _gkeys.RefreshDivert(); } catch { /* next tick / health retry handles it */ }
            }
        }
    }

    /// <summary>
    /// Called on device arrival and by the periodic health retry. If the pipeline
    /// is up, re-send divert; otherwise rebuild it — the keyboard may have just
    /// woken or been plugged in. A sleep→wake often fires no device event and the
    /// keyboard reports no feature while asleep, so retrying from any non-running
    /// state is what makes the app recover after idle.
    /// </summary>
    public void ReArm()
    {
        lock (_gate)
        {
            if (Status == ControllerStatus.Running && _gkeys is not null)
            {
                try { _gkeys.ReArm(); } catch { Start(); }
                return;
            }
            if (Status != ControllerStatus.Stopped) Start();
        }
    }

    private void OnKeyChanged(GKeyEvent ev)
    {
        if (!ev.Down) return; // macros fire on press
        var profile = ProfileStore.Resolve(_profiles, _foreground.Current);
        if (!profile.Keys.TryGetValue(ev.Key, out var actions) || actions.Count == 0)
            return;

        _runningMacro?.Cancel();
        _runningMacro = new CancellationTokenSource();
        var ct = _runningMacro.Token;
        _ = Task.Run(() => _engine.Run(actions, ct), ct);
    }

    private void SetStatus(ControllerStatus s)
    {
        if (Status == s) return;
        Status = s;
        StatusChanged?.Invoke(s);
    }

    public void Dispose()
    {
        Stop();
        _hook.Dispose();
        _foreground.Dispose();
        _store.Dispose();
    }
}
