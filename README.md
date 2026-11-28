# CSV to Project XML — V2

A Windows desktop converter for the archive import package described in the V1 reverse-engineered specification. V2 is built as a **.NET 10 MAUI Blazor Hybrid** application. The UI is Arabic/RTL, the conversion engine is a separate plain .NET library, and all processing is local/offline after installation.

## What is included

```text
CsvProjectXmlV2/
├── CsvProjectXml.sln
├── src/
│   ├── CsvProjectXml.Core/          # CSV parser, validation, XML writer, batch logic
│   └── CsvProjectXml.Maui/          # .NET MAUI + Blazor Hybrid Windows desktop UI
├── tests/
│   └── CsvProjectXml.Core.Tests/    # Unit tests + optional byte-for-byte golden test
└── samples/golden/README.md         # Expected fixture layout
```

## V2 features

### 1. Convert one department or a whole folder

**Single mode** accepts two file paths:

- `<slug>_data.csv`
- `<slug>_fields.csv`

The common filename prefix is matched against `names.json`. The user does not need to type the Arabic department name or Franko slug manually. The converter rejects files that do not share the same slug or have no matching `names.json` entry.

**Batch mode** accepts a folder containing pairs named `<slug>_data.csv` and `<slug>_fields.csv`. It reads the department registry and processes each complete pair independently. Its report includes:

- departments converted successfully;
- departments with no matching CSV files;
- departments with only one file of the pair;
- departments whose conversion failed, including file/line/column details;
- CSV files that do not match an entry in `names.json`;
- row count, film count, field count, output file sizes, and SHA-256 hashes for each successful department.

The batch scanner examines the **top level** of the selected input folder; it does not recurse into subfolders.

### 2. Arabic output folders, while keeping Franko project identity

The value in `names.json` (`value`) remains the project identity written into these XML elements:

- `PROJECT_SHORT_DESC`
- `PROJECT_NAME`

The Arabic `text` value is available as the output folder name. There is a checkbox in the conversion page:

- **Unchecked (default):** folder name is the stored slug, matching the V1 folder layout.
- **Checked:** folder name is the trimmed Arabic display name; XML project details still contain the stored slug.

This setting applies to both single and batch modes.

### 3. Manage `names.json`

The department page can add, edit, and remove entries from the selected `names.json` file.

- Existing `value` slugs are never recalculated when the Arabic text is edited.
- A new entry receives `max(index) + 1`, a generated slug preview, and a trimmed Arabic name.
- An existing slug can be edited manually; the UI warns that changing it affects CSV filename matching.
- Removing an entry only changes `names.json`. It does not delete its source CSV files or existing XML output.
- The exact selected `names.json` file is updated atomically and its path is remembered in MAUI preferences. That file is the registry source of truth; it is not silently copied into an unrelated app-data folder.

## V1 compatibility behavior

The default conversion path preserves the observable V1 rules from the supplied specification:

- UTF-8 CSV input with an optional BOM; strict CSV parsing, including quoted commas and escaped quotes;
- fields in the order they occur in `*_fields.csv`, not sorted by the `ID` column;
- data rows in their original CSV order; source row `ID` is ignored;
- `FILM_NUMBER`, `FILM_TYPE`, `PHOTO_PATH`, then custom fields in the fields-file order;
- Windows backslashes in `PHOTO_PATH`;
- raw values are not trimmed, normalized, Arabic-digit-converted, or otherwise changed by the default path;
- data XML has the UTF-8 declaration; the other XML files do not;
- UTF-8 output without BOM, CRLF structure newlines, TAB indentation, and no final newline;
- `<DocumentElement/>` lookup XML when `ListValues` is empty;
- the four filenames and V1-style slug folder name by default.

Built-in field types are `NVARCHAR=1` and `LIST=5` (both confirmed against real V1 output). Other types, such as `INT` and `DATE`, fail explicitly until the V1 code is known; add it to `FieldTypeMap` in `conversion-settings.json`, or enable `UnmappedFieldTypeAsText` (Settings page) to write them as NVARCHAR and list each one for review. A repeated FieldName with no Arabic name of its own is ignored and reported.

`ListValues` is a JSON array typed into the CSV without quoting, e.g. `["A","B"]`; it is read as the rest of the line, so commas and quotes inside it are fine. V1 writes it to `Project_lookup_Fields.xml` with the list fields in reverse CSV order, each field's items in reverse list order, and `FIELD_LOOKUP_ORDER` equal to the position of the field in that reversed sequence. With no list values the file is exactly `<DocumentElement/>`. Values are XML-escaped for `&`, `<`, and `>` while Arabic text, quotes, TABs, and trailing spaces are preserved.

