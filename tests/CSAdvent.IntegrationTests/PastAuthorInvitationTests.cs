using System.Globalization;
using System.Net;
using CSAdvent.Web.Data;
using CSAdvent.Web.Email;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.IntegrationTests;

/// <summary>
/// Past authors are invited back when a season's sign-ups open (#133). Season 2141's sign-ups open on November 1, 2141;
/// the past authors are on a few late-December 2023 archive posts (no other test reads their contacts):
/// <list type="bullet">
/// <item>Ann wrote two of them, with her address in two spellings: one invitation.</item>
/// <item>Cy's post is linked to his account, with his profile name.</item>
/// <item>Sue is on the announcements list, Tia already has a 2141 spot, and Oli unsubscribed: none for them.</item>
/// </list>
/// Season 2352 checks they keep going while sign-ups are open (#210), with Lee and Cid on the last two posts of 2020.
/// </summary>
public sealed class PastAuthorInvitationTests(AppHostFixture fixture)
{
    private const int Year = 2141;
    private const string Subject = "C# Advent 2141 sign-ups are open: you wrote for us before";

    private static readonly DateTimeOffset OpensAt = new(2141, 11, 1, 5, 0, 0, TimeSpan.Zero);

    private const int LaterYear = 2352;
    private const string LaterSubject = "C# Advent 2352 sign-ups are open: you wrote for us before";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PastAuthorsAreInvitedOnceWhenSignUpsOpen()
    {
        await SetUpAsync();
        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);

        var waiting = WebUtility.HtmlDecode(await organizer.GetPageAsync($"/manage/announcements?year={Year}"));
        Assert.Contains("will be invited at 9 AM Eastern on November 1, when sign-ups open.", waiting);

        // Before the morning send time, nothing goes out.
        await RunAsync(OpensAt.AddHours(2));
        Assert.Equal(0, await fixture.CountEmailsAsync("invite-ann@example.com", Subject));

        await RunAsync(OpensAt.AddHours(10));
        var ann = await fixture.WaitForEmailAsync("invite-ann@example.com", Subject);
        Assert.Contains("you've written 2 posts for C# Advent since 2023", ann.Text);
        Assert.Contains("Unsubscribe: https://localhost:7092/emails/stop?token=", ann.Text);
        var cy = await fixture.WaitForEmailAsync("invite-cy@example.com", Subject);
        Assert.Contains("Come back for C# Advent 2141, Cy Profile!", WebUtility.HtmlDecode(cy.Html));

        // A later run sends nobody a second one, and the skipped never get one.
        await RunAsync(OpensAt.AddHours(11));
        Assert.Equal(1, await fixture.CountEmailsAsync("invite-ann@example.com", Subject));
        foreach (var skipped in new[] { "invite-sue@example.com", "invite-tia@example.com", "invite-oli@example.com" })
        {
            Assert.Equal(0, await fixture.CountEmailsAsync(skipped, Subject));
        }

        await using (var db = await fixture.CreateDbContextAsync())
        {
            var invited = await db.PastAuthorInvitations.Where(i => i.SeasonYear == Year).Select(i => i.Fingerprint).ToListAsync(CancellationToken);
            Assert.Contains(EmailOptOutLinks.Fingerprint("invite-ann@example.com"), invited);
            Assert.DoesNotContain(EmailOptOutLinks.Fingerprint("invite-sue@example.com"), invited);
        }

