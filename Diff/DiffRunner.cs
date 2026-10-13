using System.Collections.Concurrent;
using System.Text;
using SharpVision.Cli;
using SharpVision.Export;

namespace SharpVision.Diff;

/// Compares two already-extracted output folders (output/&lt;region&gt;/&lt;version&gt;/)
/// and reports what was added, removed or changed. Operates purely on the extracted
/// files — it never loads sqpack, so it starts instantly. Assets (icons/maps/sound/
/// loading) are diffed as opaque file trees; CSV sheets get a row/column content diff.
/// A short summary goes to the console; full detail is written under output/diff/.
public static class DiffRunner
{
    // Asset categories (folder name -> short label) diffed by raw file content.
    // Order (after csv) mirrors the `full` extraction: maps, loading, sound, icons.
    private static readonly (string Dir, string Label)[] AssetCategories =
    {
        ("maps", "maps"),
        ("loadingimages", "loading"),
        ("sound", "sound"),
        ("icons", "icons"),
    };

    /// Returns false if the two folders couldn't be resolved (nothing to diff).
    public static bool Run(AppOptions options)
    {
        if (!Resolve(options, out var oldDir, out var newDir, out var region, out var oldName, out var newName))
            return false;

        string reportRoot = region == null
            ? Path.Combine(Output.DiffRoot, $"{oldName}__{newName}")
            : Path.Combine(Output.DiffRoot, region, $"{oldName}__{newName}");
        // Start clean so a re-run doesn't leave stale reports (e.g. a sheet that
        // differed last time but doesn't now). Only ever our own diff subfolder.
        if (Directory.Exists(reportRoot))
        {
            try { Directory.Delete(reportRoot, recursive: true); }
            catch (IOException) { Style.Warn($"Could not clear old report at {reportRoot} (a file may be open); results may be mixed."); }
        }
        Directory.CreateDirectory(reportRoot);

        Console.WriteLine();
        Style.Heading($"diff  {region ?? "(custom)"}   {oldName} -> {newName}");
        Console.WriteLine();

        var counts = new List<CatCount>();

        // CSV: file-level added/removed sheets + per-sheet content diff for the rest.
        var csv = DiffCsv(oldDir, newDir, reportRoot);
        counts.Add(new CatCount("csv", csv.Added.Count, csv.Removed.Count, csv.Changed.Count));

        // Assets: opaque file-tree diffs.
        foreach (var (dir, label) in AssetCategories)
        {
            var td = FileTreeDiff.Compare(Path.Combine(oldDir, dir), Path.Combine(newDir, dir), label);
            WriteList(reportRoot, $"{label}-added.txt", td.Added);
            WriteList(reportRoot, $"{label}-removed.txt", td.Removed);
            WriteList(reportRoot, $"{label}-changed.txt", td.Changed);
            WriteAssetHtml(reportRoot, dir, label, td, oldDir, newDir);
            counts.Add(new CatCount(label, td.Added.Count, td.Removed.Count, td.Changed.Count));
        }

        WriteSummary(reportRoot, region, oldName, newName, counts, csv);
        PrintSummary(counts, csv.HeaderMismatches, reportRoot);
        return true;
    }

    // --- resolution -------------------------------------------------------------

