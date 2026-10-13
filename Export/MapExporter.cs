using Lumina.Data.Files;
using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;
using SharpVision.Cli;
using SixLabors.ImageSharp;

namespace SharpVision.Export;

/// Extracts map tile textures to JPEG.
public static class MapExporter
{
    public static void Export(Lumina.GameData lumina, bool overwrite)
    {
        var sheet = lumina.GetExcelSheet<Map>();
        if (sheet == null)
            return;

        if (!SheetColumns.TryGetStringColumn(typeof(Map), "Id", "MapExporter", out var idProperty))
            return;

        string directoryPath = Output.Dir("maps");
        Directory.CreateDirectory(directoryPath);

        // Collect the map ids up front (single-threaded sheet access — the Excel
        // module isn't relied on concurrently), de-duplicated so two threads never
        // target the same output file. Extraction below then runs in parallel.
        var ids = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var map in sheet)
        {
            var idValue = idProperty.GetValue(map);
            if (idValue == null)
                continue;
            var idString = ((ReadOnlySeString)idValue).AsSpan().ToString();
            if (!string.IsNullOrEmpty(idString) && seen.Add(idString))
                ids.Add(idString);
        }

        Parallelism.EnsureStreamPool(lumina);

        ExportRun.ForEach("Maps", ids, idString =>
        {
            // Map ids look like "xxx/yyy"; skip anything that doesn't.
            string[] parts = idString.Split('/');
            if (parts.Length != 2)
                return;

            var outputFilePath = Path.Combine(directoryPath, parts[0], $"{parts[0]}.{parts[1]}.jpg");
            if (!PathGuard.IsWithin(directoryPath, outputFilePath))
            {
                Style.Error($"\nSkipped map {idString}: output path escapes the maps folder.");
                return;
            }
            if (!overwrite && File.Exists(outputFilePath))
                return;

            // Map tile sqpack path; some maps use the "m_m" variant instead.
            string filePath = "ui/map/" + idString + "/" + parts[0] + parts[1] + "_m.tex";
            if (!lumina.FileExists(filePath))
                filePath = "ui/map/" + idString + "/" + parts[0] + parts[1] + "m_m.tex";
            if (!lumina.FileExists(filePath))
                return;

            var file = lumina.GetFile<TexFile>(filePath);
            if (file == null)
                return;

            Directory.CreateDirectory(Path.Combine(directoryPath, parts[0])); // idempotent / thread-safe

            var image = TexImage.GetImage(file);
            if (image == null)
            {
                Style.Error($"\nFailed to decode map {idString}");
                return;
            }

            AtomicWrite.To(outputFilePath, tmp => image.SaveAsJpeg(tmp));
        }, idString => $"Failed to extract map {idString}");
    }
}
