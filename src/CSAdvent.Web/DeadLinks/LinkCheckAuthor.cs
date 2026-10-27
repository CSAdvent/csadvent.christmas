using CSAdvent.Web.Manage;

namespace CSAdvent.Web.DeadLinks;

/// <summary>
/// An author's posts on the dead links page, so the organizers can ask them about all of theirs at once. Authors are who they
/// are on Authors: someone linked to a GitHub account is one author whatever names they've used, and an archive author who
/// isn't linked yet is grouped by name.
/// </summary>
/// <param name="Key">An unlinked author's <see cref="AuthorService.Key"/>; null for a linked one.</param>
/// <param name="Name">The name on their latest post.</param>
/// <param name="Emails">Every address we have for them, from their posts.</param>
/// <param name="Posts">Oldest season first.</param>
public sealed record LinkCheckAuthor(int? ContributorId, string? Key, string Name, IReadOnlyList<string> Emails, IReadOnlyList<LinkCheckRow> Posts)
{
    public string Profile => AuthorRow.ProfileOf(ContributorId, Key);

    /// <summary>The posts by author, in order of the authors' names.</summary>
    public static IReadOnlyList<LinkCheckAuthor> Group(IEnumerable<LinkCheckRow> rows) =>
        [.. rows
            .GroupBy(r => (r.ContributorId, Key: r.ContributorId is null ? AuthorService.Key(r.AuthorName ?? "") : null))
            .Select(author =>
            {
                List<LinkCheckRow> posts = [.. author.OrderBy(r => r.Year).ThenBy(r => r.Day).ThenBy(r => r.Part)];
                return new LinkCheckAuthor(
                    author.Key.ContributorId,
                    author.Key.Key,
                    posts[^1].AuthorName ?? "",
                    [.. posts.Select(r => r.AuthorEmail).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase)],
                    posts);
            })
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)];
}
