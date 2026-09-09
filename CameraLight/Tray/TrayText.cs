using CameraLight.Base;

namespace CameraLight.Tray;

/// <summary>
/// The wording the tooltip and the status window share, so they never describe the same state
/// two different ways.
/// </summary>
public static class TrayText
{
    public static string LightState(LightStatus status) => status switch
    {
        { IsFailing: true } => $"Unreachable ({status.FailingLights.Count} light(s) failing)",
        { ForcedOff: true } => "Off (held manually)",
        { Applied: State.On } => "On",
        { Applied: State.Off } => "Off",
        _ => "Starting up"
    };

    public static string InUse(IReadOnlyList<DeviceUsage> usages) =>
        usages.Count == 0
            ? "Nothing"
            : string.Join(", ", usages.Select(usage => $"{usage.DisplayName} ({usage.Kind.ToString().ToLowerInvariant()})"));

    /// <summary>
    /// A NotifyIcon tooltip is capped at 63 characters and silently fails above it, so the app
    /// list is trimmed rather than trusted.
    /// </summary>
    public static string Tooltip(LightStatus status, IReadOnlyList<DeviceUsage> usages)
    {
        var text = $"CameraLight: {LightState(status)}";
        if (usages.Count > 0)
        {
            text += $" — {string.Join(", ", usages.Select(usage => usage.DisplayName))}";
        }

        return text.Length <= 63 ? text : text[..60] + "...";
    }
}
