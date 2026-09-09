using System.Text.Json.Serialization;

namespace CameraLight.Base;

public enum UsageEventKind
{
    Started,
    Stopped,
    LightOn,
    LightOff,
    LightFailed,
    ForcedOff,
    Resumed,
    Excluded
}

/// <summary>
/// One line of the user-facing history. Serilog keeps the diagnostic log; this keeps the record of
/// what actually triggered the light, in a form the history window can list and filter.
/// </summary>
public record UsageEvent(
    DateTimeOffset At,
    UsageEventKind Kind,
    DeviceKind? Device = null,
    string? AppKey = null,
    string? DisplayName = null,
    string? Detail = null)
{
    [JsonIgnore]
    public string Description => Kind switch
    {
        UsageEventKind.Started => $"{Device} in use by {DisplayName}",
        UsageEventKind.Stopped => $"{Device} released by {DisplayName}",
        UsageEventKind.LightOn => "Lights on",
        UsageEventKind.LightOff => "Lights off",
        UsageEventKind.LightFailed => $"Light unreachable: {Detail}",
        UsageEventKind.ForcedOff => "Turned off manually",
        UsageEventKind.Resumed => "Automatic control resumed",
        UsageEventKind.Excluded => $"Ignored {DisplayName}",
        _ => Kind.ToString()
    };
}
