using System.Collections.Concurrent;
using System.Globalization;
using Lumina.Excel;
using SharpVision.SeStrings;

namespace SharpVision.Export;

/// Exports Excel sheets to CSV (csv/&lt;lang&gt;/&lt;Sheet&gt;.csv): named columns
/// flattened from the typed sheets (via <see cref="RowFlattener"/>), with a raw
/// fallback (by EXH column) for any sheet the typed pass can't load. CSV writing
/// goes through <see cref="Csv"/>.
public static class CsvSheetExporter
{
    // (lang, sheetName) pairs the typed pass exported, so the raw fallback knows
    // which sheets still need exporting. Concurrent: the typed pass runs in parallel.
    private static readonly ConcurrentDictionary<(string Lang, string Sheet), byte> _exportedSheets = new();
    private static void MarkExported(string lang, string sheet) => _exportedSheets[(lang, sheet)] = 0;
    private static bool AlreadyExported(string lang, string sheet) => _exportedSheets.ContainsKey((lang, sheet));

    public static void ExportRow<T>(Lumina.GameData lumina, string lang, bool overwrite) where T : struct, IExcelRow<T>
    {
        var sheet = lumina.GetExcelSheet<T>();
        if (sheet == null)
            return;

        var path = Csv.FilePath(lang, typeof(T).Name);
        if (!overwrite && File.Exists(path))
        {
            MarkExported(lang, typeof(T).Name); // so the raw fallback skips it too
            return;
        }

        var props = RowFlattener.GetDataProperties(typeof(T));
        var rowIdProp = typeof(T).GetProperty("RowId")!;

        AtomicWrite.To(path, tmp =>
        {
            using var writer = new StreamWriter(tmp, false);
            bool headerWritten = false;
            foreach (var row in sheet)
            {
                object boxed = row;
                var names = new List<string>();
                var values = new List<string>();
                foreach (var p in props)
                    RowFlattener.Flatten(p.Name, p.GetValue(boxed), names, values);

                if (!headerWritten)
                {
                    Csv.WriteLine(writer, Csv.Prepend("#", names));
                    headerWritten = true;
                }

                var id = Convert.ToString(rowIdProp.GetValue(boxed), CultureInfo.InvariantCulture) ?? string.Empty;
                Csv.WriteLine(writer, Csv.Prepend(id, values));
            }
        });

        MarkExported(lang, typeof(T).Name);
    }

    public static void ExportSubrow<T>(Lumina.GameData lumina, string lang, bool overwrite) where T : struct, IExcelSubrow<T>
    {
        var sheet = lumina.GetSubrowExcelSheet<T>();
        if (sheet == null)
            return;

        var path = Csv.FilePath(lang, typeof(T).Name);
        if (!overwrite && File.Exists(path))
        {
            MarkExported(lang, typeof(T).Name); // so the raw fallback skips it too
            return;
        }

        var props = RowFlattener.GetDataProperties(typeof(T));
        var (rawColumnCount, stringCols) = GetStringColumns(lumina, typeof(T).Name);

        AtomicWrite.To(path, tmp =>
        {
            using var writer = new StreamWriter(tmp, false);
            bool headerWritten = false;
            foreach (var rows in sheet)
            {
                var subrows = new List<T>();
                foreach (var r in rows)
                    subrows.Add(r);

                // Base for the row's shared string pool (Lumina subrow string-buffer
                // workaround; see PORTING.md "Subrow shared string buffer").
                // Lumina reads each subrow's strings against that subrow's own
                // RowOffset; that's only correct for the LAST subrow, because the
                // EXD anchors the shared string pool right after it. So all subrows
                // must resolve strings against the last subrow's RowOffset. (For a
                // single-subrow row this equals Lumina's own base, i.e. no change.)
                bool fixStrings = stringCols.Count > 0 && subrows.Count > 0;
                nuint sharedBase = fixStrings ? (nuint)subrows[subrows.Count - 1].RowOffset : 0;

                foreach (var row in subrows)
                {
                    object boxed = row;
                    var names = new List<string>();
                    var values = new List<string>();
                    foreach (var p in props)
                        RowFlattener.Flatten(p.Name, p.GetValue(boxed), names, values);

                    // Work around Lumina's subrow string-buffer bug: re-read string
                    // columns against the row's shared pool base. Only for flat sheets
                    // (1:1 column<->value), which is where it's safe and needed.
                    // Guarded so a geometry mismatch can never abort the whole export.
                    if (fixStrings && values.Count == rawColumnCount)
                    {
                        var page = row.ExcelPage;
                        nuint rowOff = (nuint)row.RowOffset;
                        foreach (var (idx, fieldOffset) in stringCols)
                        {
                            try
                            {
                                // ReadString(fieldPos, base): reads the u32 offset at the
                                // field, then the string at base + offset.
                                values[idx] = SeStringMarkdown.Format(page.ReadString(rowOff + fieldOffset, sharedBase));
                            }
                            catch (Exception)
                            {
                                // Keep Lumina's value if the corrected read fails for any
                                // reason (off-page, layout assumption no longer holds, etc.).
                            }
                        }
                    }

                    if (!headerWritten)
                    {
                        Csv.WriteLine(writer, Csv.Prepend("#", names));
                        headerWritten = true;
                    }

                    // Use the stored subrow id (matches oxidizer), not Lumina's sequential index.
                    var id = $"{row.RowId}.{StoredSubrowId(row.ExcelPage, (nuint)row.RowOffset)}";
                    Csv.WriteLine(writer, Csv.Prepend(id, values));
                }
            }
        });

        MarkExported(lang, typeof(T).Name);
    }

