using System.Text;

namespace CSAdvent.Web.Email;

/// <summary>
/// What an email says, independent of how it looks. <see cref="BrandedEmail"/> renders it as branded HTML and
/// <see cref="EmailText"/> as the plain-text alternative, so the two never drift apart.
/// </summary>
public sealed record EmailContent
{
    public required string Subject { get; init; }

    /// <summary>The preview line inboxes show after the subject.</summary>
    public required string Preview { get; init; }

    public required string Heading { get; init; }

    public required IReadOnlyList<EmailBlock> Body { get; init; }

    /// <summary>Why the recipient got this email, shown in the footer.</summary>
    public string? Reason { get; init; }

    /// <summary>For emails people can turn off: the one-click link that does it, in the footer and the headers.</summary>
    public string? UnsubscribeUrl { get; init; }
}

/// <summary>Part of a paragraph or list item: text, a link, or a line break. Strings convert to text.</summary>
public abstract record EmailSpan
{
    public static readonly EmailSpan LineBreak = new BreakSpan();

    public static implicit operator EmailSpan(string text) => new TextSpan(text);
}

public sealed record TextSpan(string Text) : EmailSpan;

public sealed record LinkSpan(string Text, string Url) : EmailSpan;

public sealed record BreakSpan : EmailSpan;

public abstract record EmailBlock;

public sealed record ParagraphBlock(IReadOnlyList<EmailSpan> Spans) : EmailBlock;

public sealed record ListBlock(IReadOnlyList<IReadOnlyList<EmailSpan>> Items) : EmailBlock;

/// <summary>The email's main call to action.</summary>
public sealed record ButtonBlock(string Label, string Url) : EmailBlock;

/// <summary>Labeled values, such as the details of a request.</summary>
public sealed record DetailsBlock(IReadOnlyList<DetailRow> Rows) : EmailBlock;

public sealed record DetailRow(string Label, string Value);

/// <summary>Shorthand for building email bodies.</summary>
public static class EmailBlocks
{
    public static ParagraphBlock Paragraph(params EmailSpan[] spans) => new(spans);

    public static ListBlock List(params EmailSpan[][] items) => new(items);

    public static ButtonBlock Button(string label, string url) => new(label, url);

    public static DetailsBlock Details(params DetailRow[] rows) => new(rows);

    public static LinkSpan Link(string text, string url) => new(text, url);

    /// <summary>Removes line breaks and other control characters, for text that ends up in a header such as the subject.</summary>
    public static string SingleLine(string text) => string.Concat(text.Where(c => !char.IsControl(c))).Trim();
}

/// <summary>The plain-text alternative of an email.</summary>
public static class EmailText
{
    public static string Render(EmailContent content, string baseUrl)
    {
        var text = new StringBuilder();
        text.Append(content.Heading).Append("\n\n");

        foreach (var block in content.Body)
        {
            switch (block)
            {
                case ParagraphBlock paragraph:
                    text.Append(Spans(paragraph.Spans)).Append("\n\n");
                    break;
                case ListBlock list:
                    foreach (var item in list.Items)
                    {
                        text.Append("- ").Append(Spans(item)).Append('\n');
                    }

                    text.Append('\n');
                    break;
                case ButtonBlock button:
                    text.Append(button.Label).Append(": ").Append(button.Url).Append("\n\n");
                    break;
                case DetailsBlock details:
                    foreach (var row in details.Rows)
                    {
                        text.Append(row.Label).Append(": ").Append(row.Value).Append('\n');
                    }

                    text.Append('\n');
                    break;
            }
        }

        var site = baseUrl.TrimEnd('/');
        text.Append("--\n")
            .Append("C# Advent: a community calendar of C# content, every December since 2017.\n")
            .Append(site).Append('\n')
            .Append("Privacy: ").Append(site).Append("/privacy\n")
            .Append("Terms: ").Append(site).Append("/terms\n");
        if (content.Reason is { } reason)
        {
            text.Append('\n').Append(reason).Append('\n');
        }

        if (content.UnsubscribeUrl is { } unsubscribe)
        {
            text.Append("Stop these emails: ").Append(unsubscribe).Append('\n');
        }

        return text.ToString();
    }

    private static string Spans(IEnumerable<EmailSpan> spans) => string.Concat(spans.Select(span => span switch
    {
        TextSpan text => text.Text,
        // Plain text can't hide a link behind words, so show the address (once, if the words are the address).
        LinkSpan link when link.Text == link.Url => link.Url,
        LinkSpan link => $"{link.Text} ({link.Url})",
        BreakSpan => "\n",
        _ => "",
    }));
}
