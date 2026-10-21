using CSAdvent.Web.Content;
using CSAdvent.Web.Email;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static CSAdvent.Web.Email.EmailBlocks;

namespace CSAdvent.UnitTests.Email;

public sealed class EmailComposerTests
{
    private static readonly SiteOptions Site = new() { ContactEmail = "hello@csadvent.christmas", BaseUrl = "https://csadvent.christmas" };

    private static readonly EmailContent Content = new()
    {
        Subject = "Line one\r\nBcc: someone@example.com",
        Preview = "A preview line",
        Heading = "Hi <b>Ada</b> & friends",
        Body =
        [
            Paragraph("Read the ", Link("terms", "https://csadvent.christmas/terms"), ", then ", Link("https://example.com", "https://example.com"), "."),
            List(["First"], ["Second"]),
            Button("Review the request", "https://csadvent.christmas/manage/requests/42"),
            Details(new DetailRow("Name", "Ada <script>")),
            Paragraph("Thanks,", EmailSpan.LineBreak, "The organizers"),
        ],
        Reason = "You're getting this because of a test.",
    };

    private static EmailComposer CreateComposer() =>
        new(
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLoggerFactory.Instance,
            Options.Create(Site));

    [Fact]
    public async Task TheHtmlIsBrandedAndEncoded()
    {
        var html = await CreateComposer().RenderHtmlAsync(Content);

        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("src=\"https://csadvent.christmas/images/csadvent-logo.png\"", html);
        Assert.Contains("background=\"https://csadvent.christmas/images/email-header.jpg\"", html);
        Assert.Contains("A preview line", html);
        Assert.Contains("Hi &lt;b&gt;Ada&lt;/b&gt; &amp; friends", html);
        Assert.Contains("Ada &lt;script&gt;", html);
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("href=\"https://csadvent.christmas/manage/requests/42\"", html);
        Assert.Contains(">Review the request</a>", html);
        Assert.Contains("<li style=\"margin:0 0 8px;\">Second</li>", html);
        Assert.Contains("Thanks,<br>The organizers", html);
        Assert.Contains("href=\"https://csadvent.christmas/privacy\"", html);
        Assert.Contains("You&#x27;re getting this because of a test.", html);
    }

    [Fact]
    public void ThePlainTextAlternativeSaysTheSame()
    {
        var text = CreateComposer().RenderText(Content);

        Assert.StartsWith("Hi <b>Ada</b> & friends\n\n", text);
        Assert.Contains("Read the terms (https://csadvent.christmas/terms), then https://example.com.", text);
        Assert.Contains("- First\n- Second\n", text);
        Assert.Contains("Review the request: https://csadvent.christmas/manage/requests/42", text);
        Assert.Contains("Name: Ada <script>", text);
        Assert.Contains("Thanks,\nThe organizers", text);
        Assert.Contains("Privacy: https://csadvent.christmas/privacy", text);
        Assert.EndsWith("You're getting this because of a test.\n", text);
    }

    [Fact]
    public async Task RepliesGoToTheOrganizersUnlessSaidOtherwise()
    {
        var composer = CreateComposer();
        var to = new EmailRecipient("ada@example.com", "Ada");

        Assert.Equal("hello@csadvent.christmas", (await composer.ComposeAsync(Content, to)).ReplyTo);
        Assert.Equal("grace@example.com", (await composer.ComposeAsync(Content, to, replyTo: "grace@example.com")).ReplyTo);
    }

    [Fact]
    public async Task SubjectsAreASingleLine()
    {
        var email = await CreateComposer().ComposeAsync(Content, new EmailRecipient("ada@example.com"));

        Assert.Equal("Line oneBcc: someone@example.com", email.Subject);
    }
}
