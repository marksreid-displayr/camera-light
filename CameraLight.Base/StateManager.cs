using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CameraLight.Base;

public class StateManager(IOptions<StateManagerOptions> options, IEnumerable<IIndicatorLightService> indicatorLightServices, ILogger<StateManager> logger) : IStateManager
{
    private const int MaxRetryDelayMilliseconds = 5 * 60 * 1000;
    private const int MaxBackoffDoublings = 6;

    private readonly IIndicatorLightService[] _lights = indicatorLightServices.ToArray();
    private readonly int _delayMilliseconds = (int)(options.Value.DelayMilliseconds ?? throw new Exception("DelayMilliseconds should not be empty"));

    // What the Worker last asked for. Written by the Worker thread, read by the applier.
    private volatile State _requestedState = State.Unknown;

    // What each light is believed to be showing. A light that threw is removed so it gets re-driven.
    private readonly ConcurrentDictionary<IIndicatorLightService, State> _appliedPerLight = new();

    // Gate ensuring only one applier loop runs at a time. 0 = idle, 1 = running.
    private int _applierRunning;

    private DateTime _lastApply = DateTime.MinValue;
    private int _consecutiveFailures;

    /// <summary>
    /// Records the state the lights should be in. Never blocks: the Worker ticks once a second and
    /// must not be held up by a light that is slow or unreachable.
    /// </summary>
    public void ChangeState(State newState)
    {
        _requestedState = newState;
        EnsureApplierRunning();
    }

    private void EnsureApplierRunning()
    {
        if (Interlocked.CompareExchange(ref _applierRunning, 1, 0) != 0)
        {
            return;
        }
        _ = Task.Run(ApplyLoop);
    }

    private async Task ApplyLoop()
    {
        try
        {
            while (HasWorkToDo())
            {
                // Pace changes so a window title that flaps doesn't strobe the lights, and so a
                // failing light retries on a widening interval instead of hammering every tick.
                var wait = _consecutiveFailures == 0
                    ? _delayMilliseconds
                    : Math.Min(_delayMilliseconds * (1 << Math.Min(_consecutiveFailures, MaxBackoffDoublings)), MaxRetryDelayMilliseconds);
                var sinceLastApply = (long)(DateTime.UtcNow - _lastApply).TotalMilliseconds;
                if (sinceLastApply < wait)
                {
                    await Task.Delay((int)(wait - sinceLastApply));
                }

                // Re-read: the requested state may have flipped back while we were waiting.
                var target = _requestedState;
                if (target == State.Unknown)
                {
                    return;
                }

                _lastApply = DateTime.UtcNow;
                if (await Apply(target))
                {
                    _consecutiveFailures = 0;
                }
                else
                {
                    _consecutiveFailures++;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Indicator light applier loop failed unexpectedly");
        }
        finally
        {
            Interlocked.Exchange(ref _applierRunning, 0);
            // A ChangeState that landed while the gate was held would otherwise be dropped.
            if (HasWorkToDo())
            {
                EnsureApplierRunning();
            }
        }
    }

    private bool HasWorkToDo()
    {
        var target = _requestedState;
        return target != State.Unknown
               && _lights.Any(light => !_appliedPerLight.TryGetValue(light, out var applied) || applied != target);
    }

    /// <summary>
    /// Drives every light that isn't already showing <paramref name="target"/>. Each light is
    /// isolated, so one unreachable light neither blocks nor fails the others.
    /// </summary>
    private async Task<bool> Apply(State target)
    {
        var results = await Task.WhenAll(_lights.Select(async light =>
        {
            if (_appliedPerLight.TryGetValue(light, out var applied) && applied == target)
            {
                return true;
            }

            try
            {
                switch (target)
                {
                    case State.On:
                        await light.TurnOn();
                        break;
                    case State.Off:
                        await light.TurnOff();
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(target), target, null);
                }
                _appliedPerLight[light] = target;
                return true;
            }
            catch (Exception ex)
            {
                // Forget the last known state so the next pass re-drives this light.
                _appliedPerLight.TryRemove(light, out _);
                logger.LogError(ex, "{Light} failed to apply state {State}, will retry", light.GetType().Name, target);
                return false;
            }
        }));

        return results.All(applied => applied);
    }
}
