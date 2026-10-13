namespace SharpVision.Export;

/// Single source of truth for where files are written. Everything goes under an
/// "output" folder next to the executable (so the location doesn't depend on the
/// current working directory), split into:
///   output/extract/&lt;region&gt;/&lt;version&gt;/   extracted assets
///   output/diff/&lt;region&gt;/&lt;old&gt;__&lt;new&gt;/  diff reports (see DiffRunner)
/// Extracts are grouped by region then client build version, so a region's
/// successive patches sit as sibling folders (easy patch-to-patch diffs) and the
/// region keeps clients apart now that KR/Global/CN share a version string.
public static class Output
{
    private static string Base => AppContext.BaseDirectory;

    // Per-run subpath under extract/ (e.g. "korean/2026.05.01.0000.0000"), set once
    // via Configure() after the region + client version are known.
    private static string? _run;

    /// Names the per-run extract folder "<region>/<gameVersion>".
    public static void Configure(string region, string gameVersion) =>
        _run = Path.Combine(region, gameVersion);

    /// Root for extracted assets (output/extract/). The diff command reads the
    /// per-region version folders from here.
    public static string ExtractRoot => Path.Combine(Base, "output", "extract");

    /// Root for diff reports (output/diff/).
    public static string DiffRoot => Path.Combine(Base, "output", "diff");

    /// The current extraction's output folder: output/extract/<region>/<version>.
    public static string Root => _run == null
        ? ExtractRoot
        : Path.Combine(ExtractRoot, _run);

    public static string Dir(params string[] parts) =>
        Path.Combine(new[] { Root }.Concat(parts).ToArray());
}
