namespace SharpVision.Cli;

/// Prints a loaded client's build version and detected languages (shown once the
/// install is chosen, in the interactive menu or — for the CLI path — at startup).
public static class ClientInfo
{
    public static void Print(Lumina.GameData gd, IReadOnlyList<string> codes)
    {
        Console.WriteLine();
        Style.WriteLine(GameVersion.Describe(gd), ConsoleColor.Cyan);
        if (codes.Count == 0)
            return;
        Console.Write("Detected languages: ");
        Style.Write(string.Join(", ", codes), ConsoleColor.Cyan);
        Console.Write(" (primary: ");
        Style.Write(codes[0], ConsoleColor.Cyan);
        Console.WriteLine(").");
    }
}
