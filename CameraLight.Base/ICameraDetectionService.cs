namespace CameraLight.Base;

public interface ICameraDetectionService
{
    Task<bool> IsActive();
}