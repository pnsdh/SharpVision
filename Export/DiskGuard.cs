using SharpVision.Cli;

namespace SharpVision.Export;

/// Disk-space safety for extraction. Two guards:
///  - <see cref="PreflightWarn"/>: warn (and, when interactive, confirm) before
///    writing if the output drive is low on space.
///  - <see cref="Trip"/> / <see cref="Tripped"/> / <see cref="ReportIfTripped"/>:
///    detect an out-of-disk write error mid-run so the exporters stop cleanly with
///    one message instead of spamming a failure per file.
public static class DiskGuard
{
    // Rough per-category output sizes, from a representative full Korean extraction
    // (patch 2026.05.01): icons ~38 KB per scanned id over the default range (normal
    // ~8.3 KB, hd ~30 KB), sound ~5 GB, csv ~200 MB per language, maps ~200 MB, loading
    // ~10 MB. Used only to size the pre-flight warning; real sizes vary by client/patch/
    // range/languages, so the warning leaves a margin and the mid-run guard is the
    // actual safety net.
    private const long Mb = 1L << 20;
    private const long SoundBytes = 5_200 * Mb;
    private const long CsvPerLangBytes = 210 * Mb;
    private const long MapsBytes = 210 * Mb;
    private const long LoadingBytes = 12 * Mb;

    private static volatile bool _tripped;

    /// True once an out-of-disk write error has been seen; exporters short-circuit on it.
    public static bool Tripped => _tripped;

    /// If <paramref name="ex"/> is an out-of-disk error, marks the run tripped and
    /// returns true (the caller should stop). Otherwise returns false (a normal
    /// per-item failure the caller can log and skip). Thread-safe.
    public static bool Trip(Exception ex)
    {
        if (!IsDiskFull(ex))
            return false;
        _tripped = true;
        return true;
    }

    /// Prints one stop message if the run tripped on disk space (call after extraction).
    public static void ReportIfTripped()
    {
        if (_tripped)
            Style.Error("\nOut of disk space — extraction stopped. Free up space and re-run to continue "
                + "(existing files are skipped; add --overwrite if the interrupted file is incomplete).");
    }

    /// Rough free space the chosen extraction will need, for the pre-flight warning.
    /// <paramref name="iconCount"/> is the number of icon ids that will be scanned
    /// (last - first + 1); <paramref name="languageCount"/> the CSV languages.
    public static long RequiredBytes(ExtractTarget target, IconKind kind, int iconCount, int languageCount)
    {
        long icons = Math.Max(0, (long)iconCount) * IconBytesPerId(kind);
        long csv = CsvPerLangBytes * Math.Max(1, languageCount);
        return target switch
        {
            ExtractTarget.Icons => icons,
            ExtractTarget.Sound => SoundBytes,
            ExtractTarget.Csv => csv,
            ExtractTarget.Maps => MapsBytes,
            ExtractTarget.Loading => LoadingBytes,
            ExtractTarget.Full => csv + MapsBytes + LoadingBytes + SoundBytes + icons,
            _ => 0,
        };
    }

    private static long IconBytesPerId(IconKind kind) => kind switch
    {
        IconKind.Normal => 8_300,
        IconKind.Hd => 30_000,
        _ => 38_000, // Both
    };

    /// Warns when the output drive's free space is below 1.25x the run's estimated
    /// need. Returns false only when an interactive user declines.
    public static bool PreflightWarn(long requiredBytes, bool interactive)
    {
        long free = FreeBytes(Output.ExtractRoot);
        if (free < 0 || free >= requiredBytes + requiredBytes / 4)
            return true;

        Style.Warn($"Output drive has {Human(free)} free, but this run may need about {Human(requiredBytes)}.");
        if (!interactive)
            return true;

        Console.Write("Continue anyway? (y/N)> ");
        var input = Console.ReadLine()?.Trim().ToLowerInvariant();
        return input is "y" or "yes";
    }

    // Locale-independent out-of-disk detection (messages are localized; HResult isn't).
    private static bool IsDiskFull(Exception ex)
    {
        for (Exception? e = ex; e != null; e = e.InnerException)
        {
            if (e is not IOException io)
                continue;
            // Windows: ERROR_DISK_FULL (0x70) / ERROR_HANDLE_DISK_FULL (0x27).
            if (io.HResult == unchecked((int)0x80070070) || io.HResult == unchecked((int)0x80070027))
                return true;
            // Unix: ENOSPC (28).
            if (!OperatingSystem.IsWindows() && (io.HResult & 0xFFFF) == 28)
                return true;
        }
        return false;
    }

    private static long FreeBytes(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            return string.IsNullOrEmpty(root) ? -1 : new DriveInfo(root).AvailableFreeSpace;
        }
        catch
        {
            return -1;
        }
    }

    private static string Human(long bytes) =>
        bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.0} GB"
        : bytes >= 1L << 20 ? $"{bytes / (double)(1L << 20):0.0} MB"
        : $"{bytes} B";
}
