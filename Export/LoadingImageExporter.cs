using Lumina.Data.Files;
using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;
using SharpVision.Cli;
using SixLabors.ImageSharp;

namespace SharpVision.Export;

/// Extracts loading-screen image textures to JPEG.
public static class LoadingImageExporter
{
    public static void Export(Lumina.GameData lumina, bool overwrite)
    {
        var sheet = lumina.GetExcelSheet<LoadingImage>();
        if (sheet == null)
            return;

        // Schema names this column "FileName"; fall back to the sheet's only string
        // column so a future rename doesn't silently break extraction.
        if (!SheetColumns.TryGetStringColumn(typeof(LoadingImage), "FileName", "LoadingImageExporter",
                out var fileName, fallbackToFirst: true))
            return;

        string directoryPath = Output.Dir("loadingimages");
        Directory.CreateDirectory(directoryPath);

        // Collect the (non-empty) image file names up front, then extract.
        var names = new List<string>();
        foreach (var loadingImage in sheet)
        {
            var fileValue = fileName.GetValue(loadingImage);
            if (fileValue == null)
                continue;
            var fileString = ((ReadOnlySeString)fileValue).AsSpan().ToString();
            if (!string.IsNullOrEmpty(fileString))
                names.Add(fileString);
        }

        // Few images and small files, so a sequential pass is fine.
        ExportRun.Sequential("Loading images", names, fileString =>
        {
            var outputFilePath = Path.Combine(directoryPath, fileString + ".jpg");
            if (!PathGuard.IsWithin(directoryPath, outputFilePath))
            {
                Style.Error($"\nSkipped loading image {fileString}: output path escapes the folder.");
                return;
            }
            if (!overwrite && File.Exists(outputFilePath))
                return;

            string filePath = "ui/loadingimage/" + fileString + ".tex";
            if (!lumina.FileExists(filePath))
                return;

            var file = lumina.GetFile<TexFile>(filePath);
            if (file == null)
                return;

            var image = TexImage.GetImage(file);
            if (image == null)
            {
                Style.Error($"\nFailed to decode loading image {fileString}");
                return;
            }

            AtomicWrite.To(outputFilePath, tmp => image.SaveAsJpeg(tmp));
        }, fileString => $"Failed to write loading image {fileString}");
    }
}
