using System.Net;

namespace CameraLight.Base;

public interface IDnsResolver
{
    Task<IReadOnlyList<IPAddress>> Resolve(string host, CancellationToken cancellationToken);
}
