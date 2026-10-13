namespace SharpVision;

/// Reads the game build versions from a loaded GameData. FFXIV stores a date-build
/// version (e.g. 2026.05.01.0000.0000) per sqpack repository — the base "ffxiv" plus
/// each expansion ex1..exN. (There is no marketing patch number like "7.5" in the
/// data; that would need an external mapping.)
public static class GameVersion
{
    /// The base "ffxiv" repository's date-build version (e.g. "2026.05.01.0000.0000"),
    /// or "unknown" if unavailable. Used to name the per-run output folder.
    public static string BaseVersion(Lumina.GameData gd) =>
        gd.Repositories.TryGetValue("ffxiv", out var ffxiv) && !string.IsNullOrEmpty(ffxiv.Version)
            ? ffxiv.Version
            : "unknown";

    /// One-line summary: the base game version and the highest expansion present,
    /// e.g. "ffxiv: 2026.05.01.0000.0000  (ex5: 2026.05.01.0000.0000)".
    public static string Describe(Lumina.GameData gd)
    {
        var repos = gd.Repositories;
        string baseVer = BaseVersion(gd);

        // The latest expansion repo (highest ExpansionId), resolved dynamically so a
        // future ex6+ is picked up without code changes.
        var lastEx = repos.Values
            .Where(r => r.ExpansionId > 0 && !string.IsNullOrEmpty(r.Version))
            .OrderByDescending(r => r.ExpansionId)
            .FirstOrDefault();

        return lastEx != null
            ? $"ffxiv: {baseVer}  ({lastEx.Name}: {lastEx.Version})"
            : $"ffxiv: {baseVer}";
    }
}
