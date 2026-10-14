namespace SharpVision.Cli;

/// Startup banner: program name/version (+ source commit) plus the referenced
/// Lumina.Excel and EXDSchema versions. Values come from the build-generated
/// <see cref="BuildInfo"/> (see the GenerateBuildInfo target in SharpVision.csproj).
public static class Banner
{
    public static void Print()
    {
        var commit = BuildInfo.Commit != "unknown" ? $" ({BuildInfo.Commit})" : "";
        Style.WriteLine($"SharpVision v{BuildInfo.Version}{commit}", ConsoleColor.Cyan);

        if (!string.IsNullOrEmpty(BuildInfo.Lumina))
            Console.WriteLine($"  Lumina: {BuildInfo.Lumina}");

        if (!string.IsNullOrEmpty(BuildInfo.LuminaExcel) && BuildInfo.LuminaExcel != "unknown")
            Console.WriteLine($"  Lumina.Excel: {BuildInfo.LuminaExcel}");

        if (!string.IsNullOrEmpty(BuildInfo.ExdSchemaVersion) && BuildInfo.ExdSchemaVersion != "unknown")
            Console.WriteLine($"  {BuildInfo.ExdSchemaName}: {BuildInfo.ExdSchemaVersion}");

        Console.WriteLine();
    }
}
