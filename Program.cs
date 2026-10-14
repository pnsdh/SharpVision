using SharpVision.Cli;
using SharpVision.Diagnostics;
using SharpVision.Diff;
using SharpVision.Export;

namespace SharpVision;

class Program
{
    static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (ArgumentException ex) when (ex.ParamName == "dataPath")
        {
            // Lumina throws this when the path exists but isn't the sqpack folder.
            Style.Error("The given path is not a valid sqpack folder.");
            Console.Error.WriteLine(@"Point it at the game's 'sqpack' folder, e.g. ...\FINAL FANTASY XIV\game\sqpack.");
            return 1;
        }
        catch (Exception ex)
        {
            // Last-resort guard: turn any unexpected failure into a clean message and a
            // non-zero exit instead of dumping a raw stack trace.
            Style.Error($"Unexpected error: {ex.GetType().Name}: {ex.Message}");
            Console.Error.WriteLine("This is likely a bug or an environment/IO problem. See --help for usage.");
            return 1;
        }
    }

    static int Run(string[] args)
    {
        Banner.Print();
        switch (AppOptions.Parse(args, out var options))
        {
            case AppOptions.ParseResult.Help:
                return 0;
            case AppOptions.ParseResult.Error:
                return 1;
            case AppOptions.ParseResult.Menu:
                AppOptions? fromMenu;
                try
                {
                    fromMenu = InteractiveMenu.Run(string.IsNullOrEmpty(options.SqpackPath) ? null : options.SqpackPath);
                }
                catch (EndOfStreamException)
                {
                    // No interactive console (e.g. stdin redirected/closed).
                    Console.Error.WriteLine("Interactive menu needs a console. Pass a sqpack path and command instead (see --help).");
                    return 1;
                }
                if (fromMenu == null)
                    return 0; // user aborted the menu — not an error
                options = fromMenu;
                break;
        }

        return Extract(options);
    }

    // Runs the requested diagnostic or extraction and returns a process exit code
    // (0 = success). Loading Lumina.GameData (here or inside Detect) throws
    // ArgumentException(dataPath) when the path isn't a real sqpack; Main turns that
    // into a friendly message instead of a crash.
    static int Extract(AppOptions options)
    {
        // diff works on already-extracted output files only — handle it before any
        // sqpack/GameData load so it starts instantly.
        if (options.Target == ExtractTarget.Diff)
            return DiffRunner.Run(options) ? 0 : 1;

        switch (options.Target)
        {
            case ExtractTarget.DumpSeString:
                SeStringDump.Dump(options.SqpackPath, options.DumpSheet!);
                return 0;
            case ExtractTarget.DumpSubrow:
                SeStringDump.DumpSubrow(options.SqpackPath, options.DumpSheet!);
                return 0;
            case ExtractTarget.DumpScd:
                SeStringDump.DumpScd(options.SqpackPath, options.DumpSheet!);
                return 0;
            case ExtractTarget.DumpScdSkips:
                SeStringDump.DumpScdSkips(options.SqpackPath);
                return 0;
            case ExtractTarget.DumpLangTime:
                SeStringDump.LangTime(options.SqpackPath);
                return 0;
        }

        // Auto-detect which candidate languages this sqpack ships (optionally
        // restricted to the languages the user requested).
        var languages = LanguageDetection.Detect(options.SqpackPath, options.Languages, options.LanguageProbe);
        if (languages.Count == 0)
        {
            if (options.Languages != null)
                Style.Warn($"None of the requested languages ({string.Join(", ", options.Languages)}) are available in this client.");
            else
                Style.Error($"No supported language found in sqpack at '{options.SqpackPath}'.");
            return 1;
        }

        // The first available language is the "primary" used for language-neutral
        // assets (icons, maps, loading) and the icon-path prefix.
        var primary = languages[0];
        var codes = languages.Select(l => l.Code).ToList();

        // All output goes under output/<region>/<client version>/ so runs against
        // different clients/versions never overwrite each other (KR/Global now share
        // a version string, so the region is what keeps them apart).
        Output.Configure(Region.Slug(codes), GameVersion.BaseVersion(primary.Gd));

        // The interactive menu already shows the client version + languages right
        // after the install is picked (it carries a probe). For the CLI path (no
        // probe), show it here once the client is loaded.
        if (options.LanguageProbe == null)
            ClientInfo.Print(primary.Gd, codes);

        // csv --sheets: check the names against the client's sheet list before writing
        // anything, so a typo fails the run instead of being silently skipped.
        HashSet<string>? sheets = null;
        if (options.Sheets != null)
        {
            if (!SheetExporter.ResolveSheetNames(primary.Gd, options.Sheets, out sheets, out var unknown))
            {
                Style.Error($"Unknown sheet name(s): {string.Join(", ", unknown)}.");
                return 1;
            }
        }

        // Warn (and, when interactive, confirm) if the output drive's free space is
        // below this run's estimated output size.
        long required = DiskGuard.RequiredBytes(options.Target, options.IconKind,
            options.IconEnd - options.IconStart + 1, codes.Count);
        if (!DiskGuard.PreflightWarn(required, options.Interactive))
            return 0; // user chose not to continue — intentional, not a failure

        switch (options.Target)
        {
            case ExtractTarget.Full:
                // Stop between stages if a stage hit out-of-disk (DiskGuard.Tripped).
                SheetExporter.ExportCsv(languages, options.Overwrite);
                if (!DiskGuard.Tripped) MapExporter.Export(primary.Gd, options.Overwrite);
                if (!DiskGuard.Tripped) LoadingImageExporter.Export(primary.Gd, options.Overwrite);
                if (!DiskGuard.Tripped) SoundExporter.Export(primary.Gd, options.Overwrite, SoundSource.All);
                if (!DiskGuard.Tripped) IconExporter.ExportIcons(options.IconStart, options.IconEnd, primary.Gd, options.Overwrite, $"{primary.Code}/", options.IconKind);
                break;

            case ExtractTarget.Csv:
                SheetExporter.ExportCsv(languages, options.Overwrite, sheets);
                if (sheets != null && !DiskGuard.Tripped)
                {
                    // A subset run is typically scripted: report a requested sheet that
                    // didn't come out as a failure, not just a logged line.
                    var missing = SheetExporter.MissingOutputs(languages, sheets);
                    if (missing.Count > 0)
                    {
                        Style.Error($"\nRequested sheet(s) not extracted: {string.Join(", ", missing)}.");
                        return 1;
                    }
                }
                break;

            case ExtractTarget.Icons:
                IconExporter.ExportIcons(options.IconStart, options.IconEnd, primary.Gd, options.Overwrite, $"{primary.Code}/", options.IconKind);
                break;

            case ExtractTarget.Maps:
                MapExporter.Export(primary.Gd, options.Overwrite);
                break;

            case ExtractTarget.Loading:
                LoadingImageExporter.Export(primary.Gd, options.Overwrite);
                break;

            case ExtractTarget.Sound:
                SoundExporter.Export(primary.Gd, options.Overwrite, options.SoundSource);
                break;
        }

        DiskGuard.ReportIfTripped();
        return DiskGuard.Tripped ? 1 : 0; // out-of-disk during extraction is a failure
    }
}
