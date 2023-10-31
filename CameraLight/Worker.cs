namespace CameraLight;

// ReSharper disable once SuggestBaseTypeForParameterInConstructor
public class Worker(ILogger<Worker> logger, IStateManager stateManager, ICameraDetectionService cameraDetectionService) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var isActive = await cameraDetectionService.IsActive();
            stateManager.ChangeState(isActive ? State.On : State.Off);
            await Task.Delay(1000, stoppingToken);
        }
    }
}