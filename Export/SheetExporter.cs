using System.Reflection;

namespace SharpVision.Export;

/// Orchestrates sheet export: discovers all generated row/subrow types and runs
/// the CSV exporter for each, with a live progress bar per phase.
public static class SheetExporter
{
    public static void ExportCsv(
        IReadOnlyList<(Lumina.GameData Gd, string Code)> langs,
        bool overwrite)
    {
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
        foreach (var type in DiscoverTypes(typeof(Lumina.Excel.IExcelRow<>)))
            foreach (var (gd, code) in langs)
                work.Add((type, rowM, gd, code));
        foreach (var type in DiscoverTypes(typeof(Lumina.Excel.IExcelSubrow<>)))
            foreach (var (gd, code) in langs)
                work.Add((type, subM, gd, code));

        ExportRun.ForEach("CSV sheets", work,
            w => w.Method.MakeGenericMethod(w.Type).Invoke(null, new object[] { w.Gd, w.Code, overwrite }),
            w => $"Failed to export {w.Type.Name} ({w.Code})");

        // Raw fallback for any sheet the typed pass couldn't export.
        CsvSheetExporter.ExportRawFallback(langs, overwrite);
    }

    private static List<Type> DiscoverTypes(Type rowInterface) =>
        Assembly.GetAssembly(typeof(Lumina.Excel.Sheets.Action))!.GetTypes()
            .Where(t => t.Namespace == "Lumina.Excel.Sheets"
                && !t.IsAbstract
                && t.GetInterfaces().Any(i => i.IsGenericType
                    && i.GetGenericTypeDefinition() == rowInterface
                    && i.GenericTypeArguments[0] == t))
            .ToList();
}
