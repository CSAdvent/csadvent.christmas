using System.ComponentModel.DataAnnotations;

namespace CSAdvent.Web.Email.Queue;

/// <summary>
/// How fast the email queue (#152) sends. Azure Communication Services allows the whole site (every replica, every kind of
/// email) 30 emails a minute and 100 an hour, and refuses the rest with 429, so the defaults leave some headroom under both.
/// The counts come from the queue's own <c>sent_at</c> times, so every replica shares them.
/// </summary>
public sealed class EmailQueueOptions : IValidatableObject
{
    public const string SectionName = "Email:Queue";

    /// <summary>Emails a minute, at most: any 60 seconds.</summary>
    [Range(1, 100_000)]
    public int PerMinute { get; set; } = 25;

    /// <summary>Emails an hour, at most: any 60 minutes.</summary>
    [Range(1, 1_000_000)]
    public int PerHour { get; set; } = 90;

    /// <summary>
    /// List mail (<see cref="EmailPriorities.FirstListMail"/> and below: readers' daily posts, announcements and invitations)
    /// only goes while the last hour's emails of every kind number fewer than this, and is spread across the hour
    /// (<see cref="ListPerMinute"/>). The rest of <see cref="PerHour"/> is kept for the emails people are waiting for, which
    /// can come at any moment of the hour: on sign-up day, the claims the announcement itself brings.
    /// </summary>
    [Range(0, 1_000_000)]
    public int ListPerHour { get; set; } = 50;

    /// <summary>
    /// List mail a minute, at most: <see cref="ListPerHour"/> spread over the hour, rounded up (1 for 50). Without it, list mail
    /// queued at 9 AM would take its whole share of the hour in the first two minutes, before anything else arrived.
    /// </summary>
    public int ListPerMinute => ListPerHour <= 0 ? 0 : (ListPerHour + 59) / 60;

    /// <summary>
    /// The most one pass sends before it lets go of the lock. A pass sends the emails it takes in priority order, but anything
    /// queued after it started waits for it to finish, so a pass with hundreds of emails to send (where the limits allow it, as
    /// in development and the tests) would hold up a transactional email behind all of them. With a pass's worth sent, the
    /// worker starts the next pass straight away. The default is the minute's limit, so in production it changes nothing.
    /// </summary>
    [Range(1, 100_000)]
    public int MaxPerPass { get; set; } = 25;

    /// <summary>How often the worker looks for emails queued by another replica, or that are due again. Its own replica wakes it straight away.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>How long one email may take to send before it counts as a failed attempt.</summary>
    public TimeSpan SendTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How long a pass holds the emails it takes. It stops starting new sends halfway through, so a slow pass never sends an
    /// email another replica has picked up again; an email held by a replica that died goes again once this has passed.
    /// </summary>
    public TimeSpan Lease { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How long to wait after each failed attempt: 1, 5 and 15 minutes, then hourly. With <see cref="MaxAttempts"/> at 6, an
    /// email that keeps failing is given up on about 2 hours 20 minutes after its first try.
    /// </summary>
    public static readonly IReadOnlyList<TimeSpan> Backoff = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromHours(1)];

    /// <summary>Attempts before an email is marked Failed, for an organizer to retry or discard.</summary>
    [Range(1, 100)]
    public int MaxAttempts { get; set; } = 6;

    /// <summary>How long to stop sending when the email service says it's had enough (429) without saying for how long.</summary>
    public TimeSpan ThrottlePause { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>The wait after failed attempt number <paramref name="attempts"/> (1 for the first): the last entry repeats.</summary>
    public static TimeSpan BackoffAfter(int attempts) => Backoff[Math.Clamp(attempts - 1, 0, Backoff.Count - 1)];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (PerMinute > PerHour)
        {
            yield return new ValidationResult("Email:Queue:PerMinute can't be more than PerHour.", [nameof(PerMinute)]);
        }

        if (ListPerHour > PerHour)
        {
            yield return new ValidationResult("Email:Queue:ListPerHour can't be more than PerHour.", [nameof(ListPerHour)]);
        }

        if (PollInterval <= TimeSpan.Zero || SendTimeout <= TimeSpan.Zero || Lease <= TimeSpan.Zero || ThrottlePause <= TimeSpan.Zero)
        {
            yield return new ValidationResult("Email:Queue's PollInterval, SendTimeout, Lease and ThrottlePause must be more than zero.");
        }
    }
}