    private static bool Resolve(AppOptions o, out string oldDir, out string newDir,
        out string? region, out string oldName, out string newName)
    {
        oldDir = newDir = oldName = newName = string.Empty;
        region = null;

        if (o.DiffRegion != null)
        {
            string regionDir = Path.Combine(Output.ExtractRoot, o.DiffRegion);
            if (!Directory.Exists(regionDir))
            {
                Style.Error($"No such region folder: {regionDir}");
                Style.Hint("Extract a client first, or pass two folders: diff <oldDir> <newDir>.");
                return false;
            }
            var versions = Directory.GetDirectories(regionDir)
                .OrderBy(Path.GetFileName, StringComparer.Ordinal)
                .ToList();
            if (versions.Count < 2)
            {
                Style.Error($"Need at least two version folders under '{regionDir}' to diff (found {versions.Count}).");
                return false;
            }
            oldDir = versions[^2];
            newDir = versions[^1];
            region = o.DiffRegion;
        }
        else
        {
            oldDir = Path.GetFullPath(o.DiffOld!);
            newDir = Path.GetFullPath(o.DiffNew!);
            if (!Directory.Exists(oldDir)) { Style.Error($"No such folder: {oldDir}"); return false; }
            if (!Directory.Exists(newDir)) { Style.Error($"No such folder: {newDir}"); return false; }
            // If both sit under the same region folder, label the report with it.
            var op = new DirectoryInfo(oldDir).Parent?.Name;
            var np = new DirectoryInfo(newDir).Parent?.Name;
            if (op != null && op == np)
                region = op;
        }

        oldName = new DirectoryInfo(oldDir).Name;
        newName = new DirectoryInfo(newDir).Name;
        return true;
    }

    // --- CSV --------------------------------------------------------------------

    private sealed class CsvResult
    {
        public List<string> Added { get; } = new();   // sheets only in new
        public List<string> Removed { get; } = new();  // sheets only in old
        public List<string> Changed { get; } = new();  // sheets with content changes
        public List<string> HeaderMismatches { get; } = new(); // schema-vs-raw structure differs
    }

    private static CsvResult DiffCsv(string oldDir, string newDir, string reportRoot)
    {
        var oldSheets = DiffFs.Enumerate(Path.Combine(oldDir, "csv"), "*.csv");
        var newSheets = DiffFs.Enumerate(Path.Combine(newDir, "csv"), "*.csv");
        var (added, removed, common) = DiffFs.Partition(oldSheets, newSheets);

        var result = new CsvResult();
        result.Added.AddRange(added);
        result.Removed.AddRange(removed);

        if (common.Count > 0)
        {
            var changed = new ConcurrentBag<string>();
            var mismatches = new ConcurrentBag<string>();
            var combined = new ConcurrentBag<(string Sheet, List<string[]> Rows)>();
            using var progress = new Progress("diff csv", common.Count);
            Parallel.ForEach(common, DiffFs.Parallel, rel =>
            {
                progress.Tick();
                try
                {
                    var diff = CsvDiff.Compare(CsvFile.Load(oldSheets[rel]), CsvFile.Load(newSheets[rel]));
                    if (diff.HeaderMismatch)
                        mismatches.Add(rel);
                    if (diff.HasChanges)
                    {
                        changed.Add(rel);
                        var rows = BuildRows(diff);
                        WriteSheetReport(reportRoot, rel, rows);
                        combined.Add((rel, rows));
                    }
                }
                catch (Exception ex)
                {
                    Style.Error($"\nFailed to diff {rel}: {ex.Message}");
                }
            });
            result.Changed.AddRange(changed);
            result.HeaderMismatches.AddRange(mismatches);
            WriteCombinedCsv(reportRoot, combined);
        }
        result.Changed.Sort(StringComparer.OrdinalIgnoreCase);
        result.HeaderMismatches.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }

    // The diff rows for one sheet (Change,Key,Column,Old,New), in report order.
    // Shared by the per-sheet file and the combined csv-changes.csv.
    private static List<string[]> BuildRows(SheetDiff diff)
    {
        var rows = new List<string[]>();
        if (diff.HeaderMismatch)
            rows.Add(new[] { "note", "", "", "", "schema/raw structure mismatch — cell changes are positional best-effort" });
        foreach (var col in diff.AddedColumns)
            rows.Add(new[] { "column-added", "", col, "", "" });
        foreach (var col in diff.RemovedColumns)
            rows.Add(new[] { "column-removed", "", col, "", "" });
        foreach (var rc in diff.ChangedRows)
            foreach (var cell in rc.Cells)
                rows.Add(new[] { "changed", rc.Key, cell.Column, cell.Old, cell.New });
        foreach (var key in diff.AddedRows)
            rows.Add(new[] { "added", key, "", "", "" });
        foreach (var key in diff.RemovedRows)
            rows.Add(new[] { "removed", key, "", "", "" });
        return rows;
    }

