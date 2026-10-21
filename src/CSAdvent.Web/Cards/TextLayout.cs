namespace CSAdvent.Web.Cards;

/// <summary>Wrapping and sizing text to fit a box, independent of how it's drawn.</summary>
public static class TextLayout
{
    public const string Ellipsis = "…";

    /// <summary>Breaks text into lines no wider than <paramref name="maxWidth"/>, at spaces. A word wider than a line gets a line of its own.</summary>
    public static List<string> Wrap(string text, Func<string, float> measure, float maxWidth)
    {
        var lines = new List<string>();
        var line = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = line.Length == 0 ? word : $"{line} {word}";
            if (line.Length > 0 && measure(candidate) > maxWidth)
            {
                lines.Add(line);
                line = word;
            }
            else
            {
                line = candidate;
            }
        }

        if (line.Length > 0)
        {
            lines.Add(line);
        }

        return lines;
    }

    /// <summary>
    /// The largest of <paramref name="sizes"/> (largest first) at which the text fits in <paramref name="maxLines"/>. If none
    /// does, the smallest, with the last line cut short and an ellipsis.
    /// </summary>
    public static (float Size, List<string> Lines) Fit(
        string text,
        Func<float, Func<string, float>> measureAt,
        float maxWidth,
        int maxLines,
        IReadOnlyList<float> sizes)
    {
        foreach (var size in sizes)
        {
            var lines = Wrap(text, measureAt(size), maxWidth);
            if (lines.Count <= maxLines)
            {
                return (size, lines);
            }
        }

        var smallest = sizes[^1];
        var measure = measureAt(smallest);
        var kept = Wrap(text, measure, maxWidth).Take(maxLines).ToList();
        var last = kept[^1];
        while (last.Length > 0 && measure(last + Ellipsis) > maxWidth)
        {
            last = last[..^1].TrimEnd();
        }

        kept[^1] = last + Ellipsis;
        return (smallest, kept);
    }
}
