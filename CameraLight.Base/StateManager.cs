using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CameraLight.Base;

public class StateManager : IStateManager
{
    private const int MaxRetryDelayMilliseconds = 5 * 60 * 1000;
    private const int MaxBackoffDoublings = 6;

    private readonly IOptionsMonitor<StateManagerOptions> _options;
    private readonly IEventLog _eventLog;
    private readonly ILogger<StateManager> _logger;

    // One driver per light, each with its own loop and backoff, so a light that is failing or slow
    // never holds up the others.
    private readonly LightDriver[] _drivers;

    // What the Worker last asked for. Written by the Worker thread, read by the drivers.
    private volatile State _requestedState = State.Unknown;

    // Set from the tray menu when a light is stuck on and the user wants it off now.
    private volatile bool _forcedOff;

    public StateManager(
        IOptionsMonitor<StateManagerOptions> options,
        IEnumerable<IIndicatorLightService> indicatorLightServices,
        IEventLog eventLog,
        ILogger<StateManager> logger)
    {
        _options = options;
        _eventLog = eventLog;
        _logger = logger;
        _drivers = indicatorLightServices.Select(light => new LightDriver(this, light)).ToArray();
    }

    public event EventHandler<LightStatus>? StatusChanged;

    private int DelayMilliseconds =>
        (int)(_options.CurrentValue.DelayMilliseconds ?? throw new Exception("DelayMilliseconds should not be empty"));

    public LightStatus Status
    {
        get
        {
            var failing = _drivers.Where(driver => driver.Failure is not null).ToArray();
            var nextAttempt = failing
                .Select(driver => driver.NextAttempt)
                .Where(next => next is not null)
                .Min();
            return new LightStatus(
                _requestedState,
                AppliedState(),
                _forcedOff,
                failing.ToDictionary(driver => driver.Light.Name, driver => driver.Failure!),
                failing.Length == 0 ? 0 : failing.Max(driver => driver.ConsecutiveFailures),
                nextAttempt is { } next ? new DateTimeOffset(next, TimeSpan.Zero).ToLocalTime() : null);
        }
    }

    /// <summary>
    /// Records the state the lights should be in. Never blocks: the Worker ticks once a second and
    /// must not be held up by a light that is slow or unreachable.
    /// </summary>
    public void ChangeState(State newState)
    {
        var changed = _requestedState != newState;
        _requestedState = newState;
        foreach (var driver in _drivers)
        {
            driver.Kick(wake: changed);
        }
        if (changed)
        {
            RaiseStatusChanged();
        }
    }

    public void SetForcedOff(bool forcedOff)
    {
        var changed = _forcedOff != forcedOff;
        _forcedOff = forcedOff;

        if (changed)
        {
            _eventLog.Append(new UsageEvent(DateTimeOffset.Now,
                forcedOff ? UsageEventKind.ForcedOff : UsageEventKind.Resumed));
            _logger.LogInformation("Manual override {State}", forcedOff ? "engaged" : "released");
        }

        foreach (var driver in _drivers)
        {
            // The believed state can be a lie: HomeBridge accepts an off, returns 200, and the bulb
            // stays on. Forget it so the off is really sent, including when the override is already
            // engaged and the user is asking a second time because the light is still on.
            driver.ResetForManualOverride(forgetApplied: forcedOff);
        }
        RaiseStatusChanged();
    }

    private State EffectiveTarget => _forcedOff ? State.Off : _requestedState;

    private State AppliedState()
    {
        if (_drivers.Length == 0)
        {
            return State.Unknown;
        }

        var states = _drivers.Select(driver => driver.Applied).Distinct().ToArray();
        return states.Length == 1 ? states[0] : State.Unknown;
    }

    private void RaiseStatusChanged() => StatusChanged?.Invoke(this, Status);

    private sealed class LightDriver(StateManager owner, IIndicatorLightService light)
    {
        public IIndicatorLightService Light => light;

        // What this light is believed to be showing. Unknown until it answers, and again after it throws
        // so the next pass re-drives it.
        private volatile State _applied = State.Unknown;
        public State Applied => _applied;

