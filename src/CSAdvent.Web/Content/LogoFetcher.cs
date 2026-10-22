namespace CSAdvent.Web.Content;

/// <summary>
/// Finds a site's logo: reads its home page for the icons it declares, then downloads candidates until one is big enough.
/// Its <see cref="HttpClient"/> only connects to public addresses (<see cref="PublicAddresses"/>).
/// </summary>
public sealed class LogoFetcher(HttpClient http, ILogger<LogoFetcher> logger)
{
    /// <summary>Icons are declared in the page's head, so there's no need to read a whole long page.</summary>
    private const int MaxPageBytes = 512 * 1024;

    private const int MaxImageBytes = 1024 * 1024;

    /// <summary>An icon this big looks sharp on the page, so there's no need to try the rest.</summary>
    private const int GoodEnough = 96;

    /// <summary>A client that only connects to public addresses and gives up on a slow site after 10 seconds.</summary>
    public static HttpClient CreateClient()
    {
        var http = new HttpClient(PublicAddresses.CreateHandler()) { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("CSAdvent/1.0 (+https://csadvent.christmas/others)");
        return http;
    }

    /// <summary>The site's logo as a PNG (<see cref="LogoImages"/>), or null if none of the candidates is an image.</summary>
    public async Task<byte[]?> FetchAsync(Uri site, CancellationToken cancellationToken = default)
    {
        var (html, page) = await GetPageAsync(site, cancellationToken);

        byte[]? best = null;
        var bestSize = 0;
        foreach (var candidate in LogoFinder.FindCandidates(html, page))
        {
            if (await GetAsync(candidate, MaxImageBytes, cancellationToken) is not { } image)
            {
                continue;
            }

            if (LogoImages.ToPng(image.Body, out var size) is { } png && size > bestSize)
            {
                (best, bestSize) = (png, size);
                if (size >= GoodEnough)
                {
                    break;
                }
            }
        }

        return best;
    }

    private async Task<(string Html, Uri Page)> GetPageAsync(Uri site, CancellationToken cancellationToken)
    {
        // Without the page, the fallbacks (/apple-touch-icon.png and /favicon.ico) are still worth trying.
        if (await GetAsync(site, MaxPageBytes, cancellationToken, truncate: true) is not { } page)
        {
            return ("", site);
        }

        return (System.Text.Encoding.UTF8.GetString(page.Body), page.FinalUri);
    }

    /// <summary>The body, or null if the request fails or the body is over <paramref name="limit"/> bytes (unless truncating).</summary>
    private async Task<(byte[] Body, Uri FinalUri)?> GetAsync(Uri uri, int limit, CancellationToken cancellationToken, bool truncate = false)
    {
        try
        {
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var body = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                if (body.Length + read > limit)
                {
                    if (!truncate)
                    {
                        return null;
                    }

                    body.Write(buffer, 0, (int)(limit - body.Length));
                    break;
                }

                body.Write(buffer, 0, read);
            }

            return (body.ToArray(), response.RequestMessage?.RequestUri ?? uri);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug(exception, "Couldn't get {Uri} while looking for a logo.", uri);
            return null;
        }
    }
}
