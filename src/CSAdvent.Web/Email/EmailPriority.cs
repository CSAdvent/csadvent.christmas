namespace CSAdvent.Web.Email;

/// <summary>
/// Which emails the queue (#152) sends first when the email service's limits can't take them all: lower goes first, and
/// within a priority the oldest goes first. From <see cref="ReadersDailyPosts"/> on it's list mail, which only goes while the
/// last hour's emails are under <see cref="Queue.EmailQueueOptions.ListPerHour"/>, so there's always room left in the hour
/// for the emails people are waiting for.
/// </summary>
public enum EmailPriority
{
    /// <summary>
    /// Transactional emails and everything to the organizers' inbox: someone has just done something and is waiting for it,
    /// or it's about their spot, a request or a backup offer. Also the day-of last call, and samples and tests organizers send.
    /// </summary>
    Urgent = 0,

    /// <summary>The authors' daily email and post link reminders, and any optional email without a priority of its own.</summary>
    Normal = 10,

    /// <summary>The day's posts for announcement subscribers who asked for them (#206). The first kind of list mail.</summary>
    ReadersDailyPosts = 20,

    /// <summary>Announcements to the list, organizers' and the "sign-ups are open" email.</summary>
    Announcement = 30,

    /// <summary>Invitations to past authors when sign-ups open (#133).</summary>
    Invitation = 40,
}

public static class EmailPriorities
{
    /// <summary>The first priority that's list mail, held back once the hour's emails reach <see cref="Queue.EmailQueueOptions.ListPerHour"/>.</summary>
    public const EmailPriority FirstListMail = EmailPriority.ReadersDailyPosts;

    public static bool IsListMail(this EmailPriority priority) => priority >= FirstListMail;
}
