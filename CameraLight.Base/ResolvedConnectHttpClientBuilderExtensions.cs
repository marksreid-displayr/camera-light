using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;

namespace CameraLight.Base;

public static class ResolvedConnectHttpClientBuilderExtensions
{
    /// <summary>
    /// Takes host name resolution out of the socket layer's hands and gives it to
    /// <see cref="IDnsResolver"/>, so a connection can still be made when the system resolver
    /// can't answer. Every address returned is tried before the connection is called a failure.
    /// </summary>
    public static IHttpClientBuilder UseFallbackDns(this IHttpClientBuilder builder) =>
        builder.ConfigurePrimaryHttpMessageHandler(serviceProvider =>
        {
            var resolver = serviceProvider.GetRequiredService<IDnsResolver>();

            return new SocketsHttpHandler
            {
                ConnectCallback = async (context, cancellationToken) =>
                {
                    var addresses = await resolver.Resolve(context.DnsEndPoint.Host, cancellationToken);

                    Exception? lastFailure = null;
                    foreach (var address in addresses)
                    {
                        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                        try
                        {
                            await socket.ConnectAsync(address, context.DnsEndPoint.Port, cancellationToken);
                            return new NetworkStream(socket, ownsSocket: true);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            socket.Dispose();
                            lastFailure = ex;
                        }
                        catch
                        {
                            socket.Dispose();
                            throw;
                        }
                    }

                    throw lastFailure ?? new SocketException((int)SocketError.HostNotFound);
                }
            };
        });
}
