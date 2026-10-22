using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.Web.Content;

/// <summary>An editable piece of site copy, with the Markdown it shows until an organizer edits it.</summary>
public sealed record ContentDefinition(string Key, string Name, string UsedOn, string Default);

/// <summary>Every editable block. Pages ask <see cref="ContentService"/> for these by key.</summary>
public static class ContentCatalog
{
    public const string About = "about";
    public const string OtherAdvents = "other-advents";
    public const string BetweenSeasons = "between-seasons";

    public static IReadOnlyList<ContentDefinition> All { get; } =
    [
        new(About, "About", "The About page, above the organizers", """
            C# Advent is a way for C# enthusiasts to celebrate the Advent and help build up the C# community. Every day from
            December 1 to December 25, new C# content is "unlocked" on the calendar: one post a day in 2017, and two a day since.

            ## History

            C# Advent started in 2017, inspired by the [F# Advent Calendar](https://sergeytihon.com/fsadvent/), which Sergey
            Tihon has run since 2010 (in English since 2014). It began with his permission, and it's run every December since.

            ## How it works

            1. **Request a spot.** You don't need a topic yet.
            2. **Prepare your content**, in English, and don't publish it before your day. Host it anywhere: your own site,
               dev.to, Medium, YouTube, GitHub Pages, or somewhere else.
            3. **Link to [csadvent.christmas](https://csadvent.christmas)** in your content, so your readers can find the rest of
               the Advent.
            4. **Publish on your day.** Your content has to be related to C# in some way; otherwise, it's up to you.
            5. **Share it** with the hashtags #csharp and #csadvent.

            Can't make it? Drop out from your My spots page, and a backup poster takes your day. The full rules are in the
            [terms](terms).
            """),
        new(OtherAdvents, "Other Advents introduction", "The Other Advents page, above the list", """
            C# Advent isn't the only developer advent calendar. Here are some others to enjoy this December. Know one we're
            missing? Email [hello@csadvent.christmas](mailto:hello@csadvent.christmas).
            """),
        new(BetweenSeasons, "Between seasons", "The home page from January until registration opens, below the thank-you", """
            Thank you to every author who wrote a post, and to everyone who read and shared them. C# Advent only happens because
            of you.

            See you next December! Until then, catch up on anything you missed in the archive, and follow C# Advent to hear when
            registration opens.
            """),
    ];

    public static ContentDefinition? Find(string key) => All.FirstOrDefault(d => d.Key == key);
}

public sealed record ContentRevisionView(int Id, DateTimeOffset CreatedAt, string? CreatedBy);

public sealed record ContentBlockView(ContentDefinition Definition, string Body, bool IsDefault, DateTimeOffset? UpdatedAt, string? UpdatedBy, IReadOnlyList<ContentRevisionView> Revisions);

/// <summary>Site copy organizers edit without a redeploy. Every save is kept as a revision, so earlier copy can be brought back.</summary>
public sealed class ContentService(AdventDbContext db, TimeProvider timeProvider)
{
    /// <summary>The block's current Markdown, or its default until it's edited.</summary>
    public async Task<string> GetAsync(string key, CancellationToken cancellationToken = default) =>
        await db.ContentBlocks.Where(b => b.Key == key).Select(b => b.Body).SingleOrDefaultAsync(cancellationToken)
        ?? ContentCatalog.Find(key)?.Default
        ?? throw new ArgumentException($"There's no content block called {key}.", nameof(key));

    public async Task<IReadOnlyList<ContentBlockView>> ListAsync(CancellationToken cancellationToken = default)
    {
        var blocks = await db.ContentBlocks.AsNoTracking().Include(b => b.UpdatedBy).ToListAsync(cancellationToken);
        return [.. ContentCatalog.All.Select(definition =>
        {
            var block = blocks.SingleOrDefault(b => b.Key == definition.Key);
            return new ContentBlockView(definition, block?.Body ?? definition.Default, block is null, block?.UpdatedAt, block?.UpdatedBy?.GithubLogin, []);
        })];
    }

    public async Task<ContentBlockView?> GetForEditAsync(string key, CancellationToken cancellationToken = default)
    {
        if (ContentCatalog.Find(key) is not { } definition)
        {
            return null;
        }

        var block = await db.ContentBlocks.AsNoTracking().Include(b => b.UpdatedBy).SingleOrDefaultAsync(b => b.Key == key, cancellationToken);
        var revisions = await db.ContentBlockRevisions
            .AsNoTracking()
            .Where(r => r.ContentBlockKey == key)
            .OrderByDescending(r => r.CreatedAt)
            .Take(20)
            .Select(r => new ContentRevisionView(r.Id, r.CreatedAt, r.CreatedBy == null ? null : r.CreatedBy.GithubLogin))
            .ToListAsync(cancellationToken);

        return new ContentBlockView(definition, block?.Body ?? definition.Default, block is null, block?.UpdatedAt, block?.UpdatedBy?.GithubLogin, revisions);
    }

    /// <summary>An earlier revision's Markdown, to load into the editor.</summary>
    public Task<string?> GetRevisionAsync(string key, int revisionId, CancellationToken cancellationToken = default) =>
        db.ContentBlockRevisions.Where(r => r.ContentBlockKey == key && r.Id == revisionId).Select(r => r.Body).SingleOrDefaultAsync(cancellationToken);

    public async Task<bool> SaveAsync(string key, string body, int? contributorId, CancellationToken cancellationToken = default)
    {
        if (ContentCatalog.Find(key) is null)
        {
            return false;
        }

        var now = timeProvider.GetUtcNow();
        var block = await db.ContentBlocks.SingleOrDefaultAsync(b => b.Key == key, cancellationToken);
        if (block is null)
        {
            block = new ContentBlock { Key = key, Body = body };
            db.ContentBlocks.Add(block);
        }

        block.Body = body;
        block.UpdatedAt = now;
        block.UpdatedById = contributorId;
        block.Revisions.Add(new ContentBlockRevision { Body = body, CreatedAt = now, CreatedById = contributorId });
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}

/// <summary>An editor's change to a block.</summary>
public sealed class ContentForm
{
    [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "The content can't be empty.")]
    [System.ComponentModel.DataAnnotations.StringLength(20000, ErrorMessage = "Keep the content to 20,000 characters or fewer.")]
    public string Body { get; set; } = "";
}
