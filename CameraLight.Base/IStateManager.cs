namespace CameraLight.Base;

/// <summary>
/// What the lights are doing, and why. Everything the tray icon and status window need to explain
/// themselves without reaching into the state manager's internals.
/// </summary>
/// <param name="Requested">The state detection last asked for, ignoring any manual override.</param>
/// <param name="Applied">
/// The state every light is believed to be showing, or <see cref="State.Unknown"/> while they
/// disagree or a light has not answered yet.
/// </param>
/// <param name="ForcedOff">True while the user has taken manual control and held the lights off.</param>
/// <param name="FailingLights">Lights that threw on their last attempt, with the reason.</param>
/// <param name="ConsecutiveFailures">How many applies in a row have failed; drives the backoff.</param>
/// <param name="NextAttemptAt">When the failing lights will be retried.</param>
public record LightStatus(
    State Requested,
    State Applied,
    bool ForcedOff,
    IReadOnlyDictionary<string, string> FailingLights,
    int ConsecutiveFailures,
    DateTimeOffset? NextAttemptAt)
{
    public bool IsFailing => FailingLights.Count > 0;
}

public interface IStateManager
{
    void ChangeState(State newState);

    /// <summary>
    /// Holds the lights off whatever detection says, for when a light is stuck on. In memory only:
    /// a restart returns to automatic control.
    /// </summary>
    void SetForcedOff(bool forcedOff);

    LightStatus Status { get; }

    event EventHandler<LightStatus>? StatusChanged;
}