        Assert.Contains("Invited ", WebUtility.HtmlDecode(await organizer.GetPageAsync($"/manage/announcements?year={Year}")));
    }

    [Fact]
    public async Task InvitationsKeepGoingWhileSignUpsAreOpen()
    {
        var opensAt = new DateTimeOffset(2352, 11, 1, 5, 0, 0, TimeSpan.Zero);
        await TestSeasons.EnsureAsync(fixture, LaterYear, claimsOpen: false, season => season.ClaimsOpenAt = opensAt);
        int closedSlot;
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var posts = await db.Slots
                .Include(s => s.Contact)
                .Where(s => s.SeasonYear == 2020 && s.PostUrl != null && s.AuthorName != null)
                .OrderByDescending(s => s.Day).ThenByDescending(s => s.Part)
                .Take(2)
                .ToListAsync(CancellationToken);
            posts[0].ContributorId = null;
            posts[0].Contact = new SlotContact { Email = "invite-lee-2352@example.com" };
            closedSlot = posts[1].Id;

            // Someone invited on the first morning.
            db.PastAuthorInvitations.Add(new PastAuthorInvitation { SeasonYear = LaterYear, Fingerprint = EmailOptOutLinks.Fingerprint("invite-first-2352@example.com"), SentAt = opensAt.AddHours(5) });
            await db.SaveChangesAsync(CancellationToken);
        }

        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);
        var page = WebUtility.HtmlDecode(await organizer.GetPageAsync($"/manage/announcements?year={LaterYear}"));
        Assert.Contains("Invited 1 past author so far, with", page);
        Assert.Contains("still to go", page);

        // Three days after sign-ups opened, the ones still waiting are invited, as fast as the email queue has room for them
        // (other tests add past authors too).
        foreach (var hour in new[] { 0, 1, 2 })
        {
            await RunAsync(opensAt.AddDays(3).AddHours(hour), LaterYear);
        }

        await fixture.WaitForEmailAsync("invite-lee-2352@example.com", LaterSubject);

        // Once the last day is revealed, sign-ups are closed, and so are the invitations.
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var slot = await db.Slots.Include(s => s.Contact).SingleAsync(s => s.Id == closedSlot, CancellationToken);
            slot.ContributorId = null;
            slot.Contact = new SlotContact { Email = "invite-cid-2352@example.com" };
            await db.SaveChangesAsync(CancellationToken);
        }

        await RunAsync(new DateTimeOffset(2352, 12, 25, 6, 0, 0, TimeSpan.Zero), LaterYear);
        await using (var db = await fixture.CreateDbContextAsync())
        {
            Assert.False(await db.PastAuthorInvitations.AnyAsync(i => i.SeasonYear == LaterYear && i.Fingerprint == EmailOptOutLinks.Fingerprint("invite-cid-2352@example.com"), CancellationToken));
        }
    }

    private async Task SetUpAsync()
    {
        await TestSeasons.EnsureAsync(fixture, Year, claimsOpen: false, season =>
        {
            season.ClaimsOpenAt = OpensAt;
            var taken = season.Slots.Single(s => s is { Day: 1, Part: 1 });
            taken.AuthorName = "Tia Taken";
            taken.Contact = new SlotContact { Email = "invite-tia@example.com" };
        });

        await using var db = await fixture.CreateDbContextAsync();
        if (await db.Contributors.AnyAsync(c => c.GithubUserId == 8951, CancellationToken))
        {
            return;
        }

        var posts = await db.Slots
            .Include(s => s.Contact)
            .Where(s => s.SeasonYear == 2023 && s.Day >= 19 && s.Day <= 25 && s.PostUrl != null && s.AuthorName != null)
            .OrderBy(s => s.Day).ThenBy(s => s.Part)
            .Take(6)
            .ToListAsync(CancellationToken);

        void Contact(int index, string email)
        {
            posts[index].ContributorId = null;
            posts[index].Contact = new SlotContact { Email = email };
        }

        Contact(0, "Invite-Ann@Example.com");
        Contact(1, "invite-ann@example.com");
        Contact(3, "invite-sue@example.com");
        Contact(4, "invite-tia@example.com");
        Contact(5, "invite-oli@example.com");

        var cy = new Contributor
        {
            GithubUserId = 8951,
            GithubLogin = "invite-cy",
            Email = "invite-cy@example.com",
            EmailVerifiedAt = DateTimeOffset.UtcNow,
            DisplayName = "Cy Profile",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Contributors.Add(cy);
        await db.SaveChangesAsync(CancellationToken);
        posts[2].ContributorId = cy.Id;

        db.AnnouncementSubscribers.Add(new AnnouncementSubscriber
        {
            Email = "invite-sue@example.com",
            CreatedAt = DateTimeOffset.UtcNow,
            ConfirmationSentAt = DateTimeOffset.UtcNow,
            ConfirmedAt = DateTimeOffset.UtcNow,
        });
        db.EmailOptOuts.Add(new EmailOptOut { Fingerprint = EmailOptOutLinks.Fingerprint("invite-oli@example.com"), OptedOutAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(CancellationToken);
    }

    private async Task RunAsync(DateTimeOffset at, int year = Year)
    {
        using var client = fixture.CreateWebClient();
        var query = $"at={Uri.EscapeDataString(at.ToString("O", CultureInfo.InvariantCulture))}&year={year}";
        var response = await client.PostAsync($"/dev/announcements/run?{query}", null, CancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
