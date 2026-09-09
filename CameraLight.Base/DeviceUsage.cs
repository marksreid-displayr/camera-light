namespace CameraLight.Base;

public enum DeviceKind
{
    Camera,
    Microphone
}

/// <summary>
/// One app holding one device right now, as recorded in the Capability Access Manager consent store.
/// </summary>
/// <param name="Kind">The device being held.</param>
/// <param name="AppKey">
/// The consent store's identity for the app: an executable path for desktop apps, a package family
/// name for packaged ones. This is what exceptions are matched against.
/// </param>
/// <param name="DisplayName">A short name for the app, for tooltips and the history list.</param>
public record DeviceUsage(DeviceKind Kind, string AppKey, string DisplayName);
