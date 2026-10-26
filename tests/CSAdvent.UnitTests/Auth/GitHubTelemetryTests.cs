using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using CSAdvent.Web.Auth;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace CSAdvent.UnitTests.Auth;

public sealed class GitHubTelemetryTests
{
    [Theory]
    [InlineData("https://api.github.com/users/octocat", "https://api.github.com/users/Redacted")]
    [InlineData("https://api.github.com/users/octocat/social_accounts", "https://api.github.com/users/Redacted/social_accounts")]
    [InlineData("https://github.com/CalvinAllen.png?size=*", "https://github.com/Redacted.png?size=*")]
    public void RedactsLogins(string url, string expected) => Assert.Equal(expected, GitHubTelemetry.Redact(url));

    [Theory]
    [InlineData("https://api.github.com/user")]
    [InlineData("https://api.github.com/user/emails")]
    [InlineData("https://github.com/login/oauth/access_token")]
    [InlineData("https://mastodon.social/api/v1/statuses")]
    [InlineData("not a url")]
    public void LeavesEverythingElseAlone(string url) => Assert.Null(GitHubTelemetry.Redact(url));

    [Fact]
    public async Task TheTracedRequestKeepsNoLogin()
    {
        // A real request through .NET's own HTTP tracing, to a local server standing in for GitHub's API.
        using var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        var answered = AnswerOnceAsync(server);

        var spans = new List<Activity>();
        using var tracing = Sdk.CreateTracerProviderBuilder()
            .AddHttpClientInstrumentation(GitHubTelemetry.Configure)
            .AddProcessor(new Collector(spans))
            .Build();
        using var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (_, cancellationToken) =>
            {
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                await socket.ConnectAsync((IPEndPoint)server.LocalEndpoint, cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            },
        };
        using var http = new HttpClient(handler);

        using var response = await http.GetAsync("http://api.github.com/users/telemetry-tess/social_accounts", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        await answered;

        Activity span;
        lock (spans)
        {
            span = Assert.Single(spans, s => s.GetTagItem("server.address") is "api.github.com");
        }

        Assert.Equal("http://api.github.com/users/Redacted/social_accounts", span.GetTagItem("url.full"));
    }

    private static async Task AnswerOnceAsync(TcpListener server)
    {
        using var client = await server.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
        var stream = client.GetStream();
        var request = new StringBuilder();
        var buffer = new byte[1024];
        while (!request.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
        {
            var read = await stream.ReadAsync(buffer, TestContext.Current.CancellationToken);
            if (read == 0)
            {
                break;
            }

            request.Append(Encoding.ASCII.GetString(buffer, 0, read));
        }

        await stream.WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\n[]"u8.ToArray(), TestContext.Current.CancellationToken);
    }

    private sealed class Collector(List<Activity> spans) : BaseProcessor<Activity>
    {
        public override void OnEnd(Activity data)
        {
            lock (spans)
            {
                spans.Add(data);
            }
        }
    }
}
