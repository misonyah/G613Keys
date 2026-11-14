using System.Collections.Concurrent;
using G613Keys.Core.Hidpp;

namespace G613Keys.Tests;

/// <summary>
/// In-memory IReportChannel for tests. Records writes and, optionally, computes a
/// reply per write via <see cref="OnWrite"/>. Reports can also be injected
/// directly with <see cref="Inject"/> (e.g. GKey notifications).
/// </summary>
public sealed class FakeChannel : IReportChannel
{
    private readonly BlockingCollection<byte[]> _inbound = new();

    public HidppReportKind Kind { get; }
    public int OutputLength { get; }
    public int InputLength { get; }
    public List<byte[]> Writes { get; } = new();

    /// <summary>Given a written wire buffer, return a reply to deliver, or null.</summary>
    public Func<byte[], byte[]?>? OnWrite { get; set; }

    public FakeChannel(HidppReportKind kind = HidppReportKind.Short)
    {
        Kind = kind;
        OutputLength = InputLength = HidppReport.LengthFor(kind);
    }

    public void Write(byte[] wire)
    {
        Writes.Add(wire);
        var reply = OnWrite?.Invoke(wire);
        if (reply is not null) _inbound.Add(reply);
    }

    public void Inject(byte[] report) => _inbound.Add(report);

    public byte[]? Read(int timeoutMs)
        => _inbound.TryTake(out var r, timeoutMs) ? r : null;

    public void Dispose() => _inbound.Dispose();
}
