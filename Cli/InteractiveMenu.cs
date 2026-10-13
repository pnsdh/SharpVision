using SharpVision.Export;

namespace SharpVision.Cli;

/// English interactive prompts shown when the program is run with no command.
/// All input is collected up front (before the slow sqpack load) and returned as
/// a ready-to-run <see cref="AppOptions"/>.
public static class InteractiveMenu
{
    private enum MenuMode { Extract, Diff }

    public static AppOptions? Run(string? presetPath)
    {
        // With no preset path, first choose extract vs diff. A preset path means the
        // user already pointed at a client (sqpack), so skip straight to extraction.
        if (string.IsNullOrWhiteSpace(presetPath))
        {
            var mode = ChooseMode();
            if (mode == null)
                return null;
            if (mode == MenuMode.Diff)
                return ChooseDiff();
        }

        // Resolve the sqpack and, from its actual data, the languages it ships.
        // The probe carries the loaded GameData so detection can reuse it later.
        string sqpack;
        LanguageDetection.Probe? probe;
        if (string.IsNullOrWhiteSpace(presetPath))
        {
            var chosen = ChooseSqpack();
            if (chosen == null)
                return null;
            (sqpack, probe) = chosen.Value;
        }
        else
        {
            Console.WriteLine("Reading client data...");
            var resolved = ResolveSqpack(presetPath);
            if (resolved == null)
            {
                Style.Error($"'{presetPath}' is not a valid FFXIV sqpack folder.");
                Console.Error.WriteLine(@"Point it at the game's 'sqpack' folder (or its install root), e.g. ...\FINAL FANTASY XIV\game\sqpack.");
                return null;
            }
            (sqpack, probe) = resolved.Value;
        }

        // Report the picked client's version + languages right after selection.
        if (probe != null)
            ClientInfo.Print(probe.Gd, probe.Codes);

        var target = ChooseTarget();
        if (target == null)
            return null;

        var options = new AppOptions { SqpackPath = sqpack, Target = target.Value, LanguageProbe = probe, Interactive = true };

        // Languages only affect sheets; icons/maps/loading are language-neutral.
        // Ask only when the client actually ships more than one language (the
        // global client bundles ja/en/de/fr); the Korean and Chinese (simplified/
        // traditional) clients each ship one, so there's nothing to choose. If the probe failed
        // (unknown), ask to be safe. Leaving Languages = null lets detection resolve
        // to whatever is present.
        if ((target is ExtractTarget.Full or ExtractTarget.Csv) && (probe == null || probe.Codes.Count > 1))
            options.Languages = ChooseLanguages();

        if (target == ExtractTarget.Icons)
        {
            (options.IconStart, options.IconEnd) = ChooseIconRange();
            options.IconKind = ChooseIconKind();
        }

        if (target == ExtractTarget.Sound)
            options.SoundSource = ChooseSoundSource();

        options.Overwrite = AskYesNo("Overwrite existing files?", defaultYes: false);
        return options;
    }

    private static MenuMode? ChooseMode()
    {
        Console.WriteLine();
        Style.Heading("What do you want to do?");
        Option(1, "Extract from a game client");
        Option(2, "Diff two extracted outputs (compare patches)");
        return AskNumber("> ", 1, 2) == 2 ? MenuMode.Diff : MenuMode.Extract;
    }

    // Builds a diff of two already-extracted outputs: pick a region under
    // output/extract/, then the old and new version (defaulting to the latest two).
    // Returns null if there's nothing to diff yet.
    private static AppOptions? ChooseDiff()
    {
        string extractRoot = Output.ExtractRoot;
        var regions = Directory.Exists(extractRoot)
            ? Directory.GetDirectories(extractRoot)
                .OrderBy(Path.GetFileName, StringComparer.Ordinal)
                .ToList()
            : new List<string>();
        if (regions.Count == 0)
        {
            Style.Warn($"No extracted output found under {extractRoot}. Extract a client first.");
            return null;
        }

        string region = regions.Count == 1 ? regions[0] : ChooseFromList("Select a region:", regions);

        var versions = Directory.GetDirectories(region)
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .ToList();
        if (versions.Count < 2)
        {
            Style.Warn($"Need at least two versions under '{Path.GetFileName(region)}' to diff (found {versions.Count}).");
            return null;
        }

        Console.WriteLine();
        Style.Heading($"Versions in {Path.GetFileName(region)} (oldest first):");
        for (int i = 0; i < versions.Count; i++)
        {
            Console.Write("  ");
            Style.Write($"{i + 1})", ConsoleColor.Cyan);
            Console.WriteLine($" {Path.GetFileName(versions[i])}");
        }

        int oldDefault = versions.Count - 1; // second newest
        int newDefault = versions.Count;     // newest
        int oldIdx = AskNumber($"Old version # (Enter = {oldDefault})> ", 1, versions.Count, oldDefault);
        int newIdx = AskNumber($"New version # (Enter = {newDefault})> ", 1, versions.Count, newDefault);

        return new AppOptions
        {
            Target = ExtractTarget.Diff,
            DiffOld = versions[oldIdx - 1],
            DiffNew = versions[newIdx - 1],
        };
    }

