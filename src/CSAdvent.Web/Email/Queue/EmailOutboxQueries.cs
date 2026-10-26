using CSAdvent.Web.Data;

namespace CSAdvent.Web.Email.Queue;

/// <summary>Which queued emails are due, and in what order they go: the database's queries and the unit tests' fake share these.</summary>
public static class EmailOutboxQueries
{
    /// <summary>Pending, due (again), and not held by a pass.</summary>
    public static IQueryable<QueuedEmail> Due(this IQueryable<QueuedEmail> emails, DateTimeOffset now) =>
        emails.Where(e => e.Status == QueuedEmailStatus.Pending && e.NextAttemptAt <= now && (e.LockedUntil == null || e.LockedUntil <= now));

    /// <summary>Pending, not held by a pass, and too late to send.</summary>
    public static IQueryable<QueuedEmail> PastSendBefore(this IQueryable<QueuedEmail> emails, DateTimeOffset now) =>
        emails.Where(e => e.Status == QueuedEmailStatus.Pending && e.SendBefore != null && e.SendBefore <= now && (e.LockedUntil == null || e.LockedUntil <= now));

    /// <summary>List mail (readers' daily posts, announcements, invitations), or everything else.</summary>
    public static IQueryable<QueuedEmail> ListMail(this IQueryable<QueuedEmail> emails, bool listMail) =>
        listMail
            ? emails.Where(e => e.Priority >= EmailPriorities.FirstListMail)
            : emails.Where(e => e.Priority < EmailPriorities.FirstListMail);

    /// <summary>Lowest priority number first, and within a priority, the oldest.</summary>
    public static IOrderedQueryable<QueuedEmail> InSendingOrder(this IQueryable<QueuedEmail> emails) =>
        emails.OrderBy(e => e.Priority).ThenBy(e => e.Id);
}
