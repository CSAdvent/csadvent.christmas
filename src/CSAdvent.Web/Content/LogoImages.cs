using SkiaSharp;

namespace CSAdvent.Web.Content;

/// <summary>
/// Turns a downloaded or uploaded image into the PNG the site serves: redrawn, never passed through, so nothing but a plain
/// image of at most <see cref="Size"/> pixels ever comes from csadvent.christmas.
/// </summary>
public static class LogoImages
{
    public const int Size = 128;

    /// <summary>Bigger than any real icon. Refusing these stops a tiny file from decoding into gigabytes.</summary>
    private const int MaxSourceSize = 4096;

    /// <summary>
    /// The image as a PNG scaled down to fit <see cref="Size"/>, and the longer side of the original (to prefer bigger icons),
    /// or null if it isn't an image Skia can read (PNG, JPEG, GIF, WebP, ICO or BMP; not SVG).
    /// </summary>
    public static byte[]? ToPng(byte[] data, out int sourceSize)
    {
        sourceSize = 0;
        using var stream = new SKMemoryStream(data);
        using var codec = SKCodec.Create(stream);
        if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0 || codec.Info.Width > MaxSourceSize || codec.Info.Height > MaxSourceSize)
        {
            return null;
        }

        using var bitmap = SKBitmap.Decode(codec);
        if (bitmap is null)
        {
            return null;
        }

        sourceSize = Math.Max(bitmap.Width, bitmap.Height);
        var scale = Math.Min(1f, (float)Size / sourceSize);
        var width = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
        var height = Math.Max(1, (int)Math.Round(bitmap.Height * scale));

        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var image = SKImage.FromBitmap(bitmap);
        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.DrawImage(image, SKRect.Create(width, height), new SKSamplingOptions(SKCubicResampler.Mitchell));

        using var snapshot = surface.Snapshot();
        using var png = snapshot.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }
}
