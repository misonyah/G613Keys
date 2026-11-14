using System.Diagnostics;
using G613Keys.Core.GKeys;
using G613Keys.Core.Macros;
using G613Keys.Core.Profiles;

namespace G613Keys;

/// <summary>
/// Config window: pick/add/remove a per-app profile and edit each of the six
/// G-keys as a short macro script. Each key can be recorded by pressing it,
/// tested in place, and is validated live. Follows the Windows light/dark theme
/// and shows which profile is active for the current foreground app.
/// </summary>
internal sealed class ConfigForm : Form
{
    private readonly ProfileStore _store = new();
    private List<Profile> _profiles;

    private readonly ComboBox _profileCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240 };
    private readonly ComboBox _matchCombo = new() { Width = 220 };
    private readonly Label _activeBanner = new() { AutoSize = true, Tag = "subtle" };
    private readonly Button _saveButton = new() { Text = "Save", AutoSize = true, Enabled = false };

    private readonly TextBox[] _keyBoxes = new TextBox[GKeyService.GKeyCount];
    private readonly Button[] _recordButtons = new Button[GKeyService.GKeyCount];
    private readonly Label[] _errorLabels = new Label[GKeyService.GKeyCount];

    private readonly ChordRecorder _recorder = new();
    private readonly ForegroundWatcher _foreground = new();

    private Panel? _keyScroll;
    private int _current = -1;
    private int _recordingIndex = -1;
    private string _lastRealApp = "";
    private bool _dirty;
    private bool _loading;

    private static readonly string SelfExe =
        System.IO.Path.GetFileName(Environment.ProcessPath ?? "G613Keys.exe");

    public ConfigForm()
    {
        Text = "G613Keys — Configuration";
        Size = new Size(660, 820);
        MinimumSize = new Size(560, 560);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9f);
        BackColor = Theme.Palette.Window;

        _profiles = _store.Load().ToList();

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(12) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildBanner(), 0, 1);
        root.Controls.Add(BuildKeyArea(), 0, 2);
        root.Controls.Add(BuildButtons(), 0, 3);
        Controls.Add(root);

        _recorder.ChordCaptured += line => BeginInvoke(() => OnChordCaptured(line));
        _recorder.Stopped += () => BeginInvoke(StopRecording);
        _foreground.ForegroundChanged += exe => BeginInvoke(() => OnForeground(exe));

        Load += (_, _) =>
        {
            Theme.ApplyTitleBar(Handle);
            if (_keyScroll is not null) _keyScroll.AutoScrollPosition = Point.Empty; // start at G1
        };
        FormClosing += OnFormClosing;

        ApplyTheme(this);
        RefreshProfileCombo();
        if (_profiles.Count > 0) _profileCombo.SelectedIndex = IndexOfDefaultFirst();
        OnForeground(ForegroundWatcher.CurrentForegroundExe());
    }

    private int IndexOfDefaultFirst()
    {
        int d = _profiles.FindIndex(p => p.IsDefault);
        return d >= 0 ? d : 0;
    }

    private Control BuildHeader()
    {
        var panel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        panel.Controls.Add(Lbl("Profile:"));
        _profileCombo.SelectedIndexChanged += (_, _) => SwitchTo(_profileCombo.SelectedIndex);
        panel.Controls.Add(_profileCombo);
        panel.Controls.Add(Btn("Add", AddProfile));
        panel.Controls.Add(Btn("Remove", RemoveProfile));

        panel.Controls.Add(Lbl("     Matches app:"));
        foreach (var exe in RunningExes()) _matchCombo.Items.Add(exe);
        _matchCombo.Leave += (_, _) => UpdateMatch();
        _matchCombo.SelectedIndexChanged += (_, _) => UpdateMatch();
        panel.Controls.Add(_matchCombo);
        panel.Controls.Add(Btn("Add for active app", AddProfileForActiveApp));
        return panel;
    }

    private Control BuildBanner()
    {
        var p = new Panel { AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(2, 6, 2, 2) };
        p.Controls.Add(_activeBanner);
        return p;
    }

    private Control BuildKeyArea()
    {
        const int cardHeight = 126;
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        // Fixed-height rows and a width that tracks the panel: nothing auto-sizes
        // its width, so the Record/Test column can never be pushed off the edge.
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = GKeyService.GKeyCount,
            Height = cardHeight * GKeyService.GKeyCount,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < GKeyService.GKeyCount; i++)
        {
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, cardHeight));
            grid.Controls.Add(BuildKeyCard(i), 0, i);
        }
        scroll.Controls.Add(grid);
        _keyScroll = scroll;
        return scroll;
    }

    private Control BuildKeyCard(int i)
    {
        int idx = i;
        var card = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 10),
            Padding = new Padding(10, 8, 10, 8),
            Tag = "card",
        };
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var keyLabel = new Label
        {
            Text = $"G{i + 1}",
            Font = new Font("Segoe UI Semibold", 11f, FontStyle.Bold),
            AutoSize = true,
            Anchor = AnchorStyles.Top,
            Margin = new Padding(0, 4, 8, 0),
            Tag = "accent",
        };
        card.Controls.Add(keyLabel, 0, 0);

        // Content column: textbox on top, a bar (Record, Test, inline error) below.
        var content = new Panel { Dock = DockStyle.Fill };

        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 30, WrapContents = false, Margin = Padding.Empty };
        var rec = Btn("● Record", () => ToggleRecording(idx));
        rec.AutoSize = false; rec.Size = new Size(90, 24);
        _recordButtons[i] = rec;
        bar.Controls.Add(rec);
        var test = Btn("▶ Test", () => _ = TestKey(idx));
        test.AutoSize = false; test.Size = new Size(72, 24);
        bar.Controls.Add(test);
        var error = new Label { AutoSize = true, Margin = new Padding(8, 5, 0, 0), ForeColor = Theme.Palette.Error, Tag = "error" };
        _errorLabels[i] = error;
        bar.Controls.Add(error);

        var box = new TextBox
        {
            Multiline = true,
            Dock = DockStyle.Fill,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9.5f),
            AcceptsReturn = true,
            BorderStyle = BorderStyle.FixedSingle,
            Tag = "field",
        };
        box.TextChanged += (_, _) => OnKeyTextChanged(idx);
        _keyBoxes[i] = box;

        content.Controls.Add(bar);   // edge-docked first
        content.Controls.Add(box);   // fill added last takes remaining space
        card.Controls.Add(content, 1, 0);

        return card;
    }

    private Control BuildButtons()
    {
        var panel = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, Dock = DockStyle.Fill, AutoSize = true };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var help = new Label
        {
            AutoSize = true,
            Tag = "subtle",
            Margin = new Padding(0, 8, 0, 0),
            Text = "One action per line:  key: Ctrl+Shift+S   •   text: hello   •   delay: 100   •   mouse: left   •   run: app.exe   •   repeat 3 { key: Up }",
        };
        panel.Controls.Add(help, 0, 0);

        var right = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill };
        _saveButton.Click += (_, _) => Save();
        right.Controls.Add(_saveButton);
        right.Controls.Add(Btn("Close", Close));
        panel.Controls.Add(right, 1, 0);
        return panel;
    }

    // ---- profile selection & editing ------------------------------------

    private void RefreshProfileCombo()
    {
        var sel = _profileCombo.SelectedIndex;
        _profileCombo.Items.Clear();
        foreach (var p in _profiles)
            _profileCombo.Items.Add(p.IsDefault ? "(default — all apps)" : p.Match);
        if (sel >= 0 && sel < _profileCombo.Items.Count) _profileCombo.SelectedIndex = sel;
    }

    private void SwitchTo(int index)
    {
        if (index < 0 || index >= _profiles.Count) return;
        if (_current >= 0) CaptureEdits();
        StopRecording();
        _current = index;
        _loading = true;
        var p = _profiles[index];
        _matchCombo.Text = p.IsDefault ? "*" : p.Match;
        _matchCombo.Enabled = !p.IsDefault;
        for (int i = 0; i < _keyBoxes.Length; i++)
        {
            _keyBoxes[i].Text = p.Keys.TryGetValue((GKey)i, out var acts) ? MacroScript.Format(acts) : "";
            ValidateBox(i);
        }
        _loading = false;
    }

    private void OnKeyTextChanged(int i)
    {
        ValidateBox(i);
        if (!_loading) MarkDirty();
    }

    private void ValidateBox(int i)
    {
        var errs = MacroScript.Validate(_keyBoxes[i].Text);
        _errorLabels[i].Text = errs.Count == 0 ? "" : $"⚠ line {errs[0].Line}: {errs[0].Message}";
    }

    private void CaptureEdits()
    {
        if (_current < 0) return;
        var keys = new Dictionary<GKey, List<MacroAction>>();
        for (int i = 0; i < _keyBoxes.Length; i++)
        {
            var actions = MacroScript.Parse(_keyBoxes[i].Text).ToList();
            if (actions.Count > 0) keys[(GKey)i] = actions;
        }
        _profiles[_current] = _profiles[_current] with { Keys = keys };
    }

    private void UpdateMatch()
    {
        if (_current < 0) return;
        var p = _profiles[_current];
        if (p.IsDefault) return;
        var name = _matchCombo.Text.Trim();
        if (name.Length == 0 || name == p.Match) return;
        _profiles[_current] = p with { Match = name };
        MarkDirty();
        RefreshProfileCombo();
        UpdateBanner();
    }

    private void AddProfile() => AddProfileNamed("app.exe");

    private void AddProfileForActiveApp()
    {
        if (_lastRealApp.Length == 0) { MessageBox.Show("No active app detected yet — focus the app you want, then try again.", "G613Keys"); return; }
        var existing = _profiles.FindIndex(p => !p.IsDefault && string.Equals(p.Match, _lastRealApp, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0) { _profileCombo.SelectedIndex = existing; return; }
        AddProfileNamed(_lastRealApp);
    }

    private void AddProfileNamed(string match)
    {
        CaptureEdits();
        _profiles.Add(new Profile(match, new Dictionary<GKey, List<MacroAction>>()));
        MarkDirty();
        RefreshProfileCombo();
        _profileCombo.SelectedIndex = _profiles.Count - 1;
    }

    private void RemoveProfile()
    {
        if (_current < 0) return;
        if (_profiles[_current].IsDefault) { MessageBox.Show("The default profile can't be removed.", "G613Keys"); return; }
        _profiles.RemoveAt(_current);
        _current = -1;
        MarkDirty();
        RefreshProfileCombo();
        if (_profiles.Count > 0) _profileCombo.SelectedIndex = IndexOfDefaultFirst();
    }

    private void Save()
    {
        CaptureEdits();
        if (!_profiles.Any(p => p.IsDefault)) _profiles.Add(Profile.EmptyDefault());
        _store.Save(_profiles);
        _dirty = false;
        _saveButton.Enabled = false;
    }

    private void MarkDirty()
    {
        _dirty = true;
        _saveButton.Enabled = true;
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        StopRecording();
        if (!_dirty) return;
        var r = MessageBox.Show("Save changes before closing?", "G613Keys", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (r == DialogResult.Cancel) { e.Cancel = true; return; }
        if (r == DialogResult.Yes) Save();
    }

    // ---- record / test --------------------------------------------------

    private void ToggleRecording(int i)
    {
        if (_recordingIndex == i) { StopRecording(); return; }
        StopRecording();
        _recordingIndex = i;
        _recordButtons[i].Text = "■ Esc to stop";
        _recordButtons[i].ForeColor = Theme.Palette.Error;
        _keyBoxes[i].Focus();
        _recorder.Start();
    }

    private void StopRecording()
    {
        if (_recordingIndex < 0) return;
        _recorder.Stop();
        _recordButtons[_recordingIndex].Text = "● Record";
        _recordButtons[_recordingIndex].ForeColor = Theme.Palette.Text;
        _recordingIndex = -1;
    }

    private void OnChordCaptured(string line)
    {
        if (_recordingIndex < 0) return;
        var box = _keyBoxes[_recordingIndex];
        if (box.Text.Length > 0 && !box.Text.EndsWith('\n')) box.AppendText(Environment.NewLine);
        box.AppendText(line);
    }

    private async Task TestKey(int i)
    {
        var text = _keyBoxes[i].Text;
        var errs = MacroScript.Validate(text);
        if (errs.Count > 0) { _errorLabels[i].Text = $"⚠ line {errs[0].Line}: {errs[0].Message}"; return; }
        var actions = MacroScript.Parse(text);
        if (actions.Count == 0) return;

        // Minimize so the macro's keystrokes land on the window behind us, not this form.
        var prev = WindowState;
        WindowState = FormWindowState.Minimized;
        await Task.Delay(700);
        try { await new MacroEngine().Run(actions); } catch { /* bad user macro shouldn't crash the UI */ }
        await Task.Delay(120);
        WindowState = prev;
        Activate();
    }

    // ---- active-app banner ---------------------------------------------

    private void OnForeground(string exe)
    {
        if (string.IsNullOrEmpty(exe) || string.Equals(exe, SelfExe, StringComparison.OrdinalIgnoreCase))
            return; // ignore ourselves so the banner keeps showing the real app
        _lastRealApp = exe;
        UpdateBanner();
    }

    private void UpdateBanner()
    {
        if (_lastRealApp.Length == 0) { _activeBanner.Text = "Active app: (none yet)"; return; }
        var p = ProfileStore.Resolve(_profiles, _lastRealApp);
        _activeBanner.Text = p.IsDefault
            ? $"Active app: {_lastRealApp}  →  using the default profile"
            : $"Active app: {_lastRealApp}  →  using profile '{p.Match}'";
    }

    // ---- theming & helpers ---------------------------------------------

    private static Label Lbl(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(0, 8, 4, 0) };

    private Button Btn(string text, Action onClick)
    {
        var b = new Button { Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, Margin = new Padding(4, 2, 0, 2) };
        b.Click += (_, _) => onClick();
        return b;
    }

    private void ApplyTheme(Control root)
    {
        var c = Theme.Palette;
        foreach (Control ctl in root.Controls)
        {
            switch (ctl)
            {
                case TextBox tb:
                    tb.BackColor = c.FieldBack; tb.ForeColor = c.Text;
                    break;
                case ComboBox cb:
                    cb.BackColor = c.FieldBack; cb.ForeColor = c.Text; cb.FlatStyle = FlatStyle.Flat;
                    break;
                case Button bt:
                    bt.BackColor = c.Surface; bt.ForeColor = c.Text;
                    bt.FlatAppearance.BorderColor = c.Border;
                    break;
                case Label lb:
                    lb.ForeColor = (string?)lb.Tag switch
                    {
                        "error" => c.Error,
                        "accent" => c.Accent,
                        "subtle" => c.SubtleText,
                        _ => c.Text,
                    };
                    break;
                case TableLayoutPanel when (string?)ctl.Tag == "card":
                    ctl.BackColor = c.Surface;
                    break;
            }
            if (ctl.HasChildren) ApplyTheme(ctl);
        }
    }

    private static IEnumerable<string> RunningExes()
    {
        var seen = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                var name = p.MainWindowHandle != IntPtr.Zero ? p.MainModule?.ModuleName : null;
                if (!string.IsNullOrEmpty(name)) seen.Add(name);
            }
            catch { /* access denied for some processes — skip */ }
            finally { p.Dispose(); }
        }
        return seen;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _recorder.Dispose();
            _foreground.Dispose();
            _store.Dispose();
        }
        base.Dispose(disposing);
    }
}