    // Per-sheet diff as CSV (Change,Key,Column,Old,New) so it opens in a spreadsheet
    // for easy filtering/sorting. Reuses the extractor's CSV escaping for consistency.
    private static void WriteSheetReport(string reportRoot, string rel, List<string[]> rows)
    {
        string path = Path.Combine(reportRoot, "csv", rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using var w = new StreamWriter(path, false);
        Csv.WriteLine(w, new[] { "Change", "Key", "Column", "Old", "New" });
        foreach (var row in rows)
            Csv.WriteLine(w, row);
    }

    // All changed sheets in one CSV (Sheet,Change,Key,Column,Old,New). Handy for small
    // patches (hotfix/minor) where opening each per-sheet file is overkill.
    private static void WriteCombinedCsv(string reportRoot, IEnumerable<(string Sheet, List<string[]> Rows)> sheets)
    {
        var ordered = sheets.OrderBy(s => s.Sheet, StringComparer.OrdinalIgnoreCase).ToList();
        if (ordered.Count == 0)
            return;

        using var w = new StreamWriter(Path.Combine(reportRoot, "csv-changes.csv"), false);
        Csv.WriteLine(w, new[] { "Sheet", "Change", "Key", "Column", "Old", "New" });
        foreach (var (sheet, rows) in ordered)
        {
            string name = sheet.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? sheet[..^4] : sheet;
            foreach (var row in rows)
                Csv.WriteLine(w, new[] { name }.Concat(row));
        }
    }

    // --- reports ----------------------------------------------------------------

    private static void WriteList(string reportRoot, string name, List<string> lines)
    {
        if (lines.Count == 0)
            return;
        string dir = Path.Combine(reportRoot, "assets");
        Directory.CreateDirectory(dir);
        File.WriteAllLines(Path.Combine(dir, name), lines);
    }

    // A browsable HTML gallery for one asset category: changed (old → new side by
    // side), added (new), removed (old). Images show as thumbnails (click to open
    // full); sound shows an <audio> player. Files are referenced by absolute file://
    // URI from the two source folders, so the page is viewed on this machine.
    private static void WriteAssetHtml(string reportRoot, string categoryDir, string label, TreeDiff td, string oldDir, string newDir)
    {
        if (td.Added.Count == 0 && td.Removed.Count == 0 && td.Changed.Count == 0)
            return;

        bool audio = label == "sound";
        string oldCat = Path.Combine(oldDir, categoryDir);
        string newCat = Path.Combine(newDir, categoryDir);

        string Media(string baseDir, string rel)
        {
            string uri = new Uri(Path.Combine(baseDir, rel.Replace('/', Path.DirectorySeparatorChar))).AbsoluteUri;
            return audio
                ? $"<audio controls preload=\"none\" src=\"{uri}\"></audio>"
                : $"<a href=\"{uri}\" target=\"_blank\"><img src=\"{uri}\" loading=\"lazy\"></a>";
        }

        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>").Append(label).Append(" diff</title>\n");
        sb.Append("<style>body{font-family:sans-serif;background:#1e1e1e;color:#ddd;margin:16px}")
          .Append("h1{font-size:18px}h2{font-size:15px;border-bottom:1px solid #444;margin-top:24px}")
          .Append(".item{display:inline-block;margin:6px;text-align:center;vertical-align:top;font-size:11px;color:#aaa;max-width:170px;overflow-wrap:anywhere}")
          .Append(".pair{display:flex;gap:4px;justify-content:center}")
          .Append("img{max-width:128px;max-height:128px;background:#333;image-rendering:pixelated}")
          .Append("</style></head><body>\n");
        sb.Append("<h1>").Append(label).Append(" &mdash; old &rarr; new</h1>\n");

        void Section(string title, List<string> rels, bool showOld, bool showNew)
        {
            if (rels.Count == 0)
                return;
            sb.Append("<h2>").Append(title).Append(" (").Append(rels.Count).Append(")</h2>\n");
            foreach (var rel in rels)
            {
                sb.Append("<div class=\"item\"><div class=\"pair\">");
                if (showOld) sb.Append(Media(oldCat, rel));
                if (showNew) sb.Append(Media(newCat, rel));
                sb.Append("</div><div>").Append(HtmlEscape(rel)).Append("</div></div>\n");
            }
        }

        Section("Changed", td.Changed, showOld: true, showNew: true);
        Section("Added", td.Added, showOld: false, showNew: true);
        Section("Removed", td.Removed, showOld: true, showNew: false);
        sb.Append("</body></html>\n");

        string dir = Path.Combine(reportRoot, "assets");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, label + ".html"), sb.ToString());
    }

    private static string HtmlEscape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static void WriteSummary(string reportRoot, string? region, string oldName, string newName,
        List<CatCount> counts, CsvResult csv)
    {
        var sb = new StringBuilder();
        sb.Append("# SharpVision diff\n\n");
        sb.Append("- Region: ").Append(region ?? "(custom)").Append('\n');
        sb.Append("- Old: ").Append(oldName).Append('\n');
        sb.Append("- New: ").Append(newName).Append('\n');
        sb.Append("- Generated: ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append("\n\n");

        sb.Append("## Summary\n\n");
        sb.Append("| Category | Added | Removed | Changed |\n|---|--:|--:|--:|\n");
        foreach (var c in counts)
            sb.Append($"| {c.Name} | {c.Added} | {c.Removed} | {c.Changed} |\n");

        AppendSheetList(sb, "Added sheets", csv.Added);
        AppendSheetList(sb, "Removed sheets", csv.Removed);
        AppendSheetList(sb, "Changed sheets (detail in csv/)", csv.Changed);
        AppendSheetList(sb, "⚠ Schema/raw structure mismatch (cell diff unreliable)", csv.HeaderMismatches);

        sb.Append("\n## Assets\n\nPer category, see `assets/<category>.html` for a visual gallery of the changed\nassets, and `assets/<category>-{added,removed,changed}.txt` for the path lists.\n");

        File.WriteAllText(Path.Combine(reportRoot, "summary.md"), sb.ToString());
    }

    private static void AppendSheetList(StringBuilder sb, string title, List<string> sheets)
    {
        if (sheets.Count == 0)
            return;
        sb.Append("\n## ").Append(title).Append(" (").Append(sheets.Count).Append(")\n\n");
        foreach (var s in sheets)
            sb.Append("- ").Append(s).Append('\n');
    }

    private static void PrintSummary(List<CatCount> counts, List<string> headerMismatches, string reportRoot)
    {
        int wa = counts.Max(c => c.Added.ToString().Length);
        int wr = counts.Max(c => c.Removed.ToString().Length);
        int wc = counts.Max(c => c.Changed.ToString().Length);

        foreach (var c in counts)
        {
            Console.Write($"  {c.Name,-9} ");
            Style.Write(("+" + c.Added).PadLeft(wa + 1), ConsoleColor.Green);
            Console.Write("  ");
            Style.Write(("-" + c.Removed).PadLeft(wr + 1), ConsoleColor.Red);
            Console.Write("  ");
            Style.Write(("~" + c.Changed).PadLeft(wc + 1), ConsoleColor.Yellow);
            Console.WriteLine();
        }

        if (headerMismatches.Count > 0)
        {
            Console.WriteLine();
            Style.Warn($"{headerMismatches.Count} sheet(s) had a schema-vs-raw structure mismatch; their " +
                       "cell-level diffs are positional best-effort (row add/remove is still reliable).");
            Style.Hint("Re-extract both versions with the same build/schema for an exact cell diff.");
        }

        Console.WriteLine();
        Console.Write("Wrote report to ");
        Style.WriteLine(reportRoot, ConsoleColor.Cyan);
    }

    private sealed record CatCount(string Name, int Added, int Removed, int Changed);
}
