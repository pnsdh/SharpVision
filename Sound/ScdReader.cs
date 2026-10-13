using System.Buffers.Binary;
using System.Reflection;
using VGAudio.Containers.Hca;
using VGAudio.Containers.Wave;

namespace SharpVision.Sound;

/// Minimal reader for FFXIV SCD ("SEDBSSCF" v3) audio containers, extracting
/// Ogg Vorbis entries as playable .ogg. Format/layout details: see SCD-FORMAT.md.
///
/// Why this exists: Lumina's <c>ScdFile.GetAudio</c> has no HCA branch — codec 0x1A
/// falls through to the default case, which returns only the raw (still scrambled)
/// data region, without the HCA header that lives in the sub-info. That can't be
/// decoded, so we need our own reader for BGM regardless. Handling Ogg here too
/// keeps both codecs on one path that bounds-checks each entry and reports a skip
/// reason instead of throwing.
///
/// Note: the Ogg branch used to be broken upstream as well (it read
/// OggHeaderSize + Size bytes from the data region, which only holds Size bytes,
/// throwing on essentially every BGM). Lumina 7.6.1 fixed that (NotAdam/Lumina#131),
/// and our Ogg output is equivalent to theirs — so if HCA support ever lands
/// upstream, this reader could be retired.
///
/// Layout (derived from the container + the public SCD format notes):
///   header: "SEDBSSCF", version@0x08, headerSize@0x0E (u16), fileSize@0x10
///   offsets header @headerSize: soundCount@+0x02 (u16), audioTableOffset@+0x0C (u32)
///   audio table: soundCount × u32 entry offsets
///   entry (0x20): size@0x00, channels@0x04, freq@0x08, codec@0x0C,
///                 loopStart@0x10, loopEnd@0x14, subInfoSize@0x18, auxCount@0x1C (u16)
///   sub-info @ entry+0x20 (after aux chunks): ogg header(0x20) + seek table + vorbis header
///     ogg header: encodeVersion@0x00 (0x03 = table XOR), xorByte@0x02,
///                 seekTableSize@0x10 (u32), vorbHeaderSize@0x14 (u32)
///   audio data @ entry+0x20+subInfoSize, length = size
public static class ScdReader
{
    public const uint CodecOggVorbis = 0x06;
    public const uint CodecMsAdpcm = 0x0C;
    public const uint CodecHca = 0x1A;

    public sealed class Entry
    {
        public uint Codec;
        public byte[]? Output;             // playable bytes, when produced
        public string Extension = ".ogg";  // matches Output's format
        public string? Skipped;            // reason, when not produced
    }

    public static IReadOnlyList<Entry> Read(byte[] d)
    {
        var entries = new List<Entry>();
        if (d.Length < 0x40
            || BinaryPrimitives.ReadUInt64LittleEndian(d) != 0x4643535342444553UL) // "SEDBSSCF"
            return entries;

        int oh = U16(d, 0x0E); // offsets header
        if (oh + 0x10 > d.Length)
            return entries;

        int count = U16(d, oh + 0x02);
        int audioTable = (int)U32(d, oh + 0x0C);

        for (int i = 0; i < count; i++)
        {
            int tablePos = audioTable + i * 4;
            if (tablePos + 4 > d.Length)
                break;
            int entry = (int)U32(d, tablePos);
            if (entry <= 0 || entry + 0x20 > d.Length)
            {
                entries.Add(new Entry { Skipped = "bad entry offset" });
                continue;
            }

            uint size = U32(d, entry + 0x00);
            uint codec = U32(d, entry + 0x0C);
            uint subInfoSize = U32(d, entry + 0x18);
            int auxCount = U16(d, entry + 0x1C);

            if (codec != CodecOggVorbis && codec != CodecHca)
            {
                entries.Add(new Entry { Codec = codec, Skipped = $"codec 0x{codec:X}" });
                continue;
            }

            // Sub-info begins after the entry header; skip any aux chunks
            // (each: 4-byte tag + 4-byte total chunk size + body).
            int sub = entry + 0x20;
            for (int a = 0; a < auxCount && sub + 8 <= d.Length; a++)
            {
                uint chunkSize = U32(d, sub + 4);
                if (chunkSize == 0)
                    break;
                sub += (int)chunkSize;
            }

            var ent = new Entry { Codec = codec };
            try
            {
                string? reason;
                if (codec == CodecOggVorbis)
                {
                    ent.Output = BuildOgg(d, entry, size, subInfoSize, oggHeader: sub, out reason);
                    ent.Extension = ".ogg";
                }
                else // HCA
                {
                    ent.Output = BuildHca(d, entry, size, subInfoSize, sub, out reason);
                    ent.Extension = ".wav";
                }
                if (ent.Output == null)
                    ent.Skipped = reason;
            }
            catch (Exception ex)
            {
                ent.Skipped = ex.GetType().Name;
            }
            entries.Add(ent);
        }

        return entries;
    }

