namespace G613Keys.Core.Hidpp;

/// <summary>
/// Routes HID++ requests to the right collection (by report id / kind) and
/// matches replies; inbound reports that match no pending request are surfaced
/// as notifications (GKey events). One reader thread per channel.
/// </summary>
public sealed class HidppTransport : IDisposable
{
    private sealed class Waiter
    {
        public required Func<HidppReport, bool> Match;
        public readonly ManualResetEventSlim Signal = new(false);
        public HidppReport? Reply;
    }

    private readonly IReadOnlyList<IReportChannel> _channels;
    private readonly List<Waiter> _waiters = new();
    private readonly object _waitLock = new();
    private readonly List<Thread> _readers = new();
    private volatile bool _running;

    /// <summary>Inbound reports not claimed by a pending request (e.g. GKey events).</summary>
    public event Action<HidppReport>? Notification;

    public HidppTransport(IEnumerable<IReportChannel> channels)
    {
        _channels = channels.ToList();
        if (_channels.Count == 0)
            throw new ArgumentException("No HID++ channels supplied", nameof(channels));
    }

    public IReadOnlyList<IReportChannel> Channels => _channels;

    /// <summary>Start the reader threads. Call once after construction.</summary>
    public void Start()
    {
        if (_running) return;
        _running = true;
        foreach (var ch in _channels)
        {
            var reader = new Thread(() => ReadLoop(ch)) { IsBackground = true, Name = "hidpp-read" };
            _readers.Add(reader);
            reader.Start();
        }
    }

    private void ReadLoop(IReportChannel ch)
    {
        while (_running)
        {
            byte[]? wire;
            try
            {
                wire = ch.Read(2000);
            }
            catch
            {
                // Never let a transient read error kill the pipe.
                Thread.Sleep(50);
                continue;
            }

            if (wire is null || wire.Length < 4) continue;

            HidppReport report;
            try { report = HidppReport.Parse(wire); }
            catch { continue; } // malformed / partial frame — drop it

            if (TryDeliver(report)) continue;
            try { Notification?.Invoke(report); } catch { /* handler errors are the handler's problem */ }
        }
    }

    private bool TryDeliver(HidppReport report)
    {
        lock (_waitLock)
        {
            for (int i = 0; i < _waiters.Count; i++)
            {
                var w = _waiters[i];
                if (!w.Match(report)) continue;
                w.Reply = report;
                _waiters.RemoveAt(i);
                w.Signal.Set();
                return true;
            }
        }
        return false;
    }

    /// <summary>Does an inbound report answer this request (normal or error reply)?</summary>
    public static bool IsReplyTo(HidppReport req, HidppReport r)
    {
        if (r.DeviceIndex != req.DeviceIndex) return false;
        // Normal reply: echoes feature index and software id.
        if (r.FeatureIndex == req.FeatureIndex && r.SoftwareId == req.SoftwareId)
            return true;
        // Error reply (1.0 0x8F or 2.0 0xFF): echoes the request's feature byte in
        // FuncSwId and the request's func|swId in Params[0].
        if ((r.Is20Error || r.Is10Error) && r.Params.Length >= 1
            && r.FuncSwId == req.FeatureIndex && r.Params[0] == req.FuncSwId)
            return true;
        return false;
    }

    /// <summary>Send a request on the matching channel and await its reply.</summary>
    public HidppReport? Request(HidppReport req, int timeoutMs = 500)
    {
        var ch = _channels.FirstOrDefault(c => c.Kind == (HidppReportKind)req.ReportId)
                 ?? throw new InvalidOperationException($"No channel for report id 0x{req.ReportId:X2}");

        var waiter = new Waiter { Match = r => IsReplyTo(req, r) };
        lock (_waitLock) _waiters.Add(waiter);
        try
        {
            ch.Write(req.ToWire(ch.OutputLength));
            return waiter.Signal.Wait(timeoutMs) ? waiter.Reply : null;
        }
        finally
        {
            lock (_waitLock) _waiters.Remove(waiter);
            waiter.Signal.Dispose();
        }
    }

    public void Dispose()
    {
        _running = false;
        foreach (var r in _readers)
            r.Join(500);
        foreach (var ch in _channels)
            ch.Dispose();
    }
}
