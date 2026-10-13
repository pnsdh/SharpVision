namespace SharpVision.Cli;

/// Small console-color helper for CLI messages. Uses <see cref="Console.ForegroundColor"/>
/// (not ANSI escapes) so it works on legacy Windows consoles and, crucially, adds
/// no characters to the text — the progress bar's width/truncation math stays
/// correct. Color is applied only to a real console (not when redirected) and is
/// suppressed when the standard NO_COLOR variable is set, so piped output stays clean.
public static class Style
{
    public static readonly bool Enabled =
        !Console.IsOutputRedirected
        && Environment.GetEnvironmentVariable("NO_COLOR") is not { Length: > 0 };

    /// Writes a colored segment (no newline) then restores the previous color.
    public static void Write(string text, ConsoleColor color)
    {
        if (!Enabled)
        {
            Console.Write(text);
            return;
        }
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.Write(text);
        Console.ForegroundColor = prev;
    }

    public static void WriteLine(string text, ConsoleColor color)
    {
        Write(text, color);
        Console.WriteLine();
    }

    // Semantic shortcuts (full line).
    public static void Error(string text) => WriteLine(text, ConsoleColor.Red);
    public static void Warn(string text) => WriteLine(text, ConsoleColor.Yellow);
    public static void Success(string text) => WriteLine(text, ConsoleColor.Green);
    public static void Heading(string text) => WriteLine(text, ConsoleColor.Cyan);
    public static void Hint(string text) => WriteLine(text, ConsoleColor.DarkGray);
}
