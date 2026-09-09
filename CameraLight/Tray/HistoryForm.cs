using CameraLight.Base;

namespace CameraLight.Tray;

/// <summary>
/// The record of what triggered the light. Right-clicking a row is the quickest route to an
/// exception, which is how apps like Windows Hello face unlock get excluded.
/// </summary>
public sealed class HistoryForm : TrayForm
{
    private enum Filter
    {
        Everything,
        Camera,
        Microphone,
        Lights
    }

    private readonly IEventLog _eventLog;
    private readonly UserSettingsStore _settings;

    private readonly ListView _list = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        MultiSelect = false,
        HideSelection = false
    };

    private readonly ComboBox _filter = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 140
    };

    private readonly ToolStripMenuItem _exclude = new("Never let this app turn the lights on");
    private readonly ToolStripMenuItem _copy = new("Copy app identity");

    public HistoryForm(IEventLog eventLog, UserSettingsStore settings)
    {
        _eventLog = eventLog;
        _settings = settings;

        Text = "CameraLight history";
        ClientSize = new Size(760, 460);
        MinimizeBox = true;

        _list.Columns.Add("When", 150);
        _list.Columns.Add("Device", 90);
        _list.Columns.Add("App", 200);
        _list.Columns.Add("Event", 290);

        var menu = new ContextMenuStrip();
        menu.Items.Add(_exclude);
        menu.Items.Add(_copy);
        menu.Opening += (_, _) =>
        {
            var appKey = SelectedAppKey();
            _exclude.Enabled = appKey is not null;
            _copy.Enabled = appKey is not null;
        };
        _list.ContextMenuStrip = menu;
        _exclude.Click += (_, _) => ExcludeSelected();
        _copy.Click += (_, _) => CopySelected();

        _filter.Items.AddRange(["Everything", "Camera", "Microphone", "Lights"]);
        _filter.SelectedIndex = 0;
        _filter.SelectedIndexChanged += (_, _) => Reload();

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 8, 8, 4) };
        top.Controls.Add(new Label { Text = "Show", AutoSize = true, Padding = new Padding(0, 6, 6, 0) });
        top.Controls.Add(_filter);

        Controls.Add(_list);
        Controls.Add(top);

        _eventLog.Appended += OnAppended;
        Reload();
    }

    private void OnAppended(object? sender, UsageEvent usageEvent) => OnUi(Reload);

    private void Reload()
    {
        var filter = (Filter)_filter.SelectedIndex;

        _list.BeginUpdate();
        try
        {
            _list.Items.Clear();
            foreach (var usageEvent in _eventLog.Recent().Where(usageEvent => Matches(usageEvent, filter)))
            {
                var item = new ListViewItem(usageEvent.At.ToString("ddd HH:mm:ss, d MMM"))
                {
                    Tag = usageEvent
                };
                item.SubItems.Add(usageEvent.Device?.ToString() ?? string.Empty);
                item.SubItems.Add(usageEvent.DisplayName ?? string.Empty);
                item.SubItems.Add(usageEvent.Description);
                _list.Items.Add(item);
            }
        }
        finally
        {
            _list.EndUpdate();
        }
    }

    private static bool Matches(UsageEvent usageEvent, Filter filter) => filter switch
    {
        Filter.Camera => usageEvent.Device == DeviceKind.Camera,
        Filter.Microphone => usageEvent.Device == DeviceKind.Microphone,
        Filter.Lights => usageEvent.Kind is UsageEventKind.LightOn or UsageEventKind.LightOff
            or UsageEventKind.LightFailed or UsageEventKind.ForcedOff or UsageEventKind.Resumed,
        _ => true
    };

    private UsageEvent? Selected() =>
        _list.SelectedItems.Count == 1 ? _list.SelectedItems[0].Tag as UsageEvent : null;

    private string? SelectedAppKey()
    {
        var appKey = Selected()?.AppKey;
        return string.IsNullOrWhiteSpace(appKey) ? null : appKey;
    }

    private void ExcludeSelected()
    {
        var usageEvent = Selected();
        if (usageEvent?.AppKey is not { } appKey || string.IsNullOrWhiteSpace(appKey))
        {
            return;
        }

        var answer = MessageBox.Show(this,
            $"Stop {usageEvent.DisplayName} from turning the lights on?{Environment.NewLine}{Environment.NewLine}{appKey}",
            "Add exception", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
        if (answer != DialogResult.OK)
        {
            return;
        }

        try
        {
            _settings.AddException(appKey);
            _eventLog.Append(new UsageEvent(DateTimeOffset.Now, UsageEventKind.Excluded, usageEvent.Device, appKey,
                usageEvent.DisplayName));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not save the exception", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void CopySelected()
    {
        if (SelectedAppKey() is { } appKey)
        {
            Clipboard.SetText(appKey);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _eventLog.Appended -= OnAppended;
        }

        base.Dispose(disposing);
    }
}
