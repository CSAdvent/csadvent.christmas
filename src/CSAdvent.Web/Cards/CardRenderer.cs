using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CSAdvent.Web.Calendar;
using Microsoft.Extensions.Caching.Memory;
using SkiaSharp;

namespace CSAdvent.Web.Cards;

/// <summary>A revealed entry's card: what a shared link to its page shows.</summary>
public sealed record EntryCard(int Year, int Day, int Part, int SlotsPerDay, string Title, string Author);

/// <summary>Links to the generated cards. The <c>v</c> query changes with the text, so an edited title gets a fresh card.</summary>
public static class CardLinks
{
    public static string Entry(EntryCard card) => $"cards/{card.Year}/{card.Day}/{card.Part}.jpg?v={Version($"{card.Title}\n{card.Author}")}";

    public static string Season(int year, int? postCount) => $"cards/{year}.jpg" + (postCount is { } count ? $"?v={count}" : "");

    /// <summary>A season whose sign-ups haven't opened yet: its card says when they do.</summary>
    public static string Upcoming(int year, DateOnly signUpsOpenOn) =>
        string.Create(CultureInfo.InvariantCulture, $"cards/{year}.jpg?v=signups-{signUpsOpenOn:yyyy-MM-dd}");

    /// <summary>
    /// The home page's card: the season on the calendar, or between seasons the next season once its sign-ups are scheduled,
    /// and otherwise the general card. Never a finished season, which would say it's still that year's Advent.
    /// </summary>
    public static string Home(HomePage home) => home switch
    {
        { Calendar: { } calendar } => Season(calendar.Year, postCount: null),
        { NextSeason: { SignUpsOpenOn: { } opens } next } => Upcoming(next.Year, opens),
        _ => General,
    };

    /// <summary>"C# Advent: two C# posts a day, December 1–25", with no year.</summary>
    public const string General = "images/social-card.jpg";

    private static string Version(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..12];
}

/// <summary>
/// Draws the 1200×630 share cards (the size Facebook, LinkedIn, Mastodon, Bluesky and Discord all use) on the site's
/// snowflake background, with the fonts embedded in the app so they look the same everywhere. Rendered cards are cached,
/// since a card only changes when its text does.
/// </summary>
public sealed class CardRenderer : IDisposable
{
    public const int Width = 1200;
    public const int Height = 630;

    private const float Margin = 72;
    private static readonly SKColor White = SKColors.White;
    private static readonly SKColor Pale = SKColor.Parse("#b9d1ec");
    private static readonly SKColor Muted = SKColor.Parse("#8fb4df");
    private static readonly SKColor Accent = SKColor.Parse("#4e88c5");
    private static readonly float[] TitleSizes = [72, 66, 60, 54, 48, 44];

