using System.Collections.Concurrent;
using Lumina.Data;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;
using SharpVision.Cli;
using SharpVision.Sound;

namespace SharpVision.Export;

/// Extracts game music referenced by the BGM and OrchestrionPath sheets to
/// playable .ogg.
///
/// Lumina's ScdFile.GetAudio has no HCA branch (codec 0x1A, used by some BGM), so
/// we read the raw .scd bytes and reconstruct the audio ourselves via
/// <see cref="ScdReader"/> — see its header for the full rationale.
///
/// Design note — why this is the one exporter that does NOT use the shared
/// <see cref="ExportRun"/> driver: it needs two things ExportRun doesn't model.
///   (1) A per-item output count: a .scd is a multi-entry container, so one path can
///       yield several files; ExportScd returns that count and it's summed across
///       threads (ExportRun's work is void).
///   (2) Skip-reason tallying: entries we can't turn into a playable file (e.g. the
///       MS-ADPCM codec, or a decode failure) are normal, non-exception outcomes that
///       the run summarizes by reason — diagnostics for the hand-rolled SCD reader.
///       ExportRun only knows "failure = one logged error", not classified skips.
/// Both traits are inherent to SCD/sound, not to Lumina's missing HCA support. So if
/// HCA ever lands upstream, the win is dropping ScdReader for Lumina's GetAudio (simpler decoding
/// here) — folding this into ExportRun would still be a SEPARATE choice: either give
/// up the count/skip diagnostics (then ExportRun.ForEach fits), or extend ExportRun
/// with result aggregation + a skip callback. Revisit then.
public static class SoundExporter
{
    public static void Export(Lumina.GameData lumina, bool overwrite, SoundSource source)
    {
        string root = Output.Dir("sound");
        Directory.CreateDirectory(root);

        // BGM and OrchestrionPath reference different files (orchestrion rolls
        // differ in length/edit/quality even when it's the same theme), so they
        // are kept as separate sources the user can pick between.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var paths = new List<string>();
        if (source != SoundSource.Orchestrion)
            CollectScdPaths<BGM>(lumina, paths, seen);
        if (source != SoundSource.Bgm)
            CollectScdPaths<OrchestrionPath>(lumina, paths, seen);

        // Thread-safe tallies: extraction below runs in parallel.
        int ogg = 0;
        var skips = new ConcurrentDictionary<string, int>();
        void Skip(string reason) => skips.AddOrUpdate(reason, 1, (_, v) => v + 1);

        string label = source switch
        {
            SoundSource.Bgm => "Sound (BGM)",
            SoundSource.Orchestrion => "Sound (orchestrion)",
            _ => "Sound",
        };

        // Pool .dat stream handles so worker threads can read concurrently. Each
        // path is unique (de-duped above) so no two threads write the same file;
        // ScdReader is pure and the v3 table is a thread-safe Lazy.
        Parallelism.EnsureStreamPool(lumina);

        using (var progress = new Progress(label, paths.Count))
        {
            Parallel.ForEach(paths, Parallelism.Options, path =>
            {
                progress.Tick();
                if (DiskGuard.Tripped) return;
                try
                {
                    var raw = lumina.GetFile<FileResource>(path);
                    if (raw != null)
                        Interlocked.Add(ref ogg, ExportScd(raw.Data, path, root, overwrite, Skip));
                }
                catch (Exception ex)
                {
                    if (DiskGuard.Trip(ex)) return;
                    Skip("exception: " + ex.GetType().Name);
                }
            });
        }

        Style.Success($"Sound: {ogg} files exported (.ogg, or .wav for HCA).");
        if (skips.Count > 0)
            Style.Hint("skipped by reason: "
                + string.Join(", ", skips.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} = {kv.Value}")));
    }

    // Adds the unique, existing .scd paths from sheet T's "File" column. The
    // schema field name is guarded (Map pattern): a rename warns rather than
    // silently dropping a source.
    private static void CollectScdPaths<T>(Lumina.GameData lumina, List<string> paths, HashSet<string> seen)
        where T : struct, IExcelRow<T>
    {
        var sheet = lumina.GetExcelSheet<T>();
        if (sheet == null)
            return;

        if (!SheetColumns.TryGetStringColumn(typeof(T), "File", "SoundExporter", out var fileProperty))
            return;

        foreach (var row in sheet)
        {
            var value = fileProperty.GetValue(row);
            string path = value == null ? string.Empty : ((ReadOnlySeString)value).ExtractText();
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".scd", StringComparison.OrdinalIgnoreCase))
                continue;
            if (seen.Add(path) && lumina.FileExists(path))
                paths.Add(path);
        }
    }

    // Writes the playable entries of one .scd and returns how many files it
    // produced (so the caller can tally atomically across threads).
    private static int ExportScd(byte[] data, string scdPath, string root, bool overwrite, Action<string> skip)
    {
        // Mirror the sqpack path under sound/ (drop the ".scd" extension).
        string relativeNoExt = scdPath[..^4].Replace('/', Path.DirectorySeparatorChar);

        int written = 0;
        var entries = ScdReader.Read(data);
        if (entries.Count == 0)
            skip("not an SCD / no audio table");
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry.Output == null)
            {
                skip(entry.Skipped ?? "unknown");
                continue;
            }

            string suffix = entries.Count > 1 ? $"_{i}" : string.Empty;
            string outPath = Path.Combine(root, relativeNoExt + suffix + entry.Extension);
            if (!PathGuard.IsWithin(root, outPath))
            {
                skip("output path escapes sound/");
                continue;
            }
            if (!overwrite && File.Exists(outPath))
                continue;

            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
            AtomicWrite.To(outPath, tmp => File.WriteAllBytes(tmp, entry.Output));
            written++;
        }
        return written;
    }
}