    private static string ChooseFromList(string heading, List<string> paths)
    {
        Console.WriteLine();
        Style.Heading(heading);
        for (int i = 0; i < paths.Count; i++)
        {
            Console.Write("  ");
            Style.Write($"{i + 1})", ConsoleColor.Cyan);
            Console.WriteLine($" {Path.GetFileName(paths[i])}");
        }
        return paths[AskNumber("> ", 1, paths.Count) - 1];
    }

    // Returns the chosen (sqpackPath, languageProbe), or null if aborted. Each
    // candidate is probed so the list shows the region/languages read from the
    // actual client data (not a folder-name guess); the probe is reused downstream.
    private static (string Path, LanguageDetection.Probe? Probe)? ChooseSqpack()
    {
        Console.WriteLine("Detecting FFXIV installations...");
        var candidates = GamePathFinder.Find(); // already de-duplicated by path

        if (candidates.Count == 0)
        {
            Style.Warn("No FFXIV installation found automatically.");
            return Manual();
        }

        // Read each install's real languages from its data (one sqpack index load
        // each, ~0.9s); the selected one's probe is reused so it isn't loaded twice.
        Console.WriteLine("Reading client data...");
        var probes = candidates.Select(LanguageDetection.TryProbe).ToArray();

        Console.WriteLine();
        Style.Heading("Select the FFXIV installation to extract from:");
        for (int i = 0; i < candidates.Count; i++)
        {
            // Region/languages from the client's real data; if the probe failed, show
            // "???" rather than a folder-name guess (the real region/languages still
            // appear once the client is loaded).
            string region = probes[i] != null ? RegionDisplay(probes[i]!.Codes) : "???";
            Console.Write("  ");
            Style.Write($"{i + 1})", ConsoleColor.Cyan);
            Console.WriteLine($" {region,-26} {candidates[i]}");
        }
        Console.Write("  ");
        Style.Write($"{candidates.Count + 1})", ConsoleColor.Cyan);
        Console.WriteLine(" Enter a path manually");

        int choice = AskNumber("> ", 1, candidates.Count + 1);
        if (choice == candidates.Count + 1)
            return Manual();
        return (candidates[choice - 1], probes[choice - 1]);

        // Prompt for a path and validate it on the spot, re-prompting until a real
        // sqpack is given (or the user enters nothing to abort).
        static (string Path, LanguageDetection.Probe? Probe)? Manual()
        {
            while (true)
            {
                Console.Write("sqpack folder path (or install root)> ");
                var input = Console.ReadLine()?.Trim().Trim('"');
                if (string.IsNullOrWhiteSpace(input))
                {
                    Style.Error("No path entered. Aborting.");
                    return null;
                }

                Console.WriteLine("Reading client data...");
                var resolved = ResolveSqpack(input);
                if (resolved != null)
                    return resolved;

                Style.Error($"'{input}' is not a valid FFXIV sqpack folder. Point to the 'sqpack' folder or the install root, then try again.");
            }
        }
    }

    // Resolves a user-supplied path to a usable sqpack: accepts either the sqpack
    // folder itself or an install root (…/game/sqpack), and confirms it by probing
    // its real data. Returns null if neither is a valid client.
    private static (string Path, LanguageDetection.Probe? Probe)? ResolveSqpack(string input)
    {
        foreach (var candidate in new[] { input, Path.Combine(input, "game", "sqpack") })
        {
            if (!Directory.Exists(candidate))
                continue;
            var probe = LanguageDetection.TryProbe(candidate);
            if (probe != null)
                return (candidate, probe);
        }
        return null;
    }

    // The region name (see Region) with the real codes shown in parentheses.
    private static string RegionDisplay(IReadOnlyList<string> codes) =>
        codes.Count == 0 ? "Unknown" : $"{Region.Name(codes)} ({string.Join(",", codes)})";

