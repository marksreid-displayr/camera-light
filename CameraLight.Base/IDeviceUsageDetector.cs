namespace CameraLight.Base;

public interface IDeviceUsageDetector
{
    DeviceKind Kind { get; }

    Task<IReadOnlyList<DeviceUsage>> InUse();
}
