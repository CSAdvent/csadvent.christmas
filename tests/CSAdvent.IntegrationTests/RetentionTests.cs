using CSAdvent.Web.Calendar;
using CSAdvent.Web.Data;
using CSAdvent.Web.Retention;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace CSAdvent.IntegrationTests;

/// <summary>
/// The retention policy on the privacy page, run against the real database with a fake clock. The contact details live on
/// archived seasons (whose deletion times have passed) and on 2105 (whose deletion time is far away). Authors keep their
/// email addresses; requests, backup lists and non-authors' emails go.
/// </summary>
public sealed class RetentionTests(AppHostFixture fixture)
{
    private const int FutureYear = 2105;

    // October 2026: every archived season is past its deletion time (2025's was February 1, 2026).
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task DeletesRequestsBackupListsAndNonAuthorsEmailsButKeepsAuthorsEmails()
    {
        await TestSeasons.EnsureAsync(fixture, FutureYear, claimsOpen: false, season =>
        {
            season.Slots.Single(s => s is { Day: 1, Part: 1 }).AuthorName = "Active Dev";
            season.Slots.Single(s => s is { Day: 2, Part: 1 }).AuthorName = "Lapsed Author";
        });

        int archivedSlotId, futureSlotId, lapsedId, activeId, authorId;
        await using (var db = await fixture.CreateDbContextAsync())
        {
            // Verified before the latest deletion time and not since, and verified again after it.
            var lapsed = new Contributor
            {
                GithubUserId = 8101,
                GithubLogin = "lapsed-dev",
                Email = "lapsed@example.com",
                EmailVerifiedAt = new DateTimeOffset(2025, 11, 2, 12, 0, 0, TimeSpan.Zero),
                BlueskyHandle = "lapsed.bsky.social",
                CreatedAt = new DateTimeOffset(2025, 11, 2, 12, 0, 0, TimeSpan.Zero),
            };
            var active = new Contributor
            {
                GithubUserId = 8102,
                GithubLogin = "active-dev",
                Email = "active@example.com",
                EmailVerifiedAt = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero),
                CreatedAt = new DateTimeOffset(2025, 11, 2, 12, 0, 0, TimeSpan.Zero),
            };
            // An author who hasn't signed in since either: they keep their address, to hear about the next season.
            var author = new Contributor
            {
                GithubUserId = 8104,
                GithubLogin = "lapsed-author",
                Email = "author@example.com",
                EmailVerifiedAt = new DateTimeOffset(2025, 11, 2, 12, 0, 0, TimeSpan.Zero),
                CreatedAt = new DateTimeOffset(2025, 11, 2, 12, 0, 0, TimeSpan.Zero),
            };
            db.Contributors.AddRange(lapsed, active, author);
            await db.SaveChangesAsync(CancellationToken);
            (await db.Slots.SingleAsync(s => s.SeasonYear == FutureYear && s.Day == 2 && s.Part == 1, CancellationToken)).ContributorId = author.Id;

            var archivedSlot = await db.Slots.SingleAsync(s => s.SeasonYear == 2017 && s.Day == 1 && s.Part == 1, CancellationToken);
            archivedSlot.Contact = new SlotContact { Email = "lapsed@example.com" };
            var futureSlot = await db.Slots.SingleAsync(s => s.SeasonYear == FutureYear && s.Day == 1 && s.Part == 1, CancellationToken);
            futureSlot.Contact = new SlotContact { Email = "active@example.com" };

            var archivedSeason = await db.Seasons.SingleAsync(s => s.Year == 2017, CancellationToken);
            archivedSeason.Substitutes.Add(new Substitute { Name = "Stand By", Email = "standby@example.com", SignedUpAt = Now });
            await db.SaveChangesAsync(CancellationToken);

            db.ClaimRequests.AddRange(Request(archivedSlot.Id, lapsed.Id), Request(futureSlot.Id, active.Id));
            await db.SaveChangesAsync(CancellationToken);
            (archivedSlotId, futureSlotId, lapsedId, activeId, authorId) = (archivedSlot.Id, futureSlot.Id, lapsed.Id, active.Id, author.Id);
        }

        ContactDeletion deletion;
        await using (var db = await fixture.CreateDbContextAsync())
        {
            deletion = await CreateRetention(db, Now).DeleteExpiredAsync(CancellationToken);
        }

