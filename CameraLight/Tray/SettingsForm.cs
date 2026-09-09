using CameraLight.Base;
using Microsoft.Extensions.Options;

namespace CameraLight.Tray;

/// <summary>
/// The settings the user is expected to change: which apps to ignore, whether the microphone
/// counts, and how often to look. Light addresses and credentials stay in appsettings.json.
/// </summary>
public sealed class SettingsForm : TrayForm
{
    private readonly IOptionsMonitor<DetectionOptions> _options;
    private readonly UserSettingsStore _settings;
    private readonly IEventLog _eventLog;

    private readonly ListBox _exceptions = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly ComboBox _seen = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown };
    private readonly CheckBox _microphone = new() { AutoSize = true, Text = "Also turn the lights on for the microphone" };
    private readonly NumericUpDown _pollInterval = new() { Minimum = 250, Maximum = 10000, Increment = 250, Width = 90 };

    public SettingsForm(IOptionsMonitor<DetectionOptions> options, UserSettingsStore settings, IEventLog eventLog)
    {
        _options = options;
        _settings = settings;
        _eventLog = eventLog;

        Text = "CameraLight settings";
        ClientSize = new Size(620, 460);
        MinimizeBox = true;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(12) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        layout.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "Apps listed here never turn the lights on. A match is any part of the app's identity, "
                   + "so \"WindowsHello\" is enough to cover face unlock."
        });

        _exceptions.Height = 180;
        layout.Controls.Add(_exceptions);

        var add = new Button { Text = "Add", AutoSize = true };
        var remove = new Button { Text = "Remove", AutoSize = true };
        var addRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, AutoSize = true };
        addRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        addRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        addRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        addRow.Controls.Add(_seen);
        addRow.Controls.Add(add);
        addRow.Controls.Add(remove);
        layout.Controls.Add(addRow);

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, AutoSize = true };
        bottom.Controls.Add(_microphone);

        var intervalRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        intervalRow.Controls.Add(new Label { Text = "Check every", AutoSize = true, Padding = new Padding(0, 6, 6, 0) });
        intervalRow.Controls.Add(_pollInterval);
        intervalRow.Controls.Add(new Label { Text = "ms", AutoSize = true, Padding = new Padding(4, 6, 0, 0) });
        bottom.Controls.Add(intervalRow);

        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        var save = new Button { Text = "Save", AutoSize = true };
        var cancel = new Button { Text = "Cancel", AutoSize = true };
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        bottom.Controls.Add(buttons);
        layout.Controls.Add(bottom);

        Controls.Add(layout);
        AcceptButton = save;
        CancelButton = cancel;

        add.Click += (_, _) => AddException();
        remove.Click += (_, _) => RemoveSelected();
        save.Click += (_, _) => Save();
        cancel.Click += (_, _) => Hide();

        Reload();
    }

    /// <summary>Reloads the form from the live settings; called whenever the window is shown.</summary>
    public void Reload()
    {
        var current = _options.CurrentValue;

        _exceptions.Items.Clear();
        foreach (var app in current.IgnoredApps ?? [])
        {
            _exceptions.Items.Add(app);
        }

        _microphone.Checked = current.MonitorMicrophone;
        _pollInterval.Value = Math.Clamp(current.PollIntervalMilliseconds, (int)_pollInterval.Minimum,
            (int)_pollInterval.Maximum);

        // Offer the apps that have actually held a device here, so nobody has to type a registry key.
        _seen.Items.Clear();
        var seen = _eventLog.Recent()
            .Where(usageEvent => !string.IsNullOrWhiteSpace(usageEvent.AppKey))
            .Select(usageEvent => usageEvent.AppKey!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase);
        foreach (var app in seen)
        {
            _seen.Items.Add(app);
        }
        _seen.Text = string.Empty;
    }

    private void AddException()
    {
        var app = _seen.Text.Trim();
        if (app.Length == 0)
        {
            return;
        }

        if (!_exceptions.Items.Cast<string>().Any(existing => string.Equals(existing, app, StringComparison.OrdinalIgnoreCase)))
        {
            _exceptions.Items.Add(app);
        }

        _seen.Text = string.Empty;
    }

    private void RemoveSelected()
    {
        if (_exceptions.SelectedIndex >= 0)
        {
            _exceptions.Items.RemoveAt(_exceptions.SelectedIndex);
        }
    }

    private void Save()
    {
        try
        {
            _settings.Save(_exceptions.Items.Cast<string>(), _microphone.Checked, (int)_pollInterval.Value);
            Hide();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not save settings", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