The data CSV is streamed to a staging file. The four output files are committed as a department folder only after that department passes validation. This prevents a failed conversion from leaving a partial set of XML files. An existing output folder containing unrelated files or subdirectories is not overwritten.

## Requirements

- Windows 10 version 1809 or later / Windows 11 (the project currently targets Windows desktop only).
- .NET 10 SDK.
- Visual Studio 2026 18.0 or later with the **.NET MAUI development** workload, or a matching .NET 10 MAUI command-line workload setup.
- Internet is not required by the application at runtime. NuGet restore and MAUI workload installation may require network access during development/build.

Microsoft's current .NET MAUI support policy lists .NET MAUI 10 as supported through May 11, 2027; the latest .NET MAUI 10 patch shown on November 14, 2026 is 10.0.130. See [the MAUI support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/maui), [supported platforms](https://learn.microsoft.com/en-us/dotnet/maui/supported-platforms?view=net-maui-10.0), and [Blazor Hybrid documentation](https://learn.microsoft.com/en-us/aspnet/core/blazor/hybrid/?view=aspnetcore-10.0).

## Build and run

Open `CsvProjectXml.sln` in Visual Studio 2026 and select `CsvProjectXml.Maui` as the startup project. Restore NuGet packages, then run the Windows target.

From PowerShell, the equivalent commands are:

```powershell
dotnet workload install maui
dotnet restore .\CsvProjectXml.sln
dotnet build .\src\CsvProjectXml.Maui\CsvProjectXml.Maui.csproj -f net10.0-windows10.0.19041.0
```

For an unpackaged Windows publish folder:

```powershell
dotnet publish .\src\CsvProjectXml.Maui\CsvProjectXml.Maui.csproj `
  -c Release `
  -f net10.0-windows10.0.19041.0 `
  -r win-x64 `
  --self-contained false
```

The publish output is under `src\CsvProjectXml.Maui\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish`. The source was packaged without a prebuilt binary; the .NET SDK was not available in the packaging environment, so run the build and test commands on a Windows machine with the MAUI workload installed.

## First-time setup

1. Start the application.
2. Open **إدارة الإدارات** (Department Management).
3. Select the existing `names.json` file used by the data package.
4. Optionally inspect **إعدادات التصدير**. The default constants and field map reflect V1's observed values.
5. Open **تحويل الملفات** (Conversion).
6. For a single conversion, select the data CSV and fields CSV. Their shared filename prefix is matched automatically to `names.json`.
7. For bulk conversion, switch to **معالجة جماعية**, select the CSV source folder, then select the output folder.
8. Leave the Arabic output-folder option unchecked for the V1 folder layout. Enable it when the new Arabic-folder behavior is desired.

The app remembers the selected `names.json` and output folder paths using MAUI preferences. Settings are stored beside `names.json` in `conversion-settings.json`.

## `names.json` format

The file is a UTF-8 JSON array:

```json
[
  {
    "index": 1,
    "value": "aladara_alaama_llahsae_w_altqaryr_hyeh_altnmya_alsnaaya",
    "text": "الادارة العامة للاحصاء و التقارير"
  }
]
```

The existing stored `value` is authoritative. The application does not regenerate legacy slugs, including the documented index 13 and index 56 anomalies. New department slugs use the transliteration table from the V1 specification and the fixed `_hyeh_altnmya_alsnaaya` suffix.

## `conversion-settings.json`

The settings file is created beside `names.json` after the user saves settings. If it does not exist, the application uses the safer V2 defaults. The original V1 behavior is available as an explicit, opt-in `LegacyTolerantMode`:

```json
{
  "ProjectCode": 444,
  "OrgCode": 37,
  "UserModificationCode": 71,
  "IsCivil": 1,
  "IsStructureCreated": 1,
  "TimestampFormat": "MM/dd/yyyy hh:mm:ss tt",
  "EmptyValueMode": "SelfClosingTags",
  "ExtraColumnsMode": "Hold",
  "LegacyTolerantMode": false,
  "RejectDuplicatePhotoPaths": false,
  "QuoteHandling": "Keep",
  "ValidateNotNull": false,
  "ValidateFieldSize": true,
  "EnabledFeatureIds": [],
  "FieldTypeMap": {
    "NVARCHAR": 1,
    "LIST": 5
  }
}
```

