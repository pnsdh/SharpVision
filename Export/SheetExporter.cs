using System.Reflection;

namespace SharpVision.Export;

/// Orchestrates sheet export: discovers all generated row/subrow types and runs
/// the CSV exporter for each, with a live progress bar per phase.
public static class SheetExporter
{
    /// Exports every sheet, or only <paramref name="sheets"/> (canonical names from
    /// <see cref="ResolveSheetNames"/>) when given. A subset run marks each language
    /// folder partial (see <see cref="CsvPartial"/>) unless it already held a full
    /// extract; a full run clears the marker once it completes.
    public static void ExportCsv(
        IReadOnlyList<(Lumina.GameData Gd, string Code)> langs,
        bool overwrite,
        IReadOnlySet<string>? sheets = null)
    {
        // Decide the partial markers before writing anything: a subset run only marks
        // a folder that was empty (or already partial), never one a full run filled.
        var markPartial = sheets == null
            ? new List<string>()
            : langs.Select(l => Output.Dir("csv", l.Code))
                .Where(dir => CsvPartial.IsPartial(dir) || !CsvPartial.HasSheets(dir))
                .ToList();

        // Pool .dat stream handles per client so the parallel passes below can read
        // concurrently (each language is a separate GameData).
        foreach (var (gd, _) in langs)
            Parallelism.EnsureStreamPool(gd);

        var rowM = typeof(CsvSheetExporter).GetMethod(nameof(CsvSheetExporter.ExportRow))!;
        var subM = typeof(CsvSheetExporter).GetMethod(nameof(CsvSheetExporter.ExportSubrow))!;

        // One work item per (sheet type, language), so progress reflects the real
        // multi-language work instead of one coarse tick per type (which covered all
        // languages and made the bar stall). Row sheets then subrow sheets.
        var work = new List<(Type Type, MethodInfo Method, Lumina.GameData Gd, string Code)>();
        foreach (var type in DiscoverTypes(typeof(Lumina.Excel.IExcelRow<>), sheets))
            foreach (var (gd, code) in langs)
                work.Add((type, rowM, gd, code));
        foreach (var type in DiscoverTypes(typeof(Lumina.Excel.IExcelSubrow<>), sheets))
            foreach (var (gd, code) in langs)
                work.Add((type, subM, gd, code));

        foreach (var dir in markPartial)
            CsvPartial.Mark(dir);

        ExportRun.ForEach("CSV sheets", work,
            w => w.Method.MakeGenericMethod(w.Type).Invoke(null, new object[] { w.Gd, w.Code, overwrite }),
            w => $"Failed to export {w.Type.Name} ({w.Code})");

        // Raw fallback for any sheet the typed pass couldn't export.
        CsvSheetExporter.ExportRawFallback(langs, overwrite, sheets);

        // A completed full run has every sheet, so the folder is no longer partial.
        if (sheets == null && !DiskGuard.Tripped)
            foreach (var (_, code) in langs)
                CsvPartial.Clear(Output.Dir("csv", code));
    }

    /// Maps user-supplied sheet names to the client's own spelling (case-insensitive).
    /// Returns false, with the names that matched no sheet, if any is unknown.
    public static bool ResolveSheetNames(Lumina.GameData gd, IEnumerable<string> requested,
        out HashSet<string> resolved, out List<string> unknown)
    {
        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in gd.Excel.SheetNames)
            byName.TryAdd(name, name);

        resolved = new HashSet<string>(StringComparer.Ordinal);
        unknown = new List<string>();
        foreach (var name in requested)
        {
            if (byName.TryGetValue(name, out var canonical))
                resolved.Add(canonical);
            else
                unknown.Add(name);
        }
        return unknown.Count == 0;
    }

    /// csv/&lt;lang&gt;/&lt;Sheet&gt;.csv files that should exist after a subset run but don't
    /// (the sheet failed to export or yielded no data), as "lang/Sheet".
    public static List<string> MissingOutputs(IReadOnlyList<(Lumina.GameData Gd, string Code)> langs, IReadOnlySet<string> sheets) =>
        langs.SelectMany(l => sheets
                .Where(s => !File.Exists(Output.Dir("csv", l.Code, s + ".csv")))
                .Select(s => $"{l.Code}/{s}"))
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static List<Type> DiscoverTypes(Type rowInterface, IReadOnlySet<string>? sheets) =>
        Assembly.GetAssembly(typeof(Lumina.Excel.Sheets.Action))!.GetTypes()
            .Where(t => t.Namespace == "Lumina.Excel.Sheets"
                && !t.IsAbstract
                && (sheets == null || sheets.Contains(t.Name))
                && t.GetInterfaces().Any(i => i.IsGenericType
                    && i.GetGenericTypeDefinition() == rowInterface
                    && i.GenericTypeArguments[0] == t))
            .ToList();
}
