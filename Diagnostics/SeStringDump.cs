using Lumina.Data;
using Lumina.Data.Files;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Lumina.Text.Payloads;
using Lumina.Text.ReadOnly;
using SharpVision.Sound;

namespace SharpVision.Diagnostics;

/// Single-sheet diagnostic: prints the macro codes in string columns so we can
/// see how a value (e.g. the player name) is encoded. Loads one sheet only — fast.
public static class SeStringDump
{
    // Opens a GameData for diagnostics defaulted to a language the sqpack actually
    // ships (sheet dumps need a present language; file dumps are language-neutral).
    private static Lumina.GameData Open(string sqpackPath)
    {
        var langs = LanguageDetection.Detect(sqpackPath);
        return langs.Count > 0 ? langs[0].Gd : new Lumina.GameData(sqpackPath, new());
    }

    // Experiment: are the HCA frames scrambled the same way SCD scrambles ogg
    // (v3 table XOR)? A plain HCA frame starts with the 0xFFFF sync, so we apply
    // candidate descrambles to frame 0 and see which yields FF FF.
    private static void HcaExperiment(byte[] d, int entry, uint size, uint subInfoSize)
    {
        int subStart = entry + 0x20;
        int dataStart = entry + 0x20 + (int)subInfoSize;

        int hca = -1;
        for (int i = subStart; i + 3 < dataStart && i + 3 < d.Length; i++)
            if ((d[i] & 0x7F) == 'H' && (d[i + 1] & 0x7F) == 'C' && (d[i + 2] & 0x7F) == 'A') { hca = i; break; }
        if (hca < 0 || hca + 8 > d.Length) { Console.WriteLine("  [hca] magic not found"); return; }

        int dataOffset = (d[hca + 6] << 8) | d[hca + 7]; // HCA header size (big-endian)
        int frame = hca + dataOffset;
        Console.WriteLine($"  [hca] header@0x{hca:X} dataOffset={dataOffset} frame0@0x{frame:X} (dataStart@0x{dataStart:X}{(frame == dataStart ? "" : " DIFFER")})");
        if (frame + 4 > d.Length) return;

        Console.WriteLine($"  [hca] frame0 raw: {d[frame]:X2} {d[frame + 1]:X2} {d[frame + 2]:X2} {d[frame + 3]:X2}  (plain HCA frame starts FF FF)");
        Console.WriteLine($"  [hca] single-byte guess: f0^FF=0x{d[frame] ^ 0xFF:X2} f1^FF=0x{d[frame + 1] ^ 0xFF:X2} {((d[frame] ^ 0xFF) == (d[frame + 1] ^ 0xFF) ? "(consistent single-byte!)" : "")}");

        var table = ReflectV3Table();
        if (table == null) { Console.WriteLine("  [hca] v3 table unavailable"); return; }
        byte b1 = (byte)(size & 0x7F), b2 = (byte)(b1 & 0x3F);
        Console.WriteLine($"  [hca] v3 key byte1=0x{b1:X} byte2=0x{b2:X}");

        byte a0 = (byte)(table[(b2 + 0) & 0xFF] ^ d[frame] ^ b1), a1 = (byte)(table[(b2 + 1) & 0xFF] ^ d[frame + 1] ^ b1);
        Console.WriteLine($"  [hca] candA (v3, j@frame):    {a0:X2} {a1:X2} {(a0 == 0xFF && a1 == 0xFF ? "<= FF FF!" : "")}");

        byte c0 = (byte)(table[(b2 + dataOffset) & 0xFF] ^ d[frame] ^ b1), c1 = (byte)(table[(b2 + dataOffset + 1) & 0xFF] ^ d[frame + 1] ^ b1);
        Console.WriteLine($"  [hca] candB (v3, j@hcaStart): {c0:X2} {c1:X2} {(c0 == 0xFF && c1 == 0xFF ? "<= FF FF!" : "")}");
    }

