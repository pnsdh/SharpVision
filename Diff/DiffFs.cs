namespace SharpVision.Diff;

/// Shared filesystem helpers for the diff: file-tree enumeration, the added/removed/
/// common partition, and one parallelism policy. Lumina-free — the diff reads only
/// the already-extracted output files.
internal static class DiffFs
{
    /// One worker per logical core, shared by the diff's parallel passes.
    public static readonly ParallelOptions Parallel = new() { MaxDegreeOfParallelism = Environment.ProcessorCount };

    /// Relative path (forward slashes) -> absolute path, for every file under
    /// <paramref name="dir"/> matching <paramref name="pattern"/> (recursive).
    /// Empty when the directory doesn't exist.
    public static Dictionary<string, string> Enumerate(string dir, string pattern = "*")
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(dir))
            return map;
        foreach (var path in Directory.EnumerateFiles(dir, pattern, SearchOption.AllDirectories))
            map[Path.GetRelativePath(dir, path).Replace('\\', '/')] = path;
        return map;
    }

    /// Splits two relative-path maps into added (new only), removed (old only) and
    /// common (in both) keys. Added/Removed come back sorted; Common is left in
    /// enumeration order for the caller to process.
    public static (List<string> Added, List<string> Removed, List<string> Common) Partition(
        IReadOnlyDictionary<string, string> old, IReadOnlyDictionary<string, string> @new)
    {
        var added = @new.Keys.Where(k => !old.ContainsKey(k)).ToList();
        var removed = old.Keys.Where(k => !@new.ContainsKey(k)).ToList();
        var common = @new.Keys.Where(old.ContainsKey).ToList();
        added.Sort(StringComparer.OrdinalIgnoreCase);
        removed.Sort(StringComparer.OrdinalIgnoreCase);
        return (added, removed, common);
    }
}
