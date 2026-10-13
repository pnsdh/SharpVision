namespace SharpVision.Export;

/// Defense-in-depth: keeps a data-derived output path from escaping its intended
/// folder (e.g. a sheet value containing "..\.." or an absolute path). Inputs are
/// trusted, SE-authored game data, so this should never trigger in practice — it
/// just guarantees every write stays under the folder it's meant to.
public static class PathGuard
{
    /// True if <paramref name="candidate"/> resolves to a path inside
    /// <paramref name="baseDir"/>. Returns false for anything that escapes — and for
    /// paths that don't even resolve (e.g. illegal characters), which are skipped too.
    public static bool IsWithin(string baseDir, string candidate)
    {
        try
        {
            string baseFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(baseDir))
                + Path.DirectorySeparatorChar;
            string candFull = Path.GetFullPath(candidate);
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return candFull.StartsWith(baseFull, comparison);
        }
        catch
        {
            return false;
        }
    }
}
