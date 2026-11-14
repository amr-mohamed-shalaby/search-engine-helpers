# V1 compatibility report — V2 implementation

## Implemented in the default conversion path

- `Project.xml` uses the observed template, field order, and default constants (`444`, `37`, `71`, `1`, `1`). The constants are editable in the export settings.
- `Project_Fields.xml` writes `FILM_NUMBER`, `FILM_TYPE`, then custom fields in `*_fields.csv` row order. Field type codes must be explicitly mapped; `NVARCHAR=1` is the only preconfigured mapping.
- `Project_Data.xml` preserves CSV row order, writes `FILM_NUMBER`, `FILM_TYPE`, `PHOTO_PATH`, then custom fields, and ignores source row IDs.
- Raw custom values are passed through unchanged by default: no trim, normalization, Arabic digit conversion, or whitespace removal.
- UTF-8 output has no BOM; XML structure lines use CRLF; indentation is literal TAB; the final XML line has no newline. Only the data XML receives the declaration.
- `PHOTO_PATH` uses literal Windows backslashes. Lookup XML is exactly `<DocumentElement/>` when `ListValues` is empty.
- XML text escapes `&`, `<`, and `>`; Arabic, quotes, tabs, and trailing spaces are preserved.
- Data CSV is read row by row. A department is staged and committed only after validation succeeds.
- Timestamp override supports deterministic reruns. The default timestamp uses local machine time and invariant English formatting.
- Existing `names.json` slugs are authoritative and are not re-derived. The registry UI can add, edit, and remove entries in the selected file.

## Added V2 behavior

- Single-project mode resolves a department from the common slug in the selected data/fields filenames.
- Batch mode scans the selected folder's top level, converts matched file pairs independently, and reports missing pairs, missing departments, and orphan CSV files.
- The result folder may optionally use the Arabic `text` value while XML project details continue to use the stored Franko `value` slug. This option is off by default to retain the V1 folder layout.
- Each successful department reports row count, film count, field count, output file sizes, and SHA-256 hashes.
- The transformation pipeline supports multiple registered field-value features. A feature is applied only when explicitly enabled; an enabled but unregistered feature ID fails visibly.

## Deliberate validation additions

- Exact CSV headers and row widths are checked.
- Unknown field types, duplicate custom field names, system-field name collisions, duplicate `(FilmId, PortfolioId)` pairs, invalid XML 1.0 characters, and values beyond `FieldSize` fail the department.
- Non-empty `ListValues` fails because the target lookup XML schema is unknown.
- `NotNull` is ignored by default like V1; optional validation parses and enforces it.

## Golden verification status

The byte-for-byte golden test is implemented and is conditionally skipped until `CSV_XML_GOLDEN_DIR` points to the original sample package. No original CSV/XML files were attached with the specification, so a real V1-vs-V2 byte comparison could not be performed in this delivery. The application source also could not be compiled in the packaging environment because the .NET SDK was not installed. Build and test commands are in `README.md`.

## Assumptions requiring owner confirmation

1. CSV is the input and XML is the output.
2. Windows desktop is the first deployment target; runtime processing is offline.
3. The five observed project constants are global defaults. If they vary per department, add per-department configuration.
4. Timestamp default is `MM/dd/yyyy hh:mm:ss AM/PM` in local machine time. Confirm date order and the archive system's timezone.
5. Empty values default to paired tags. Confirm whether the importer prefers `<field></field>` or `<field/>`.
6. `NVARCHAR=1` is the only confirmed field type mapping. Confirm codes for every other target type.
7. Non-empty `ListValues` is not implemented until the lookup XML schema is supplied.
8. `FilmType` is copied from the data CSV and validated as a non-negative integer; no master mapping is assumed.
9. `NotNull` validation remains off by default.
10. Arabic output folder naming is opt-in; confirm whether it should become the default.
11. Batch scanning is non-recursive (top-level CSVs only).
12. The selected `names.json` file is the authoritative registry and is edited in place.
13. Golden compatibility still requires the original three CSV pairs, their four XML files per department, and the exact timestamps.