        public volatile string? Failure;
        public int ConsecutiveFailures;
        public DateTime? NextAttempt;

        private DateTime _lastApply = DateTime.MinValue;

        // Gate ensuring only one loop runs for this light at a time. 0 = idle, 1 = running.
        private int _running;

        // Cuts a pacing or backoff wait short when the target changes or the user forces the light off,
        // so a new request is never stuck behind a retry scheduled for the old one.
        private readonly SemaphoreSlim _wake = new(0, 1);

        private bool HasWorkToDo()
        {
            var target = owner.EffectiveTarget;
            return target != State.Unknown && _applied != target;
        }

        public void Kick(bool wake)
        {
            if (wake)
            {
                Wake();
            }
            EnsureRunning();
        }

        public void ResetForManualOverride(bool forgetApplied)
        {
            if (forgetApplied)
            {
                _applied = State.Unknown;
            }
            // Don't make the user wait out a backoff or the pacing delay for a light they want off now.
            ConsecutiveFailures = 0;
            _lastApply = DateTime.MinValue;
            Wake();
            EnsureRunning();
        }

        private void Wake()
        {
            try
            {
                _wake.Release();
            }
            catch (SemaphoreFullException)
            {
                // Already signalled; the loop will see it.
            }
        }

        private void EnsureRunning()
        {
            if (!HasWorkToDo() || Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            {
                return;
            }
            _ = Task.Run(Loop);
        }

        private async Task Loop()
        {
            try
            {
                while (HasWorkToDo())
                {
                    // Pace changes so a camera that flaps doesn't strobe the light, and so a failing
                    // light retries on a widening interval instead of hammering every tick.
                    var delay = owner.DelayMilliseconds;
                    var wait = ConsecutiveFailures == 0
                        ? delay
                        : Math.Min(delay * (1 << Math.Min(ConsecutiveFailures, MaxBackoffDoublings)), MaxRetryDelayMilliseconds);
                    var sinceLastApply = (long)(DateTime.UtcNow - _lastApply).TotalMilliseconds;
                    if (sinceLastApply < wait)
                    {
                        NextAttempt = _lastApply.AddMilliseconds(wait);
                        owner.RaiseStatusChanged();
                        if (await _wake.WaitAsync((int)(wait - sinceLastApply)))
                        {
                            // Something changed: work out the wait again from scratch.
                            continue;
                        }
                    }

                    // Re-read: the requested state may have flipped back while we were waiting.
                    var target = owner.EffectiveTarget;
                    if (target == State.Unknown)
                    {
                        return;
                    }

                    _lastApply = DateTime.UtcNow;
                    NextAttempt = null;
                    if (await Apply(target))
                    {
                        ConsecutiveFailures = 0;
                    }
                    else
                    {
                        ConsecutiveFailures++;
                    }
                    owner.RaiseStatusChanged();
                }
            }
            catch (Exception ex)
            {
                owner._logger.LogError(ex, "{Light} applier loop failed unexpectedly", light.Name);
            }
            finally
            {
                Interlocked.Exchange(ref _running, 0);
                // A request that landed while the gate was held would otherwise be dropped.
                EnsureRunning();
            }
        }

        private async Task<bool> Apply(State target)
        {
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
                _applied = target;
                Failure = null;
                owner._eventLog.Append(new UsageEvent(DateTimeOffset.Now,
                    target == State.On ? UsageEventKind.LightOn : UsageEventKind.LightOff,
                    Detail: light.Name));
                return true;
            }
            catch (Exception ex)
            {
                // Forget the last known state so the next pass re-drives this light.
                _applied = State.Unknown;
                Failure = ex.Message;
                owner._logger.LogError(ex, "{Light} failed to apply state {State}, will retry", light.Name, target);
                owner._eventLog.Append(new UsageEvent(DateTimeOffset.Now, UsageEventKind.LightFailed,
                    Detail: $"{light.Name}: {ex.Message}"));
                return false;
            }
        }
    }
}
