using System.Text;

namespace SharpVision.Diff;

/// A parsed extracted-CSV file: column names and rows keyed by the first field
/// (RowId, or RowId.SubrowId for subrow sheets). Mirrors the writer in
/// Export/Csv.cs — '\n'-terminated records, RFC4180-style quoting (a field is
/// quoted when it contains a comma, quote, CR or LF; an embedded quote is doubled).
///
/// Schema-backed sheets start with a '#' header row naming the columns; the raw
/// fallback writes no header (HasHeader == false), so columns are positional
/// ("col0", "col1", ...) and aligned by index when diffing.
internal sealed class CsvFile
{
    public bool HasHeader { get; }
    public IReadOnlyList<string> Columns { get; }
    public IReadOnlyDictionary<string, string[]> Rows { get; }

    private CsvFile(bool hasHeader, IReadOnlyList<string> columns, Dictionary<string, string[]> rows)
    {
        HasHeader = hasHeader;
        Columns = columns;
        Rows = rows;
    }

    public static CsvFile Load(string path)
    {
        var records = ParseRecords(File.ReadAllText(path));
        bool hasHeader = records.Count > 0 && records[0].Count > 0 && records[0][0] == "#";

        List<string> columns;
        int firstDataIndex;
        if (hasHeader)
        {
            columns = records[0].GetRange(1, records[0].Count - 1);
            firstDataIndex = 1;
        }
        else
        {
            int maxCells = 0;
            foreach (var r in records)
                maxCells = Math.Max(maxCells, r.Count - 1);
            columns = Enumerable.Range(0, Math.Max(0, maxCells)).Select(i => $"col{i}").ToList();
            firstDataIndex = 0;
        }

        var rows = new Dictionary<string, string[]>();
        for (int i = firstDataIndex; i < records.Count; i++)
        {
            var rec = records[i];
            if (rec.Count == 0)
                continue;
            // Duplicate keys don't occur in practice (RowId / RowId.SubrowId are
            // unique); last one wins if they ever did.
            rows[rec[0]] = rec.GetRange(1, rec.Count - 1).ToArray();
        }

        return new CsvFile(hasHeader, columns, rows);
    }

    // Splits CSV text into records (each a list of unescaped fields), handling
    // quoted fields that contain commas, doubled quotes, and embedded CR/LF.
    private static List<List<string>> ParseRecords(string text)
    {
        var records = new List<List<string>>();
        var fields = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        bool pending = false; // a field/record is in progress (vs. clean record boundary)

        void EndField()
        {
            fields.Add(sb.ToString());
            sb.Clear();
        }
        void EndRecord()
        {
            EndField();
            records.Add(fields);
            fields = new List<string>();
            pending = false;
        }

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQuotes = false;
                }
                else sb.Append(c);
                continue;
            }

            switch (c)
            {
                case '"': inQuotes = true; pending = true; break;
                case ',': EndField(); pending = true; break;
                case '\r': break;            // ignore; the paired '\n' ends the record
                case '\n': EndRecord(); break;
                default: sb.Append(c); pending = true; break;
            }
        }

        // Flush a trailing record if the text didn't end on a newline.
        if (pending || sb.Length > 0 || fields.Count > 0)
            EndRecord();

        return records;
    }
}