    private static byte[]? BuildOgg(byte[] d, int entry, uint size, uint subInfoSize, int oggHeader, out string? reason)
    {
        reason = null;
        if (oggHeader + 0x20 > d.Length)
        {
            reason = "ogg header out of range";
            return null;
        }

        byte encodeVersion = d[oggHeader + 0x00];
        byte xorByte = d[oggHeader + 0x02];
        uint seekTableSize = U32(d, oggHeader + 0x10);
        uint vorbHeaderSize = U32(d, oggHeader + 0x14);

        int vorbOff = oggHeader + 0x20 + (int)seekTableSize;
        int dataOff = entry + 0x20 + (int)subInfoSize;

        if (vorbOff + vorbHeaderSize > d.Length || dataOff + size > d.Length)
        {
            reason = "data out of range";
            return null;
        }

        // Output = vorbis header + audio data.
        var ogg = new byte[vorbHeaderSize + size];
        Array.Copy(d, vorbOff, ogg, 0, (int)vorbHeaderSize);
        Array.Copy(d, dataOff, ogg, (int)vorbHeaderSize, (int)size);

        if (encodeVersion == 0x03)
        {
            // Table-based XOR over the WHOLE stream, keyed off the data size. The
            // 256-byte table lives in Lumina's ScdFile; reuse it via reflection
            // rather than embedding a copy.
            var table = V3Table.Value;
            if (table == null)
            {
                reason = "v3 table unavailable";
                return null;
            }
            ApplyV3Xor(ogg, 0, size, table);
        }
        else if (xorByte != 0)
        {
            // v2: single-byte XOR over the vorbis header only (no-op when the
            // file is unencrypted and xorByte is 0).
            for (int j = 0; j < vorbHeaderSize; j++)
                ogg[j] ^= xorByte;
        }

        return ogg;
    }

    // CRI HCA: the HCA header lives at the end of the sub-info and the encoded
    // frames are the data region, contiguous — so [HCA header .. data end] is a
    // complete HCA file. FFXIV's HCA here is unencrypted (cipher type 0), so it
    // decodes straight to WAV with VGAudio (no key needed).
    private static byte[]? BuildHca(byte[] d, int entry, uint size, uint subInfoSize, int subStart, out string? reason)
    {
        reason = null;
        int dataEnd = entry + 0x20 + (int)subInfoSize + (int)size;
        if (dataEnd > d.Length)
        {
            reason = "data out of range";
            return null;
        }

        int hcaStart = FindHca(d, subStart, entry + 0x20 + (int)subInfoSize);
        if (hcaStart < 0)
        {
            reason = "HCA header not found";
            return null;
        }

        var hca = new byte[dataEnd - hcaStart];
        Array.Copy(d, hcaStart, hca, 0, hca.Length);

        int dataOffset = (hca[6] << 8) | hca[7]; // HCA header size (big-endian)
        if (dataOffset < 8 || dataOffset + 2 > hca.Length)
        {
            reason = "bad HCA header";
            return null;
        }

        // The header is stored plain, but the frames are v3-table XOR-scrambled
        // (same table as the Ogg path; keystream indexed from the HCA header start,
        // keyed off the data size). A plain HCA frame begins with the 0xFFFF sync,
        // so only descramble when it isn't already plain.
        if (!(hca[dataOffset] == 0xFF && hca[dataOffset + 1] == 0xFF))
        {
            var table = V3Table.Value;
            if (table == null)
            {
                reason = "v3 table unavailable";
                return null;
            }
            ApplyV3Xor(hca, dataOffset, size, table);

            if (!(hca[dataOffset] == 0xFF && hca[dataOffset + 1] == 0xFF))
            {
                reason = "HCA frame descramble failed (unexpected scrambling)";
                return null;
            }
        }

        try
        {
            var audio = new HcaReader().Read(hca);
            using var ms = new MemoryStream();
            new WaveWriter().WriteToStream(audio, ms);
            return ms.ToArray();
        }
        catch (Exception ex)
        {
            reason = $"HCA decode failed ({ex.GetType().Name}: {ex.Message})";
            return null;
        }
    }

    // Finds the "HCA" magic (plain or high-bit-set) within [from, to).
    private static int FindHca(byte[] d, int from, int to)
    {
        to = Math.Min(to, d.Length - 3);
        for (int i = from; i <= to; i++)
        {
            byte a = (byte)(d[i] & 0x7F), b = (byte)(d[i + 1] & 0x7F), c = (byte)(d[i + 2] & 0x7F);
            if (a == 'H' && b == 'C' && c == 'A')
                return i;
        }
        return -1;
    }

    // Applies the SCD v3 table XOR in place over buf[from..], keyed off the data
    // size. Used for both the Ogg stream (from 0) and the HCA frames (from the
    // header size); the keystream is indexed by the absolute array position.
    private static void ApplyV3Xor(byte[] buf, int from, uint size, byte[] table)
    {
        byte b1 = (byte)(size & 0x7F), b2 = (byte)(b1 & 0x3F);
        for (int j = from; j < buf.Length; j++)
            buf[j] = (byte)(table[(b2 + j) & 0xFF] ^ buf[j] ^ b1);
    }

    private static uint U32(byte[] d, int o) => BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(o));
    private static ushort U16(byte[] d, int o) => BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(o));

    // The 256-byte v3 XOR table is a private static field on Lumina's ScdFile
    // (OggXorTable). It's the same table the HCA frames use, so we borrow it via
    // reflection instead of vendoring a copy.
    private static readonly Lazy<byte[]?> V3Table = new(() =>
    {
        try
        {
            var scd = typeof(Lumina.Data.Files.ScdFile);
            foreach (var t in new[] { scd }.Concat(scd.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)))
                foreach (var f in t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                    if (f.FieldType == typeof(byte[]) && f.GetValue(null) is byte[] a && a.Length == 256)
                        return a;
        }
        catch { /* fall through */ }
        return null;
    });
}
