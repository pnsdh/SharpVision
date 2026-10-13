using System.Collections.Concurrent;
using SharpVision.Cli;

namespace SharpVision.Diff;

/// Added / removed / changed file lists (relative paths, '/'-separated) for one
/// asset category folder.
internal sealed class TreeDiff
{
    public List<string> Added { get; } = new();
    public List<string> Removed { get; } = new();
    public List<string> Changed { get; } = new();
}

/// Compares two folders as opaque file trees: which files were added, removed, or
/// changed (byte content differs). Used for icons/maps/sound/loading, where only
/// "did this asset change" matters (no structured content diff).
internal static class FileTreeDiff
{
    public static TreeDiff Compare(string oldDir, string newDir, string label)
    {
        var oldFiles = DiffFs.Enumerate(oldDir);
        var newFiles = DiffFs.Enumerate(newDir);
        var (added, removed, common) = DiffFs.Partition(oldFiles, newFiles);

        var result = new TreeDiff();
        result.Added.AddRange(added);
        result.Removed.AddRange(removed);

        if (common.Count > 0)
        {
            var changed = new ConcurrentBag<string>();
            using var progress = new Progress($"diff {label}", common.Count);
            Parallel.ForEach(common, DiffFs.Parallel, rel =>
            {
                progress.Tick();
                if (!ContentEqual(oldFiles[rel], newFiles[rel]))
                    changed.Add(rel);
            });
            result.Changed.AddRange(changed);
            result.Changed.Sort(StringComparer.OrdinalIgnoreCase);
        }
        return result;
    }

    private const int BufSize = 1 << 16;

    private static bool ContentEqual(string a, string b)
    {
        var fa = new FileInfo(a);
        var fb = new FileInfo(b);
        if (fa.Length != fb.Length)
            return false;
        if (fa.Length == 0)
            return true;

        // Rent buffers so comparing 100k+ icons doesn't churn the GC. Rent may return
        // a larger array, so always read/compare exactly BufSize bytes per block to
        // keep both sides aligned.
        var pool = System.Buffers.ArrayPool<byte>.Shared;
        byte[] ba = pool.Rent(BufSize);
        byte[] bb = pool.Rent(BufSize);
        try
        {
            using var sa = fa.OpenRead();
            using var sb = fb.OpenRead();
            int read;
            while ((read = ReadBlock(sa, ba)) > 0)
            {
                ReadBlock(sb, bb); // equal length -> same block sizes, so they align
                if (!ba.AsSpan(0, read).SequenceEqual(bb.AsSpan(0, read)))
                    return false;
            }
            return true;
        }
        finally
        {
            pool.Return(ba);
            pool.Return(bb);
        }
    }

    private static int ReadBlock(Stream s, byte[] buf)
    {
        int total = 0, n;
        while (total < BufSize && (n = s.Read(buf, total, BufSize - total)) > 0)
            total += n;
        return total;
    }
}