    private static byte[]? ReflectV3Table()
    {
        try
        {
            var scd = typeof(ScdFile);
            foreach (var t in new[] { scd }.Concat(scd.GetNestedTypes(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)))
                foreach (var f in t.GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
                    if (f.FieldType == typeof(byte[]) && f.GetValue(null) is byte[] a && a.Length == 256)
                        return a;
        }
        catch { /* ignore */ }
        return null;
    }

    private static void DumpHex(byte[] data, int start, int length, string label)
    {
        Console.WriteLine($"--- {label} (0x{start:X}..0x{start + length:X}) ---");
        var sb = new System.Text.StringBuilder();
        for (int row = 0; row < length; row += 16)
        {
            sb.Append($"0x{start + row:X4}: ");
            for (int j = 0; j < 16 && row + j < length; j++)
                sb.Append(data[start + row + j].ToString("X2")).Append(' ');
            sb.Append("  ");
            for (int j = 0; j < 16 && row + j < length; j++)
            {
                byte b = data[start + row + j];
                sb.Append(b >= 32 && b < 127 ? (char)b : '.');
            }
            sb.Append('\n');
        }
        Console.WriteLine(sb.ToString());
    }

    // Lists every BGM/OrchestrionPath .scd that the sound exporter skips, with
    // enough detail (magic, version, audio count, codec, reason) to see what they
    // are — in a single pass.
    public static void DumpScdSkips(string sqpackPath)
    {
        var gd = Open(sqpackPath);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var paths = new List<string>();
        CollectScdFilePaths<BGM>(gd, paths, seen);
        CollectScdFilePaths<OrchestrionPath>(gd, paths, seen);

        int shown = 0;
        foreach (var path in paths)
        {
            var raw = gd.GetFile<FileResource>(path);
            if (raw == null)
                continue;
            var d = raw.Data;
            var entries = ScdReader.Read(d);
            if (entries.Any(e => e.Output != null))
                continue; // produced playable audio -> not skipped

            shown++;
            string magic = d.Length >= 8 ? System.Text.Encoding.ASCII.GetString(d, 0, 8) : "?";
            int version = d.Length >= 12 ? BitConverter.ToInt32(d, 8) : -1;
            Console.WriteLine($"[{shown}] {path}  ({d.Length} bytes) magic='{magic}' v{version}");
            if (entries.Count == 0)
                Console.WriteLine("       -> no audio entries (count 0 / not an SCD)");
            else
                for (int i = 0; i < entries.Count; i++)
                    Console.WriteLine($"       -> entry[{i}] codec=0x{entries[i].Codec:X} skip='{entries[i].Skipped}'");
        }
        Console.WriteLine($"total skipped: {shown}");
    }

    // Measures how long it takes to learn a sqpack's languages: the GameData
    // construction (the sqpack index load) plus one .exh header read. Used to
    // evaluate showing each install's real languages in the picker instead of a
    // folder-name heuristic. Runs the probe twice to expose any OS file-cache
    // warm-up effect (first call cold, second warm).
    public static void LangTime(string sqpackPath)
    {
        Console.WriteLine($"sqpack: {sqpackPath}");
        for (int pass = 1; pass <= 2; pass++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var gd = new Lumina.GameData(sqpackPath, new() { DefaultExcelLanguage = Language.Korean });
            long ctor = sw.ElapsedMilliseconds;

            sw.Restart();
            var exh = gd.GetFile<Lumina.Data.Files.Excel.ExcelHeaderFile>("exd/Item.exh");
            var langs = exh?.Languages ?? Array.Empty<Language>();
            long header = sw.ElapsedMilliseconds;

            Console.WriteLine($"[pass {pass}] GameData ctor (index load): {ctor} ms | "
                + $"read Item.exh + Languages: {header} ms | total: {ctor + header} ms");
            Console.WriteLine($"          languages present ({langs.Length}): {string.Join(", ", langs)}");
        }
    }

    private static void CollectScdFilePaths<T>(Lumina.GameData gd, List<string> paths, HashSet<string> seen)
        where T : struct, IExcelRow<T>
    {
        var sheet = gd.GetExcelSheet<T>();
        var prop = typeof(T).GetProperty("File");
        if (sheet == null || prop == null || prop.PropertyType != typeof(ReadOnlySeString))
            return;
        foreach (var row in sheet)
        {
            var v = prop.GetValue(row);
            string p = v == null ? string.Empty : ((ReadOnlySeString)v).ExtractText();
            if (!string.IsNullOrEmpty(p) && p.EndsWith(".scd", StringComparison.OrdinalIgnoreCase)
                && seen.Add(p) && gd.FileExists(p))
                paths.Add(p);
        }
    }

    // Diagnoses why Lumina fails to parse a given .scd: dumps the raw header and
    // shows exactly where ScdFile / GetAudio throws.
    public static void DumpScd(string sqpackPath, string filePath)
    {
        var gd = Open(sqpackPath);

        var raw = gd.GetFile<FileResource>(filePath);
        if (raw == null)
        {
            Console.WriteLine($"Not found: {filePath}");
            return;
        }

        var data = raw.Data;
        uint U32(int o) => BitConverter.ToUInt32(data, o);
        ushort U16(int o) => BitConverter.ToUInt16(data, o);

        Console.WriteLine($"{filePath}: {data.Length} bytes (0x{data.Length:X})");
        Console.WriteLine($"magic='{System.Text.Encoding.ASCII.GetString(data, 0, 8)}' version@0x08={U32(8)} headerSize@0x0E={U16(0x0E)} fileSize@0x10=0x{U32(0x10):X}");

        // Offsets header sits at headerSize (typically 0x30): three table counts +
        // a 0x270F sentinel + three table offsets.
        int oh = U16(0x0E);
        if (oh + 0x1C <= data.Length)
            Console.WriteLine($"offsetsHeader@0x{oh:X}: counts[{U16(oh)},{U16(oh + 2)},{U16(oh + 4)}] sentinel=0x{U16(oh + 6):X} tableOffsets[0x{U32(oh + 8):X}, 0x{U32(oh + 0xC):X}, 0x{U32(oh + 0x10):X}] unk@0x18=0x{U32(oh + 0x18):X}");

        // Follow the audio-header table (offsets header +0x0C) to the AudioBasicDesc
        // and decode it + the sub-info (aux chunks + ogg seek-table header).
        if (oh + 0x10 <= data.Length)
        {
            uint audioTable = U32(oh + 0x0C);
            uint entry = U32((int)audioTable);
            int p = (int)entry;
            Console.WriteLine($"audioTable@0x{audioTable:X} -> entry@0x{entry:X}");
            if (p + 0x20 <= data.Length)
            {
                uint size = U32(p), ch = U32(p + 4), freq = U32(p + 8), codec = U32(p + 0xC);
                uint loopS = U32(p + 0x10), loopE = U32(p + 0x14), v18 = U32(p + 0x18);
                ushort aux = U16(p + 0x1C);
                Console.WriteLine($"  entry: size=0x{size:X}({size}) ch={ch} freq={freq} codec=0x{codec:X}(6=Ogg,0xC=ADPCM) loop=[{loopS},{loopE}] @0x18=0x{v18:X}({v18}) auxCount@0x1C={aux}");
                long expectedEnd = p + 0x20 + v18 + size; // entry + subInfo(@0x18) + data
                Console.WriteLine($"  [check] entry+0x20+@0x18+size = 0x{expectedEnd:X}; fileLen=0x{data.Length:X} => {(expectedEnd <= data.Length ? "FITS" : "OVERRUN by " + (expectedEnd - data.Length))}");

                if (codec == 0x1A)
                    HcaExperiment(data, p, size, v18);
            }
            DumpHex(data, p, Math.Min(0x100, data.Length - p), "audio entry region");
        }

        try
        {
            var scd = gd.GetFile<ScdFile>(filePath);
            Console.WriteLine($"ScdFile loaded. AudioDataCount={scd!.AudioDataCount}");
            for (int i = 0; i < scd.AudioDataCount; i++)
            {
                try
                {
                    var a = scd.GetAudio(i);
                    Console.WriteLine($"  audio[{i}] Format={a.AudioBasicDesc.Format} dataLen={(a.AudioData?.Length ?? -1)}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  audio[{i}] EXC {ex.GetType().Name}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"GetFile<ScdFile> EXC {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }

    // Probes where the stored subrow id lives relative to a RawSubrow's RowOffset,
    // by printing candidate u16 reads for the first subrows. Compare against
    // oxidizer's known ids (e.g. MassivePcContentBattleTalk row 0 has a 65535).
    public static void DumpSubrow(string sqpackPath, string sheetName)
    {
        var gd = Open(sqpackPath);
        var sheet = gd.GetSubrowExcelSheet<RawSubrow>(null, sheetName);
        if (sheet == null)
        {
            Console.WriteLine($"Not a subrow sheet / not found: {sheetName}");
            return;
        }

        var raw = gd.Excel.GetRawSheet(sheetName, null)!;
        int colCount = raw.Columns.Count;
        // String columns: (index, byte offset within the subrow's fixed data).
        var stringCols = new List<(int Index, uint Offset)>();
        for (int c = 0; c < colCount; c++)
            if (raw.Columns[c].Type.ToString() == "String")
                stringCols.Add((c, (uint)raw.GetColumnOffset(c)));

        Console.WriteLine($"sheet {sheetName}: {colCount} cols, string cols at [{string.Join(", ", stringCols.Select(s => $"#{s.Index}@{s.Offset}"))}]");
        Console.WriteLine("Per subrow string col:  LUMINA = what Lumina actually returns (row.ReadColumn),");
        Console.WriteLine("                        FIXED  = re-read against the LAST subrow's RowOffset.");

        int parentShown = 0;
        foreach (var rows in sheet)
        {
            var subs = new List<RawSubrow>();
            foreach (var s in rows) subs.Add(s);
            if (subs.Count < 2) continue; // only multi-subrow rows show the bug

            var page = subs[0].ExcelPage;
            long lastOff = (long)(ulong)subs[subs.Count - 1].RowOffset; // corrected base

            Console.WriteLine($"--- row {subs[0].RowId}: count={subs.Count} lastSubrowOffset={lastOff}");

            int sub = 0;
            foreach (var row in subs)
            {
                long off = (long)(ulong)row.RowOffset;
                foreach (var (idx, colOff) in stringCols)
                {
                    uint stored = page.ReadUInt32((UIntPtr)(off + colOff));
                    // LUMINA: exactly what the typed/raw read produces today.
                    string lumina = row.ReadColumn(idx) is ReadOnlySeString s
                        ? "\"" + s.ExtractText().Replace("\n", "\\n") + "\"" : "<non-string>";
                    // FIXED: ReadString(field, lastSubrowOffset) — our workaround.
                    string @fixed = Show(() => page.ReadString((UIntPtr)(off + colOff), (UIntPtr)lastOff));
                    Console.WriteLine($"  sub#{sub} (subrowId {row.SubrowId}) @off {off} stored={stored} | LUMINA={lumina} | FIXED={@fixed}");
                }
                if (++sub >= 10) break;
            }
            if (++parentShown >= 4) break;
        }
    }

    private static string Show(Func<ReadOnlySeString> read)
    {
        try { return "\"" + read().ExtractText().Replace("\n", "\\n") + "\""; }
        catch (Exception ex) { return $"<{ex.GetType().Name}>"; }
    }

    public static void Dump(string sqpackPath, string sheetName)
    {
        var gd = Open(sqpackPath);
        var raw = gd.Excel.GetRawSheet(sheetName, null);
        if (raw == null)
        {
            Console.WriteLine($"Sheet not found: {sheetName}");
            return;
        }

        int columnCount = raw.Columns.Count;
        var sheet = gd.GetExcelSheet<RawRow>(null, sheetName);
        if (sheet == null)
        {
            Console.WriteLine("No rows.");
            return;
        }

        // Macros already handled by the renderer; surface the ones still dropped.
        var handled = new HashSet<MacroCode>
        {
            MacroCode.NewLine, MacroCode.Italic, MacroCode.ColorType,
            MacroCode.EdgeColorType, MacroCode.SoftHyphen, MacroCode.NonBreakingSpace,
            MacroCode.Hyphen,
        };

        int shown = 0;
        foreach (var row in sheet)
        {
            for (int c = 0; c < columnCount; c++)
            {
                if (row.ReadColumn(c) is not ReadOnlySeString ros)
                    continue;

                var macros = new List<string>();
                bool interesting = false;
                foreach (var payload in ros)
                {
                    if (payload.Type == ReadOnlySePayloadType.Text || payload.Type == ReadOnlySePayloadType.Invalid)
                        continue;
                    macros.Add(payload.MacroCode.ToString());
                    if (!handled.Contains(payload.MacroCode))
                        interesting = true;
                }

                if (!interesting)
                    continue;

                Console.WriteLine($"row {row.RowId} col {c}: [{string.Join(", ", macros)}]  macro='{ros}'");
                if (++shown >= 25)
                    return;
            }
        }
    }
}
