using CSAdvent.Web.Claims;
using CSAdvent.Web.Content;
using CSAdvent.Web.Retrospectives;

namespace CSAdvent.UnitTests.Retrospectives;

public sealed class RetrospectiveDraftTests
{
    internal static RetrospectiveStats Stats(int year = 2026, bool traffic = true) => new(
        year,
        SlotsPerDay: 2,
        Spots: 50,
        Posts: 48,
        Authors: 45,
        FirstTimeAuthors: 12,
        AuthorsBySeasonsWritten: [new(1, 12), new(2, 20), new(3, 10), new(9, 3)],
        BackupsSteppedIn: traffic ? 2 : 0,
        Requests: traffic ? 130 : 0,
        RequestsFirstDay: traffic ? 61 : 0,
        Views: traffic ? 4321 : 0,
        Clicks: traffic ? 12345 : 0,
        ClicksPerDay: traffic ? [.. Enumerable.Range(1, 31).Select(day => day == 3 ? 1500 : 300)] : [],
        ClicksBySource: traffic ? [new("calendar", 6000), new("bluesky", 3000), new("email", 2000), new("rss", 1345)] : [],
        MostRead: traffic
            ? [new(3, 1, "Span<T> and *you*", "Ada_Lovelace", 900), new(5, 2, "Records [deep dive]", "Grace Hopper", 1)]
            : [],
        SocialPosts: traffic ? [new(SocialPlatform.Bluesky, 48), new(SocialPlatform.Mastodon, 47)] : [],
        History: [new(2017, 25, 24, 24), new(2025, 50, 46, 20), new(year, 48, 45, 12)]);

    [Fact]
    public void TheDraftIsAWholePostWithTheSeasonsNumbers()
    {
        var body = RetrospectiveDraft.Body(Stats());

        Assert.Equal("C# Advent 2026: a look back", RetrospectiveDraft.Title(2026));
        Assert.StartsWith("C# Advent 2026 is a wrap! Thank you to the 45 authors who wrote this season's 48 posts", body);
        Assert.Contains("- **48 posts** by **45 authors**, two a day from December 1 to 25.", body);
        Assert.Contains("- **12 first-time authors** and **33 returning**, who'd written for C# Advent before.", body);
        Assert.Contains("3 of this season's authors have now written for C# Advent in 9 different seasons.", body);
        Assert.Contains("Backup posters stepped in **2 times**", body);
        Assert.Contains("130 requests for spots, 61 of them in the first 24 hours", body);
        Assert.Contains("Readers clicked through to the posts **12,345 times** in December, and opened their pages on our site 4,321 times.", body);
        Assert.Contains("The busiest day was December 3, with 1,500 clicks.", body);
        Assert.Contains("Most clicks came from the calendar, Bluesky and the daily email.", body);
        Assert.Contains("We announced the posts on social media: 48 on Bluesky and 47 on Mastodon.", body);
        Assert.Contains("That brings C# Advent to **123 posts** by **56 different authors** since the first season in 2017.", body);
        Assert.EndsWith("See you next December!\n", body);
    }

    [Fact]
    public void TheMostReadPostsLinkToTheirPagesWithTheirTitlesShownAsTyped()
    {
        var body = RetrospectiveDraft.Body(Stats());

        Assert.Contains(@"1. [Span\<T\> and \*you\*](2026/day/3/1?via=retrospective) by Ada\_Lovelace (900 clicks)", body);
        Assert.Contains(@"2. [Records \[deep dive\]](2026/day/5/2?via=retrospective) by Grace Hopper (1 click)", body);

        var html = Markdown.ToHtml(body).Value;
        Assert.Contains("<a href=\"2026/day/3/1?via=retrospective\">Span&lt;T&gt; and *you*</a> by Ada_Lovelace", html);
        Assert.Contains(">Records [deep dive]</a>", html);
    }

    [Fact]
    public void SeasonsWithoutTrafficLeaveOutWhatIsntKnown()
    {
        var body = RetrospectiveDraft.Body(Stats(2019, traffic: false));

        Assert.Contains("## The season", body);
        Assert.Contains("first-time authors", body);
        Assert.DoesNotContain("## Readers", body);
        Assert.DoesNotContain("most-read", body);
        Assert.DoesNotContain("social media", body);
        Assert.DoesNotContain("Backup posters", body);
        Assert.DoesNotContain("requests", body);
    }

    [Fact]
    public void TheFirstSeasonIsTheFirst()
    {
        var stats = Stats(2017, traffic: false) with { SlotsPerDay = 1, FirstTimeAuthors = 45, History = [new(2017, 48, 45, 45)], AuthorsBySeasonsWritten = [new(1, 45)] };

        var body = RetrospectiveDraft.Body(stats);

        Assert.Contains("one a day from December 1 to 25", body);
        Assert.Contains("This was the very first C# Advent.", body);
        Assert.DoesNotContain("returning", body);
        Assert.DoesNotContain("different seasons", body);
    }

    [Fact]
    public void TheSummaryIsForLinkPreviewsAndTheFeed()
    {
        Assert.Equal("A look back at C# Advent 2026: 48 posts by 45 authors, and 12,345 clicks through to them.", RetrospectiveDraft.Summary(Stats()));
        Assert.Equal("A look back at C# Advent 2019: 48 posts by 45 authors.", RetrospectiveDraft.Summary(Stats(2019, traffic: false)));
    }
}
