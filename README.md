# SharpVision

FINAL FANTASY XIV의 게임 데이터(`sqpack`)에서 **Excel 시트를 CSV로**, **아이콘·맵·로딩 이미지를 그림 파일로** 추출하는 .NET CLI 도구입니다. [Lumina](https://github.com/NotAdam/Lumina) / [Lumina.Excel](https://github.com/NotAdam/Lumina.Excel)를 기반으로 동작합니다.

## 주요 기능

- **Excel 시트 → CSV**: 게임의 모든 정의형 시트(`Item`, `Action`, `Map` 등)를 시트당 CSV 한 개로 내보냅니다. SeString은 [ironworks](https://github.com/ackwell/ironworks)/[xiv-data-oxidizer](https://github.com/skyborn-industries/xiv-data-oxidizer)와 **동일한 방식**(파라미터 치환·조건문·마크다운 스타일)으로 렌더링됩니다 — 평가기 포팅 상세는 [SeString/Evaluator/PORTING.md](SeString/Evaluator/PORTING.md). 생성 타입이 없는 시트는 raw로 보충해 **누락이 없습니다**.
- **언어 자동 감지**: sqpack에 실제로 들어 있는 언어를 자동으로 찾아 해당 언어만 내보냅니다. 후보는 `ja`, `en`, `de`, `fr`, `ko`, `chs`, `cht`이며, **글로벌·한국어·중국어 클라이언트 모두** 같은 명령으로 동작합니다.
- **아이콘 추출**: `ui/icon/...`의 `.tex`를 PNG로 변환합니다(일반 + `_hr1` 고해상도, 선택 가능). 범위·청크 단위 추출을 지원하며, 기본값은 `1`–`259999`(현행 아이콘 최대치 + 여유)를 훑어 **현재 존재하는 모든 아이콘**을 가져옵니다(빈 ID는 빠르게 건너뜀). 더 높은 범위가 필요하면 `--range`로 지정하세요. 여러 스레드로 **병렬 추출**해 속도가 빠릅니다.
- **맵 / 로딩 이미지 추출**: 맵 타일과 로딩 화면 이미지를 JPEG로 내보냅니다.
- **음원 추출**: `BGM`·`OrchestrionPath` 시트가 참조하는 `.scd`를 재생 가능한 형식으로 내보냅니다. 두 소스는 별개 파일(오케스트리온은 길이·편집·음질이 다름)이라 **전체 / BGM만 / 오케스트리온만** 선택할 수 있습니다. 자체 SCD 리더([Sound/ScdReader.cs](Sound/ScdReader.cs))로 컨테이너를 파싱해 대부분의 Ogg Vorbis는 헤더 XOR(v2/v3)을 복호화해 `.ogg`로 복원하고, 일부 CRI HCA 엔트리는 프레임 디스크램블 후 VGAudio로 `.wav`로 디코딩합니다([SCD 포맷 메모](Sound/SCD-FORMAT.md)). MS-ADPCM 등 그 외 코덱은 건너뜁니다. 효과음/보이스는 시트로 열거되지 않아(경로 목록 필요) 범위 밖입니다.
- **패치 비교 (diff)**: 이미 추출해 둔 두 출력(예: 두 패치)을 비교해 **추가/삭제/변경된 파일** 목록을 뽑습니다. 새 음악·새 아이콘·바뀐 아이콘 등이 한눈에 보이고, CSV는 **행 단위로 어떤 값이 무엇으로 바뀌었는지**(예: 스킬명 변경)까지 리포트합니다. sqpack을 로드하지 않아 즉시 동작합니다.
- **대화형 메뉴 + 설치 자동 탐지**: 인자 없이 실행하면 영어 메뉴가 뜨고, 설치된 FFXIV 클라이언트(글로벌/Steam/한국어/중국어 등)를 자동으로 찾아 선택지로 보여줍니다.
- **라이브 진행 표시**: 추출 중 단계별 진행 막대·퍼센트·경과 시간·예상 종료 시간을 한 줄로 갱신해 보여줍니다.

## 요구 사항

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- 로컬에 설치된 FINAL FANTASY XIV의 `sqpack` 폴더
  예) `.../FINAL FANTASY XIV - A Realm Reborn/game/sqpack`
- 충분한 디스크 여유 공간: 전체 추출은 ~15 GB 규모입니다(아이콘 ~9 GB, 음원 ~5 GB, 나머지 합쳐 ~0.4 GB). 출력 드라이브 여유가 그 추출의 예상 필요량(타깃·범위·해상도·언어 수로 추정)보다 적으면 시작 전에 경고하고(대화형은 계속 여부를 물음), 추출 도중 공간이 바닥나면 깨끗이 멈춥니다(다시 실행하면 이어받음).

## 빌드

서브모듈(Lumina.Excel, EXDSchema)이 필요하므로 `--recursive`로 받습니다.

```bash
git clone --recursive https://github.com/pnsdh/SharpVision.git
cd SharpVision
dotnet build -c Release
```

이미 받은 저장소를 갱신할 때는 서브모듈도 함께 맞춥니다: `git pull && git submodule update --init --recursive`. EXDSchema는 저장소가 매일 자동으로 최신화하므로(아래 [의존성](#의존성) 참고), 이렇게 받아서 다시 빌드하면 최신 게임 패치에 맞는 시트 타입을 쓰게 됩니다.

빌드하면 실행 파일이 만들어집니다.

- Windows: `bin/Release/net10.0/SharpVision.exe`
- Linux / macOS: `bin/Release/net10.0/SharpVision` (또는 `dotnet bin/Release/net10.0/SharpVision.dll`)

배포용 단일 실행 파일이 필요하면 `publish`를 사용하세요(.NET 런타임 포함, 대상 OS 지정):

```bash
dotnet publish -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true -o publish
# 결과: publish/SharpVision.exe
```

## 사용법

크게 다음 방식이 있습니다.

### 1) 대화형 메뉴 (인자 없이 실행)

```bash
SharpVision.exe
```

먼저 **무엇을 할지**(게임 클라이언트에서 추출 / 추출해 둔 두 출력 비교(diff))를 묻습니다. 추출을 고르면, 설치된 FFXIV 클라이언트를 자동으로 찾아(레지스트리 + Steam 라이브러리 + 기본 설치 폴더) 선택지로 보여줍니다. 이때 각 설치의 **실제 데이터를 읽어 지역·언어를 표시**합니다(폴더 이름 추측이 아니라 클라이언트가 실제로 담고 있는 언어 기준 — 설치당 sqpack 인덱스 1회 로드, ~1초). 탐지 결과가 없으면 경로를 직접 입력할 수 있습니다. 이어서 추출 대상(전체/CSV/아이콘/맵/로딩/음원), 언어(시트 추출 + 글로벌 클라이언트일 때만 — 한국어·중국어(간체/번체) 클라는 언어가 하나뿐이라 묻지 않고 자동 선택), 아이콘 범위·해상도, 덮어쓰기 여부를 묻고 실행합니다. 선택한 설치의 로드 결과는 그대로 추출 단계에 재사용되어 중복 로드가 없습니다.

```text
Detecting FFXIV installations...
Reading client data...

Select the FFXIV installation to extract from:
  1) Korean (ko)                C:\Program Files (x86)\FINAL FANTASY XIV - KOREA\game\sqpack
  2) Global (en,de,fr,ja)       ...\steamapps\common\FINAL FANTASY XIV Online\game\sqpack
  3) Enter a path manually
> 1

What do you want to extract?
  1) Full (csv + maps + loading + all icons + sound)
  2) CSV sheets
  3) Icons
  4) Maps
  5) Loading images
  6) Sounds
> 2
Overwrite existing files? (y/N)>
```

### 2) 서브커맨드 (스크립트/자동화)

```bash
SharpVision.exe <sqpackPath> <command> [options]
```

`sqpackPath`(FFXIV `sqpack` 폴더 경로)는 **필수**입니다.

> 개발 중에는 빌드 없이 `dotnet run -- <sqpackPath> <command> [options]`로도 실행할 수 있습니다(프로젝트 폴더 안에서, 인자 앞에 `--` 필요).

#### 명령

| 명령 | 설명 |
|------|------|
| `full` | 전체 추출: CSV + 맵 + 로딩 + 아이콘(전체) + 음원. |
| `csv` | 시트를 CSV로 추출. |
| `icons` | 아이콘 추출. 아래 아이콘 옵션 사용 가능. |
| `maps` | 맵 이미지 추출. |
| `loading` | 로딩 화면 이미지 추출. |
| `sound` | `BGM`·`OrchestrionPath` 시트가 참조하는 음원을 `.ogg`로 추출. |
| `dump sestring <Sheet>` / `dump subrow <Sheet>` | 진단용. |

#### 옵션

| 옵션 | 설명 |
|------|------|
| `--overwrite` | 이미 있는 파일도 다시 추출합니다. **기본값은 건너뜀**(이어받기/빠른 재실행). |
| `--lang <codes>` | 시트를 추출할 언어를 쉼표로 지정 (예: `en` 또는 `en,ja`). **기본값은 감지된 전부.** `csv`/`full`에만 영향(아이콘·맵·로딩은 언어 무관). 코드: `ja en de fr ko chs cht`. |
| `--sheets <names>` | (`csv`) 지정한 시트만 쉼표로 추출 (예: `--sheets Action,Status`). 대소문자 무시. **기본값은 전부.** 없는 시트 이름이 섞이면 아무것도 쓰지 않고 오류로 종료하며, 요청한 시트가 추출되지 않은 경우에도 오류(종료 코드 1)로 끝납니다. |
| `--range <start-end>` | (`icons`) 연속 아이콘 범위 (예: `--range 68000-68999`). **기본은 `1-259999`.** |
| `--chunk <n>` | (`icons`) 아이콘 범위를 `n`개 단위 청크로 분할. |
| `--index <k>` | (`icons`) `--chunk`와 함께 쓰는 0-기반 청크 번호. |
| `--normal-only` / `--hd-only` | (`icons`) 일반 해상도만 / 고해상도(`_hr1`)만. **기본은 둘 다.** |
| `--bgm` / `--orchestrion` | (`sound`) BGM만 / 오케스트리온만. **기본은 둘 다.** |

> 글로벌 클라이언트는 4개 언어(`ja`/`en`/`de`/`fr`)가 모두 들어 있어, 기본값으로 두면 **네 언어 모두** 추출됩니다(`csv/<lang>/...` 4벌). 한 언어만 필요하면 `--lang en`처럼 좁히세요. 한국어·중국어 클라이언트는 언어가 하나뿐이라 영향이 없습니다.

#### 예시

```bash
# 전체 추출
SharpVision.exe "C:\path\to\game\sqpack" full

# 시트를 CSV로 추출
SharpVision.exe "C:\path\to\game\sqpack" csv

# 글로벌 클라에서 영어 시트만 추출
SharpVision.exe "C:\path\to\game\sqpack" csv --lang en

# 이미 추출한 것도 덮어쓰며 추출
SharpVision.exe "C:\path\to\game\sqpack" csv --overwrite

# 필요한 시트만 추출 (--lang, --overwrite와 함께 쓸 수 있음)
SharpVision.exe "C:\path\to\game\sqpack" csv --sheets Action,Status,TerritoryType --lang en

# 특정 아이콘 범위(68000번대)만 추출
SharpVision.exe "C:\path\to\game\sqpack" icons --range 68000-68999

# 아이콘을 10000개씩 나눈 것 중 3번째 청크만 추출
SharpVision.exe "C:\path\to\game\sqpack" icons --chunk 10000 --index 3

# 고해상도(_hr1) 아이콘만 추출
SharpVision.exe "C:\path\to\game\sqpack" icons --hd-only

# 음원을 .ogg로 추출 (BGM + 오케스트리온)
SharpVision.exe "C:\path\to\game\sqpack" sound

# 오케스트리온 음원만 추출
SharpVision.exe "C:\path\to\game\sqpack" sound --orchestrion
```

한국어 클라이언트도 동일하게 동작합니다(언어는 자동으로 `ko`로 감지됨).

### 3) 패치 비교 (diff)

이미 추출해 둔 두 출력 폴더를 비교합니다. sqpack을 로드하지 않으므로 즉시 동작하며, sqpack 경로도 필요 없습니다.

```bash
# 두 출력 폴더를 직접 지정
SharpVision.exe diff "output/extract/korean/2026.05.01.0000.0000" "output/extract/korean/2026.06.01.0000.0000"

# 지역만 주면 output/extract/<지역>/ 아래 최신 두 버전을 자동 비교
SharpVision.exe diff korean
```

`icons`/`maps`/`loading`/`sound`는 파일 단위로 **추가/삭제/변경**만 가립니다(새 음악, 바뀐 아이콘 등). 각 카테고리는 `assets/<category>.html`로도 나와, 변경된 에셋을 브라우저에서 한눈에 볼 수 있습니다(이미지는 old→new 썸네일, 사운드는 재생 플레이어 — 원본 파일을 `file://`로 참조하므로 추출 폴더가 그대로 있어야 함). `csv`는 시트 추가/삭제뿐 아니라 **행 단위 변경**(추가/삭제된 행, 그리고 변경된 행의 `컬럼: 옛값 → 새값` — 예: 번역으로 바뀐 스킬명)까지 비교합니다. 컬럼은 이름 기준으로 정렬해 비교하므로 패치 간 컬럼이 추가/삭제/이름변경/순서변경돼도 어긋나지 않습니다(헤더는 게임 버전이 아니라 추출 시점의 EXDSchema가 정함). 드물게 한쪽은 스키마(이름 헤더), 다른 쪽은 raw(헤더 없음)로 추출된 시트는 셀 비교가 부정확할 수 있어 ⚠ 경고로 표시합니다 — 이때도 행 추가/삭제는 RowId 기준이라 정확합니다.

`csv --sheets`로 일부 시트만 추출한 언어 폴더(`csv/<lang>/.partial` 표시 파일이 있음)는 대부분의 시트가 없으므로, 그 폴더 때문에 생기는 시트 추가/삭제는 결과에서 빼고 ⚠ 경고로 알립니다. 양쪽에 다 있는 시트는 그대로 비교합니다. 같은 버전을 나중에 전체 추출하면 이미 있는 시트는 건너뛰고 나머지만 채운 뒤 표시 파일을 지웁니다.

콘솔에는 카테고리별 추가/삭제/변경 개수 요약만 출력하고, 상세 리포트는 `output/diff/<지역>/<옛버전>__<새버전>/`에 남깁니다.

```text
output/diff/korean/2026.05.01.0000.0000__2026.06.01.0000.0000/
  summary.md                         # 카테고리별 요약 + 추가/삭제/변경 시트 목록 (markdown)
  csv-changes.csv                    # 전 시트 변경을 한 파일로 (Sheet,Change,Key,Column,Old,New — 작은 패치에 편리)
  csv/<lang>/<Sheet>.csv             # 시트별 행/열 변경 (Change,Key,Column,Old,New — 스프레드시트로 열기)
  assets/<category>.html             # 변경/추가/삭제된 에셋을 한눈에 (이미지 썸네일·사운드 플레이어)
  assets/<category>-added.txt 등     # 아이콘/맵/사운드/로딩 경로 목록
```

> 인자 없이 실행한 대화형 메뉴에서도 "Diff two extracted outputs"를 골라 같은 비교를 할 수 있습니다(지역과 비교할 두 버전을 목록에서 선택).

## 출력 구조

추출물은 **실행 파일이 있는 폴더** 아래 `output/extract/<지역>/<버전>/`에 생성됩니다(현재 작업 디렉터리가 아니라). 지역(`korean`/`global`/`simplified-chinese`/`traditional-chinese`)과 게임 빌드 버전으로 묶어, 서로 다른 클라이언트·패치의 결과가 섞이거나 덮어써지지 않습니다(7.5부터 한국·글로벌·중국 클라가 같은 버전 문자열을 쓰므로 **지역**으로 구분). 한 지역의 연속된 패치가 형제 폴더로 쌓여 패치 간 비교가 쉽습니다. (diff 리포트는 `output/diff/` 아래에 따로 생성됩니다.)

```
output/extract/<지역>/<버전>/
  csv/<lang>/<SheetName>.csv                   # 시트 데이터 CSV(시트당 1개)
  icons/normal/<NNN000>/<NNNNNN>.png           # 아이콘 (일반)
  icons/hd/<NNN000>/<NNNNNN>_hr1.png           # 아이콘 (고해상도)
  maps/<prefix>/<prefix>.<suffix>.jpg          # 맵 타일
  loadingimages/<name>.jpg                     # 로딩 화면 이미지
  sound/music/.../<name>.ogg                   # BGM 음원 (sqpack 경로 구조 보존)
```

예) 한국어 클라이언트, 빌드 `2026.05.01.0000.0000` → `output/extract/korean/2026.05.01.0000.0000/csv/ko/Item.csv`

- CSV 형식: 첫 줄은 `#`(행 id 컬럼) + 컬럼 이름, 이후 각 행은 `<RowId>`(서브로우 시트는 `<RowId>.<SubrowId>`) + 값. 배열 컬럼은 `Name[0]`, `Name[1]`…, 중첩 구조는 `Name.Field`, 참조 컬럼은 참조 대상 RowId, 문자열은 SeString을 마크다운으로 렌더링한 텍스트입니다. 스키마에 없는 시트는 헤더 없이 EXH 컬럼 순서대로 raw 추출됩니다.
- `<지역>`은 감지된 언어로 정해집니다: 단일 언어 클라는 그 언어(`korean`/`simplified-chinese`/`traditional-chinese`), 여러 언어를 담은 글로벌 클라는 `global`. `<버전>`은 게임 빌드 버전(date-build)이며, 읽지 못하면 `unknown`으로 대체됩니다.
- `<lang>`은 감지된 언어 코드(`ja`/`en`/`de`/`fr`/`ko`/`chs`/`cht`)입니다.
- 아이콘은 해상도별로 `icons/normal`·`icons/hd`로 나뉘고, 그 안에서 ID를 1000 단위 폴더로 묶습니다. 예) 아이콘 `068123` → `icons/normal/068000/068123.png`, 고해상도는 `icons/hd/068000/068123_hr1.png`
- 기본은 **이미 있는 파일을 건너뜁니다.** 데이터가 갱신된 패치 직후 등 강제로 다시 만들려면 `--overwrite`(또는 대화형 메뉴의 Overwrite 질문)를 사용하세요.

## 의존성

| 패키지 | 버전 | 관리 | 용도 |
|--------|------|------|------|
| [Lumina](https://www.nuget.org/packages/Lumina) | 7.7.1 | NuGet (수동 갱신) | sqpack 읽기 / 파일 추출 |
| [Lumina.Excel](https://github.com/NotAdam/Lumina.Excel) | master 고정 커밋 | **소스 빌드** (`external/Lumina.Excel`, 서브모듈, 수동 갱신) | 정의형 Excel 시트 타입 생성기 |
| [EXDSchema](https://github.com/xivdev/EXDSchema) | `latest` 브랜치 | 서브모듈 (`external/EXDSchema`, **매일 자동 갱신**) | 시트 타입을 생성할 스키마 |
| [SixLabors.ImageSharp](https://www.nuget.org/packages/SixLabors.ImageSharp) | 3.1.12 | NuGet | 텍스처 → PNG/JPEG 변환 |
| [VGAudio](https://www.nuget.org/packages/VGAudio) | 2.2.1 | NuGet | CRI HCA 음원 디코딩 (.scd 일부) |

- **Lumina.Excel**은 NuGet 대신 소스로 빌드합니다. 어떤 스키마로 `Lumina.Excel.Sheets.*` 타입을 생성할지는 `SharpVision.csproj`의 **`ExdSchema` 속성**이 정하며, 기본값은 `latest`(`external/EXDSchema`, 최신 클라이언트와 일치). 이 한 값이 Lumina.Excel의 `SchemaPath`와 기동 배너에 표시되는 스키마 이름·버전까지 함께 결정하므로, 최신 스키마가 빌드를 깨뜨리면 `dotnet build -p:ExdSchema=stable`로 Lumina.Excel에 고정된 stable 스키마로 빌드할 수 있습니다.
- **EXDSchema 자동 갱신**: GitHub Action(`.github/workflows/update-schema.yml`)이 매일 `latest` 브랜치의 새 커밋을 확인해, 그 스키마로 **빌드가 통과할 때만** 서브모듈 포인터를 올리는 커밋을 main에 남깁니다. 새 스키마가 현재 생성기와 맞지 않아 빌드가 깨지면 반영하지 않고 실패로 남으며, 그때는 Lumina.Excel을 직접 갱신합니다.
- **버전**: 프로그램 버전(SemVer, `SharpVision.csproj`의 `Version`)은 코드·동작이 바뀔 때만 올리고 `vX.Y.Z` 태그를 답니다. 스키마 자동 갱신은 버전을 올리지 않으며, 대신 기동 배너에 커밋 해시와 스키마 날짜가 함께 표시되어 빌드를 정확히 식별할 수 있습니다(예: `SharpVision v1.1.0 (abc1234)`).
- ImageSharp는 3.1.x를 사용합니다. 4.x도 같은 Split License(오픈소스는 무료)이지만 빌드 시 Six Labors 라이선스 키를 검사해, 키 없이는 Release 빌드가 실패하기 때문입니다.

## 크레딧

- 코드 베이스는 [TomestoneGG/Lumenstone](https://github.com/TomestoneGG/Lumenstone)을 참조해 시작했습니다.
- CSV의 SeString 렌더링은 [ironworks](https://github.com/ackwell/ironworks)(MIT)의 `sestring::format` 평가기를 C#로 포팅한 것입니다 — 상세·출처는 [SeString/Evaluator/PORTING.md](SeString/Evaluator/PORTING.md).

## 라이선스

- SharpVision 자체는 **MIT 라이선스**입니다 — [LICENSE](LICENSE).
- 의존·포팅·동봉한 서드파티 구성요소의 라이선스 전문은 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)에 정리되어 있습니다(Lumina/Lumina.Excel: WTFPL, ImageSharp: Six Labors Split License, VGAudio/ironworks/xiv-data-oxidizer: MIT 등).
- 이 도구는 사용자가 이미 보유한 게임 파일을 읽을 뿐이며, 추출되는 FINAL FANTASY XIV 데이터(텍스처·음원·텍스트 등)는 © SQUARE ENIX입니다. 위 라이선스는 *소프트웨어*에 대한 것이고 게임 데이터에 대한 권리를 부여하지 않습니다.
