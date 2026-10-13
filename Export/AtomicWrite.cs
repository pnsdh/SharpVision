namespace SharpVision.Export;

/// Writes a file atomically: the work writes to a temporary file in the same
/// directory, which is then renamed over the destination. So an interrupted write
/// (crash, Ctrl+C, out of disk) never leaves a half-written file at the final path —
/// re-running cleanly recreates it. The temp shares the destination's directory
/// (hence volume) so the rename is a true atomic replace.
public static class AtomicWrite
{
    /// Calls <paramref name="writeToTemp"/> with a temp path to write, then moves it
    /// over <paramref name="path"/>. The destination directory must already exist.
    public static void To(string path, Action<string> writeToTemp)
    {
        string temp = path + ".tmp";
        try
        {
            writeToTemp(temp);
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { /* best effort */ }
            throw;
        }
    }
}
