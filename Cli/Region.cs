namespace SharpVision.Cli;

/// The region/client a set of detected languages belongs to. A single-language
/// client is named by its language; anything multi-language is the global client.
/// Single source of truth for both the menu label and the output-folder slug.
public static class Region
{
    public static string Name(IReadOnlyList<string> codes) => codes.Count switch
    {
        0 => "Unknown",
        1 => codes[0] switch
        {
            "ko" => "Korean",
            "chs" => "Simplified Chinese",
            "cht" => "Traditional Chinese",
            // ja/en/de/fr only ever ship together (the global client), so a lone
            // one of those never reaches here; treat anything else as Global.
            _ => "Global",
        },
        _ => "Global",
    };

    /// Filesystem-friendly slug for the output folder, e.g. "simplified-chinese".
    public static string Slug(IReadOnlyList<string> codes) =>
        Name(codes).ToLowerInvariant().Replace(' ', '-');
}
