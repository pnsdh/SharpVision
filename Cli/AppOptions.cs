namespace SharpVision.Cli;

/// What to extract.
public enum ExtractTarget
{
    Full,
    Csv,
    Icons,
    Maps,
    Loading,
    Sound,
    Diff,
    DumpSeString,
    DumpSubrow,
    DumpScd,
    DumpScdSkips,
    DumpLangTime,
}

/// Which icon resolutions to export.
public enum IconKind
{
    Both,
    Normal,
    Hd,
}

/// Which music sources to extract for the `sound` command.
public enum SoundSource
{
    All,
    Bgm,
    Orchestrion,
}

/// Parsed command-line options. The CLI is command-based:
///   SharpVision [sqpackPath] &lt;command&gt; [options]
/// With no command, the program runs the interactive menu instead.
public sealed class AppOptions
{
    public const int DefaultIconStart = 1;
    // Default upper bound. The path format (ui/icon/{id/1000:D3}000/{id:D6}.tex)
    // can address up to 999999, but icons currently top out around ~251700 (KR
    // client; global may be slightly higher). 259999 covers that with headroom and
    // keeps the progress meaningful (no huge empty tail). Use --range to go higher.
    public const int DefaultIconEnd = 259999;

    public string SqpackPath = string.Empty;
    public ExtractTarget Target;
    public bool Overwrite;
    public int IconStart = DefaultIconStart;
    public int IconEnd = DefaultIconEnd;
    public IconKind IconKind = IconKind.Both;
    public SoundSource SoundSource = SoundSource.All;
    public string? DumpSheet;

    /// `diff` operands: either two explicit output folders (DiffOld/DiffNew) or a
    /// single region (DiffRegion) whose latest two version folders are compared.
    public string? DiffOld;
    public string? DiffNew;
    public string? DiffRegion;

    /// Language codes to extract sheets for (null = all available). Only affects
    /// csv; icons/maps/loading are language-neutral.
    public List<string>? Languages;

    /// Pre-read language probe from the interactive picker (null for the CLI path).
    /// Carried so detection can reuse the already-loaded sqpack index.
    public LanguageDetection.Probe? LanguageProbe;

    /// True when built by the interactive menu (lets the disk-space pre-flight prompt
    /// to continue; the CLI path warns and proceeds without blocking).
    public bool Interactive;

    public enum ParseResult { Parsed, Menu, Help, Error }

    private static readonly Dictionary<string, ExtractTarget> Commands = new(StringComparer.OrdinalIgnoreCase)
    {
        ["full"] = ExtractTarget.Full,
        ["csv"] = ExtractTarget.Csv,
        ["icons"] = ExtractTarget.Icons,
        ["maps"] = ExtractTarget.Maps,
        ["loading"] = ExtractTarget.Loading,
        ["sound"] = ExtractTarget.Sound,
    };

    /// Parses arguments. Returns:
    ///   Parsed - run with <paramref name="options"/>;
    ///   Menu   - no command given, run the interactive menu (options.SqpackPath
    ///            holds an explicit preset path, or empty to auto-detect);
    ///   Help   - printed usage on request (exit 0);
    ///   Error  - printed an error/usage for a bad invocation (exit non-zero).
    public static ParseResult Parse(string[] args, out AppOptions options)
    {
        options = new AppOptions();

        if (HasFlag(args, "-h") || HasFlag(args, "--help"))
        {
            PrintUsage();
            return ParseResult.Help;
        }

        var tokens = new List<string>(args);

        // Optional leading sqpack path (anything that isn't a flag or a command).
        string? pathArg = null;
        if (tokens.Count > 0 && !tokens[0].StartsWith('-')
            && !Commands.ContainsKey(tokens[0])
            && !tokens[0].Equals("dump", StringComparison.OrdinalIgnoreCase)
            && !tokens[0].Equals("diff", StringComparison.OrdinalIgnoreCase))
        {
            pathArg = tokens[0];
            tokens.RemoveAt(0);
        }

        string? command = tokens.Count > 0 ? tokens[0] : null;

        // No command -> interactive menu (carry any explicit path as a preset).
        if (command == null)
        {
            options.SqpackPath = pathArg ?? string.Empty;
            return ParseResult.Menu;
        }
        tokens.RemoveAt(0);

        // diff compares two already-extracted output folders; it needs no sqpack path.
        if (command.Equals("diff", StringComparison.OrdinalIgnoreCase))
            return ParseDiff(tokens, options);

        // A non-interactive command requires an explicit sqpack path.
        if (string.IsNullOrWhiteSpace(pathArg))
        {
            Style.Error("No sqpack path provided.");
            Console.WriteLine("Pass the sqpack folder as the first argument, e.g.:");
            Console.WriteLine("  SharpVision \"C:\\...\\game\\sqpack\" full");
            Console.WriteLine("Or run with no command for the interactive menu (auto-detects installations).");
            return ParseResult.Error;
        }
        if (!Directory.Exists(pathArg))
        {
            Style.Error($"sqpack path does not exist: '{pathArg}'.");
            return ParseResult.Error;
        }
        options.SqpackPath = pathArg;

        options.Overwrite = HasFlag(tokens, "--overwrite") || HasFlag(tokens, "--force");

        if (TryGetOptionValue(tokens, "--lang", out var langCsv))
            options.Languages = langCsv
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => s.ToLowerInvariant())
                .ToList();

