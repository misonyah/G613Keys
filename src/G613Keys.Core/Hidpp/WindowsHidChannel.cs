using System.Runtime.InteropServices;
using G613Keys.Core.Native;

namespace G613Keys.Core.Hidpp;

/// <summary>
/// A real HID++ collection opened with overlapped CreateFile. Because the handle
/// is overlapped, ReadFile/WriteFile MUST pass an OVERLAPPED or they fail with
/// err 87 (see HIDPP-FINDINGS §3).
/// </summary>
public sealed class WindowsHidChannel : IReportChannel
{
    // Logitech LIGHTSPEED receiver.
    private const string Vid = "vid_046d";
    private const string Pid = "pid_c53d";

    private readonly IntPtr _handle;
    public HidppReportKind Kind { get; }
    public int OutputLength { get; }
    public int InputLength { get; }
    public string DevicePath { get; }

    private WindowsHidChannel(string path, IntPtr handle, HidppReportKind kind, int outLen, int inLen)
    {
        DevicePath = path;
        _handle = handle;
        Kind = kind;
        OutputLength = outLen;
        InputLength = inLen;
    }

    /// <summary>
    /// Enumerate the receiver's vendor HID++ collections. Selects by HID usage
    /// (page 0xFF00, usage 0x01→short, 0x02→long), never by largest report —
    /// the 32-byte DJ collection is a trap.
    /// </summary>
    public static IReadOnlyList<HidppCollection> Discover()
    {
        var result = new List<HidppCollection>();
        Hid.HidD_GetHidGuid(out var guid);
        Hid.CM_Get_Device_Interface_List_Size(out uint size, ref guid, null, Hid.CM_GET_DEVICE_INTERFACE_LIST_PRESENT);
        if (size == 0) return result;
        var buf = new char[size];
        Hid.CM_Get_Device_Interface_List(ref guid, null, buf, size, Hid.CM_GET_DEVICE_INTERFACE_LIST_PRESENT);

        foreach (var path in Hid.SplitMultiSz(buf))
        {
            var lp = path.ToLowerInvariant();
            if (!lp.Contains(Vid) || !lp.Contains(Pid)) continue;

            var h = Hid.CreateFile(path, 0, Hid.FILE_SHARE_RW, IntPtr.Zero, Hid.OPEN_EXISTING, 0, IntPtr.Zero);
            if (h == Hid.INVALID_HANDLE) continue;
            try
            {
                if (!Hid.HidD_GetPreparsedData(h, out var pp)) continue;
                try
                {
                    if (Hid.HidP_GetCaps(pp, out var caps) != Hid.HIDP_STATUS_SUCCESS) continue;
                    if (caps.UsagePage != 0xFF00) continue;

                    if (caps.Usage == 0x01 && caps.OutputReportByteLength == HidppReport.ShortLength)
                        result.Add(new HidppCollection(path, HidppReportKind.Short, caps.OutputReportByteLength, caps.InputReportByteLength));
                    else if (caps.Usage == 0x02 && caps.OutputReportByteLength == HidppReport.LongLength)
                        result.Add(new HidppCollection(path, HidppReportKind.Long, caps.OutputReportByteLength, caps.InputReportByteLength));
                }
                finally { Hid.HidD_FreePreparsedData(pp); }
            }
            finally { Hid.CloseHandle(h); }
        }
        return result;
    }

    /// <summary>Open a discovered collection for read/write with overlapped I/O.</summary>
    public static WindowsHidChannel Open(HidppCollection c)
    {
        var h = Hid.CreateFile(c.DevicePath, Hid.GENERIC_READ | Hid.GENERIC_WRITE, Hid.FILE_SHARE_RW,
            IntPtr.Zero, Hid.OPEN_EXISTING, Hid.FILE_FLAG_OVERLAPPED, IntPtr.Zero);
        if (h == Hid.INVALID_HANDLE)
            throw new InvalidOperationException($"Failed to open HID++ collection (err {Marshal.GetLastWin32Error()})");
        return new WindowsHidChannel(c.DevicePath, h, c.Kind, c.OutputReportByteLength, c.InputReportByteLength);
    }

    /// <summary>Open all HID++ collections of the receiver, if present.</summary>
    public static IReadOnlyList<WindowsHidChannel> OpenAll()
        => Discover().Select(Open).ToList();

    public void Write(byte[] wire)
    {
        var ev = Hid.CreateEvent(IntPtr.Zero, true, false, null);
        try
        {
            var ov = new NativeOverlapped { EventHandle = ev };
            if (Hid.WriteFile(_handle, wire, (uint)wire.Length, out _, ref ov))
                return;
            int e = Marshal.GetLastWin32Error();
            if (e == Hid.ERROR_IO_PENDING)
            {
                Hid.WaitForSingleObject(ev, 1000);
                if (Hid.GetOverlappedResult(_handle, ref ov, out _, false))
                    return;
                e = Marshal.GetLastWin32Error();
            }
            throw new IOException($"HID WriteFile failed (err {e})");
        }
        finally { Hid.CloseHandle(ev); }
    }

    public byte[]? Read(int timeoutMs)
    {
        var buf = new byte[InputLength];
        var ev = Hid.CreateEvent(IntPtr.Zero, true, false, null);
        try
        {
            var ov = new NativeOverlapped { EventHandle = ev };
            if (Hid.ReadFile(_handle, buf, (uint)buf.Length, out uint n, ref ov))
                return Trim(buf, n);

            if (Marshal.GetLastWin32Error() != Hid.ERROR_IO_PENDING)
                return null;

            if (Hid.WaitForSingleObject(ev, (uint)timeoutMs) == Hid.WAIT_OBJECT_0
                && Hid.GetOverlappedResult(_handle, ref ov, out uint got, false))
                return Trim(buf, got);

            Hid.CancelIo(_handle);
            return null;
        }
        finally { Hid.CloseHandle(ev); }
    }

    private static byte[] Trim(byte[] b, uint n)
    {
        int len = (int)Math.Min(n, (uint)b.Length);
        var o = new byte[len];
        Array.Copy(b, o, len);
        return o;
    }

    public void Dispose()
    {
        if (_handle != IntPtr.Zero && _handle != Hid.INVALID_HANDLE)
            Hid.CloseHandle(_handle);
    }
}
