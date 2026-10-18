using System.Net;
using System.Net.Sockets;

using Auxilia.Application.Abstractions.Mailbox;

namespace Auxilia.Infrastructure.Adapters.Network;

/// <summary>Every address of a host name, so the caller can refuse it when any of them is not public (SSRF).</summary>
internal sealed class DnsHostAddressResolver : IHostAddressResolver
{
    public async Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(host);

        try
        {
            return await Dns.GetHostAddressesAsync(host, cancellationToken);
        }
        catch (SocketException)
        {
            return [];
        }
    }
}
