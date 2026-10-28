# SeString evaluator — ironworks port notes

`SeString/Evaluator/` is a hand C# port of Rust **ironworks**'
`sestring::format` evaluator, so our CSV output matches
[`xiv-data-oxidizer`](https://github.com/skyborn-industries/xiv-data-oxidizer)
(which uses ironworks). This file documents the mapping so the port can be
re-synced when ironworks updates.

## Ported from

- **ironworks**: `https://github.com/ackwell/ironworks`
  - Repo HEAD at port time: `a9b40991b80f7466c2acdfbbe288e4f524a0301a` (2025-12-30)
  - `ironworks/src/sestring/format/` last changed: `9a2b590415d4edf30bc21d29b47c09571a9c04b8` (2025-05-18)
- **xiv-data-oxidizer**: `src/formatter.rs` (the `MarkdownWriter`) and
  `src/export.rs` (the `Input` it feeds ironworks).

## How to re-sync when ironworks updates

1. In the ironworks clone: `git fetch` then
   `git diff 9a2b590..HEAD -- ironworks/src/sestring/format` (and
   `macro_kind.rs`, `expression.rs`, `payload.rs`).
2. For each changed Rust file, apply the same change to the C# file below.
3. Update the commit hashes above.
4. Rebuild, then re-run `--csv-only` and compare against oxidizer's output
   (`diff -rq`). See the parity notes at the bottom.

## File mapping (ironworks → C#)

| ironworks `sestring/format/*.rs` | C# |
|---|---|
| `format.rs` (macro dispatch, `format_expression`) | `SeStringEvaluator.cs` (`Format`, `FormatMacro`, `FormatExpression`) |
| `expression.rs` (`evaluate_expression`, cmp/eq, time) | `SeStringEvaluator.cs` (`EvaluateExpression`, `Cmp`, `Eq`, `Time`) |
| `value.rs` (`Value`) | `SeValue.cs` |
| `input.rs` (`Input`, `Player`, `Gender`) | `SeInput.cs`, `SeValue.cs` (`Player`/`Gender`) |
| `write.rs` (`Write` trait) + oxidizer `formatter.rs` | `SeWriters.cs` (`ISeWriter`, `MarkdownWriter`, `PlainWriter`) |
| `style.rs` (color/style macros) | `SeStringEvaluator.cs` (`HandleColor`, `HandleColorType`, `StyleToggle`) |
| `number.rs` | `SeStringEvaluator.cs` (`Hex`/`Kilo`/`Byte`/`Sec`/`Digit`/`Float`/`Ordinal`/`FormatFloat`) |
| `text.rs` | `SeStringEvaluator.cs` (`Caps`/`Head`/`HeadAll`/`Lower`/`LowerHead`/`Ruby`/`Split`) |
| `control_flow.rs` + `runtime.rs` | `SeStringEvaluator.cs` (`If`/`Switch`/`PcName`/`IfPcGender`/`IfPcName`/`IfSelf`/`FormatBranch`) |
| `time.rs` | `SeStringEvaluator.cs` (`SetTime`/`SetResetTime`, `FfxivEpoch`) |
| `character.rs` | `SeStringEvaluator.cs` (NewLine/SoftHyphen/NonBreakingSpace/Hyphen/Key cases) |
| `excel.rs` (sheet/noun/sound/level_pos) | **no-op** in ironworks → no-op here |
| `argument.rs` (`Arguments`) | `SeStringEvaluator.Args` (nested class) |
| `macro_kind.rs` (byte values) | dispatch `switch` in `FormatMacro` |

## Key porting decisions (must stay faithful)

- **Input = oxidizer's** (`SeInput.Oxidizer()`): only global parameter `1 =
  "Player Player"` is set. Default player is `"Firstname Lastname"`, `Male`.
  Unset params → `Unknown`. If oxidizer's `export.rs` Input changes, update here.
- **`Value.Unknown` renders as `"UNKNOWN"`** (`value.rs`). Unset parameters,
  `StackColor`, and `Unknown` expressions evaluate to this. This is intentional —
  it's how the literal `UNKNOWN` appears in output.
- **MarkdownWriter is asymmetric, by design** (matches oxidizer `formatter.rs`):
  `Bold`→`**`, `Italic`→`*` (ignoring enabled), `PushColor(Foreground)`→`**`,
  `PopColor(usage != Foreground)`→`**`. Do not "balance" it.
- **No-op macros** (ironworks `format.rs` routes these to `format_macro_noop`,
  and `excel.rs` is unimplemented): `Sheet`, `SheetSub`, `*Noun`, `Sound`,
  `LevelPos`, `Icon`, `Wait`, `Scale`, `Time`, `Link`, `Fixed`, `Josa`,
  `Josaro`, and unknown codes. If ironworks *implements* any of these later
  (esp. `excel.rs` / nouns / Korean `josa`), port it then.

## Lumina ↔ ironworks adapter details

ironworks parses SeStrings itself; we instead read Lumina's `ReadOnlySeString`.
The adapter points:

- Macro dispatch is on Lumina `MacroCode`. **`Split` (0x2C)** and **`Key`
  (0x17)** are matched by numeric byte (`(MacroCode)0x2C` / `0x17`) because older
  Lumina enums don't name them. ironworks byte values live in `macro_kind.rs` —
  if Lumina renames/renumbers, revisit these.
- Expression types map to Lumina `ExpressionType`: `LocalNumber`/`GlobalNumber`/
  `LocalString`/`GlobalString` (parameters, via `TryGetParameterExpression`);
  `GreaterThan(OrEqualTo)`/`LessThan(OrEqualTo)`/`Equal`/`NotEqual` (binary, via
  `TryGetBinaryExpression`); `Millisecond..Year`/`StackColor` (placeholders, via
  `TryGetPlaceholderExpression`); integer literals via `TryGetUInt`; immediate
  SeStrings via `TryGetString`. A payload's arguments come from
  `payload as ICollection<ReadOnlySeExpression>`.
- **Text unescaping** (`SeStringEvaluator.UnescapeText`): Lumina renders text
  payloads in macro-string form (escaping `<` and `\`); ironworks writes raw
  UTF-8. We undo the escaping. Not in ironworks — Lumina-specific.
- **Float formatting** (`CsvSheetExporter.RustFloat`): .NET formats small/large
  floats in scientific notation; Rust's `f32` Display uses plain decimal. We
  expand scientific notation to match. Not in ironworks — .NET-specific.

## Subrow id handling (Lumina-specific adapter)

Lumina numbers subrows sequentially (`0,1,2,…`) via `SubrowId`, but the EXD
stores an explicit 2-byte id in the `SubrowHeader` right before each subrow's
field data; ironworks/oxidizer emit that stored id (`12,65535,…`). We read it
straight from the page — `CsvSheetExporter.StoredSubrowId` does
`ExcelPage.ReadUInt16(RowOffset - 2)` (big-endian; `RowOffset` points at the
field data). Used for both typed and raw subrow CSV.

## Subrow shared string buffer (fixed upstream in Lumina 7.7.2)

Lumina ≤ 7.7.1 had a bug reading **string columns of subrow sheets**:
`ExcelPage.ReadString(fieldPos, structOffset)` resolved a string at
`ReadUInt32(fieldPos) + structOffset + dataOffset` with `structOffset` = *that
subrow's own* `RowOffset`. But the EXD lays out one **shared** string pool for the
whole parent row, anchored right after the last subrow's fixed data — so only the
**last** subrow resolved correctly; earlier ones came out fragmented/empty (e.g.
`QuestDefineClient` row 69939 gave `_COSPLAY0` instead of `NPCEQUIP_COSPLAY0`).

SharpVision used to work around it in `CsvSheetExporter.ExportSubrow<T>` by
re-reading every `String` column against the last subrow's `RowOffset`. Lumina
7.7.2 fixed it upstream ([NotAdam/Lumina#126](https://github.com/NotAdam/Lumina/issues/126)),
so the workaround was removed. Verified on the Korean client (2026.10.06), full
CSV extract: Lumina 7.7.2 without the workaround is byte-identical to 7.7.1 with
it (all 7912 files), while 7.7.1 without it differs in exactly
`CustomTalkDefineClient` and `QuestDefineClient`.

Note: 7.7.2 locates the row's string pool from the subrow's **stored** id (see
above), which only matches the physical position when stored ids are sequential.
14 subrow sheets have non-sequential stored ids, but none has a string column
(checked against EXDSchema `columns.yml`), so this doesn't affect output today.
The `dump subrow` diagnostic (`SeStringDump.cs`) prints the candidate bases if
this ever needs re-checking.

## Known residual differences vs oxidizer (NOT port bugs)

~0.1% of CSV cells differ across ~11 sheets; not data loss:

1. **EXDSchema snapshot (~11 sheets)** — we generate Lumina sheet types from the
   *latest experimental* EXDSchema; oxidizer uses a different commit, so sheets
   like `Item`, `Mount`, `Status` have different column names/order. Matching
   would mean pinning oxidizer's exact schema commit (we prefer latest).
