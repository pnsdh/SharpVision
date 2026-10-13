using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using Lumina.Text.ReadOnly;
using SharpVision.SeStrings;

namespace SharpVision.Export;

/// Converts a typed Excel row (or a raw EXD cell) into CSV column names/values,
/// matching xiv-data-oxidizer's output. SeString cells render via
/// <see cref="SeStringMarkdown"/>.
public static class RowFlattener
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> _propertyCache = new();

    /// Public data properties of a generated sheet/struct type, excluding Lumina's
    /// row plumbing (RowId/SubrowId/ExcelPage/RowOffset). Cached.
    public static PropertyInfo[] GetDataProperties(Type t) =>
        _propertyCache.GetOrAdd(t, static type =>
            type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetIndexParameters().Length == 0 && !IsSkippedColumn(p.Name))
                .ToArray());

    private static bool IsSkippedColumn(string name) =>
        name is "RowId" or "SubrowId" or "ExcelPage" or "RowOffset";

    // Recursively flattens a row property into one or more CSV columns.
    public static void Flatten(string name, object? value, List<string> names, List<string> values)
    {
        if (value is null)
        {
            names.Add(name);
            values.Add(string.Empty);
            return;
        }

        if (value is ReadOnlySeString ros)
        {
            names.Add(name);
            values.Add(SeStringMarkdown.Format(ros));
            return;
        }

        var t = value.GetType();

        // NOTE: the branches below dispatch on Lumina's internal type/namespace
        // names ("Lumina.Excel", "RowRef"/"SubrowRef", "Collection`1",
        // "Lumina.Excel.Sheets"). These are not part of a stable public contract,
        // so if a Lumina upgrade renames a type or moves a namespace, a value that
        // used to flatten as a reference/collection/nested struct would silently
        // fall through to the scalar case instead. Re-check these strings (and
        // `Type.ToString() == "String"` in CsvSheetExporter.GetStringColumns) when
        // bumping Lumina.

        // RowRef / RowRef<T> / SubrowRef / SubrowRef<T> -> referenced row id.
        // Reference columns are signed in the raw EXD, so an unset reference
        // (RowId 0xFFFFFFFF) is written as -1, matching the source data.
        if (t.Namespace == "Lumina.Excel" && (t.Name.StartsWith("RowRef") || t.Name.StartsWith("SubrowRef")))
        {
            var rid = t.GetProperty("RowId")?.GetValue(value);
            int signed = rid is null ? -1 : unchecked((int)Convert.ToUInt32(rid, CultureInfo.InvariantCulture));
            names.Add(name);
            values.Add(signed.ToString(CultureInfo.InvariantCulture));
            return;
        }

        // Collection<T> -> indexed columns Name[i]
        if (t.IsGenericType && t.GetGenericTypeDefinition().FullName == "Lumina.Excel.Collection`1")
        {
            int i = 0;
            foreach (var element in (System.Collections.IEnumerable)value)
            {
                Flatten($"{name}[{i}]", element, names, values);
                i++;
            }
            return;
        }

        // Nested generated struct -> flatten its data properties as Name.Field
        if (t.Namespace == "Lumina.Excel.Sheets" && !t.IsPrimitive && !t.IsEnum)
        {
            foreach (var p in GetDataProperties(t))
                Flatten($"{name}.{p.Name}", p.GetValue(value), names, values);
            return;
        }

        // scalar (numbers, bool, enum, ...)
        names.Add(name);
        values.Add(FormatScalar(value));
    }

    // Formats a raw EXD cell (from RawRow.ReadColumn) into a CSV value.
    public static string FormatRawCell(object? value)
    {
        if (value is null)
            return string.Empty;
        if (value is ReadOnlySeString ros)
            return SeStringMarkdown.Format(ros);
        if (value is string s)
            return s;
        return FormatScalar(value);
    }

    private static string FormatScalar(object value) => value switch
    {
        // Match Rust's f32 Display (used by ironworks/oxidizer): shortest decimal,
        // never scientific notation.
        float f => RustFloat(f),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
    };

    private static string RustFloat(float value)
    {
        var s = value.ToString("R", CultureInfo.InvariantCulture);
        int e = s.IndexOfAny(new[] { 'E', 'e' });
        return e < 0 ? s : ExpandScientific(s, e);
    }

    private static string ExpandScientific(string s, int eIndex)
    {
        string mantissa = s.Substring(0, eIndex);
        int exponent = int.Parse(s.Substring(eIndex + 1), CultureInfo.InvariantCulture);

        bool negative = mantissa.StartsWith("-");
        if (negative)
            mantissa = mantissa.Substring(1);

        int dot = mantissa.IndexOf('.');
        string digits = dot < 0 ? mantissa : mantissa.Remove(dot, 1);
        int pointPos = (dot < 0 ? mantissa.Length : dot) + exponent;

        string result;
        if (pointPos <= 0)
            result = "0." + new string('0', -pointPos) + digits;
        else if (pointPos >= digits.Length)
            result = digits + new string('0', pointPos - digits.Length);
        else
            result = digits.Substring(0, pointPos) + "." + digits.Substring(pointPos);

        return negative ? "-" + result : result;
    }
}
