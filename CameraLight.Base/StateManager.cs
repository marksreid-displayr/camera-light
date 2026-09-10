using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CameraLight.Base;

public class StateManager(
    IOptionsMonitor<StateManagerOptions> options,
    IEnumerable<IIndicatorLightService> indicatorLightServices,
    IEventLog eventLog,
    ILogger<StateManager> logger) : IStateManager
{
    private const int MaxRetryDelayMilliseconds = 5 * 60 * 1000;
    private const int MaxBackoffDoublings = 6;

    private readonly IIndicatorLightService[] _lights = indicatorLightServices.ToArray();

    // What the Worker last asked for. Written by the Worker thread, read by the applier.
    private volatile State _requestedState = State.Unknown;

    // Set from the tray menu when a light is stuck on and the user wants it off now.
    private volatile bool _forcedOff;

    // What each light is believed to be showing. A light that threw is removed so it gets re-driven.
    private readonly ConcurrentDictionary<IIndicatorLightService, State> _appliedPerLight = new();

    // Why each failing light last failed, so the status window can say more than "something broke".
    private readonly ConcurrentDictionary<string, string> _failures = new();

    // Gate ensuring only one applier loop runs at a time. 0 = idle, 1 = running.
    private int _applierRunning;

    private DateTime _lastApply = DateTime.MinValue;
    private int _consecutiveFailures;
    private DateTime? _nextAttempt;

    public event EventHandler<LightStatus>? StatusChanged;

    private int DelayMilliseconds =>
        (int)(options.CurrentValue.DelayMilliseconds ?? throw new Exception("DelayMilliseconds should not be empty"));

    public LightStatus Status => new(
        _requestedState,
        AppliedState(),
        _forcedOff,
        _failures.ToDictionary(failure => failure.Key, failure => failure.Value),
        _consecutiveFailures,
        _nextAttempt is { } next ? new DateTimeOffset(next, TimeSpan.Zero).ToLocalTime() : null);

    /// <summary>
    /// Records the state the lights should be in. Never blocks: the Worker ticks once a second and
    /// must not be held up by a light that is slow or unreachable.
    /// </summary>
    public void ChangeState(State newState)
    {
        var changed = _requestedState != newState;
        _requestedState = newState;
        EnsureApplierRunning();
        if (changed)
        {
            RaiseStatusChanged();
        }
    }

    public void SetForcedOff(bool forcedOff)
    {
        var changed = _forcedOff != forcedOff;
        _forcedOff = forcedOff;

        if (forcedOff)
        {
            // The believed state can be a lie: HomeBridge accepts an off, returns 200, and the bulb
            // stays on. Forget it so the off is really sent, including when the override is already
            // engaged and the user is asking a second time because the light is still on.
            _appliedPerLight.Clear();
        }

        if (changed)
        {
            eventLog.Append(new UsageEvent(DateTimeOffset.Now,
                forcedOff ? UsageEventKind.ForcedOff : UsageEventKind.Resumed));
            logger.LogInformation("Manual override {State}", forcedOff ? "engaged" : "released");
        }

        // Don't make the user wait out a backoff or the pacing delay for a light they want off now.
        _consecutiveFailures = 0;
        _lastApply = DateTime.MinValue;
        EnsureApplierRunning();
        RaiseStatusChanged();
    }

    private State EffectiveTarget => _forcedOff ? State.Off : _requestedState;

    private State AppliedState()
    {
        if (_lights.Length == 0)
        {
            return State.Unknown;
        }

        var states = _lights
            .Select(light => _appliedPerLight.TryGetValue(light, out var applied) ? applied : State.Unknown)
            .Distinct()
            .ToArray();
        return states.Length == 1 ? states[0] : State.Unknown;
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
                // Pace changes so a camera that flaps doesn't strobe the lights, and so a failing
                // light retries on a widening interval instead of hammering every tick.
                var delay = DelayMilliseconds;
                var wait = _consecutiveFailures == 0
                    ? delay
                    : Math.Min(delay * (1 << Math.Min(_consecutiveFailures, MaxBackoffDoublings)), MaxRetryDelayMilliseconds);
                var sinceLastApply = (long)(DateTime.UtcNow - _lastApply).TotalMilliseconds;
                if (sinceLastApply < wait)
                {
                    _nextAttempt = _lastApply.AddMilliseconds(wait);
                    RaiseStatusChanged();
                    await Task.Delay((int)(wait - sinceLastApply));
                }

                // Re-read: the requested state may have flipped back while we were waiting.
                var target = EffectiveTarget;
                if (target == State.Unknown)
                {
                    return;
                }

                _lastApply = DateTime.UtcNow;
                _nextAttempt = null;
                if (await Apply(target))
                {
                    _consecutiveFailures = 0;
                }
                else
                {
                    _consecutiveFailures++;
                }
                RaiseStatusChanged();
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
        var target = EffectiveTarget;
        return target != State.Unknown
               && _lights.Any(light => !_appliedPerLight.TryGetValue(light, out var applied) || applied != target);
    }

    /// <summary>
    /// Drives every light that isn't already showing <paramref name="target"/>. Each light is
    /// isolated, so one unreachable light neither blocks nor fails the others.
    /// </summary>
    private async Task<bool> Apply(State target)
    {
        var changed = 0;

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
                _failures.TryRemove(light.Name, out _);
                Interlocked.Increment(ref changed);
                return true;
            }
            catch (Exception ex)
            {
                // Forget the last known state so the next pass re-drives this light.
                _appliedPerLight.TryRemove(light, out _);
                _failures[light.Name] = ex.Message;
                logger.LogError(ex, "{Light} failed to apply state {State}, will retry", light.Name, target);
                eventLog.Append(new UsageEvent(DateTimeOffset.Now, UsageEventKind.LightFailed,
                    Detail: $"{light.Name}: {ex.Message}"));
                return false;
            }
        }));

        if (changed > 0)
        {
            eventLog.Append(new UsageEvent(DateTimeOffset.Now,
                target == State.On ? UsageEventKind.LightOn : UsageEventKind.LightOff));
        }

        return results.All(applied => applied);
    }

    private void RaiseStatusChanged() => StatusChanged?.Invoke(this, Status);
}
