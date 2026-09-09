using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace CameraLight.Base;

/// <summary>
/// Keeps the recent history in memory for the history window and appends it to a JSONL file so it
/// survives a restart. Writing is best effort: a history that cannot be written must never stop the
/// lights from being driven.
/// </summary>
public class UsageEventLog : IEventLog
{
    private const int MaxInMemory = 500;
    private const long MaxFileBytes = 2 * 1024 * 1024;

    // Names rather than numbers, and no BOM: the history is meant to be readable if anyone opens it.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly ILogger<UsageEventLog> _logger;
    private readonly string _path;
    private readonly object _gate = new();
    private readonly LinkedList<UsageEvent> _recent = new();

    public event EventHandler<UsageEvent>? Appended;

    public UsageEventLog(ILogger<UsageEventLog> logger) : this(logger, UserPaths.History)
    {
    }

    public UsageEventLog(ILogger<UsageEventLog> logger, string path)
    {
        _logger = logger;
        _path = path;
        Load();
    }

    public void Append(UsageEvent usageEvent)
    {
        lock (_gate)
        {
            _recent.AddFirst(usageEvent);
            while (_recent.Count > MaxInMemory)
            {
                _recent.RemoveLast();
            }

            Write(usageEvent);
        }

        Appended?.Invoke(this, usageEvent);
    }

    public IReadOnlyList<UsageEvent> Recent()
    {
        lock (_gate)
        {
            return _recent.ToArray();
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return;
            }

            // Only the tail is worth keeping in memory, and the file is capped at a couple of MB.
            var lines = File.ReadAllLines(_path);
            foreach (var line in lines.Reverse().Take(MaxInMemory))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var usageEvent = JsonSerializer.Deserialize<UsageEvent>(line, JsonOptions);
                if (usageEvent is not null)
                {
                    _recent.AddLast(usageEvent);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read the event history at {Path}", _path);
        }
    }

    private void Write(UsageEvent usageEvent)
    {
        try
        {
            UserPaths.EnsureDirectory();
            Roll();
            File.AppendAllText(_path, JsonSerializer.Serialize(usageEvent, JsonOptions) + Environment.NewLine,
                Utf8NoBom);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not write to the event history at {Path}", _path);
        }
    }

    private void Roll()
    {
        var file = new FileInfo(_path);
        if (!file.Exists || file.Length < MaxFileBytes)
        {
            return;
        }

        var backup = _path + ".1";
        File.Delete(backup);
        File.Move(_path, backup);
    }
}
