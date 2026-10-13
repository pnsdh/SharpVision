namespace SharpVision.Diff;

internal sealed record CellChange(string Column, string Old, string New);
internal sealed record RowChange(string Key, List<CellChange> Cells);

/// Row- and column-level differences between two versions of one sheet.
internal sealed class SheetDiff
{
    public List<string> AddedRows { get; } = new();
    public List<string> RemovedRows { get; } = new();
    public List<RowChange> ChangedRows { get; } = new();
    public List<string> AddedColumns { get; } = new();
    public List<string> RemovedColumns { get; } = new();

    /// True when one side is schema-backed (named header) and the other is a raw
    /// (header-less) export — columns can't be name-aligned, so cell-level changes
    /// are positional best-effort. Row add/remove stays reliable (keyed by RowId).
    public bool HeaderMismatch { get; set; }

    public bool HasChanges =>
        AddedRows.Count > 0 || RemovedRows.Count > 0 || ChangedRows.Count > 0
        || AddedColumns.Count > 0 || RemovedColumns.Count > 0 || HeaderMismatch;
}

internal static class CsvDiff
{
    public static SheetDiff Compare(CsvFile oldF, CsvFile newF)
    {
        var diff = new SheetDiff { HeaderMismatch = oldF.HasHeader != newF.HasHeader };

        // Align columns by name when both have a header (a column can be inserted or
        // dropped between patches, which would shift indices); otherwise positionally.
        var pairs = new List<(string Name, int OldIdx, int NewIdx)>();
        if (oldF.HasHeader && newF.HasHeader)
        {
            var oldIdx = IndexByName(oldF.Columns);
            var newIdx = IndexByName(newF.Columns);
            foreach (var name in newF.Columns)
                if (oldIdx.TryGetValue(name, out var oi))
                    pairs.Add((name, oi, newIdx[name]));
                else
                    diff.AddedColumns.Add(name);
            foreach (var name in oldF.Columns)
                if (!newIdx.ContainsKey(name))
                    diff.RemovedColumns.Add(name);
        }
        else
        {
            int common = Math.Min(oldF.Columns.Count, newF.Columns.Count);
            for (int i = 0; i < common; i++)
                pairs.Add((newF.Columns[i], i, i));
            for (int i = common; i < newF.Columns.Count; i++)
                diff.AddedColumns.Add(newF.Columns[i]);
            for (int i = common; i < oldF.Columns.Count; i++)
                diff.RemovedColumns.Add(oldF.Columns[i]);
        }

        foreach (var key in newF.Rows.Keys)
            if (!oldF.Rows.ContainsKey(key))
                diff.AddedRows.Add(key);
        foreach (var key in oldF.Rows.Keys)
            if (!newF.Rows.ContainsKey(key))
                diff.RemovedRows.Add(key);

        foreach (var (key, newCells) in newF.Rows)
        {
            if (!oldF.Rows.TryGetValue(key, out var oldCells))
                continue;
            List<CellChange>? cells = null;
            foreach (var (name, oi, ni) in pairs)
            {
                string o = oi < oldCells.Length ? oldCells[oi] : string.Empty;
                string n = ni < newCells.Length ? newCells[ni] : string.Empty;
                if (o != n)
                    (cells ??= new()).Add(new CellChange(name, o, n));
            }
            if (cells != null)
                diff.ChangedRows.Add(new RowChange(key, cells));
        }

        diff.AddedRows.Sort(KeyComparer.Instance);
        diff.RemovedRows.Sort(KeyComparer.Instance);
        diff.ChangedRows.Sort((a, b) => KeyComparer.Instance.Compare(a.Key, b.Key));
        return diff;
    }

    private static Dictionary<string, int> IndexByName(IReadOnlyList<string> cols)
    {
        var map = new Dictionary<string, int>();
        for (int i = 0; i < cols.Count; i++)
            map[cols[i]] = i; // unique in practice; last wins otherwise
        return map;
    }
}

/// Orders CSV keys numerically (RowId, then SubrowId for "RowId.SubrowId"), so the
/// reports read in row order rather than lexical ("10" after "9", not after "1").
internal sealed class KeyComparer : System.Collections.Generic.IComparer<string>
{
    public static readonly KeyComparer Instance = new();

    public int Compare(string? a, string? b)
    {
        var (ar, asub) = Split(a);
        var (br, bsub) = Split(b);
        int c = ar.CompareTo(br);
        if (c != 0) return c;
        c = asub.CompareTo(bsub);
        if (c != 0) return c;
        return string.CompareOrdinal(a, b);
    }

    private static (long Row, long Sub) Split(string? key)
    {
        if (string.IsNullOrEmpty(key))
            return (long.MaxValue, 0);
        int dot = key.IndexOf('.');
        string rowPart = dot < 0 ? key : key[..dot];
        string subPart = dot < 0 ? string.Empty : key[(dot + 1)..];
        long row = long.TryParse(rowPart, out var r) ? r : long.MaxValue;
        long sub = long.TryParse(subPart, out var s) ? s : 0;
        return (row, sub);
    }
}