        await using (var db = await fixture.CreateDbContextAsync())
        {
            // The calendar keeps the entry, and the author's contact email stays with it.
            var archivedSlot = await db.Slots.Include(s => s.Contact).SingleAsync(s => s.Id == archivedSlotId, CancellationToken);
            Assert.Equal("lapsed@example.com", archivedSlot.Contact?.Email);
            Assert.NotNull(archivedSlot.AuthorName);
            Assert.False(await db.ClaimRequests.AnyAsync(r => r.SlotId == archivedSlotId, CancellationToken));
            Assert.False(await db.Substitutes.AnyAsync(s => s.SeasonYear == 2017, CancellationToken));

            // Someone who never had a spot keeps their account, without its email; an author keeps theirs.
            var lapsed = await db.Contributors.SingleAsync(c => c.Id == lapsedId, CancellationToken);
            Assert.Null(lapsed.Email);
            Assert.Null(lapsed.EmailVerifiedAt);
            Assert.Equal("lapsed-dev", lapsed.GithubLogin);
            Assert.Equal("lapsed.bsky.social", lapsed.BlueskyHandle);
            Assert.Equal("active@example.com", (await db.Contributors.SingleAsync(c => c.Id == activeId, CancellationToken)).Email);
            Assert.Equal("author@example.com", (await db.Contributors.SingleAsync(c => c.Id == authorId, CancellationToken)).Email);

            Assert.Equal("active@example.com", (await db.SlotContacts.SingleAsync(c => c.SlotId == futureSlotId, CancellationToken)).Email);
            Assert.True(await db.ClaimRequests.AnyAsync(r => r.SlotId == futureSlotId, CancellationToken));
        }

        Assert.True(deletion.ClaimRequests >= 1 && deletion.Substitutes >= 1 && deletion.ContributorEmails >= 1);
    }

    [Fact]
    public async Task KeepsTheBackupListUntilTheStartOfFebruary()
    {
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var season = await db.Seasons.SingleAsync(s => s.Year == 2025, CancellationToken);
            season.Substitutes.Add(new Substitute { Name = "January Backup", Email = "january@example.com", SignedUpAt = Now });
            await db.SaveChangesAsync(CancellationToken);
        }

        // 2025's deletion time is midnight Eastern on February 1, 2026 (05:00 UTC).
        var deletionTime = new DateTimeOffset(2026, 2, 1, 5, 0, 0, TimeSpan.Zero);
        await using (var db = await fixture.CreateDbContextAsync())
        {
            await CreateRetention(db, deletionTime.AddSeconds(-1)).DeleteExpiredAsync(CancellationToken);
            Assert.True(await db.Substitutes.AnyAsync(s => s.Email == "january@example.com", CancellationToken));

            await CreateRetention(db, deletionTime).DeleteExpiredAsync(CancellationToken);
            Assert.False(await db.Substitutes.AnyAsync(s => s.Email == "january@example.com", CancellationToken));
        }
    }

    [Fact]
    public async Task DeletesRequestsForOlderPosts30DaysAfterTheyreDecided()
    {
        int oldId, recentId, pendingId;
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var asker = new Contributor { GithubUserId = 8103, GithubLogin = "old-poster", CreatedAt = Now };
            db.Contributors.Add(asker);
            await db.SaveChangesAsync(CancellationToken);
            var slots = await db.Slots.Where(s => s.SeasonYear == 2020 && s.PostUrl != null).OrderBy(s => s.Id).Select(s => s.Id).Take(3).ToListAsync(CancellationToken);

            PostClaimRequest Decided(int slotId, int daysAgo) => new()
            {
                SlotId = slotId,
                ContributorId = asker.Id,
                Note = "My old blog.",
                SubmittedAt = Now.AddDays(-daysAgo - 1),
                Status = PostClaimStatus.Declined,
                DecidedAt = Now.AddDays(-daysAgo),
                DecidedBy = "organizer",
            };
            var old = Decided(slots[0], 31);
            var recent = Decided(slots[1], 29);
            var pending = new PostClaimRequest { SlotId = slots[2], ContributorId = asker.Id, SubmittedAt = Now.AddDays(-90), Status = PostClaimStatus.Pending };
            db.PostClaimRequests.AddRange(old, recent, pending);
            await db.SaveChangesAsync(CancellationToken);
            (oldId, recentId, pendingId) = (old.Id, recent.Id, pending.Id);
        }

        await using (var db = await fixture.CreateDbContextAsync())
        {
            var deletion = await CreateRetention(db, Now).DeleteExpiredAsync(CancellationToken);

            Assert.True(deletion.PostClaimRequests >= 1);
            Assert.False(await db.PostClaimRequests.AnyAsync(r => r.Id == oldId, CancellationToken));
            Assert.True(await db.PostClaimRequests.AnyAsync(r => r.Id == recentId, CancellationToken));
            Assert.True(await db.PostClaimRequests.AnyAsync(r => r.Id == pendingId, CancellationToken));
        }
    }

    private static ContactRetention CreateRetention(AdventDbContext db, DateTimeOffset now)
    {
        var clock = new FakeTimeProvider(now);
        var schedule = new AdventSchedule(Options.Create(new AdventOptions { TimeZone = "America/New_York" }), clock);
        return new ContactRetention(db, schedule, clock, NullLogger<ContactRetention>.Instance);
    }

    private static ClaimRequest Request(int slotId, int contributorId) => new()
    {
        SlotId = slotId,
        ContributorId = contributorId,
        DisplayName = "Dev",
        Email = "dev@example.com",
        TermsAcceptedAt = Now,
        TimingAcceptedAt = Now,
        SubmittedAt = Now,
        Status = ClaimRequestStatus.Approved,
    };
}
