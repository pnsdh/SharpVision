using Lumina.Data;

namespace SharpVision.Export;

/// Shared parallelism policy for the extractors: one degree of parallelism and the
/// .dat stream pool that lets Lumina read concurrently across worker threads.
public static class Parallelism
{
    /// Parallel options used by every extractor (one worker per logical core).
    public static readonly ParallelOptions Options = new() { MaxDegreeOfParallelism = Environment.ProcessorCount };

    /// Ensures the GameData has a stream pool so worker threads can each rent their
    /// own .dat handle (Lumina's intended multithreaded read path). Idempotent.
    public static void EnsureStreamPool(Lumina.GameData gd) =>
        gd.StreamPool ??= new SqPackStreamPool(Environment.ProcessorCount * 2);
}