`ProjectCode`, `OrgCode`, `UserModificationCode`, `IsCivil`, and `IsStructureCreated` can be changed here or from the settings screen. Timestamp format is culture-invariant; the default uses the local machine clock and English AM/PM. A custom timestamp can also be supplied per conversion for deterministic replay.

Empty values default to the self-closing form `<field/>`, which is what V1 wrote. `PairedTags` (`<field></field>`) is available. If `conversion-settings.json` was saved by an earlier build it still says `PairedTags`; change it or delete the file.

### Messy data: what V1 did, and the settings that control it

The recovered V1 converter removed every `"` character from the data file before parsing it, mapped data by the actual header names, omitted XML elements for expected fields whose headers were absent, dropped cells beyond the actual header width, and filled missing row cells with empty strings. It also wrote non-numeric system identifiers, ignored `FieldSize` and `NotNull`, and fell back to type code `1` for an unknown field type. These choices can silently lose or misplace data, so the safer V2 defaults do not do them:

| Setting | Default | Options |
|---|---|---|
| `LegacyTolerantMode` | `false` | `true` enables the original V1 row pipeline: remove all quotes before parsing, drop extra cells, pad missing cells, accept non-numeric system identifiers, ignore `FieldSize`/`NotNull`, and map unknown field types to `NVARCHAR=1` |
| `ExtraColumnsMode` (row has more cells than the header) | `Hold` | `MergeIntoLastColumn` joins extras; `DropExtra` drops them |
| `QuoteHandling` (a `"` inside a value, e.g. `Ringer"s`) | `Keep` | `Remove` deletes quotes after parsing; `LegacyTolerantMode` removes them before parsing, which can split quoted commas |
| `RejectDuplicatePhotoPaths` (several rows with the same FilmType, FilmId and PortfolioId) | `false` - written as in V1 and reported | `true` holds such rows back outside legacy mode |

Every detected row adjustment is listed with file and line in the conversion report (`AutoFixes`); entries marked `REVIEW` deserve a look. `ConversionSettings.V1Compatible()` enables `LegacyTolerantMode`. The fields-definition header, duplicate/empty data headers, XML tag safety, and `ListValues` syntax are still checked; data columns are mapped by their actual header names and missing expected headers are reported while their XML elements are omitted, as in V1.

## Tests

Run the unit suite:

```powershell
dotnet test .\tests\CsvProjectXml.Core.Tests\CsvProjectXml.Core.Tests.csproj
```

It covers UTF-8 BOM handling, CRLF and TAB preservation, escaping, empty values, unknown `FieldType`, extra/missing columns, ignored source IDs and `NotNull` metadata, duplicate film/portfolio pairs, Arabic output directory naming, and registry slug preservation/transliteration. The golden test is reported as skipped until `CSV_XML_GOLDEN_DIR` points to the owner-supplied original sample package.

### Run the byte-for-byte golden test

The actual sample CSV/XML package must be supplied by the owner. The test does not manufacture substitute reference files. Arrange the fixture like this:

```text
D:\csv-xml-golden\
  names.json
  golden-stamps.json
  <slug>_fields.csv
  <slug>_data.csv
  expected\
    <slug>\
      Project.xml
      Project_Fields.xml
      Project_Data.xml
      Project_lookup_Fields.xml
```

`golden-stamps.json` maps each slug to the exact original V1 generation timestamp. Example:

```json
{
  "aladara_alaama_llahsae_w_altqaryr_hyeh_altnmya_alsnaaya": "11/11/2026 09:52:32 AM",
  "aladara_alaama_lmtabaa_alarady___ljna_alhsr_hyeh_altnmya_alsnaaya": "11/11/2026 10:43:53 AM",
  "almtwr_alsnaay___mkhttat_hyeh_altnmya_alsnaaya": "11/11/2026 12:25:49 PM"
}
```

Set the environment variable and run the tests:

```powershell
$env:CSV_XML_GOLDEN_DIR = 'D:\csv-xml-golden'
dotnet test .\tests\CsvProjectXml.Core.Tests\CsvProjectXml.Core.Tests.csproj
```

The golden test compares each of the four generated XML files byte-for-byte. It is intentionally separate from the small unit fixtures and is marked **skipped** when `CSV_XML_GOLDEN_DIR` is unset. See [COMPATIBILITY_REPORT.md](COMPATIBILITY_REPORT.md) for the implemented rules and open assumptions.

## Extending the feature system

