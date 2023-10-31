namespace CameraLight;

public class MockLightService(ILogger<MockLightService> logger) : IIndicatorLightService
{
    public Task TurnOn()
    {
        logger.LogInformation("TurnOn");
        return Task.CompletedTask;
    }

    public Task TurnOff()
    {
        logger.LogInformation("TurnOff");
        return Task.CompletedTask;
    }
}