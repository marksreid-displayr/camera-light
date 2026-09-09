using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace CameraLight.Base;

/// <summary>
/// Reports the apps holding a device by reading the Capability Access Manager consent store, the
/// same records that drive the Windows camera-in-use tray indicator and the recent activity list
/// under Privacy &amp; security. Window titles used to be the signal, but the meeting apps moved to
/// WebView2 and stopped naming the meeting in the title, so they could no longer be recognised.
/// </summary>
public class ConsentStoreDetector(DeviceKind kind, ILogger<ConsentStoreDetector> logger) : IDeviceUsageDetector
{
    private const string ConsentStorePath =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore";

    // Desktop apps are grouped under this key; packaged apps sit directly under the device key.
    private const string NonPackagedKeyName = "NonPackaged";

    private readonly string _devicePath = $@"{ConsentStorePath}\{DeviceKeyName(kind)}";

    public DeviceKind Kind => kind;

    private static string DeviceKeyName(DeviceKind deviceKind) => deviceKind switch
    {
        DeviceKind.Camera => "webcam",
        DeviceKind.Microphone => "microphone",
        _ => throw new ArgumentOutOfRangeException(nameof(deviceKind), deviceKind, null)
    };

    public Task<IReadOnlyList<DeviceUsage>> InUse()
    {
        var usages = new Dictionary<string, DeviceUsage>(StringComparer.OrdinalIgnoreCase);

        // Per-user apps record under HKCU; services and machine-wide apps record under HKLM.
        foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            try
            {
                using var device = root.OpenSubKey(_devicePath);
                if (device is null)
                {
                    continue;
                }

                Collect(device, usages);

                using var nonPackaged = device.OpenSubKey(NonPackagedKeyName);
                if (nonPackaged is not null)
                {
                    Collect(nonPackaged, usages);
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
            {
                logger.LogDebug(ex, "Consent store for {Device} under {Root} is not readable", kind, root.Name);
            }
        }

        return Task.FromResult<IReadOnlyList<DeviceUsage>>(usages.Values.OrderBy(usage => usage.DisplayName).ToArray());
    }

    private void Collect(RegistryKey parent, Dictionary<string, DeviceUsage> usages)
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
                usages[app] = new DeviceUsage(kind, app, DisplayNameFor(app));
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
            {
                logger.LogDebug(ex, "Consent store entry {App} is not readable", subKeyName);
            }
        }
    }

    /// <summary>
    /// Turns a consent store key into something worth reading in a tooltip: the executable's name
    /// for a desktop app, the package name without its publisher hash for a packaged one.
    /// </summary>
    public static string DisplayNameFor(string appKey)
    {
        if (appKey.Contains('\\'))
        {
            var fileName = Path.GetFileNameWithoutExtension(appKey);
            return string.IsNullOrWhiteSpace(fileName) ? appKey : fileName;
        }

        var underscore = appKey.LastIndexOf('_');
        return underscore > 0 ? appKey[..underscore] : appKey;
    }

    /// <summary>
    /// A recorded start with no matching stop means the app is holding the device right now.
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