Future field-level behavior belongs in a class implementing `IDataValueFeature` in `CsvProjectXml.Core`. Each feature has a stable `Id`, a display name, and a `Transform` method. It is inert unless its ID is explicitly listed in `ConversionSettings.EnabledFeatureIds`. The converter runs enabled features per field and then validates the resulting value. No normalizer, whitespace trimmer, date parser, or Arabic spelling map is enabled in this initial V2 build.

The bulk workflow (`BatchConversionService`), department registry (`NamesRegistryService`), XML converter (`CsvProjectConverter`), and UI are independent services, so more screens/workflows can be added without rewriting the byte-sensitive XML emitter.

## Behavior compatibility report

| Area | V2 behavior |
|---|---|
| Project.xml template and constants | Implemented; observed constants are defaults and configurable |
| Project_Fields.xml block order and values | Implemented; system fields first, then CSV row order |
| Project_Data.xml row and child order | Implemented; streams rows and retains order |
| `PHOTO_PATH` | Implemented with literal Windows backslashes |
| Source IDs | Ignored for output and ordering; gaps are allowed |
| Raw Arabic values | Preserved by default; no trimming or Unicode normalization |
| XML escaping | Escapes `&`, `<`, `>` only in text nodes |
| Encoding/layout | UTF-8 no BOM, CRLF, TAB indentation, no final newline |
| Lookup XML | Exact empty form when every `ListValues` cell is empty; non-empty values fail because schema is unknown |
| Output folder | V1 slug by default; Arabic display name is optional |
| Timestamp | Injectable override; default local time and invariant format |
| Type map | Explicit; only `NVARCHAR=1` is preconfigured |
| Batch conversion | Implemented, independent per department, with unmatched/missing reports |
| `names.json` CRUD | Implemented; existing slugs retained unless edited manually |
| Golden verification | Harness included; cannot be fully executed until source samples and V1 XML are supplied |

## Assumptions and items to confirm with the owner

The following choices are implementation defaults, not facts established by the sample files:

1. **Direction:** CSV is the input and XML is the output.
2. **Platform:** initial delivery targets Windows desktop only, although MAUI itself is cross-platform.
3. **Project identifiers:** `444`, `37`, `71`, `1`, and `1` are defaults; the settings file allows them to be changed.
4. **Timestamp:** `MM/dd/yyyy hh:mm:ss AM/PM`, local machine time, English formatting. Confirm date order and the importing system's expected time zone.
5. ~~Empty values~~ - confirmed from V1 output: self-closing `<field/>` (now the default).
6. **Field types:** `NVARCHAR=1` and `LIST=5` are confirmed. `DATE` and any other type still need their V1 codes.
7. ~~Lookup values~~ - confirmed from V1 output; see the `ListValues` section above.
8. **FilmType:** the input `FilmType` is validated as a non-negative integer and copied; no department-to-FilmType master table is assumed.
9. **NotNull:** ignored by default, as in V1; an opt-in validation flag is available.
10. **Folder naming:** Arabic output folders are opt-in to protect V1 layout compatibility. Confirm whether the Arabic-folder feature should become the default in a later version.
11. **Batch scan:** top-level CSV files only; nested folder recursion is not enabled.
12. **Registry location:** the user chooses the authoritative `names.json`; CRUD edits that exact file. `conversion-settings.json` is saved alongside it.
13. **Empty output package:** all four files are emitted for a valid department even if the data CSV has only a header and no data rows.
14. **Golden test:** real original XML files and their per-department timestamps must be supplied to prove byte identity against the real V1 package.

The observed index 13 and index 56 slug anomalies are preserved when those entries are loaded. New entries use the transliteration routine; the two existing exceptions are not repaired automatically.

## Manual conflict resolution

When a data CSV contains row-level validation problems, conversion now performs a preflight scan and collects the affected rows instead of stopping on the first row. In single-project mode, use **إصلاح التعارضات يدوياً** to open the repair page. In bulk mode, each department with repairable row conflicts gets its own **إصلاح التعارضات** action.

The repair page allows changing cell values, merging adjacent cells (useful when an unquoted comma split one Arabic value into two cells), removing a cell, or adding a blank cell. Submitting writes a new UTF-8-with-BOM CSV copy under:

```text
<output-folder>\_repaired-inputs\<department-slug>\<repair-session-id>\data-revision-N.csv
```

The original CSV is opened read-only and is never overwritten. The repaired copy is revalidated; remaining conflicts are shown again. XML conversion starts only after the repaired copy passes validation.

Repair sessions are held in memory for the current app process. Keep the app open while resolving a session; closing/restarting the application clears its pending UI session, but leaves both the original CSV and any saved repair-copy files untouched.
