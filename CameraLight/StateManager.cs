using Microsoft.Extensions.Options;

namespace CameraLight;

public class StateManager(IOptions<StateManagerOptions> options, IIndicatorLightService indicatorLightService) : IStateManager
{
    private CancellationTokenSource? _cts;
    private volatile State _currentState = State.Unknown;
    private volatile State _desiredState = State.Unknown;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly long _delayMilliseconds = options.Value.DelayMilliseconds ?? throw new Exception("DelayMilliseconds should not be empty");
    private DateTime _lastStateChange = DateTime.MinValue;

    public void ChangeState(State newState)
    {
        try
        {
            _lock.Wait();
            if (_desiredState == newState)
            {
                return;
            }

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            if (_currentState == newState)
            {
                return;
            }

            _desiredState = newState;
        }
        finally
        {
            _lock.Release();
        }
        _cts = new CancellationTokenSource();
        Task.Run(async () =>
        {
            try
            {
                var millisecondsSinceLastChange = (long)(DateTime.UtcNow - _lastStateChange).TotalMilliseconds;
                if (millisecondsSinceLastChange < _delayMilliseconds)
                {
                    await Task.Delay((int)(_delayMilliseconds - millisecondsSinceLastChange), _cts.Token);
                }
                try
                {   
                    await _lock.WaitAsync(_cts.Token);
                    switch (newState)
                    {
                        case State.On:
                            await indicatorLightService.TurnOn();
                            _currentState = State.On;
                            break;
                        case State.Off:
                            await indicatorLightService.TurnOff();
                            _currentState = State.Off;
                            break;
                        case State.Unknown:
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(newState), newState, null);
                    }
                    _desiredState = State.Unknown;
                    _lastStateChange = DateTime.UtcNow;
                }
                finally
                {
                    _lock.Release();
                }

            }
            catch (TaskCanceledException)
            {
                // Task was canceled, do nothing
            }
        });
    }
}