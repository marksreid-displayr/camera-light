using CameraLight.Base;
using Microsoft.Extensions.Options;

namespace CameraLight;

/// <summary>
/// Polls every detector, drops the apps the user has excepted, and asks for the lights to be on
/// whenever anything is left. Also keeps the record of what started and stopped, which is the only
/// thing the history window and the tray tooltip have to go on.
/// </summary>
internal sealed class UsageMonitor(
    IEnumerable<IDeviceUsageDetector> detectors,
    IOptionsMonitor<DetectionOptions> options,
    IStateManager stateManager,
    IDisplayIdleBlocker displayIdleBlocker,
    IEventLog eventLog,
    ILogger<UsageMonitor> logger) : BackgroundService, IUsageMonitor
{
    private readonly IDeviceUsageDetector[] _detectors = detectors.ToArray();

    private IReadOnlyList<DeviceUsage> _current = [];
    private Dictionary<string, DeviceUsage> _lastInUse = new(StringComparer.OrdinalIgnoreCase);
    private string _lastSettingsSummary = string.Empty;

    public IReadOnlyList<DeviceUsage> Current => _current;

    public event EventHandler<IReadOnlyList<DeviceUsage>>? CurrentChanged;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var settings = options.CurrentValue;
            LogSettings(settings);

            try
            {
                await Tick(settings);
            }
            catch (Exception ex)
            {
                // A detector that throws must not end the loop: the lights would silently stop
                // following the camera, which is exactly the failure this app is meant to avoid.
                logger.LogError(ex, "Detection pass failed");
            }

            await Task.Delay(Math.Max(settings.PollIntervalMilliseconds, 250), stoppingToken);
        }
    }

    /// <summary>
    /// Settings can change under the app while it runs, so the log says what is actually in force
    /// rather than what was in force at startup.
    /// </summary>
    private void LogSettings(DetectionOptions settings)
    {
        var summary =
            $"microphone={settings.MonitorMicrophone}, interval={settings.PollIntervalMilliseconds}ms, "
            + $"ignoring=[{string.Join(", ", settings.IgnoredApps ?? [])}]";
        if (summary == _lastSettingsSummary)
        {
            return;
        }

        _lastSettingsSummary = summary;
        logger.LogInformation("Watching the camera with {Settings}", summary);
    }

    private async Task Tick(DetectionOptions settings)
    {
        var inUse = new Dictionary<string, DeviceUsage>(StringComparer.OrdinalIgnoreCase);

        foreach (var detector in _detectors)
        {
            if (detector.Kind == DeviceKind.Microphone && !settings.MonitorMicrophone)
            {
                continue;
            }

            foreach (var usage in await detector.InUse())
            {
                if (settings.Ignores(usage.AppKey))
                {
                    continue;
                }

                // A key is per device, so an app on both camera and microphone is listed once each.
                inUse[$"{usage.Kind}|{usage.AppKey}"] = usage;
            }
        }

        Record(inUse);
        stateManager.ChangeState(inUse.Count > 0 ? State.On : State.Off);
        displayIdleBlocker.SetBlocked(inUse.Values.Any(usage => usage.Kind == DeviceKind.Camera));
    }

    private void Record(Dictionary<string, DeviceUsage> inUse)
    {
        var started = inUse.Where(entry => !_lastInUse.ContainsKey(entry.Key)).Select(entry => entry.Value).ToArray();
        var stopped = _lastInUse.Where(entry => !inUse.ContainsKey(entry.Key)).Select(entry => entry.Value).ToArray();

        if (started.Length == 0 && stopped.Length == 0)
        {
            return;
        }

        foreach (var usage in started)
        {
            logger.LogInformation("{Device} in use by {App}", usage.Kind, usage.AppKey);
            eventLog.Append(new UsageEvent(DateTimeOffset.Now, UsageEventKind.Started, usage.Kind, usage.AppKey,
                usage.DisplayName));
        }

        foreach (var usage in stopped)
        {
            logger.LogInformation("{Device} released by {App}", usage.Kind, usage.AppKey);
            eventLog.Append(new UsageEvent(DateTimeOffset.Now, UsageEventKind.Stopped, usage.Kind, usage.AppKey,
                usage.DisplayName));
        }

        _lastInUse = inUse;
        _current = inUse.Values.OrderBy(usage => usage.Kind).ThenBy(usage => usage.DisplayName).ToArray();
        CurrentChanged?.Invoke(this, _current);
    }
}
