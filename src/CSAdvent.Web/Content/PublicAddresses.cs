using System.Net;
using System.Net.Sockets;

namespace CSAdvent.Web.Content;

/// <summary>
/// Keeps the logo fetcher on the public internet. Organizers type the addresses it visits, and a redirect can point anywhere,
/// so every connection is checked, not just the first URL: nothing on the container's network, localhost, or the cloud
/// metadata endpoint (169.254.169.254).
/// </summary>
public static class PublicAddresses
{
    /// <summary>A handler that only connects to public addresses, follows a few redirects, and never sends cookies.</summary>
    public static SocketsHttpHandler CreateHandler() => new()
    {
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
                ?? throw new HttpRequestException($"{context.DnsEndPoint.Host} isn't a public address, so its logo isn't fetched.");

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
                || (b[0] & 0xFE) == 0xFC);                      // unique local (fc00::/7)
        }

        return false;
    }
}
