using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.IntegrationTests;

/// <summary>
/// A visit or click that can't be counted never costs the reader the page or the post (#196). The database refuses every
/// count for one 2023 post from December 20 on (no other test counts those), with a trigger that only exists during the test.
/// </summary>
public sealed class EntryCountingFailureTests(AppHostFixture fixture)
{
    private const string Browser = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0 Safari/537.36";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheEntryPageAndTheRedirectWorkWhenCountingFails()
    {
        await using var db = await fixture.CreateDbContextAsync();
        var slot = await db.Slots.AsNoTracking()
            .Where(s => s.SeasonYear == 2023 && s.Day >= 20 && s.PostUrl != null && s.PostTitle != null && s.AuthorName != null)
            .OrderBy(s => s.Day).ThenBy(s => s.Part)
            .FirstAsync(CancellationToken);

        var refuseCounts = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"""
            CREATE FUNCTION entry_counting_failure_test() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF NEW.slot_id = {slot.Id} THEN RAISE EXCEPTION 'Counting is down'; END IF;
                RETURN NEW;
            END $$;
            CREATE TRIGGER entry_counting_failure_test BEFORE INSERT ON entry_hits FOR EACH ROW EXECUTE FUNCTION entry_counting_failure_test();
            """);
        await db.Database.ExecuteSqlRawAsync(refuseCounts, CancellationToken);
        try
        {
            using var person = fixture.CreateBrowser();
            person.DefaultRequestHeaders.UserAgent.ParseAdd(Browser);

            var page = await person.GetPageAsync($"/2023/day/{slot.Day}/{slot.Part}?via=mastodon");
            Assert.Contains(System.Text.Encodings.Web.HtmlEncoder.Default.Encode(slot.PostTitle!), page);

            var click = await person.GetAsync($"/go/2023/{slot.Day}/{slot.Part}?via=mastodon", CancellationToken);
            Assert.Equal(HttpStatusCode.Redirect, click.StatusCode);
            Assert.Equal(slot.PostUrl, click.Headers.Location!.OriginalString);
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync(
                "DROP TRIGGER entry_counting_failure_test ON entry_hits; DROP FUNCTION entry_counting_failure_test();",
                CancellationToken.None);
        }

        Assert.False(await db.EntryHits.AnyAsync(h => h.SlotId == slot.Id, CancellationToken));
    }
}
