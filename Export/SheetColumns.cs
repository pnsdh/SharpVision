using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Lumina.Text.ReadOnly;
using SharpVision.Cli;

namespace SharpVision.Export;

/// Locates the string (<see cref="ReadOnlySeString"/>) column an extractor reads
/// from a sheet, guarding against schema renames. These names come from the
/// generated schema, so a rename should warn loudly rather than silently export
/// nothing.
public static class SheetColumns
{
    /// Finds the ReadOnlySeString column named <paramref name="preferredName"/>; if
    /// absent and <paramref name="fallbackToFirst"/> is set, the first ReadOnlySeString
    /// column on the sheet. Warns (prefixed with <paramref name="label"/>) and returns
    /// false if none is found.
    public static bool TryGetStringColumn(Type sheetType, string preferredName, string label,
        [NotNullWhen(true)] out PropertyInfo? column, bool fallbackToFirst = false)
    {
        var named = sheetType.GetProperty(preferredName);
        if (named != null && named.PropertyType == typeof(ReadOnlySeString))
        {
            column = named;
            return true;
        }

        column = fallbackToFirst
            ? Array.Find(sheetType.GetProperties(), p => p.PropertyType == typeof(ReadOnlySeString))
            : null;
        if (column != null)
            return true;

        Style.Warn($"{label}: '{preferredName}' (ReadOnlySeString) column not found on {sheetType.Name}; skipping.");
        return false;
    }
}
