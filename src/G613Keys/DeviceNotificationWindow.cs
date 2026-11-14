using System.Runtime.InteropServices;

namespace G613Keys;

/// <summary>
/// Hidden message window that listens for HID device arrival so the controller
/// can re-arm divert when the keyboard wakes or the receiver reconnects.
/// </summary>
internal sealed class DeviceNotificationWindow : NativeWindow, IDisposable
{
    private const int WM_DEVICECHANGE = 0x0219;
    private const int DBT_DEVICEARRIVAL = 0x8000;
    private const int DBT_DEVTYP_DEVICEINTERFACE = 0x00000005;
    private const int DEVICE_NOTIFY_WINDOW_HANDLE = 0x00000000;

    // GUID_DEVINTERFACE_HID
    private static Guid _hidGuid = new("4D1E55B2-F16F-11CF-88CB-001111000030");

    [StructLayout(LayoutKind.Sequential)]
    private struct DEV_BROADCAST_DEVICEINTERFACE
    {
        public int dbcc_size;
        public int dbcc_devicetype;
        public int dbcc_reserved;
        public Guid dbcc_classguid;
        public short dbcc_name;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr RegisterDeviceNotification(IntPtr recipient, IntPtr filter, int flags);
    [DllImport("user32.dll")]
    private static extern bool UnregisterDeviceNotification(IntPtr handle);

    private IntPtr _notify;

    /// <summary>Raised on any HID device arrival (debounce in the handler if needed).</summary>
    public event Action? DeviceArrived;

    public DeviceNotificationWindow()
    {
        CreateHandle(new CreateParams { Caption = "G613Keys.DevNotify" });
        RegisterForHid();
    }

    private void RegisterForHid()
    {
        var filter = new DEV_BROADCAST_DEVICEINTERFACE
        {
            dbcc_size = Marshal.SizeOf<DEV_BROADCAST_DEVICEINTERFACE>(),
            dbcc_devicetype = DBT_DEVTYP_DEVICEINTERFACE,
            dbcc_classguid = _hidGuid,
        };
        IntPtr buf = Marshal.AllocHGlobal(filter.dbcc_size);
        try
        {
            Marshal.StructureToPtr(filter, buf, false);
            _notify = RegisterDeviceNotification(Handle, buf, DEVICE_NOTIFY_WINDOW_HANDLE);
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_DEVICECHANGE && (int)m.WParam == DBT_DEVICEARRIVAL)
            DeviceArrived?.Invoke();
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (_notify != IntPtr.Zero) { UnregisterDeviceNotification(_notify); _notify = IntPtr.Zero; }
        if (Handle != IntPtr.Zero) DestroyHandle();
    }
}
