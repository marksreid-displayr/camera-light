using System.Diagnostics;
using CameraLight.Base;
using Microsoft.Extensions.Options;

namespace CameraLight.Tray;

/// <summary>
/// Owns the tray icon and the windows behind it. The icon is the app's only permanent surface, so
/// it has to tell the truth about the lights at a glance: red when they are on, amber when one is
/// unreachable, crossed out when the user has taken manual control.
/// </summary>
public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly IStateManager _stateManager;
    private readonly IUsageMonitor _usageMonitor;
    private readonly IEventLog _eventLog;
    private readonly UserSettingsStore _settings;
    private readonly IOptionsMonitor<DetectionOptions> _options;

    private readonly TrayIcons _icons = new();
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _forceOff;
    private readonly ToolStripMenuItem _resume;
    private readonly Control _marshal = new();

    private StatusForm? _status;
    private HistoryForm? _history;
    private SettingsForm? _settingsForm;

    public TrayApplicationContext(
        IStateManager stateManager,
        IUsageMonitor usageMonitor,
        IEventLog eventLog,
        UserSettingsStore settings,
        IOptionsMonitor<DetectionOptions> options)
    {
        _stateManager = stateManager;
        _usageMonitor = usageMonitor;
        _eventLog = eventLog;
        _settings = settings;
        _options = options;

        // Forces the handle so background threads have something to marshal onto from the start.
        _ = _marshal.Handle;

        // Kept separate from the resume item so a light that is still on can be told off again,
        // rather than the only click available flipping the override back to automatic.
        _forceOff = new ToolStripMenuItem("Turn lights off now", null, (_, _) => _stateManager.SetForcedOff(true))
        {
            CheckOnClick = false
        };
        _resume = new ToolStripMenuItem("Resume automatic", null, (_, _) => _stateManager.SetForcedOff(false))
        {
            CheckOnClick = false,
            Visible = false
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("Status", null, (_, _) => ShowStatus()));
        menu.Items.Add(new ToolStripMenuItem("History", null, (_, _) => ShowHistory()));
        menu.Items.Add(new ToolStripMenuItem("Settings", null, (_, _) => ShowSettings()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_forceOff);
        menu.Items.Add(_resume);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Open settings folder", null, (_, _) => OpenSettingsFolder()));
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => Exit()));

        _notifyIcon = new NotifyIcon
        {
            ContextMenuStrip = menu,
            Visible = true,
            Icon = _icons.Off
        };
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                ShowStatus();
            }
        };

        _stateManager.StatusChanged += OnStatusChanged;
        _usageMonitor.CurrentChanged += OnUsageChanged;
        Refresh();
    }

    private void OnStatusChanged(object? sender, LightStatus status) => OnUi(Refresh);

    private void OnUsageChanged(object? sender, IReadOnlyList<DeviceUsage> usages) => OnUi(Refresh);

    private void OnUi(Action action)
    {
        if (_marshal.IsDisposed || !_marshal.IsHandleCreated)
        {
            return;
        }

        try
        {
            _marshal.BeginInvoke(action);
        }
        catch (ObjectDisposedException)
        {
            // Shutting down; the icon is on its way out anyway.
        }
    }

    private void Refresh()
    {
        var status = _stateManager.Status;
        _notifyIcon.Icon = _icons.For(status);
        _notifyIcon.Text = TrayText.Tooltip(status, _usageMonitor.Current);
        _forceOff.Checked = status.ForcedOff;
        _resume.Visible = status.ForcedOff;
    }

    private void ShowStatus()
    {
        _status ??= new StatusForm(_stateManager, _usageMonitor);
        _status.ShowOrActivate();
    }

    private void ShowHistory()
    {
        _history ??= new HistoryForm(_eventLog, _settings);
        _history.ShowOrActivate();
    }

    private void ShowSettings()
    {
        _settingsForm ??= new SettingsForm(_options, _settings, _eventLog);
        // Pick up anything changed elsewhere, such as an exception added from the history window.
        _settingsForm.Reload();
        _settingsForm.ShowOrActivate();
    }

    private void OpenSettingsFolder()
    {
        UserPaths.EnsureDirectory();
        Process.Start(new ProcessStartInfo(UserPaths.Directory) { UseShellExecute = true });
    }

    private void Exit()
    {
        _notifyIcon.Visible = false;
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _stateManager.StatusChanged -= OnStatusChanged;
            _usageMonitor.CurrentChanged -= OnUsageChanged;

            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _status?.Dispose();
            _history?.Dispose();
            _settingsForm?.Dispose();
            _icons.Dispose();
            _marshal.Dispose();
        }

        base.Dispose(disposing);
    }
}
