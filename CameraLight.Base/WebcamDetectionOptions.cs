namespace CameraLight.Base;

public class WebcamDetectionOptions
{
    /// <summary>
    /// Apps whose camera use should not count as a meeting, matched as case-insensitive substrings
    /// of the consent store's app name (an executable path, or a package family name).
    /// </summary>
    public string[]? IgnoredApps { get; set; }
}
