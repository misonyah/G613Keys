using System.Runtime.InteropServices;

namespace G613Keys.Core.Native;

/// <summary>
/// Win32 P/Invoke for HID enumeration and overlapped I/O. Declarations ported
/// verbatim from the validated feasibility spike. No external dependencies.
/// </summary>
internal static class Hid
{
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    public static extern int CM_Get_Device_Interface_List_Size(out uint size, ref Guid cls, string? dev, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    public static extern int CM_Get_Device_Interface_List(ref Guid cls, string? dev, char[] buf, uint len, uint flags);

    public const uint CM_GET_DEVICE_INTERFACE_LIST_PRESENT = 0;

    [DllImport("hid.dll")] public static extern void HidD_GetHidGuid(out Guid g);
    [DllImport("hid.dll")] public static extern bool HidD_GetPreparsedData(IntPtr h, out IntPtr pp);
    [DllImport("hid.dll")] public static extern bool HidD_FreePreparsedData(IntPtr pp);
    [DllImport("hid.dll")] public static extern int HidP_GetCaps(IntPtr pp, out HIDP_CAPS caps);

    public const int HIDP_STATUS_SUCCESS = 0x110000;

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDP_CAPS
    {
        public ushort Usage, UsagePage;
        public ushort InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices;
        public ushort NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);

    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool CloseHandle(IntPtr h);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool WriteFile(IntPtr h, byte[] buf, uint n, out uint written, ref NativeOverlapped ov);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool ReadFile(IntPtr h, byte[] buf, uint n, out uint read, ref NativeOverlapped ov);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetOverlappedResult(IntPtr h, ref NativeOverlapped ov, out uint n, bool wait);

    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool CancelIo(IntPtr h);
    [DllImport("kernel32.dll")] public static extern IntPtr CreateEvent(IntPtr a, bool manual, bool init, string? name);
    [DllImport("kernel32.dll")] public static extern uint WaitForSingleObject(IntPtr h, uint ms);

    public const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000;
    public const uint FILE_SHARE_RW = 3, OPEN_EXISTING = 3, FILE_FLAG_OVERLAPPED = 0x40000000;
    public const uint WAIT_OBJECT_0 = 0;
    public const int ERROR_IO_PENDING = 997;

    public static readonly IntPtr INVALID_HANDLE = new(-1);

    /// <summary>Split a REG_MULTI_SZ-style wide buffer into strings.</summary>
    public static IEnumerable<string> SplitMultiSz(char[] buf)
    {
        int start = 0;
        for (int i = 0; i < buf.Length; i++)
        {
            if (buf[i] != '\0') continue;
            if (i > start) yield return new string(buf, start, i - start);
            start = i + 1;
            if (i + 1 < buf.Length && buf[i + 1] == '\0') break;
        }
    }
}