        if (command.Equals("dump", StringComparison.OrdinalIgnoreCase))
            return ParseDump(tokens, options);

        if (!Commands.TryGetValue(command, out var target))
        {
            Style.Error($"Unknown command: '{command}'.");
            PrintUsage();
            return ParseResult.Error;
        }
        options.Target = target;

        if (target is ExtractTarget.Icons or ExtractTarget.Full && !ParseIconRange(tokens, options))
            return ParseResult.Error;

        if (target == ExtractTarget.Sound)
        {
            bool bgm = HasFlag(tokens, "--bgm");
            bool orch = HasFlag(tokens, "--orchestrion");
            options.SoundSource = bgm == orch ? SoundSource.All : bgm ? SoundSource.Bgm : SoundSource.Orchestrion;
        }

        return ParseResult.Parsed;
    }

    private static ParseResult ParseDiff(List<string> tokens, AppOptions options)
    {
        options.Target = ExtractTarget.Diff;

        if (tokens.Count == 0)
        {
            Console.WriteLine("Usage: diff <oldDir> <newDir>   |   diff <region>");
            Console.WriteLine("  Two folders: compare those extracted outputs.");
            Console.WriteLine("  One region (e.g. korean): compare the latest two versions under output/<region>/.");
            return ParseResult.Error;
        }

        if (tokens.Count == 1)
        {
            options.DiffRegion = tokens[0];
            return ParseResult.Parsed;
        }

        options.DiffOld = tokens[0];
        options.DiffNew = tokens[1];
        return ParseResult.Parsed;
    }

    private static ParseResult ParseDump(List<string> tokens, AppOptions options)
    {
        // "scdskips" scans the music sheets and needs no further argument.
        if (tokens.Count >= 1 && tokens[0].Equals("scdskips", StringComparison.OrdinalIgnoreCase))
        {
            options.Target = ExtractTarget.DumpScdSkips;
            return ParseResult.Parsed;
        }

        // "langtime" measures how long it takes to read the language list from the
        // sqpack (no further argument).
        if (tokens.Count >= 1 && tokens[0].Equals("langtime", StringComparison.OrdinalIgnoreCase))
        {
            options.Target = ExtractTarget.DumpLangTime;
            return ParseResult.Parsed;
        }

        if (tokens.Count < 2)
        {
            Console.WriteLine("Usage: dump <sestring|subrow|scd> <SheetName|filePath>  |  dump scdskips");
            return ParseResult.Error;
        }

        options.Target = tokens[0].ToLowerInvariant() switch
        {
            "sestring" => ExtractTarget.DumpSeString,
            "subrow" => ExtractTarget.DumpSubrow,
            "scd" => ExtractTarget.DumpScd,
            _ => (ExtractTarget)(-1),
        };
        if ((int)options.Target == -1)
        {
            Style.Error($"Unknown dump kind: '{tokens[0]}'. Use 'sestring', 'subrow', or 'scd'.");
            return ParseResult.Error;
        }

        options.DumpSheet = tokens[1];
        return ParseResult.Parsed;
    }

    private static bool ParseIconRange(List<string> tokens, AppOptions options)
    {
        bool normalOnly = HasFlag(tokens, "--normal-only");
        bool hdOnly = HasFlag(tokens, "--hd-only");
        if (normalOnly && hdOnly)
        {
            Style.Error("Use only one of --normal-only / --hd-only.");
            return false;
        }
        options.IconKind = normalOnly ? IconKind.Normal : hdOnly ? IconKind.Hd : IconKind.Both;

        if (TryGetOptionValue(tokens, "--range", out var range))
        {
            if (!TryParseRange(range, out options.IconStart, out options.IconEnd))
            {
                Style.Error($"Invalid --range value '{range}'. Expected <start-end>.");
                return false;
            }
        }

        if (TryGetIntOption(tokens, "--chunk", out var chunkSize))
        {
            if (!TryGetIntOption(tokens, "--index", out var chunkIndex))
            {
                Style.Error("--index is required when --chunk is provided.");
                return false;
            }
            if (chunkSize <= 0 || chunkIndex < 0)
            {
                Style.Error("--chunk must be > 0 and --index must be >= 0.");
                return false;
            }

            var start = options.IconStart + chunkSize * chunkIndex;
            if (start > options.IconEnd)
            {
                Style.Error($"Chunk index {chunkIndex} is outside the range {options.IconStart}-{options.IconEnd}.");
                return false;
            }
            options.IconStart = start;
            options.IconEnd = Math.Min(options.IconEnd, start + chunkSize - 1);
        }

        if (options.IconStart < 1 || options.IconEnd < options.IconStart)
        {
            Style.Error($"Invalid icon range {options.IconStart}-{options.IconEnd}.");
            return false;
        }
        return true;
    }

    public static void PrintUsage()
    {
        Style.Heading("Usage: SharpVision <sqpackPath> <command> [options]");
        Console.WriteLine("  Run with no arguments for the interactive menu (auto-detects installations).");
        Console.WriteLine("  sqpackPath is required for non-interactive commands.");
        Console.WriteLine();
        Style.Heading("Commands:");
        Console.WriteLine("  full       Extract everything (csv + maps + loading + all icons + sound).");
        Console.WriteLine("  csv        Extract Excel sheets to CSV.");
        Console.WriteLine("  icons      Extract icons. Options: --range <a-b> | --chunk <n> --index <k>,");
        Console.WriteLine("             --normal-only | --hd-only (default: both resolutions).");
        Console.WriteLine("  maps       Extract map images.");
        Console.WriteLine("  loading    Extract loading-screen images.");
        Console.WriteLine("  sound      Extract music (.ogg). Options: --bgm | --orchestrion (default: both).");
        Console.WriteLine("  diff <oldDir> <newDir> | diff <region>   Compare two extracted outputs");
        Console.WriteLine("             (added/removed/changed files; row-level detail for CSV). No sqpack path.");
        Console.WriteLine("  dump <sestring|subrow> <Sheet>   Diagnostics.");
        Console.WriteLine();
        Style.Heading("Options:");
        Console.WriteLine("  --overwrite      Re-extract files that already exist (default: skip existing).");
        Console.WriteLine("  --lang <codes>   Sheet languages, comma-separated (e.g. en or en,ja). Default: all.");
        Console.WriteLine($"                   Codes: {string.Join(" ", LanguageDetection.CandidateCodes)}");
    }

    private static bool HasFlag(IReadOnlyList<string> args, string name)
    {
        for (int i = 0; i < args.Count; i++)
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static bool TryGetOptionValue(IReadOnlyList<string> args, string optionName, out string value)
    {
        value = string.Empty;
        for (int i = 0; i < args.Count - 1; i++)
        {
            if (string.Equals(args[i], optionName, StringComparison.OrdinalIgnoreCase))
            {
                value = args[i + 1];
                return true;
            }
        }
        return false;
    }

    private static bool TryGetIntOption(IReadOnlyList<string> args, string optionName, out int value)
    {
        value = 0;
        if (!TryGetOptionValue(args, optionName, out var raw))
            return false;
        if (!int.TryParse(raw, out value))
        {
            Style.Error($"Invalid integer for {optionName}: '{raw}'.");
            return false;
        }
        return true;
    }

    private static bool TryParseRange(string range, out int start, out int end)
    {
        start = 0;
        end = 0;
        var parts = range.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 2 && int.TryParse(parts[0], out start) && int.TryParse(parts[1], out end);
    }
}
