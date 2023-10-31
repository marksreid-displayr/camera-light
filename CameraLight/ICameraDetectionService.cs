namespace CameraLight;

public interface ICameraDetectionService
{
    Task<bool> IsActive();
}