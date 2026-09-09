using CameraLight.Base;

namespace CameraLight.Tray;

/// <summary>
/// What the app thinks is happening right now: which apps hold the camera or microphone, what the
/// lights were asked to do, and why they might not have done it.
/// </summary>
public sealed class StatusForm : TrayForm
{
    private readonly IStateManager _stateManager;
    private readonly IUsageMonitor _usageMonitor;

    private readonly Label _lightState = Value();
    private readonly Label _inUse = Value();
    private readonly Label _override = Value();
    private readonly Label _problem = Value();
    private readonly Button _forceOff = new() { AutoSize = true, Text = "Turn lights off now" };
    private readonly Button _resume = new() { AutoSize = true, Text = "Resume automatic", Enabled = false };

    public StatusForm(IStateManager stateManager, IUsageMonitor usageMonitor)
    {
        _stateManager = stateManager;
        _usageMonitor = usageMonitor;

        Text = "CameraLight status";
        ClientSize = new Size(460, 240);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(14),
            AutoSize = true
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddRow(layout, "Lights", _lightState);
        AddRow(layout, "In use by", _inUse);
        AddRow(layout, "Control", _override);
        AddRow(layout, "Problem", _problem);

        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        buttons.Controls.Add(_forceOff);
        buttons.Controls.Add(_resume);
        layout.Controls.Add(new Label { Text = string.Empty, AutoSize = true });
        layout.Controls.Add(buttons);

        Controls.Add(layout);

        _forceOff.Click += (_, _) => _stateManager.SetForcedOff(true);
        _resume.Click += (_, _) => _stateManager.SetForcedOff(false);

        _stateManager.StatusChanged += OnStatusChanged;
        _usageMonitor.CurrentChanged += OnUsageChanged;

        Refresh(_stateManager.Status, _usageMonitor.Current);
    }

    private static Label Value() => new() { AutoSize = true, MaximumSize = new Size(320, 0) };

    private static void AddRow(TableLayoutPanel layout, string caption, Control value)
    {
        layout.Controls.Add(new Label { Text = caption, AutoSize = true, Font = new Font(layout.Font, FontStyle.Bold) });
        layout.Controls.Add(value);
    }

    private void OnStatusChanged(object? sender, LightStatus status) =>
        OnUi(() => Refresh(status, _usageMonitor.Current));

    private void OnUsageChanged(object? sender, IReadOnlyList<DeviceUsage> usages) =>
        OnUi(() => Refresh(_stateManager.Status, usages));

    private void Refresh(LightStatus status, IReadOnlyList<DeviceUsage> usages)
    {
        _lightState.Text = TrayText.LightState(status);
        _inUse.Text = TrayText.InUse(usages);
        _override.Text = status.ForcedOff ? "Held off manually" : "Automatic";
        _problem.Text = status.IsFailing
            ? string.Join(Environment.NewLine,
                status.FailingLights.Select(failure => $"{failure.Key}: {failure.Value}")
                    .Append(status.NextAttemptAt is { } next ? $"Next attempt at {next:HH:mm:ss}" : "Retrying"))
            : "None";

        _forceOff.Enabled = !status.ForcedOff;
        _resume.Enabled = status.ForcedOff;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _stateManager.StatusChanged -= OnStatusChanged;
            _usageMonitor.CurrentChanged -= OnUsageChanged;
        }

        base.Dispose(disposing);
    }
}
