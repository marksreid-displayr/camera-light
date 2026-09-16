using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using DnsClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CameraLight.Base;

/// <summary>
/// Resolves host names by asking the machine's own resolver first and, when that fails or stalls,
/// each DNS server configured on the network adapters in turn itself. On the VPN the system
/// resolver asks only the VPN's servers, which know nothing of the home network, so a failure
/// there is not the end of it - the router that can answer is still listed on the physical
/// adapter.
/// </summary>
public class FallbackDnsResolver(IOptions<DnsOptions> options, ILogger<FallbackDnsResolver> logger) : IDnsResolver
{
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<IPAddress>> Resolve(string host, CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(host, out var literal))
        {
            return [literal];
        }

        if (_cache.TryGetValue(host, out var cached) && cached.Expires > DateTimeOffset.UtcNow)
        {
            return cached.Addresses;
        }

        var addresses = await ResolveWithSystem(host, cancellationToken)
                        ?? await ResolveWithAdapterServers(host, cancellationToken)
                        ?? throw new HttpRequestException($"Unable to resolve '{host}' with the system resolver or any adapter's DNS server.");

        var cacheSeconds = options.Value.CacheSeconds;
        if (cacheSeconds > 0)
        {
            _cache[host] = new CacheEntry(addresses, DateTimeOffset.UtcNow.AddSeconds(cacheSeconds));
        }

        return addresses;
    }

    private async Task<IReadOnlyList<IPAddress>?> ResolveWithSystem(string host, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(options.Value.SystemTimeoutMilliseconds));

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, timeout.Token);
            if (addresses.Length > 0)
            {
                return addresses;
            }

            logger.LogWarning("System DNS returned no addresses for {Host}", host);
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            // The caller giving up is not a DNS problem, so don't paper over it with a fallback.
            cancellationToken.ThrowIfCancellationRequested();
            logger.LogWarning(ex, "System DNS could not resolve {Host}, trying the adapters' DNS servers", host);
        }

        return null;
    }

    private async Task<IReadOnlyList<IPAddress>?> ResolveWithAdapterServers(string host, CancellationToken cancellationToken)
    {
        foreach (var serverAddress in AdapterServers())
        {
            try
            {
                var client = new LookupClient(new LookupClientOptions(serverAddress)
                {
                    Timeout = TimeSpan.FromMilliseconds(options.Value.FallbackTimeoutMilliseconds),
                    UseCache = false,
                    Retries = 0
                });

                var response = await client.QueryAsync(host, QueryType.A, cancellationToken: cancellationToken);
                var addresses = response.Answers.ARecords().Select(record => record.Address).ToArray();
                if (addresses.Length > 0)
                {
                    logger.LogInformation("Resolved {Host} via adapter DNS {Server}", host, serverAddress);
                    return addresses;
                }

                logger.LogWarning("Adapter DNS {Server} returned no A records for {Host}", serverAddress, host);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Adapter DNS {Server} could not resolve {Host}", serverAddress, host);
            }
        }

        return null;
    }

    /// <summary>
    /// The servers to try, with duplicates dropped so a server listed on three adapters is only
    /// asked once.
    /// </summary>
    private static IEnumerable<IPAddress> AdapterServers() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up)
            .SelectMany(adapter => adapter.GetIPProperties().DnsAddresses)
            .Where(address => address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
            // Windows lists placeholders on adapters that have no real server: the unspecified
            // address, and the fec0::/10 site-local defaults. Asking them only burns a timeout.
            .Where(address => !address.Equals(IPAddress.Any)
                              && !address.Equals(IPAddress.IPv6Any)
                              && !address.IsIPv6SiteLocal)
            .Distinct();

    private record CacheEntry(IReadOnlyList<IPAddress> Addresses, DateTimeOffset Expires);
}