    // Raw fallback: exports any sheet the typed pass couldn't (column-layout/
    // version mismatch -> typed GetExcelSheet returns null). Reads columns from
    // the client's own EXH via RawExcelSheet, so it works regardless of version.
    // No schema names available -> no header (matches oxidizer for schema-less sheets).
    public static void ExportRawFallback(IReadOnlyList<(Lumina.GameData Gd, string Code)> langs, bool overwrite)
    {
        // Build the work list first so progress has a total.
        var work = new List<(Lumina.GameData Gd, string Code, string Name)>();
        foreach (var (gd, code) in langs)
            foreach (var name in gd.Excel.SheetNames)
                if (!AlreadyExported(code, name))
                    work.Add((gd, code, name));

        if (work.Count == 0)
            return;

        // Match oxidizer: a failed sheet is logged (by ExportRun) and the rest continue.
        ExportRun.ForEach("CSV sheets (raw)", work, w =>
        {
            var (gd, code, name) = w;
            if (!overwrite && File.Exists(Csv.FilePath(code, name)))
                return;
            ExportRaw(gd, name, code);
        }, w => $"Failed to export {w.Name} ({w.Code})");
    }

    private static void ExportRaw(Lumina.GameData lumina, string sheetName, string lang)
    {
        var raw = lumina.Excel.GetRawSheet(sheetName, null);
        if (raw == null)
            return;

        int columnCount = raw.Columns.Count;
        // Read columns in offset order (matches how the raw EXD lays them out).
        var order = Enumerable.Range(0, columnCount)
            .OrderBy(raw.GetColumnOffset)
            .ToArray();

        AtomicWrite.To(Csv.FilePath(lang, sheetName), tmp =>
        {
            using var writer = new StreamWriter(tmp, false);

            if (raw is RawSubrowExcelSheet)
            {
                var sheet = lumina.GetSubrowExcelSheet<RawSubrow>(null, sheetName);
                if (sheet == null)
                    return;
                foreach (var rows in sheet)
                    foreach (var row in rows)
                    {
                        var values = order.Select(i => RowFlattener.FormatRawCell(row.ReadColumn(i))).ToList();
                        Csv.WriteLine(writer, Csv.Prepend($"{row.RowId}.{StoredSubrowId(row.ExcelPage, (nuint)row.RowOffset)}", values));
                    }
            }
            else
            {
                var sheet = lumina.GetExcelSheet<RawRow>(null, sheetName);
                if (sheet == null)
                    return;
                foreach (var row in sheet)
                {
                    var values = order.Select(i => RowFlattener.FormatRawCell(row.ReadColumn(i))).ToList();
                    Csv.WriteLine(writer, Csv.Prepend(row.RowId.ToString(CultureInfo.InvariantCulture), values));
                }
            }
        });
    }

    // Lumina numbers raw subrows sequentially (0,1,2,...), but the EXD stores an
    // explicit 2-byte subrow id in the SubrowHeader immediately before each
    // subrow's field data. ironworks/oxidizer emit that stored id, so read it
    // straight from the page (RowOffset points at the field data; the id is the
    // big-endian u16 two bytes before it).
    private static ushort StoredSubrowId(ExcelPage page, nuint rowOffset) =>
        page.ReadUInt16(rowOffset - (nuint)2);

    // Returns the sheet's data-column count and the String columns (index + field
    // offset), used to re-read subrow strings from the shared buffer.
    private static (int ColumnCount, List<(int Index, uint Offset)> StringColumns) GetStringColumns(Lumina.GameData lumina, string sheetName)
    {
        var stringColumns = new List<(int, uint)>();
        var raw = lumina.Excel.GetRawSheet(sheetName, null);
        if (raw == null)
            return (0, stringColumns);

        int count = raw.Columns.Count;
        for (int c = 0; c < count; c++)
            if (raw.Columns[c].Type.ToString() == "String")
                stringColumns.Add((c, (uint)raw.GetColumnOffset(c)));

        return (count, stringColumns);
    }
}
