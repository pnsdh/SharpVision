using Lumina.Data.Files;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace SharpVision.Export;

/// Shared helper for decoding a Lumina texture into an ImageSharp image. Returns
/// null if the texture's pixel format isn't supported; callers log with context.
public static class TexImage
{
    public static Image<Bgra32>? GetImage(TexFile tex)
    {
        try
        {
            return Image.LoadPixelData<Bgra32>(tex.ImageData, tex.Header.Width, tex.Header.Height);
        }
        catch (System.NotSupportedException)
        {
            return null;
        }
    }
}
