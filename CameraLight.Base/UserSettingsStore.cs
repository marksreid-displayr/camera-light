using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace CameraLight.Base;

/// <summary>
/// Reads and writes the user's settings file, which is layered over the shipped appsettings.json.
/// The configuration is registered with reloadOnChange, so a write here reaches every
/// IOptionsMonitor consumer without a restart.
/// </summary>
public class UserSettingsStore(ILogger<UserSettingsStore> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly object _gate = new();

    public string Path => UserPaths.Settings;

    public void AddException(string appKey) => Update(detection =>
    {
        var ignored = ReadIgnored(detection);
        if (ignored.Any(existing => string.Equals(existing, appKey, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        ignored.Add(appKey);
        WriteIgnored(detection, ignored);
    });

    public void RemoveException(string appKey) => Update(detection =>
    {
        var ignored = ReadIgnored(detection);
        ignored.RemoveAll(existing => string.Equals(existing, appKey, StringComparison.OrdinalIgnoreCase));
        WriteIgnored(detection, ignored);
    });

    /// <summary>
    /// Writes everything the settings window owns in one pass, so the file is only reloaded once.
    /// </summary>
    public void Save(IEnumerable<string> exceptions, bool monitorMicrophone, int pollIntervalMilliseconds) =>
        Update(detection =>
        {
            WriteIgnored(detection, exceptions.Where(app => !string.IsNullOrWhiteSpace(app))
                .Select(app => app.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList());
            detection["MonitorMicrophone"] = monitorMicrophone;
            detection["PollIntervalMilliseconds"] = pollIntervalMilliseconds;
        });

    private static List<string> ReadIgnored(JsonObject detection) =>
        detection["IgnoredApps"] is JsonArray array
            ? array.Select(node => node?.GetValue<string>()).OfType<string>().ToList()
            : [];

    private static void WriteIgnored(JsonObject detection, List<string> ignored) =>
        detection["IgnoredApps"] = new JsonArray(ignored.Select(app => (JsonNode)JsonValue.Create(app)).ToArray());

    private void Update(Action<JsonObject> change)
    {
        lock (_gate)
        {
            try
            {
                UserPaths.EnsureDirectory();
                var root = Read();
                if (root["Detection"] is not JsonObject detection)
                {
                    detection = new JsonObject();
                    root["Detection"] = detection;
                }

                change(detection);
                File.WriteAllText(Path, root.ToJsonString(JsonOptions));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Could not save settings to {Path}", Path);
                throw;
            }
        }
    }

    private JsonObject Read()
    {
        if (!File.Exists(Path))
        {
            return new JsonObject();
        }

        try
        {
            return JsonNode.Parse(File.ReadAllText(Path)) as JsonObject ?? new JsonObject();
        }
        catch (JsonException ex)
        {
            // A hand-edited file that no longer parses would otherwise block every save.
            logger.LogWarning(ex, "Settings at {Path} are not valid JSON and will be rewritten", Path);
            return new JsonObject();
        }
    }
}
