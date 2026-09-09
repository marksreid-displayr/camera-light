namespace CameraLight.Base;

public class DetectionOptions
{
    /// <summary>
    /// Apps whose device use should not count as a meeting, matched as case-insensitive substrings
    /// of the consent store's app name (an executable path, or a package family name).
    /// </summary>
    public string[]? IgnoredApps { get; set; }

    /// <summary>
    /// When true an app holding the microphone lights the indicator as well, so audio-only calls
    /// count. Off by default: the microphone is held by far more apps than the camera is.
    /// </summary>
    public bool MonitorMicrophone { get; set; }

    public int PollIntervalMilliseconds { get; set; } = 1000;

    public bool Ignores(string appKey) =>
        IgnoredApps?.Any(ignored => !string.IsNullOrWhiteSpace(ignored)
                                    && appKey.Contains(ignored, StringComparison.OrdinalIgnoreCase)) ?? false;
}
