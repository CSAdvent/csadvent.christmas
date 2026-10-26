using CSAdvent.Web.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CSAdvent.Web.Data;

public enum QueuedEmailStatus
{
    /// <summary>Waiting to go, or between attempts (<see cref="QueuedEmail.NextAttemptAt"/>).</summary>
    Pending,

    /// <summary>The email service took it (or it was skipped, because its recipient unsubscribed from optional emails).</summary>
    Sent,

    /// <summary>Every attempt failed. An organizer can retry or discard it.</summary>
    Failed,

    /// <summary>Not sent by <see cref="QueuedEmail.SendBefore"/>, after which it would only have been out of date.</summary>
    Expired,
}

/// <summary>
/// An email in the outbox (#152), <c>email_outbox</c>. Everything the site sends is queued here and sent by
/// <see cref="Email.Queue.EmailOutboxWorker"/>, so one sender keeps the whole site within the email service's limits, however
/// many replicas there are, and nothing is lost if the app stops between deciding to send an email and sending it. It holds
/// the whole email, address included, so it's private: <see cref="Retention.ContactRetention"/> deletes it a week after it's
/// sent, expires or fails.
/// </summary>
public sealed class QueuedEmail
{
    public int Id { get; init; }

    /// <summary>Which email it is: its <see cref="EmailCatalog"/> key, such as <c>claim-received</c>.</summary>
    public required string Kind { get; init; }

    public EmailPriority Priority { get; init; }

    /// <summary>What it's about, for its sender's own bookkeeping: <c>announcement:12</c>, <c>backup-offer:7</c>.</summary>
    public string? Reference { get; init; }

    public required string ToAddress { get; init; }

    public string? ToName { get; init; }

    public required string ReplyTo { get; init; }

    public required string Subject { get; init; }

    public required string Html { get; init; }

    public required string Text { get; init; }

    /// <summary>The one-click unsubscribe link, sent as the <c>List-Unsubscribe</c> header too.</summary>
    public string? UnsubscribeUrl { get; init; }

    /// <summary>About something the recipient signed up for, so it goes even if they unsubscribed.</summary>
    public bool Transactional { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>When it's no use any more: unsent by then, it's <see cref="QueuedEmailStatus.Expired"/>.</summary>
    public DateTimeOffset? SendBefore { get; init; }

    public QueuedEmailStatus Status { get; set; }

    /// <summary>How many times sending it has failed.</summary>
    public int Attempts { get; set; }

    /// <summary>The earliest it's tried (again): when it was queued, or after a failure, its backoff.</summary>
    public DateTimeOffset NextAttemptAt { get; set; }

    /// <summary>While a replica is sending it, nobody else picks it up until then.</summary>
    public DateTimeOffset? LockedUntil { get; set; }

    /// <summary>When the email service took it. The sending limits count these.</summary>
    public DateTimeOffset? SentAt { get; set; }

    /// <summary>When it was sent, failed for good or expired. It's deleted a week later.</summary>
    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>Why the last attempt failed, briefly and without addresses: "The email service answered 503".</summary>
    public string? LastError { get; set; }
}

internal sealed class QueuedEmailConfiguration : IEntityTypeConfiguration<QueuedEmail>
{
    public void Configure(EntityTypeBuilder<QueuedEmail> builder)
    {
        builder.ToTable("email_outbox");

        builder.Property(e => e.Kind).HasMaxLength(50);
        builder.Property(e => e.Reference).HasMaxLength(50);
        builder.Property(e => e.ToAddress).HasMaxLength(320);
        builder.Property(e => e.ToName).HasMaxLength(320);
        builder.Property(e => e.ReplyTo).HasMaxLength(320);
        builder.Property(e => e.Subject).HasMaxLength(1000);
        builder.Property(e => e.UnsubscribeUrl).HasMaxLength(2000);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.LastError).HasMaxLength(500);

        // What the sender looks for: pending emails in sending order.
        builder.HasIndex(e => new { e.Priority, e.Id }).HasFilter("status = 'Pending'");

        // The sending limits count the last hour's sends; retention looks for finished emails; senders count their own.
        builder.HasIndex(e => e.SentAt);
        builder.HasIndex(e => e.FinishedAt);
        builder.HasIndex(e => e.Reference);
    }
}
