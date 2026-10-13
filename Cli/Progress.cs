using System.Diagnostics;

namespace SharpVision.Cli;

/// Live, single-line progress for one phase of work. Redraws in place via a
/// carriage return (throttled to ~10 fps so the drawing isn't itself a
/// bottleneck), showing a bar, percent, count, elapsed time and ETA. When output
/// is redirected (no console) it falls back to occasional plain lines instead of
/// in-place redraws. Dispose() finalizes with a summary line.
public sealed class Progress : IDisposable
{
    private const int BarWidth = 20;

    private readonly string _label;
    private readonly int _total;
    private readonly Stopwatch _sw = Stopwatch.StartNew();
    private readonly bool _live;
    private readonly long _drawIntervalMs;

    private readonly object _drawLock = new();
    private int _done;
    private long _lastDrawMs;
    private int _lastLineLen;

    public Progress(string label, int total)
    {
        _label = label;
        _total = Math.Max(0, total);
        _live = !Console.IsOutputRedirected;
        _drawIntervalMs = _live ? 100 : 3000;
        // Ensure the first Tick draws (and avoid overflow from a MinValue sentinel).
        _lastDrawMs = -_drawIntervalMs - 1;
    }

    /// Advances the counter by one and (throttled) redraws. Safe to call
    /// concurrently (e.g. from Parallel.For): the counter is atomic and drawing
    /// is serialized.
    public void Tick()
    {
        Interlocked.Increment(ref _done);
        lock (_drawLock)
            Draw(force: false);
    }

    private void Draw(bool force)
    {
        long ms = _sw.ElapsedMilliseconds;
        if (!force && ms - _lastDrawMs < _drawIntervalMs)
            return;
        _lastDrawMs = ms;

        double frac = _total > 0 ? Math.Min(1.0, (double)_done / _total) : 0;
        int fill = (int)Math.Round(frac * BarWidth);
        string bar = new string('#', fill) + new string('-', BarWidth - fill);
        TimeSpan eta = _done > 0 && _total > 0
            ? TimeSpan.FromMilliseconds(ms * (double)(_total - _done) / _done)
            : TimeSpan.Zero;

        int digits = _total.ToString().Length;
        string count = $"{_done.ToString().PadLeft(digits)}/{_total}";
        string line = $"{_label} [{bar}] {frac * 100,3:0}%  {count}  " +
                      $"elapsed {Fmt(_sw.Elapsed)}  eta {Fmt(eta)}";

        if (_live)
        {
            int width = SafeWidth();
            bool truncated = false;
            if (width > 0 && line.Length > width - 1)
            {
                line = line.Substring(0, width - 1);
                truncated = true;
            }
            if (line.Length < _lastLineLen)
                line += new string(' ', _lastLineLen - line.Length); // overwrite leftovers
            _lastLineLen = line.Length;
            Console.Write('\r');

            // Colorize in place. The prefix "{label} [{bar}] " has known fixed
            // widths, so we can recolor segments without disturbing the length math
            // above. Skip when the line was truncated to a narrow terminal.
            if (Style.Enabled && !truncated)
            {
                int tail = _label.Length + 2 + BarWidth + 2; // label + " [" + bar + "] "
                Style.Write(_label, ConsoleColor.Cyan);
                Console.Write(" [");
                Style.Write(new string('#', fill), ConsoleColor.Green);
                Console.Write(new string('-', BarWidth - fill));
                Console.Write("] ");
                Console.Write(line.Substring(tail));
            }
            else
            {
                Console.Write(line);
            }
        }
        else
        {
            Console.WriteLine(line);
        }
    }

    public void Dispose()
    {
        string summary = $"{_label}  {_done}/{_total}  done in {Fmt(_sw.Elapsed)}";
        if (_live)
        {
            if (summary.Length < _lastLineLen)
                summary += new string(' ', _lastLineLen - summary.Length);
            Console.Write('\r');
        }
        Style.WriteLine(summary, ConsoleColor.Green);
    }

    private static int SafeWidth()
    {
        try { return Console.WindowWidth; }
        catch { return 0; }
    }

    private static string Fmt(TimeSpan t) => $"{(int)t.TotalMinutes:00}:{t.Seconds:00}";
}
