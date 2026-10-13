namespace SharpVision.Export;

/// CSV output primitives shared by the sheet exporter: file paths, line writing,
/// and field escaping.
public static class Csv
{
    /// Path for csv/&lt;lang&gt;/&lt;sheet&gt;.csv, creating parent directories
    /// (sheet names can contain '/', e.g. quest/048/...).
    public static string FilePath(string lang, string sheetName)
    {
        var path = Output.Dir("csv", lang, sheetName + ".csv");
        // Defense-in-depth: a sheet name must not escape the csv folder.
        if (!PathGuard.IsWithin(Output.Dir("csv"), path))
            throw new IOException($"CSV output path escapes the csv folder: '{sheetName}'.");
        var dir = Path.GetDirectoryName(path);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        return path;
    }

    /// Yields <paramref name="first"/> (the row-id column) followed by the rest.
    public static IEnumerable<string> Prepend(string first, List<string> rest)
    {
        yield return first;
        foreach (var item in rest)
            yield return item;
    }

    public static void WriteLine(TextWriter writer, IEnumerable<string> cells)
    {
        writer.Write(string.Join(",", cells.Select(Escape)));
        writer.Write('\n');
    }

    private static string Escape(string value)
    {
        if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0)
            return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