    private static ExtractTarget? ChooseTarget()
    {
        Console.WriteLine();
        Style.Heading("What do you want to extract?");
        Option(1, "Full (csv + maps + loading + all icons + sound)");
        Option(2, "CSV sheets");
        Option(3, "Icons");
        Option(4, "Maps");
        Option(5, "Loading images");
        Option(6, "Sounds");

        return AskNumber("> ", 1, 6) switch
        {
            1 => ExtractTarget.Full,
            2 => ExtractTarget.Csv,
            3 => ExtractTarget.Icons,
            4 => ExtractTarget.Maps,
            5 => ExtractTarget.Loading,
            6 => ExtractTarget.Sound,
            _ => null,
        };
    }

    // Returns null for "all available", else the chosen codes. Languages not
    // actually present in the client are simply ignored later.
    private static List<string>? ChooseLanguages()
    {
        Console.WriteLine();
        Style.Heading("Languages to extract (Enter = all available).");
        Console.WriteLine($"  Codes: {string.Join(" ", LanguageDetection.CandidateCodes)}");

        while (true)
        {
            Console.Write("  e.g. en  or  en,ja  > ");
            var input = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(input))
                return null;

            var codes = input
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => s.ToLowerInvariant())
                .ToList();

            var unknown = codes.Where(c => !LanguageDetection.CandidateCodes.Contains(c)).ToList();
            if (unknown.Count > 0)
            {
                Style.Warn($"Unknown code(s): {string.Join(", ", unknown)}. Valid: {string.Join(" ", LanguageDetection.CandidateCodes)}");
                continue;
            }
            return codes;
        }
    }

    private static (int Start, int End) ChooseIconRange()
    {
        while (true)
        {
            Console.Write($"Icon range (Enter = all {AppOptions.DefaultIconStart}-{AppOptions.DefaultIconEnd}, or e.g. 241000-242000)> ");
            var input = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(input))
                return (AppOptions.DefaultIconStart, AppOptions.DefaultIconEnd);

            var parts = input.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && int.TryParse(parts[0], out var start) && int.TryParse(parts[1], out var end)
                && start >= 1 && end >= start)
                return (start, end);

            Style.Warn("Invalid range. Use <start-end>, e.g. 241000-242000.");
        }
    }

    private static SoundSource ChooseSoundSource()
    {
        Console.WriteLine();
        Style.Heading("Which music source?");
        Option(1, "All (BGM + orchestrion)");
        Option(2, "BGM only");
        Option(3, "Orchestrion only");
        return AskNumber("> ", 1, 3) switch
        {
            2 => SoundSource.Bgm,
            3 => SoundSource.Orchestrion,
            _ => SoundSource.All,
        };
    }

    private static IconKind ChooseIconKind()
    {
        Console.WriteLine();
        Style.Heading("Which icon resolution?");
        Option(1, "Both (normal + HD)");
        Option(2, "Normal only");
        Option(3, "HD (_hr1) only");
        return AskNumber("> ", 1, 3) switch
        {
            2 => IconKind.Normal,
            3 => IconKind.Hd,
            _ => IconKind.Both,
        };
    }

    private static bool AskYesNo(string question, bool defaultYes)
    {
        Console.Write($"{question} ({(defaultYes ? "Y/n" : "y/N")})> ");
        var input = Console.ReadLine()?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(input))
            return defaultYes;
        return input is "y" or "yes";
    }

    private static int AskNumber(string prompt, int min, int max) => AskNumber(prompt, min, max, null);

    // With a defaultValue, an empty line (just Enter) returns that default.
    private static int AskNumber(string prompt, int min, int max, int? defaultValue)
    {
        while (true)
        {
            Console.Write(prompt);
            var input = Console.ReadLine();
            if (input == null) // stdin closed (EOF / not a terminal) — don't spin
                throw new EndOfStreamException("No console input available.");
            input = input.Trim();
            if (input.Length == 0 && defaultValue is int d)
                return d;
            if (int.TryParse(input, out var n) && n >= min && n <= max)
                return n;
            Style.Warn($"Please enter a number between {min} and {max}.");
        }
    }

    // Prints a numbered menu option with the number key emphasized.
    private static void Option(int n, string text)
    {
        Console.Write("  ");
        Style.Write($"{n})", ConsoleColor.Cyan);
        Console.WriteLine($" {text}");
    }
}
