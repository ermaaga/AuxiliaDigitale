using System.Net;
using System.Net.Sockets;

namespace Auxilia.Application.Mailbox;

/// <summary>
/// Addresses a user-entered server may resolve to (N05, SSRF): public unicast only. Refused: unspecified, loopback,
/// private (RFC 1918, IPv6 unique local), link-local (cloud metadata 169.254.169.254 included), carrier-grade NAT,
/// benchmarking, multicast, reserved and documentation ranges, IPv4-mapped/translated forms of those.
/// </summary>
internal static class OutboundAddressPolicy
{
    private static readonly (IPAddress Network, int Prefix)[] RefusedV4 =
    [
        (IPAddress.Parse("0.0.0.0"), 8),
        (IPAddress.Parse("10.0.0.0"), 8),
        (IPAddress.Parse("100.64.0.0"), 10),
        (IPAddress.Parse("127.0.0.0"), 8),
        (IPAddress.Parse("169.254.0.0"), 16),
        (IPAddress.Parse("172.16.0.0"), 12),
        (IPAddress.Parse("192.0.0.0"), 24),
        (IPAddress.Parse("192.0.2.0"), 24),
        (IPAddress.Parse("192.88.99.0"), 24),
        (IPAddress.Parse("192.168.0.0"), 16),
        (IPAddress.Parse("198.18.0.0"), 15),
        (IPAddress.Parse("198.51.100.0"), 24),
        (IPAddress.Parse("203.0.113.0"), 24),
        (IPAddress.Parse("224.0.0.0"), 4),
        (IPAddress.Parse("240.0.0.0"), 4),
    ];

    private static readonly (IPAddress Network, int Prefix)[] RefusedV6 =
    [
        (IPAddress.Parse("::"), 96),
        (IPAddress.Parse("64:ff9b::"), 96),
        (IPAddress.Parse("64:ff9b:1::"), 48),
        (IPAddress.Parse("100::"), 64),
        (IPAddress.Parse("2001::"), 23),
        (IPAddress.Parse("2001:db8::"), 32),
        (IPAddress.Parse("2002::"), 16),
        (IPAddress.Parse("fc00::"), 7),
        (IPAddress.Parse("fe80::"), 10),
        (IPAddress.Parse("fec0::"), 10),
        (IPAddress.Parse("ff00::"), 8),
    ];

    public static bool IsAllowed(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => !RefusedV4.Any(range => InRange(address, range.Network, range.Prefix)),
            AddressFamily.InterNetworkV6 => !IPAddress.IPv6Loopback.Equals(address) && !RefusedV6.Any(range => InRange(address, range.Network, range.Prefix)),
            _ => false,
        };
    }

    private static bool InRange(IPAddress address, IPAddress network, int prefix)
    {
        if (address.AddressFamily != network.AddressFamily)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        var networkBytes = network.GetAddressBytes();
        var fullBytes = prefix / 8;
        for (var index = 0; index < fullBytes; index++)
        {
            if (bytes[index] != networkBytes[index])
            {
                return false;
            }
        }

        var remainingBits = prefix % 8;
        if (remainingBits == 0)
        {
            return true;
        }

        var mask = (byte)(0xFF << (8 - remainingBits));
        return (bytes[fullBytes] & mask) == (networkBytes[fullBytes] & mask);
    }
}
