using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CameraLight.Base;

public class StateManager(IOptions<StateManagerOptions> options, IEnumerable<IIndicatorLightService> indicatorLightService, ILogger<StateManager> logger) : IStateManager
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
                await _lock.WaitAsync(_cts.Token);
                try
                {
                    switch (newState)
                    {
                        case State.On:
                            await Task.WhenAll(indicatorLightService.Select(i => i.TurnOn()));
                            _currentState = State.On;
                            break;
                        case State.Off:
                            await Task.WhenAll(indicatorLightService.Select(i => i.TurnOff()));
                            _currentState = State.Off;
                            break;
                        case State.Unknown:
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(newState), newState, null);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Leave _currentState alone so the next ChangeState re-drives the lights.
                    logger.LogError(ex, "Failed to apply state {State}, will retry", newState);
                }
                finally
                {
                    // Clearing _desiredState lets an identical ChangeState get through again,
                    // and stamping the time paces the retry at DelayMilliseconds.
                    _desiredState = State.Unknown;
                    _lastStateChange = DateTime.UtcNow;
                    _lock.Release();
                }

            }
            catch (OperationCanceledException)
            {
                // Task was canceled, do nothing
            }
        });
    }
} 