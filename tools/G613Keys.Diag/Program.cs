using System.Diagnostics;
using System.Runtime.InteropServices;
using G613Keys.Core.GKeys;
using G613Keys.Core.Hidpp;
using G613Keys.Core.Input;

// G613Keys HID++ diagnostic. Run with G HUB stopped. Prints every stage of the
// pipeline and logs raw/decoded G-key traffic plus any default key output.

var log = new List<string>();
void W(string s) { Console.WriteLine(s); log.Add(s); }
long t0 = Stopwatch.GetTimestamp();
string TS() => $"{Stopwatch.GetElapsedTime(t0).TotalSeconds,6:F2}s";

W("=== G613Keys diagnostic ===");

var collections = WindowsHidChannel.Discover();
W($"HID++ collections found: {collections.Count}");
foreach (var c in collections)
    W($"  {c.Kind} reportId=0x{c.ReportId:X2} out={c.OutputReportByteLength} in={c.InputReportByteLength}");
if (collections.Count == 0)
{
    W("FAIL: no HID++ collections. Is the LIGHTSPEED receiver plugged in and G HUB fully stopped?");
    Dump(); return;
}

using var transport = new HidppTransport(WindowsHidChannel.OpenAll());
transport.Notification += r =>
{
    var p = string.Join(" ", r.Params.Select(b => b.ToString("X2")));
    W($"  {TS()} [NOTIFY] rid=0x{r.ReportId:X2} dev={r.DeviceIndex} feat=0x{r.FeatureIndex:X2} fn=0x{r.FuncSwId:X2} [{p}]");
};
transport.Start();

// Per-slot Root.getFeature(0x8010) probe with explicit error codes.
W("Probing device slots 1..6 (Root.getFeature 0x8010):");
byte? found = null;
for (byte s = 1; s <= 6; s++)
{
    var req = HidppReport.Request(HidppReportKind.Short, s, 0x00, 0x00, 0x80, 0x10);
    var r = transport.Request(req, 500);
    if (r is null) { W($"  slot {s}: no reply"); continue; }
    string kind = r.Is10Error ? $"1.0 error code=0x{r.ErrorCode:X2}"
        : r.Is20Error ? $"2.0 error code=0x{r.ErrorCode:X2}"
        : $"REPLY featureIndex={(r.Params.Length > 0 ? r.Params[0] : 0)}";
    W($"  slot {s}: {kind}");
    if (!r.Is10Error && !r.Is20Error) found ??= s;
    else if (r.ErrorCode == HidppError.ResourceError) found ??= s;
}

if (found is null)
{
    W("FAIL: no device answered on slots 1..6. If the keyboard is on Bluetooth (not the receiver) that's expected.");
    W(">>> If it's on the receiver: tap SPACEBAR to wake it, then rerun.");
    Dump(); return;
}
W($"Device slot = {found}");

var device = new HidppDevice(transport, found.Value);
byte feat = device.GetFeatureIndex(HidppDevice.GKeysFeatureId);
W($"GKeys (0x8010) feature index = {feat}" + (feat == 0 ? "  <-- 0 means the keyboard didn't report it (likely asleep)" : ""));
if (feat == 0)
{
    W(">>> Tap SPACEBAR to wake the keyboard, then rerun. (This is the startup-timing issue.)");
    Dump(); return;
}

using var gkeys = new GKeyService(transport, device, feat);
int events = 0;
gkeys.KeyChanged += e => { events++; W($"  {TS()} [GKEY] {e.Key} {(e.Down ? "DOWN" : "up")}"); };

bool ok = gkeys.Enable();
W($"Divert enable reply: {(ok ? "OK" : "FAILED/none")}");

// Watch for default output (double-input check). Do not swallow — just log.
int defaultKeys = 0;
using var hook = new KeyboardHook { ShouldSwallow = vk => { defaultKeys++; W($"  {TS()} [DEFAULT-OUT] vk=0x{vk:X2}"); return false; } };
hook.Install();

W("");
W(">>> PRESS G1..G6 now, one at a time. Listening 30s...");
// Low-level keyboard hooks need a message pump on the installing thread.
var until = Stopwatch.GetTimestamp() + (long)(30 * Stopwatch.Frequency);
while (Stopwatch.GetTimestamp() < until)
{
    while (DiagNative.PeekMessage(out _, IntPtr.Zero, 0, 0, 1)) { }
    Thread.Sleep(5);
}

W("");
W("=== SUMMARY ===");
W($"Device slot {found}, GKeys feature index {feat}, divert {(ok ? "enabled" : "FAILED")}.");
W($"G-key events seen: {events}");
W(defaultKeys == 0
    ? "Default output while diverted: NONE -> divert suppresses it (no double-input)."
    : $"Default output while diverted: {defaultKeys} event(s) -> divert does NOT suppress; app must swallow it.");
if (events == 0) W("No G-key events: either no keys were pressed, the keyboard slept, or the event format differs.");
Dump();

void Dump()
{
    var path = Path.Combine(AppContext.BaseDirectory, "g613diag.log");
    try { File.WriteAllLines(path, log); W($"(log written to {path})"); } catch { }
}

static class DiagNative
{
    [DllImport("user32.dll")]
    public static extern bool PeekMessage(out MSG m, IntPtr hwnd, uint min, uint max, uint remove);
    [StructLayout(LayoutKind.Sequential)]
    public struct MSG { public IntPtr hwnd; public uint message; public IntPtr w, l; public uint time; public int x, y; }
}
