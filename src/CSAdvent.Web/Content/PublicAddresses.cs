using System.Net;
using System.Net.Sockets;

namespace CSAdvent.Web.Content;

/// <summary>
/// Keeps the logo fetcher and the blurb check's post fetcher on the public internet. Organizers and authors type the addresses
/// they visit, and a redirect can point anywhere, so every connection is checked, not just the first URL: nothing on the
/// container's network, localhost, the cloud metadata endpoint (169.254.169.254) or Azure's host (168.63.129.16), and no
/// IPv6 address that carries an IPv4 one inside it (#192).
/// </summary>
public static class PublicAddresses
{
    /// <summary>
    /// A handler that only connects to public addresses, follows a few redirects, and never sends cookies. It never uses a
    /// proxy (#192): through one, the address checked would be the proxy's, and the proxy would reach the site for us.
    /// </summary>
    public static SocketsHttpHandler CreateHandler() => new()
    {
        UseProxy = false,
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 5,
        // The client lives as long as the app, so pick up DNS changes now and then.
        PooledConnectionLifetime = TimeSpan.FromMinutes(15),
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.All,
        ConnectCallback = async (context, cancellationToken) =>
        {
            var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
            var address = addresses.FirstOrDefault(IsPublic)
                ?? throw new HttpRequestException($"{context.DnsEndPoint.Host} isn't a public address, so it isn't fetched.");

            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        },
    };

    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.Broadcast))
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return !(b[0] == 0                                  // "this network"
                || b[0] == 10                                   // private
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)   // carrier-grade NAT
                || (b[0] == 169 && b[1] == 254)                 // link-local, including cloud metadata
                || (b[0] == 168 && b[1] == 63 && b[2] == 129 && b[3] == 16) // Azure's host (DHCP, DNS, health probes)
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)    // private
                || (b[0] == 192 && b[1] == 168)                 // private
                || (b[0] == 192 && b[1] == 0 && b[2] == 0)      // IETF protocol assignments
                || (b[0] == 198 && (b[1] == 18 || b[1] == 19))  // benchmarking
                || b[0] >= 224);                                // multicast and reserved
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b = address.GetAddressBytes();
            return !(address.IsIPv6LinkLocal
                || address.IsIPv6SiteLocal
                || address.IsIPv6Multicast
                || (b[0] & 0xFE) == 0xFC                        // unique local (fc00::/7)
                || b.AsSpan(0, 12).IndexOfAnyExcept((byte)0) < 0 // IPv4-compatible (::/96), an IPv4 address inside
                || StartsWith(b, 0x00, 0x64, 0xFF, 0x9B, 0, 0, 0, 0, 0, 0, 0, 0) // NAT64 (64:ff9b::/96), likewise
                || StartsWith(b, 0x00, 0x64, 0xFF, 0x9B, 0x00, 0x01) // local-use NAT64 (64:ff9b:1::/48)
                || StartsWith(b, 0x20, 0x02));                  // 6to4 (2002::/16), likewise
        }

        return false;
    }

    private static bool StartsWith(byte[] address, params ReadOnlySpan<byte> prefix) => address.AsSpan().StartsWith(prefix);
}
