namespace CameraLight.Base;

/// <summary>
/// The apps currently holding a monitored device, after exceptions have been applied.
/// </summary>
public interface IUsageMonitor
{
    IReadOnlyList<DeviceUsage> Current { get; }

    event EventHandler<IReadOnlyList<DeviceUsage>>? CurrentChanged;
}