    private readonly SKTypeface _bold = LoadTypeface("Inter-Bold.ttf");
    private readonly SKTypeface _regular = LoadTypeface("Inter-Regular.ttf");
    private readonly SKImage _background = LoadImage("card-background.jpg");
    private readonly SKImage _logo = LoadImage("logo.png");
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 500 });

    public byte[] RenderEntry(EntryCard card) =>
        _cache.GetOrCreate(("entry", card), entry =>
        {
            entry.Size = 1;
            return Render(canvas =>
            {
                var when = card.SlotsPerDay > 1 ? $"December {card.Day} · Post {card.Part}" : $"December {card.Day}";
                DrawHeader(canvas, $"C# Advent {card.Year}", when);

                var fonts = TitleSizes.ToDictionary(size => size, size => Font(_bold, size));
                try
                {
                    var title = Printable(card.Title, _bold);
                    var (size, lines) = TextLayout.Fit(title, size => text => fonts[size].MeasureText(text), Width - (2 * Margin), 3, TitleSizes);
                    using var white = Paint(White);
                    var y = 300f;
                    foreach (var line in lines)
                    {
                        canvas.DrawText(line, Margin, y, SKTextAlign.Left, fonts[size], white);
                        y += size * 1.18f;
                    }
                }
                finally
                {
                    foreach (var font in fonts.Values)
                    {
                        font.Dispose();
                    }
                }

                using var authorFont = Font(_regular, 36);
                using var pale = Paint(Pale);
                canvas.DrawText($"by {Printable(card.Author, _regular)}", Margin, 556, SKTextAlign.Left, authorFont, pale);
                DrawFooter(canvas);
            });
        })!;

    /// <summary>A season's card: its year, and how many posts it had once it's over, or when its sign-ups open.</summary>
    public byte[] RenderSeason(int year, int? postCount, DateOnly? signUpsOpenOn = null) =>
        _cache.GetOrCreate(("season", year, postCount, signUpsOpenOn), entry =>
        {
            entry.Size = 1;
            return Render(canvas =>
            {
                canvas.DrawImage(_logo, SKRect.Create(Margin, 190, 200, 200), new SKSamplingOptions(SKCubicResampler.Mitchell));
                using var titleFont = Font(_bold, 104);
                using var white = Paint(White);
                canvas.DrawText($"C# Advent {year}", 320, 290, SKTextAlign.Left, titleFont, white);
                using var subtitleFont = Font(_regular, 42);
                using var pale = Paint(Pale);
                var subtitle = (postCount, signUpsOpenOn) switch
                {
                    ({ } count, _) => string.Create(CultureInfo.InvariantCulture, $"{count} C# posts, December 1–25"),
                    (_, { } opens) => string.Create(CultureInfo.InvariantCulture, $"Sign-ups open {opens:MMMM d}"),
                    _ => "Two C# posts a day, December 1–25",
                };
                canvas.DrawText(subtitle, 324, 362, SKTextAlign.Left, subtitleFont, pale);
                DrawFooter(canvas);
            });
        })!;

    /// <summary>The text without characters the font can't draw (emoji, for example), which would otherwise show as boxes.</summary>
    public static string Printable(string text, SKTypeface typeface)
    {
        using var font = new SKFont(typeface);
        var kept = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune))
            {
                kept.Append(' ');
            }
            else if (font.ContainsGlyph(rune.Value))
            {
                kept.Append(rune.ToString());
            }
        }

        return string.Join(' ', kept.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    internal SKTypeface Bold => _bold;

    public void Dispose()
    {
        _cache.Dispose();
        _logo.Dispose();
        _background.Dispose();
        _regular.Dispose();
        _bold.Dispose();
    }

    private byte[] Render(Action<SKCanvas> draw)
    {
        using var surface = SKSurface.Create(new SKImageInfo(Width, Height));
        var canvas = surface.Canvas;
        canvas.DrawImage(_background, SKRect.Create(Width, Height), new SKSamplingOptions(SKCubicResampler.Mitchell));
        draw(canvas);

        // An accent bar along the bottom, like the site's header.
        using var accent = Paint(Accent);
        canvas.DrawRect(SKRect.Create(0, Height - 10, Width, 10), accent);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 88);
        return data.ToArray();
    }

    private void DrawHeader(SKCanvas canvas, string title, string subtitle)
    {
        canvas.DrawImage(_logo, SKRect.Create(Margin, 64, 96, 96), new SKSamplingOptions(SKCubicResampler.Mitchell));
        using var titleFont = Font(_bold, 40);
        using var white = Paint(White);
        canvas.DrawText(title, Margin + 120, 104, SKTextAlign.Left, titleFont, white);
        using var subtitleFont = Font(_regular, 30);
        using var pale = Paint(Pale);
        canvas.DrawText(subtitle, Margin + 120, 148, SKTextAlign.Left, subtitleFont, pale);
    }

    private void DrawFooter(SKCanvas canvas)
    {
        using var font = Font(_regular, 28);
        using var muted = Paint(Muted);
        canvas.DrawText("csadvent.christmas", Width - Margin, 556, SKTextAlign.Right, font, muted);
    }

    private static SKFont Font(SKTypeface typeface, float size) => new(typeface, size) { Subpixel = true, Edging = SKFontEdging.Antialias };

    private static SKPaint Paint(SKColor color) => new() { Color = color, IsAntialias = true };

    private static SKTypeface LoadTypeface(string name)
    {
        using var stream = OpenAsset(name);
        return SKTypeface.FromStream(stream) ?? throw new InvalidOperationException($"The card font {name} couldn't be loaded.");
    }

    private static SKImage LoadImage(string name)
    {
        using var stream = OpenAsset(name);
        using var data = SKData.Create(stream);
        return SKImage.FromEncodedData(data) ?? throw new InvalidOperationException($"The card image {name} couldn't be loaded.");
    }

    private static Stream OpenAsset(string name) =>
        typeof(CardRenderer).Assembly.GetManifestResourceStream($"cards/{name}")
        ?? throw new InvalidOperationException($"The card asset {name} isn't embedded.");
}
