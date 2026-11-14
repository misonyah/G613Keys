using System.Diagnostics;
using G613Keys.Core.GKeys;
using G613Keys.Core.Macros;
using G613Keys.Core.Profiles;

namespace G613Keys;

/// <summary>
/// Minimal config window: pick/add/remove a per-app profile and edit each of the
/// six G-keys as a short macro script (see MacroScript for the syntax).
/// </summary>
internal sealed class ConfigForm : Form
{
    private readonly ProfileStore _store = new();
    private List<Profile> _profiles;
    private readonly ComboBox _profileCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly ComboBox _matchCombo = new() { Width = 220 };
    private readonly TextBox[] _keyBoxes = new TextBox[GKeyService.GKeyCount];
    private int _current = -1;

    public ConfigForm()
    {
        Text = "G613Keys — Configuration";
        Width = 560;
        Height = 560;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(480, 480);

        _profiles = _store.Load().ToList();

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(10) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildKeyGrid(), 0, 1);
        root.Controls.Add(BuildButtons(), 0, 2);
        Controls.Add(root);

        RefreshProfileCombo();
        if (_profiles.Count > 0) _profileCombo.SelectedIndex = 0;
    }

    private Control BuildHeader()
    {
        var panel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = true };
        panel.Controls.Add(new Label { Text = "Profile:", AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 6, 4, 0) });
        _profileCombo.SelectedIndexChanged += (_, _) => SwitchTo(_profileCombo.SelectedIndex);
        panel.Controls.Add(_profileCombo);
        panel.Controls.Add(new Button { Text = "Add", AutoSize = true }.Also(b => b.Click += (_, _) => AddProfile()));
        panel.Controls.Add(new Button { Text = "Remove", AutoSize = true }.Also(b => b.Click += (_, _) => RemoveProfile()));

        panel.Controls.Add(new Label { Text = "Matches app:", AutoSize = true, Padding = new Padding(12, 6, 4, 0) });
        foreach (var exe in RunningExes()) _matchCombo.Items.Add(exe);
        _matchCombo.Leave += (_, _) => UpdateMatch();
        panel.Controls.Add(_matchCombo);
        panel.Controls.Add(new Label { Text = "(use * for the default profile)", AutoSize = true, Padding = new Padding(4, 6, 0, 0) });
        return panel;
    }

    private Control BuildKeyGrid()
    {
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = GKeyService.GKeyCount };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < GKeyService.GKeyCount; i++)
        {
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / GKeyService.GKeyCount));
            grid.Controls.Add(new Label { Text = $"G{i + 1}", AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 6, 8, 0) }, 0, i);
            var box = new TextBox { Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9), AcceptsReturn = true };
            _keyBoxes[i] = box;
            grid.Controls.Add(box, 1, i);
        }
        return grid;
    }

    private Control BuildButtons()
    {
        var panel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        panel.Controls.Add(new Button { Text = "Save", AutoSize = true, DialogResult = DialogResult.OK }.Also(b => b.Click += (_, _) => Save()));
        panel.Controls.Add(new Button { Text = "Close", AutoSize = true }.Also(b => b.Click += (_, _) => Close()));
        var help = new Label
        {
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Text = "One action per line:  key: Ctrl+Shift+S   •   text: hello   •   delay: 100   •   mouse: left   •   repeat 3 { key: Up }",
            Padding = new Padding(0, 8, 12, 0),
        };
        panel.Controls.Add(help);
        return panel;
    }

    private void RefreshProfileCombo()
    {
        _profileCombo.Items.Clear();
        foreach (var p in _profiles)
            _profileCombo.Items.Add(p.IsDefault ? "(default)" : p.Match);
    }

    private void SwitchTo(int index)
    {
        if (index < 0 || index >= _profiles.Count) return;
        if (_current >= 0) CaptureEdits();
        _current = index;
        var p = _profiles[index];
        _matchCombo.Text = p.Match;
        _matchCombo.Enabled = !p.IsDefault;
        for (int i = 0; i < _keyBoxes.Length; i++)
            _keyBoxes[i].Text = p.Keys.TryGetValue((GKey)i, out var acts) ? MacroScript.Format(acts) : "";
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
        int sel = _current;
        RefreshProfileCombo();
        _profileCombo.SelectedIndex = sel;
    }

    private void AddProfile()
    {
        CaptureEdits();
        _profiles.Add(new Profile("app.exe", new Dictionary<GKey, List<MacroAction>>()));
        RefreshProfileCombo();
        _profileCombo.SelectedIndex = _profiles.Count - 1;
    }

    private void RemoveProfile()
    {
        if (_current < 0) return;
        if (_profiles[_current].IsDefault)
        {
            MessageBox.Show("The default profile can't be removed.", "G613Keys");
            return;
        }
        _profiles.RemoveAt(_current);
        _current = -1;
        RefreshProfileCombo();
        if (_profiles.Count > 0) _profileCombo.SelectedIndex = 0;
    }

    private void Save()
    {
        CaptureEdits();
        if (!_profiles.Any(p => p.IsDefault)) _profiles.Add(Profile.EmptyDefault());
        _store.Save(_profiles);
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
}

internal static class ControlExtensions
{
    public static T Also<T>(this T control, Action<T> configure) where T : Control
    {
        configure(control);
        return control;
    }
}
