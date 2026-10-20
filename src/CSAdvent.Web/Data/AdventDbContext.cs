using Microsoft.EntityFrameworkCore;

namespace CSAdvent.Web.Data;

public sealed class AdventDbContext(DbContextOptions<AdventDbContext> options) : DbContext(options)
{
    public DbSet<Season> Seasons => Set<Season>();

    public DbSet<Slot> Slots => Set<Slot>();

    public DbSet<SlotContact> SlotContacts => Set<SlotContact>();

    public DbSet<Contributor> Contributors => Set<Contributor>();

    public DbSet<ClaimRequest> ClaimRequests => Set<ClaimRequest>();

    public DbSet<ContentBlock> ContentBlocks => Set<ContentBlock>();

    public DbSet<ContentBlockRevision> ContentBlockRevisions => Set<ContentBlockRevision>();

    public DbSet<Substitute> Substitutes => Set<Substitute>();

    public DbSet<OtherAdvent> OtherAdvents => Set<OtherAdvent>();

    public DbSet<SentReminder> SentReminders => Set<SentReminder>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.Conventions.Add(_ => new SnakeCaseNamingConvention());

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AdventDbContext).Assembly);
}
