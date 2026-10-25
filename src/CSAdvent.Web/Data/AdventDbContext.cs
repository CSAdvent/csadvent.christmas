using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.Web.Data;

public sealed class AdventDbContext(DbContextOptions<AdventDbContext> options) : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<Season> Seasons => Set<Season>();

    public DbSet<SeasonTester> SeasonTesters => Set<SeasonTester>();

    public DbSet<Slot> Slots => Set<Slot>();

    public DbSet<SlotContact> SlotContacts => Set<SlotContact>();

    public DbSet<Contributor> Contributors => Set<Contributor>();

    public DbSet<ClaimRequest> ClaimRequests => Set<ClaimRequest>();

    public DbSet<ContentBlock> ContentBlocks => Set<ContentBlock>();

    public DbSet<ContentBlockRevision> ContentBlockRevisions => Set<ContentBlockRevision>();

    public DbSet<Substitute> Substitutes => Set<Substitute>();

    public DbSet<BackupOffer> BackupOffers => Set<BackupOffer>();

    public DbSet<OtherAdvent> OtherAdvents => Set<OtherAdvent>();

    public DbSet<OtherAdventLogo> OtherAdventLogos => Set<OtherAdventLogo>();

    public DbSet<SentReminder> SentReminders => Set<SentReminder>();

    public DbSet<EntryHit> EntryHits => Set<EntryHit>();

    public DbSet<SocialPost> SocialPosts => Set<SocialPost>();

    public DbSet<SocialPlatformSetting> SocialPlatformSettings => Set<SocialPlatformSetting>();

    public DbSet<AnnouncementSubscriber> AnnouncementSubscribers => Set<AnnouncementSubscriber>();

    public DbSet<Announcement> Announcements => Set<Announcement>();

    public DbSet<AnnouncementDelivery> AnnouncementDeliveries => Set<AnnouncementDelivery>();

    public DbSet<SeasonSocialPost> SeasonSocialPosts => Set<SeasonSocialPost>();

    public DbSet<Retrospective> Retrospectives => Set<Retrospective>();

    public DbSet<PostClaimRequest> PostClaimRequests => Set<PostClaimRequest>();

    public DbSet<PostLinkRequest> PostLinkRequests => Set<PostLinkRequest>();

    public DbSet<PostLinkChange> PostLinkChanges => Set<PostLinkChange>();

    public DbSet<EmailOptOut> EmailOptOuts => Set<EmailOptOut>();

    public DbSet<BlurbCheck> BlurbChecks => Set<BlurbCheck>();

    /// <summary>
    /// ASP.NET Core's data protection keys, which protect sign-in cookies and antiforgery tokens. Kept in the database so
    /// they survive container restarts and are shared by every replica.
    /// </summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.Conventions.Add(_ => new SnakeCaseNamingConvention());

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AdventDbContext).Assembly);
}
