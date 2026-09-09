namespace CameraLight.Base;

public interface IIndicatorLightService
{
    /// <summary>How the light is named in the status window and the history.</summary>
    string Name => GetType().Name;

    Task TurnOn();
    Task TurnOff();
}
