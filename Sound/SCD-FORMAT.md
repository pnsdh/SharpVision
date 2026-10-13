# SCD reader notes (`Sound/ScdReader.cs`)

SharpVision reads FFXIV `.scd` audio containers itself instead of using Lumina's
`ScdFile`, because **`ScdFile.GetAudio` has no HCA branch**: codec `0x1A` falls
through to the default case and returns only the raw (still scrambled) data
region, without the HCA header that lives in the sub-info, so it can't be
decoded. Handling Ogg in the same reader keeps both codecs on one path that
bounds-checks each entry and reports a skip reason instead of throwing.

The Ogg branch used to be broken upstream too (it read `OggHeaderSize + Size`
bytes from the audio-data region, which only holds `Size` bytes, throwing on
essentially every Ogg SCD). Lumina 7.6.1 fixed that
([NotAdam/Lumina#131](https://github.com/NotAdam/Lumina/pull/131)), and our Ogg
output is equivalent to theirs — so if HCA support ever lands upstream, this
reader could be retired.

We still lean on Lumina for two things: the raw file bytes
(`GameData.GetFile<FileResource>(path).Data`) and the 256-byte v3 XOR table
(reflected from `ScdFile` — see "v3" below).

Most entries are Ogg Vorbis (→ `.ogg`); a few are CRI HCA (→ `.wav`, decoded with
VGAudio — see "HCA" below). MS-ADPCM and other codecs are skipped.

## Container layout (SEDBSSCF v3, little-endian)

```
0x00  "SEDBSSCF"
0x08  u32  version (3)
0x0E  u16  headerSize (0x30)
0x10  u32  fileSize

offsets header @ headerSize:
  +0x02 u16  soundCount
  +0x08 u32  table0 offset
  +0x0C u32  audio-header table offset   <-- the one we follow
  +0x10 u32  table2 offset
  +0x06 u16  sentinel 0x270F

audio-header table @ (above): soundCount × u32  -> entry offsets

entry (0x20 bytes) @ entryOffset:
  0x00 u32  size          (audio-data length in bytes)
  0x04 u32  channels
  0x08 u32  sampleRate
  0x0C u32  codec          (0x06 = Ogg Vorbis, 0x0C = MS-ADPCM, 0x1A = CRI HCA)
  0x10 u32  loopStart
  0x14 u32  loopEnd
  0x18 u32  subInfoSize    (= 0x20 + seekTableSize + vorbHeaderSize)
  0x1C u16  auxCount

aux chunks (auxCount of them), each: tag(4) + chunkSize(4, incl. header) + body.
Skip each by chunkSize.

ogg sub-header @ (entry+0x20, after aux chunks):
  0x00 u8   encodeVersion  (0x02 = single-byte XOR, 0x03 = table XOR, 0x00 = none)
  0x02 u8   xorByte        (v2)
  0x10 u32  seekTableSize
  0x14 u32  vorbHeaderSize

seek table   @ subHeader + 0x20            (seekTableSize bytes; not needed for playback)
vorbis header@ subHeader + 0x20 + seekTableSize   (vorbHeaderSize bytes)
audio data   @ entry + 0x20 + subInfoSize         (size bytes)
```

## Reconstructing the `.ogg`

```
ogg = vorbisHeader (vorbHeaderSize bytes) ++ audioData (size bytes)
```
then decrypt:

- **v2** (`encodeVersion == 0x02`): XOR the **vorbis-header** bytes only with
  `xorByte`. (Unencrypted files have `xorByte == 0` → no-op.)
- **v3** (`encodeVersion == 0x03`): XOR the **whole** stream (header + data):
  ```
  byte1 = size & 0x7F;   byte2 = byte1 & 0x3F;
  for j in 0..ogg.Length:  ogg[j] ^= table[(byte2 + j) & 0xFF] ^ byte1;
  ```
  `table` is the 256-byte XOR table. Most current/Korean-client BGM are v3.

The header is a real Ogg bitstream (the id/comment/setup pages) and the data
region is the audio pages, so concatenating them yields a valid `.ogg` (no
re-encode).

### The v3 XOR table (reflection)

The table is identical across FFXIV tools and is a `static readonly byte[256]`
field inside Lumina's `ScdFile`. Rather than vendoring a copy, `ScdReader` reflects
it out of the loaded Lumina assembly (`V3Table`). If a future Lumina drops/renames
it, the lookup returns null and v3 entries are skipped gracefully (logged). To
remove that soft dependency, dump the 256 bytes once and embed them as a constant.

## HCA (codec 0x1A → `.wav`)

A small number of BGM store **CRI HCA** instead of Ogg Vorbis (in this Korean
client, only `BGM_Town_Gri_Day/Night`). Layout:

- The **HCA header** sits at the end of the sub-info (`"HCA\0"` magic, chunks
  `fmt`/`comp`/`loop`/`ciph`/`pad`) and is stored **plain**; its `dataOffset`
  (header size, big-endian u16 at magic+6) points exactly at the data region.
- The **frames** (data region) are **v3-table XOR-scrambled** — the same table as
  Ogg v3, with the keystream indexed from the HCA header start (so frame byte at
  array index `j` uses `table[(byte2 + j) & 0xFF]`, `byte1 = size & 0x7F`,
  `byte2 = byte1 & 0x3F`). The header is left untouched.

To extract: take `[HCA header .. data end]` as one buffer, descramble the frames
(`hca[j] ^= table[(byte2+j)&0xFF] ^ byte1` for `j >= dataOffset`), then decode with
VGAudio (`HcaReader` → `WaveWriter`) to `.wav`. A plain HCA frame begins with the
`0xFFFF` sync, so we only descramble when the first frame isn't already `FF FF`
(handles both scrambled and any plain HCA). The `ciph` chunk here is type 0 (no
HCA-internal cipher), so no key is needed.

## Worked example — `music/ffxiv/BGM_System_Title.scd` (v2)

```
fileSize 0xBE040; audio entry @0x230: size=0xBC6A7, ch=2, 44100Hz, codec=0x6,
subInfoSize=0x1747, auxCount=0
sub-header @0x250: encodeVersion=0x02, xorByte=0x94, seekTableSize=0x5D0, vorbHeaderSize=0x1157
check: 0x20 + 0x5D0 + 0x1157 = 0x1747 = subInfoSize ✓
       entry+0x20+subInfoSize+size = 0xBE03E ≤ fileSize ✓
```

## Known skips (not failures)

- `codec` other than Ogg Vorbis / HCA (e.g. MS-ADPCM `0x0C`): would need
  decoding/WAV wrapping. Rare for BGM.
- `not an SCD / no audio table`: empty or placeholder files (e.g. `BGM_*_Null`
  sentinels, or content not fully present in the client).

## Diagnostics

`dump scd <path>` (`SeStringDump.DumpScd`) prints the decoded header, the audio
entry, a FITS/OVERRUN check, and a hex window — and for HCA (codec 0x1A) an
`[hca]` block that tests descramble candidates against the `0xFFFF` frame sync.
`dump scdskips` lists every BGM/orchestrion .scd that gets skipped, with reasons.

## Verification

Run `sound` and confirm the count and that files play (VLC etc.). On the Korean
client this extracts ~1237/1303 BGM (Ogg) plus the orchestrion set; the two HCA
town themes come out as `.wav`. The rest are the known skips above.
