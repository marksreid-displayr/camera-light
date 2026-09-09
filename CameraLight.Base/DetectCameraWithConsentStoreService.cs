using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Win32;

namespace CameraLight.Base;

/// <summary>
/// Reports the camera as in use by reading the Capability Access Manager consent store, the same
/// records that drive the Windows camera-in-use tray indicator and the recent activity list under
/// Privacy &amp; security. Window titles used to be the signal, but the meeting apps moved to
/// WebView2 and stopped naming the meeting in the title, so they could no longer be recognised.
/// </summary>
public class DetectCameraWithConsentStoreService : ICameraDetectionService
{
    private const string ConsentStorePath =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam";

    // Desktop apps are grouped under this key; packaged apps sit directly under the device key.
    private const string NonPackagedKeyName = "NonPackaged";

    private readonly string[] _ignoredApps;
    private readonly ILogger<DetectCameraWithConsentStoreService> _logger;

    private HashSet<string> _lastInUse = new(StringComparer.OrdinalIgnoreCase);

    public DetectCameraWithConsentStoreService(IOptions<WebcamDetectionOptions> options,
        ILogger<DetectCameraWithConsentStoreService> logger)
    {
        _ignoredApps = options.Value.IgnoredApps ?? [];
        _logger = logger;
    }

    public Task<bool> IsActive()
    {
        var inUse = InUseApps();

        // The lights only log when they change, so without this a quiet log is indistinguishable
        // from a detector that has stopped noticing meetings.
        if (!inUse.SetEquals(_lastInUse))
        {
            if (inUse.Count > 0)
            {
                _logger.LogInformation("Camera in use by {Apps}", string.Join(", ", inUse.Order()));
            }
            else
            {
                _logger.LogInformation("Camera released");
            }
            _lastInUse = inUse;
        }

        return Task.FromResult(inUse.Count > 0);
    }

    private HashSet<string> InUseApps()
    {
        var apps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Per-user apps record under HKCU; services and machine-wide apps record under HKLM.
        foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            try
            {
                using var webcam = root.OpenSubKey(ConsentStorePath);
                if (webcam is null)
                {
                    continue;
                }

                Collect(webcam, apps);

                using var nonPackaged = webcam.OpenSubKey(NonPackagedKeyName);
                if (nonPackaged is not null)
                {
                    Collect(nonPackaged, apps);
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
            {
                _logger.LogDebug(ex, "Consent store under {Root} is not readable", root.Name);
            }
        }

        return apps;
    }

    private void Collect(RegistryKey parent, HashSet<string> apps)
    {
        foreach (var subKeyName in parent.GetSubKeyNames())
        {
            if (string.Equals(subKeyName, NonPackagedKeyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                using var appKey = parent.OpenSubKey(subKeyName);
                if (appKey is null || !IsInUse(appKey))
                {
                    continue;
                }

                // Desktop app keys escape the backslashes of their executable path as '#'.
                var app = subKeyName.Replace('#', '\\');
                if (_ignoredApps.Any(ignored => app.Contains(ignored, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                apps.Add(app);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
            {
                _logger.LogDebug(ex, "Consent store entry {App} is not readable", subKeyName);
            }
        }
    }

    /// <summary>
    /// A recorded start with no matching stop means the app is holding the camera right now.
    /// </summary>
    private static bool IsInUse(RegistryKey appKey) =>
        ReadTimestamp(appKey, "LastUsedTimeStart") > 0 && ReadTimestamp(appKey, "LastUsedTimeStop") == 0;

    private static long ReadTimestamp(RegistryKey appKey, string name) =>
        appKey.GetValue(name) switch
        {
            long timestamp => timestamp,
            int timestamp => timestamp,
            _ => 0
        };
}
