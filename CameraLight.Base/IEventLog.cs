namespace CameraLight.Base;

public interface IEventLog
{
    void Append(UsageEvent usageEvent);

    /// <summary>Newest first.</summary>
    IReadOnlyList<UsageEvent> Recent();

    event EventHandler<UsageEvent>? Appended;
}
