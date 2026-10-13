# Third-party notices

SharpVision itself is licensed under the MIT License (see [LICENSE](LICENSE)).
It builds on, ports from, or distributes the third-party components listed
below. Each remains under its own license, reproduced here.

A note on the extracted data: SharpVision only reads game files you already
own. FINAL FANTASY XIV and all of its assets (textures, audio, text, etc.) are
© SQUARE ENIX CO., LTD. This tool grants no rights to that content — the
licenses below cover the *software*, not the game data it extracts.

---

## Lumina

- Source: <https://github.com/NotAdam/Lumina> (NuGet package `Lumina` 7.5.0)
- Used as: runtime dependency (sqpack reading, file extraction)
- License: DO WHAT THE FUCK YOU WANT TO PUBLIC LICENSE (WTFPL), Version 2

```
            DO WHAT THE FUCK YOU WANT TO PUBLIC LICENSE
                    Version 2, December 2004

 Copyright (C) 2004 Sam Hocevar <sam@hocevar.net>

 Everyone is permitted to copy and distribute verbatim or modified
 copies of this license document, and changing it is allowed as long
 as the name is changed.

            DO WHAT THE FUCK YOU WANT TO PUBLIC LICENSE
   TERMS AND CONDITIONS FOR COPYING, DISTRIBUTION AND MODIFICATION

  0. You just DO WHAT THE FUCK YOU WANT TO.
```

---

## Lumina.Excel

- Source: <https://github.com/NotAdam/Lumina.Excel> (built from source, `external/Lumina.Excel` submodule)
- Used as: project reference (generated Excel sheet types)
- License: DO WHAT THE FUCK YOU WANT TO PUBLIC LICENSE (WTFPL), Version 2
  (same text as Lumina, above)

---

## SixLabors.ImageSharp

- Source: <https://github.com/SixLabors/ImageSharp> (NuGet package `SixLabors.ImageSharp` 3.1.x)
- Used as: runtime dependency (texture → PNG/JPEG conversion)
- License: **Six Labors Split License, Version 1.0** — full text at
  <https://github.com/SixLabors/ImageSharp/blob/v3.1.5/LICENSE>

ImageSharp 2.x/3.x is split-licensed: a Work is licensed to you under the
**Apache License, Version 2.0** if you meet any of the qualifying criteria
(among them: the consuming software is itself open-source / source-available;
the Work is a transitive dependency; or you are a for-profit with < 1M USD
annual gross revenue, or a non-profit). All other use requires a Six Labors
Commercial License (<https://sixlabors.com/pricing/>).

SharpVision is an open-source (MIT) project, so this use falls under the
Apache 2.0 branch of the split license. (Note: ImageSharp 4.x is no longer
offered under the split license, which is why this project pins 3.1.x.)

---

## VGAudio

- Source: <https://github.com/Thealexbarney/VGAudio> (NuGet package `VGAudio` 2.2.1)
- Used as: runtime dependency (CRI HCA → WAV decoding for the few HCA `.scd`)
- License: MIT

```
The MIT License (MIT)

Copyright (c) 2016 Alex Barney

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

(VGAudio's bundled `LICENSE` omits the author name on the copyright line; it is
authored by Alex Barney.)

---

## ironworks

- Source: <https://github.com/ackwell/ironworks>
- Used as: **ported source** — `SeString/Evaluator/` is a hand C# port of
  ironworks' `sestring::format` evaluator (see
  [SeString/Evaluator/PORTING.md](SeString/Evaluator/PORTING.md))
- License: MIT

```
MIT License

Copyright (c) 2022 Saxon Landers & contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

---

## xiv-data-oxidizer

- Source: <https://github.com/skyborn-industries/xiv-data-oxidizer>
- Used as: **ported source / parity reference** — the CSV SeString rendering
  (`MarkdownWriter`) and the evaluator `Input` follow oxidizer's `formatter.rs`
  / `export.rs` so output matches it (see
  [SeString/Evaluator/PORTING.md](SeString/Evaluator/PORTING.md))
- License: MIT

```
MIT License

Copyright (c) 2025 Matt Antonelli

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

---

## EXDSchema

- Source: <https://github.com/xivdev/EXDSchema> (nested submodules under
  `external/Lumina.Excel/deps/`)
- Used as: **build-time data** — Lumina.Excel generates the `Lumina.Excel.Sheets.*`
  accessor types (column names/order/types) from these YAML schemas. The
  generated type definitions are therefore derived from EXDSchema.
- License: no `LICENSE` file is published in the repository. It is a
  community-maintained schema dataset for the FFXIV data tooling ecosystem.
  Listed here for attribution; if upstream adds an explicit license, update
  this entry accordingly.

---

## Lumenstone (origin of the codebase)

- Source: <https://github.com/TomestoneGG/Lumenstone>
- Relationship: SharpVision's codebase was started by referencing Lumenstone,
  then substantially rewritten (JSON export removed, renamed, new CLI,
  self-contained SCD reader, etc.).
- License: no `LICENSE` file is published in that repository, so its code is
  not under an explicit open-source grant. This notice records the lineage for
  attribution and transparency.
