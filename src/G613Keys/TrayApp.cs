using System.Drawing;
using System.Runtime.InteropServices;
using G613Keys.Core;

namespace G613Keys;

/// <summary>Tray-only application: owns the controller and the context menu.</summary>
internal sealed class TrayApp : ApplicationContext
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    private readonly NotifyIcon _tray;
    private readonly G613Controller _controller;
    private readonly DeviceNotificationWindow _devWindow;
    private readonly System.Windows.Forms.Timer _reArmDebounce;
    private readonly SynchronizationContext _ui;
    private readonly ToolStripMenuItem _enableItem;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _autostartItem;
    private ConfigForm? _config;
    private Icon? _ownedIcon;

    public TrayApp()
    {
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();

        _controller = new G613Controller();
        _controller.StatusChanged += s => _ui.Post(_ => OnStatusChanged(s), null);

        _statusItem = new ToolStripMenuItem("Starting…") { Enabled = false };
        _enableItem = new ToolStripMenuItem("Enabled", null, (_, _) => ToggleEnabled()) { Checked = true };
        _autostartItem = new ToolStripMenuItem("Start with Windows", null, (_, _) => ToggleAutostart())
        { Checked = Autostart.IsEnabled() };

        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_enableItem);
        menu.Items.Add(new ToolStripMenuItem("Configure…", null, (_, _) => OpenConfig()));
        menu.Items.Add(_autostartItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Quit", null, (_, _) => Quit()));

        _tray = new NotifyIcon
        {
            Icon = BuildIcon(),
            Text = "G613Keys",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _tray.DoubleClick += (_, _) => OpenConfig();

        _devWindow = new DeviceNotificationWindow();
        _reArmDebounce = new System.Windows.Forms.Timer { Interval = 400 };
        _reArmDebounce.Tick += (_, _) => { _reArmDebounce.Stop(); _controller.ReArm(); };
        _devWindow.DeviceArrived += () => _ui.Post(_ => RestartDebounce(), null);

        _controller.Start();
    }

    private void RestartDebounce() { _reArmDebounce.Stop(); _reArmDebounce.Start(); }

    private void ToggleEnabled()
    {
        if (_enableItem.Checked) { _controller.Stop(); _enableItem.Checked = false; }
        else { _controller.Start(); _enableItem.Checked = true; }
    }

    private void ToggleAutostart()
    {
        bool now = !_autostartItem.Checked;
        Autostart.SetEnabled(now);
        _autostartItem.Checked = now;
    }

    private void OpenConfig()
    {
        if (_config is { IsDisposed: false }) { _config.Activate(); return; }
        _config = new ConfigForm();
        _config.Show();
    }

    private void OnStatusChanged(ControllerStatus s)
    {
        _statusItem.Text = StatusText(s);
        _tray.Text = $"G613Keys — {StatusText(s)}";
        switch (s)
        {
            case ControllerStatus.NoGKeys:
                Balloon("G-keys unavailable", "This device does not expose the GKeys feature.");
                break;
            case ControllerStatus.NoReceiver:
                Balloon("Receiver not found", "Plug in the LIGHTSPEED receiver (G HUB must be closed).");
                break;
            case ControllerStatus.NoDevice:
                Balloon("Keyboard not detected", "Press a key to wake the G613, or check it's on the receiver.");
                break;
            case ControllerStatus.Error:
                Balloon("Error", _controller.LastError ?? "Unknown error.");
                break;
        }
    }

    private static string StatusText(ControllerStatus s) => s switch
    {
        ControllerStatus.Running => "Running",
        ControllerStatus.Stopped => "Disabled",
        ControllerStatus.NoReceiver => "No receiver",
        ControllerStatus.NoDevice => "Keyboard asleep / absent",
        ControllerStatus.NoGKeys => "G-keys unavailable",
        ControllerStatus.Error => "Error",
        _ => s.ToString(),
    };

    private void Balloon(string title, string text)
        => _tray.ShowBalloonTip(5000, title, text, ToolTipIcon.Warning);

    private Icon BuildIcon()
    {
        try
        {
            using var bmp = new Bitmap(32, 32);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using (var bg = new SolidBrush(Color.FromArgb(40, 44, 52)))
                g.FillEllipse(bg, 1, 1, 30, 30);
            using var font = new Font("Segoe UI", 12, FontStyle.Bold, GraphicsUnit.Pixel);
            using var fg = new SolidBrush(Color.FromArgb(120, 200, 255));
            var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("G6", font, fg, new RectangleF(0, 0, 32, 32), fmt);
            IntPtr h = bmp.GetHicon();
            _ownedIcon = Icon.FromHandle(h);
            return _ownedIcon;
        }
        catch
        {
            return SystemIcons.Application;
        }
    }

    private void Quit()
    {
        _tray.Visible = false;
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _reArmDebounce.Dispose();
            _devWindow.Dispose();
            _controller.Dispose();
            _tray.Dispose();
            _config?.Dispose();
            if (_ownedIcon is not null)
            {
                IntPtr h = _ownedIcon.Handle;
                _ownedIcon.Dispose();
                DestroyIcon(h);
            }
        }
        base.Dispose(disposing);
    }
}
