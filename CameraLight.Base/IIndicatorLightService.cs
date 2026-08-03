namespace CameraLight.Base;

public interface IIndicatorLightService
{
    Task TurnOn();
    Task TurnOff();
}