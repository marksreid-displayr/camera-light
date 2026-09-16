namespace CameraLight.Base;

public class DnsOptions
{
    /// <summary>
    /// How long to wait on the system resolver before giving up and asking the adapters' DNS
    /// servers directly. A VPN that is half up can leave a lookup hanging for far longer than the
    /// light is worth.
    /// </summary>
    public int SystemTimeoutMilliseconds { get; set; } = 2000;

    public int FallbackTimeoutMilliseconds { get; set; } = 2000;

    /// <summary>
    /// Successful lookups are held for this long so a flaky link costs one slow resolve, not one
    /// per request. Zero disables the cache.
    /// </summary>
    public int CacheSeconds { get; set; } = 300;
}
