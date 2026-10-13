using SharpVision.Cli;

namespace SharpVision.Export;

/// Shared driver for an extraction pass: a progress bar, the parallelism policy,
/// per-item error isolation, and the disk-space short-circuit — all in one place,
/// so cross-cutting concerns (progress, disk guard, …) are wired here instead of in
/// every exporter. Each exporter supplies the items and the per-item work;
/// describe provides the log context when an item fails.
///
/// SoundExporter deliberately does NOT use this (it needs per-item output counts and
/// classified skip reasons) — see its remarks for the rationale and the conditions
/// under which it could be folded in later.
public static class ExportRun
{
    /// Parallel pass over a collection.
    public static void ForEach<T>(string label, IReadOnlyList<T> items, Action<T> work, Func<T, string>? describe = null)
    {
        using var progress = new Progress(label, items.Count);
        Parallel.ForEach(items, Parallelism.Options, item =>
        {
            progress.Tick();
            if (DiskGuard.Tripped) return;
            try { work(item); }
            catch (Exception ex) { Report(ex, label, describe?.Invoke(item)); }
        });
    }

    /// Parallel pass over the integer range [first, lastInclusive]. Avoids
    /// materializing the range as a collection (the icon id space is large).
    public static void For(string label, int first, int lastInclusive, Action<int> work, Func<int, string>? describe = null)
    {
        using var progress = new Progress(label, lastInclusive - first + 1);
        Parallel.For(first, lastInclusive + 1, Parallelism.Options, i =>
        {
            progress.Tick();
            if (DiskGuard.Tripped) return;
            try { work(i); }
            catch (Exception ex) { Report(ex, label, describe?.Invoke(i)); }
        });
    }

    /// Sequential pass, for small sets where parallelism isn't worth it.
    public static void Sequential<T>(string label, IReadOnlyList<T> items, Action<T> work, Func<T, string>? describe = null)
    {
        using var progress = new Progress(label, items.Count);
        foreach (var item in items)
        {
            progress.Tick();
            if (DiskGuard.Tripped) break;
            try { work(item); }
            catch (Exception ex) { Report(ex, label, describe?.Invoke(item)); }
        }
    }

    // Out-of-disk stops the whole run (DiskGuard reports once at the end); any other
    // failure is logged and the pass continues with the next item.
    private static void Report(Exception ex, string label, string? context)
    {
        if (DiskGuard.Trip(ex)) return;
        Style.Error($"\n{context ?? label}: {(ex.InnerException ?? ex).Message}");
    }
}
