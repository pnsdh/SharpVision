namespace SharpVision.Export;

/// Marker for a csv/&lt;lang&gt;/ folder that holds only a subset of the sheets
/// (written by `csv --sheets`). The diff uses it so sheets that are merely missing
/// from a partial extract aren't reported as added/removed. A later full csv run
/// fills in the rest (existing files are skipped) and then clears the marker.
public static class CsvPartial
{
    // Not *.csv, so the CSV diff's sheet enumeration never picks it up.
    public const string MarkerName = ".partial";

    public static bool IsPartial(string langDir) => File.Exists(Path.Combine(langDir, MarkerName));

    /// True if the folder already has any extracted sheet.
    public static bool HasSheets(string langDir) =>
        Directory.Exists(langDir)
        && Directory.EnumerateFiles(langDir, "*.csv", SearchOption.AllDirectories).Any();

    public static void Mark(string langDir)
    {
        Directory.CreateDirectory(langDir);
        File.WriteAllText(Path.Combine(langDir, MarkerName),
            "Partial extract (csv --sheets): only some sheets are present in this folder.\n" +
            "Run a full csv extraction to fill in the rest; this marker is then removed.\n");
    }

    public static void Clear(string langDir)
    {
        var path = Path.Combine(langDir, MarkerName);
        if (File.Exists(path))
            File.Delete(path);
    }
}
