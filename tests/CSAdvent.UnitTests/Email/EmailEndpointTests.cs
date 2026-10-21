using CSAdvent.Web.Email;

namespace CSAdvent.UnitTests.Email;

public sealed class EmailEndpointTests
{
    [Fact]
    public void MailPitIsSmtp()
    {
        var endpoint = EmailEndpoint.Parse("Endpoint=smtp://localhost:1025");

        Assert.Equal(EmailTransport.Smtp, endpoint.Transport);
        Assert.Equal(("localhost", 1025), (endpoint.Uri.Host, endpoint.Uri.Port));
        Assert.Null(endpoint.Credentials);
    }

    [Fact]
    public void SmtpCanCarryCredentials()
    {
        var endpoint = EmailEndpoint.Parse("Endpoint=smtps://smtp.example.com:465;Username=advent;Password=secret");

        Assert.Equal(("advent", "secret"), (endpoint.Credentials!.UserName, endpoint.Credentials.Password));
    }

    [Fact]
    public void AzureCommunicationServicesConnectionStringsUseTheSdk()
    {
        const string connectionString = "endpoint=https://csadvent.unitedstates.communication.azure.com/;accesskey=c2VjcmV0";

        var endpoint = EmailEndpoint.Parse(connectionString);

        Assert.Equal(EmailTransport.AzureCommunicationServices, endpoint.Transport);
        Assert.Equal(connectionString, endpoint.ConnectionString);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a connection string")]
    [InlineData("Endpoint=ftp://localhost:21")]
    [InlineData("Endpoint=https://csadvent.communication.azure.com/")]
    [InlineData("Host=localhost;Port=1025")]
    public void AnythingElseIsRejected(string? connectionString) =>
        Assert.False(EmailEndpoint.TryParse(connectionString, out _));
}
