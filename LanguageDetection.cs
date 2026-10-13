using Lumina.Data;
using Lumina.Data.Files.Excel;

namespace SharpVision;

/// Auto-detects which candidate languages the supplied sqpack actually ships, so
/// the same command works on the global, Korean, and Chinese clients.
public static class LanguageDetection
{
    // Candidate languages in priority order. The first available one becomes the
    // "primary" used for language-neutral assets (UIColor, icons, maps, loading).
    private static readonly (Lumina.Data.Language Lang, string Code)[] CandidateLanguages =
    {
        (Lumina.Data.Language.English,            "en"),
        (Lumina.Data.Language.German,             "de"),
        (Lumina.Data.Language.French,             "fr"),
        (Lumina.Data.Language.Japanese,           "ja"),
        (Lumina.Data.Language.Korean,             "ko"),
        (Lumina.Data.Language.ChineseSimplified,  "chs"),
        (Lumina.Data.Language.ChineseTraditional, "cht"),
    };

    /// Candidate language codes in priority order (for the picker / help text).
    public static IReadOnlyList<string> CandidateCodes { get; } =
        CandidateLanguages.Select(c => c.Code).ToArray();

    // The global ("international") client bundles ja/en/de/fr together; ko, chs and
    // cht each ship as a separate single-language client. The global client's
    // Item.exh also lists ko/chs as data-less entries that would extract as garbage,
    // so when any of its bundled languages is present we treat it as the global client
    // and drop the standalone-client languages.
    private static readonly Language[] GlobalClientLanguages =
        { Language.Japanese, Language.English, Language.German, Language.French };

    private static void DropSpuriousLanguages(HashSet<Language> present)
    {
        if (present.Overlaps(GlobalClientLanguages))
            present.IntersectWith(GlobalClientLanguages);
    }

    /// A one-shot read of a sqpack's available languages, carrying the GameData
    /// used to read them so the caller can hand it back to <see cref="Detect"/> and
    /// avoid loading the sqpack index twice (~0.9s each).
    public sealed class Probe
    {
        public required Lumina.GameData Gd { get; init; }
        public required HashSet<Language> Present { get; init; }
        /// Present language codes, in candidate priority order.
        public required IReadOnlyList<string> Codes { get; init; }
    }

    /// Reads the languages a sqpack ships (the authoritative .exh list), building a
    /// single GameData (defaulted to the first candidate language so it can be
    /// reused by <see cref="Detect"/>). Returns null if the path isn't a usable
    /// sqpack. Costs one sqpack index load (~0.9s); the header read itself is free.
    public static Probe? TryProbe(string sqpackPath)
    {
        try
        {
            var gd = new Lumina.GameData(sqpackPath, new() { DefaultExcelLanguage = CandidateLanguages[0].Lang });
            var exh = gd.GetFile<ExcelHeaderFile>("exd/Item.exh");
            if (exh == null)
                return null;
            var present = new HashSet<Language>(exh.Languages);
            DropSpuriousLanguages(present);
            var codes = CandidateLanguages.Where(c => present.Contains(c.Lang)).Select(c => c.Code).ToArray();
            return new Probe { Gd = gd, Present = present, Codes = codes };
        }
        catch
        {
            return null;
        }
    }

    /// Detects which candidate languages this sqpack ships and returns a GameData
    /// for each.
    ///
    /// The global client bundles ja/en/de/fr together in one client, while ko, chs,
    /// and cht each live in their own separate client. Rather than
    /// constructing a GameData per candidate (each re-indexes the entire sqpack —
    /// the slow part of startup), we read the available languages once from a
    /// representative always-localized sheet's .exh header (the authoritative list
    /// Lumina itself uses), then build a GameData only for the languages actually
    /// present. The probe GameData is reused for the first candidate when it is
    /// available, so no construction is wasted on the common (global) client.
    /// <paramref name="wantCodes"/> optionally restricts the result to those
    /// language codes (null = all available); a GameData is built only for the
    /// languages that are both present and requested. <paramref name="probe"/> is
    /// an optional pre-read probe (e.g. from the install picker) whose GameData and
    /// language list are reused instead of re-loading the sqpack index.
    public static List<(Lumina.GameData Gd, string Code)> Detect(
        string sqpackPath, IReadOnlyCollection<string>? wantCodes = null, Probe? probe = null)
    {
        // Probe once to learn which languages the sqpack ships (reuse the picker's
        // probe if it was passed in).
        Lumina.GameData? probeGd = probe?.Gd;
        HashSet<Language>? present = probe?.Present;
        if (present is null)
        {
            probeGd = new Lumina.GameData(sqpackPath, new() { DefaultExcelLanguage = CandidateLanguages[0].Lang });
            var exh = probeGd.GetFile<ExcelHeaderFile>("exd/Item.exh");
            present = new HashSet<Language>(exh?.Languages ?? Array.Empty<Language>());
            DropSpuriousLanguages(present);
        }

        // Languages to export, in candidate priority order. Order matters: result[0]
        // is the "primary" GameData used for language-neutral assets.
        var targets = CandidateLanguages
            .Where(c => present.Contains(c.Lang) && (wantCodes == null || wantCodes.Contains(c.Code)))
            .ToArray();

        // One GameData per language, each re-indexing the sqpack independently, so
        // build them in parallel — this matters for the global client (ja/en/de/fr
        // in one install; ~0.9s of index load each). The first candidate (English)
        // reuses the probe; the rest get a GameData defaulted to their language.
        // Results are placed by index so candidate order is kept regardless of
        // completion order.
        var built = new Lumina.GameData[targets.Length];
        Parallel.For(0, targets.Length, i =>
        {
            var lang = targets[i].Lang;
            built[i] = lang == CandidateLanguages[0].Lang
                ? probeGd!
                : new Lumina.GameData(sqpackPath, new() { DefaultExcelLanguage = lang });
        });

        var result = new List<(Lumina.GameData Gd, string Code)>(targets.Length);
        for (int i = 0; i < targets.Length; i++)
            result.Add((built[i], targets[i].Code));
        return result;
    }
}
