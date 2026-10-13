using Lumina.Data.Files;
using SharpVision.Cli;
using SixLabors.ImageSharp;

namespace SharpVision.Export;

/// Extracts icon textures (ui/icon/...) to PNG: normal resolution into icons/normal/
/// and _hr1 high-res into icons/hd/ (separate subfolders, each grouped by 1000s).
public static class IconExporter
{
    private const string IconFileFormat = "ui/icon/{0:D3}000/{1}{2:D6}.tex";
    private const string IconHDFileFormat = "ui/icon/{0:D3}000/{1}{2:D6}_hr1.tex";

    // Resolves an icon .tex: tries the language-specific variant first
    // (ui/icon/.../<lang>/), then the generic one. GetFile returns null on a miss.
    private static TexFile? GetIcon(Lumina.GameData lumina, string type, int iconId, bool hd)
    {
        type ??= string.Empty;
        if (type.Length > 0 && !type.EndsWith("/"))
            type += "/";

        string fmt = hd ? IconHDFileFormat : IconFileFormat;
        try
        {
            var file = lumina.GetFile<TexFile>(string.Format(fmt, iconId / 1000, type, iconId));
            if (file != null || type.Length == 0)
                return file;

            // No language-specific variant; fall back to the generic icon.
            return lumina.GetFile<TexFile>(string.Format(fmt, iconId / 1000, string.Empty, iconId));
        }
        catch (FileNotFoundException)
        {
            // The index has an entry but the file is empty/zero-length — a placeholder
            // icon slot the game ships. Lumina throws FileNotFoundException ("...is
            // empty") for these, so treat them as a missing icon (skip), not an error.
            return null;
        }
    }

    public static void ExportIcons(int first, int last, Lumina.GameData lumina, bool overwrite, string langPrefix, IconKind kind = IconKind.Both)
    {
        string directoryPath = Output.Dir("icons");
        Directory.CreateDirectory(directoryPath);

        bool wantNormal = kind != IconKind.Hd;
        bool wantHd = kind != IconKind.Normal;

        // Pool .dat stream handles so worker threads can read concurrently; missing
        // icons return null, so the large empty id space is cheap to skip.
        Parallelism.EnsureStreamPool(lumina);

        ExportRun.For($"Icons {first}-{last}", first, last, i =>
        {
            string folder = $"{i / 1000:D3}000";

            // Normal and HD go into separate subfolders (icons/normal, icons/hd)
            // so the two resolutions don't share a directory.
            void ExportOne(bool hd, string resDir, string suffix)
            {
                var dir = Path.Combine(directoryPath, resDir, folder);
                var outPath = Path.Combine(dir, $"{i:D6}{suffix}.png");
                if (!overwrite && File.Exists(outPath))
                    return;

                var icon = GetIcon(lumina, langPrefix, i, hd);
                if (icon == null)
                    return;

                var image = TexImage.GetImage(icon);
                if (image == null)
                {
                    Style.Error($"\nFailed to decode icon {i:D6}{suffix}");
                    return;
                }

                Directory.CreateDirectory(dir);
                AtomicWrite.To(outPath, tmp => image.SaveAsPng(tmp));
            }

            if (wantNormal) ExportOne(false, "normal", string.Empty);
            if (wantHd) ExportOne(true, "hd", "_hr1");
        }, i => $"Failed to extract icon {i:D6}");
    }
}
