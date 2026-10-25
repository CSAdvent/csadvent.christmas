using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CSAdvent.Web.Data;

/// <summary>
/// An address that unsubscribed from every C# Advent email, through the link in any of them. Only a one-way fingerprint of
/// the address is kept (<c>EmailOptOuts.Fingerprint</c>), never the address itself: enough to recognize it, not to read it.
/// </summary>
public sealed class EmailOptOut
{
    /// <summary>The SHA-256 of the address in lowercase, as hex.</summary>
    public required string Fingerprint { get; init; }

    public DateTimeOffset OptedOutAt { get; init; }
}

internal sealed class EmailOptOutConfiguration : IEntityTypeConfiguration<EmailOptOut>
{
    public void Configure(EntityTypeBuilder<EmailOptOut> builder)
    {
        builder.HasKey(o => o.Fingerprint);
        builder.Property(o => o.Fingerprint).HasMaxLength(64).IsFixedLength();
    }
}
