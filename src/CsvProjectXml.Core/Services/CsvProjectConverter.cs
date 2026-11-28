using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CsvProjectXml.Core.Models;

namespace CsvProjectXml.Core.Services;

public sealed class CsvProjectConverter
{
    private readonly IReadOnlyList<IDataValueFeature> _features;

    public CsvProjectConverter(IEnumerable<IDataValueFeature>? features = null)
    {
        _features = features?.ToArray() ?? [];
    }

    private static readonly string[] FieldsHeader =
        ["ID", "FieldName", "FieldNameAr", "FieldType", "FieldSize", "NotNull", "ListValues"];
    private static readonly string[] DataFixedHeader = ["ID", "FilmId", "FilmType", "PortfolioId"];
    private static readonly string[] OutputFiles =
        ["Project.xml", "Project_Fields.xml", "Project_Data.xml", "Project_lookup_Fields.xml"];
    /// <summary>Type codes observed in V1 output. Entries in conversion-settings.json take precedence.</summary>
    private static readonly IReadOnlyDictionary<string, int> BuiltInFieldTypes =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["NVARCHAR"] = 1, ["LIST"] = 5 };

    private static readonly Regex XmlTagName = new("^[A-Za-z_][A-Za-z0-9_.-]*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Validates the complete data CSV and returns all row-level conflicts without writing XML.
    /// Strict mode requires an exact data header. Legacy-tolerant mode follows V1's
    /// header-keyed mapping: columns are read by their actual names and absent fields are omitted.
    /// </summary>
    public ConversionPreflightResult Validate(ConversionRequest request, ConversionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(settings);
        ValidateRequest(request, settings);
        ValidateEnabledFeatures(settings);

        var autoFixes = new List<string>();
        var fields = ReadFields(request.FieldsCsvPath, settings, autoFixes);
        var expected = DataFixedHeader.Concat(fields.Select(x => x.FieldName)).ToArray();
        using var enumerator = ReadCsvRecords(request.DataCsvPath, autoFixes,
            removeQuoteCharacters: settings.LegacyTolerantMode).GetEnumerator();
        if (!enumerator.MoveNext())
            throw new ConversionException("The data CSV is empty.", request.DataCsvPath, 1, "Header");
        var header = enumerator.Current;
        var headerMap = BuildDataHeaderMap(header, expected, settings, request.DataCsvPath, autoFixes);

        var conflicts = new List<DataRowConflict>();
        var photoPaths = new HashSet<(long FilmType, long FilmId, long PortfolioId)>();
        long recordNumber = 0;

        while (enumerator.MoveNext())
        {
            var row = enumerator.Current;
            recordNumber++;
            var normalized = NormalizeDataRow(row, header.Values, settings, request.DataCsvPath, autoFixes);
            var rowValues = MapDataRow(normalized, expected, headerMap);
            var issues = new List<RowValidationIssue>();
            void Add(string code, string message, string? column) => issues.Add(new RowValidationIssue(code, message, column));

            // Check the physical row width before mapping it to the expected field-definition order.
            // MapDataRow always returns expected.Length values, so checking its result would hide
            // extra or missing cells and prevent the conflict/repair workflow from seeing them.
            if (!settings.LegacyTolerantMode && normalized.Count != expected.Length)
            {
                var column = normalized.Count > expected.Length
                    ? $"#{expected.Length + 1} (unexpected extra column)"
                    : expected[normalized.Count];
                Add("COLUMN_COUNT",
                    $"Row has {normalized.Count} columns; expected {expected.Length}.", column);
            }
            else
            {
                var values = rowValues;
                long? filmId = TryParseNonNegative(values[1], out var parsedFilmId) ? parsedFilmId : null;
                long? filmType = TryParseNonNegative(values[2], out var parsedFilmType) ? parsedFilmType : null;
                long? portfolioId = TryParseNonNegative(values[3], out var parsedPortfolioId) ? parsedPortfolioId : null;

                if (!settings.LegacyTolerantMode)
                {
                    if (filmId is null) Add("INVALID_FILM_ID", $"'{values[1]}' is not a non-negative integer.", "FilmId");
                    if (filmType is null) Add("INVALID_FILM_TYPE", $"'{values[2]}' is not a non-negative integer.", "FilmType");
                    if (portfolioId is null) Add("INVALID_PORTFOLIO_ID", $"'{values[3]}' is not a non-negative integer.", "PortfolioId");
                }

                if (filmId is not null && filmType is not null && portfolioId is not null
                    && !photoPaths.Add((filmType.Value, filmId.Value, portfolioId.Value)))
                {
                    var message = $"PHOTO_PATH \\Scan_Tree\\{values[2]}\\{values[1]}\\Film\\{values[3]} is used by more than one row.";
                    if (settings.RejectDuplicatePhotoPaths && !settings.LegacyTolerantMode) Add("DUPLICATE_PHOTO_PATH", message, "FilmId/PortfolioId");
                    else autoFixes.Add($"{Path.GetFileName(request.DataCsvPath)}: line {row.StartLine}: REVIEW - {message} It is written as-is, like V1.");
                }

                ValidateXmlValue(values[1], "FilmId", Add);
                ValidateXmlValue(values[2], "FilmType", Add);
                ValidateXmlValue($"\\Scan_Tree\\{values[2]}\\{values[1]}\\Film\\{values[3]}", "PHOTO_PATH", Add);

                for (var index = 0; index < fields.Count; index++)
                {
                    var field = fields[index];
                    if (!headerMap.ContainsKey(field.FieldName)) continue;
                    var value = values[index + DataFixedHeader.Length];
                    var context = new DataRowContext(values[0], filmId ?? 0, filmType ?? 0,
                        portfolioId ?? 0, request.DataCsvPath, row.StartLine);
                    try
                    {
                        foreach (var feature in _features)
                        {
                            if ((settings.EnabledFeatureIds ?? []).Contains(feature.Id, StringComparer.OrdinalIgnoreCase))
                                value = feature.Transform(value, field, context);
                        }
                    }
                    catch (Exception ex)
                    {
                        Add("TRANSFORM_FAILED", ex.Message, field.FieldName);
                    }

                    if (settings.ValidateFieldSize && !settings.LegacyTolerantMode && value.Length > field.FieldSize)
                        Add("FIELD_SIZE", $"Value length {value.Length} exceeds FieldSize {field.FieldSize}.", field.FieldName);
                    if (settings.ValidateNotNull && !settings.LegacyTolerantMode && field.NotNull && value.Length == 0)
                        Add("REQUIRED_VALUE", "The field is marked NotNull but the value is empty.", field.FieldName);
                    ValidateXmlValue(value, field.FieldName, Add);
                }
            }

            if (issues.Count > 0)
                conflicts.Add(new DataRowConflict(recordNumber, row.StartLine, row.Values.ToArray(), issues));
        }

        return new ConversionPreflightResult(conflicts, autoFixes);
    }

    private static bool TryParseNonNegative(string value, out long result) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result) && result >= 0;

    private static void ValidateXmlValue(string value, string column, Action<string, string, string?> add)
    {
        try { XmlText.ValidateXml10(value, "", 0, column); }
        catch (ConversionException ex) { add("INVALID_XML_CHARACTER", ex.Message, column); }
    }

    public DepartmentConversionResult Convert(ConversionRequest request, ConversionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(settings);
        ValidateRequest(request, settings);
        ValidateEnabledFeatures(settings);

        var autoFixes = new List<string>();
        var fields = ReadFields(request.FieldsCsvPath, settings, autoFixes);
        var folderName = request.FolderNameMode == OutputFolderNameMode.ArabicDisplayName
            ? request.Department.DisplayName
            : request.Department.Value;
        ValidateFolderSegment(folderName);
        var outputRoot = Path.GetFullPath(request.OutputRoot);
        Directory.CreateDirectory(outputRoot);
        var targetDirectory = Path.Combine(outputRoot, folderName);
        ValidateExistingOutputDirectory(targetDirectory);

        var stamp = request.TimestampOverride ?? DateTime.Now.ToString(settings.TimestampFormat, CultureInfo.InvariantCulture);
        ValidateTimestamp(stamp, request.DataCsvPath);

        var stagingDirectory = Path.Combine(outputRoot, $".{folderName}.staging-{Guid.NewGuid():N}");
        var backupDirectory = Path.Combine(outputRoot, $".{folderName}.backup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingDirectory);

        try
        {
            WriteProjectXml(Path.Combine(stagingDirectory, "Project.xml"), request.Department.Value, stamp, settings);
            WriteFieldsXml(Path.Combine(stagingDirectory, "Project_Fields.xml"), fields, settings);
            var (rowCount, filmCount) = WriteDataXml(
                Path.Combine(stagingDirectory, "Project_Data.xml"), request.DataCsvPath, fields, settings, autoFixes);
            WriteLookupXml(Path.Combine(stagingDirectory, "Project_lookup_Fields.xml"), fields, settings);

            var summaries = OutputFiles.Select(fileName =>
            {
                var path = Path.Combine(stagingDirectory, fileName);
                var info = new FileInfo(path);
                using var file = File.OpenRead(path);
                var hash = System.Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant();
                return new OutputFileSummary(fileName, info.Length, hash);
            }).ToArray();

            CommitDirectory(stagingDirectory, targetDirectory, backupDirectory);
            return new DepartmentConversionResult(request.Department.Value, request.Department.DisplayName,
                targetDirectory, rowCount, filmCount, fields.Count, summaries, autoFixes);
        }
        catch
        {
            if (Directory.Exists(stagingDirectory)) Directory.Delete(stagingDirectory, recursive: true);
            if (Directory.Exists(backupDirectory) && !Directory.Exists(targetDirectory))
                Directory.Move(backupDirectory, targetDirectory);
            throw;
        }
    }

    private void ValidateEnabledFeatures(ConversionSettings settings)
    {
        var knownIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var feature in _features)
        {
            if (string.IsNullOrWhiteSpace(feature.Id) || !knownIds.Add(feature.Id))
                throw new ConversionException($"Feature registration contains an empty or duplicate feature ID '{feature.Id}'.");
        }

        foreach (var enabledId in settings.EnabledFeatureIds ?? [])
        {
            if (!knownIds.Contains(enabledId))
                throw new ConversionException($"Feature '{enabledId}' is enabled in settings but is not registered in this application build.");
        }
    }

    private static List<FieldDefinition> ReadFields(string path, ConversionSettings settings, List<string>? autoFixes = null)
    {
        // ListValues is the last column and holds a JSON list typed without CSV quoting, so it is read as the rest of the line.
        using var enumerator = ReadCsvRecords(path, autoFixes, rawTailColumn: FieldsHeader.Length - 1).GetEnumerator();
        if (!enumerator.MoveNext())
            throw new ConversionException("The fields CSV is empty.", path, 1, "Header");

        var header = enumerator.Current;
        ValidateExactHeader(header, FieldsHeader, path, "Fields CSV");
        var definitions = new List<FieldDefinition>();
        var names = new Dictionary<string, int>(StringComparer.Ordinal);

        while (enumerator.MoveNext())
        {
            var row = enumerator.Current;
            ValidateColumnCount(row, FieldsHeader.Length, path, FieldsHeader);
            // V1 does not use the source field ID or NotNull flag in the XML.
            // Preserve the ID as data and only validate NotNull when that optional rule is enabled.
            var sourceId = row.Values[0];
            var fieldName = row.Values[1];
            var fieldNameAr = row.Values[2];
            var fieldType = row.Values[3].Trim().ToUpperInvariant();
            var fieldSize = ParseInt(row.Values[4], path, row.StartLine, "FieldSize");
            var notNull = false;
            if (settings.ValidateNotNull && !settings.LegacyTolerantMode && !bool.TryParse(row.Values[5], out notNull))
                throw new ConversionException($"'{row.Values[5]}' is not a valid boolean (True or False).",
                    path, row.StartLine, "NotNull");
            var listValues = row.Values[6];

            if (!XmlTagName.IsMatch(fieldName))
                throw new ConversionException($"'{fieldName}' is not a valid supported XML element name. Use ASCII letters, digits, '_' or '-'.",
                    path, row.StartLine, "FieldName");
            if (new[] { "FILM_NUMBER", "FILM_TYPE", "PHOTO_PATH" }.Contains(fieldName, StringComparer.OrdinalIgnoreCase))
                throw new ConversionException($"'{fieldName}' conflicts with a system field name.",
                    path, row.StartLine, "FieldName");
            if (!settings.LegacyTolerantMode && names.TryGetValue(fieldName, out var firstLine))
            {
                var first = definitions.First(x => string.Equals(x.FieldName, fieldName, StringComparison.Ordinal));
                // A repeated row that adds nothing (same type, no Arabic name of its own) is a leftover line in the
                // fields file: the data file has a single column for it. Keep the first definition and report it.
                if (string.IsNullOrWhiteSpace(fieldNameAr) &&
                    string.Equals(first.FieldType, fieldType, StringComparison.Ordinal))
                {
                    autoFixes?.Add($"REVIEW: {Path.GetFileName(path)} line {row.StartLine}: FieldName '{fieldName}' is repeated (first on line {firstLine}) without an Arabic name; the repeated row was ignored.");
                    continue;
                }
            }

            if (!names.TryAdd(fieldName, row.StartLine) && !settings.LegacyTolerantMode)
                throw new ConversionException(
                    $"Duplicate FieldName '{fieldName}' (first used on line {names[fieldName]}). Each field needs a unique name; rename one of them in the fields file and in the data file header.",
                    path, row.StartLine, "FieldName");
            if (fieldSize < 0)
                throw new ConversionException("FieldSize cannot be negative.", path, row.StartLine, "FieldSize");
            var listItems = ParseListValues(listValues, fieldName, path, row.StartLine, autoFixes);

            var typeMap = settings.FieldTypeMap ?? throw new ConversionException("FieldTypeMap is missing from settings.");
            var typeMatch = typeMap.FirstOrDefault(x => string.Equals(x.Key, fieldType, StringComparison.OrdinalIgnoreCase));
            if (typeMatch.Key is null && BuiltInFieldTypes.TryGetValue(fieldType, out var builtInCode))
                typeMatch = new KeyValuePair<string, int>(fieldType, builtInCode);
            if (typeMatch.Key is null && (settings.UnmappedFieldTypeAsText || settings.LegacyTolerantMode))
            {
                typeMatch = new KeyValuePair<string, int>(fieldType, BuiltInFieldTypes["NVARCHAR"]);
                autoFixes?.Add($"REVIEW: {Path.GetFileName(path)} line {row.StartLine}: FieldType '{fieldType}' of field '{fieldName}' is not mapped; it was written as NVARCHAR ({typeMatch.Value}).");
            }
            if (typeMatch.Key is null)
                throw new ConversionException($"FieldType '{fieldType}' is not mapped. Add an explicit mapping in conversion-settings.json.",
                    path, row.StartLine, "FieldType");

            XmlText.ValidateXml10(fieldName, path, row.StartLine, "FieldName");
            XmlText.ValidateXml10(fieldNameAr, path, row.StartLine, "FieldNameAr");
            definitions.Add(new FieldDefinition(sourceId, fieldName, fieldNameAr, fieldType,
                fieldSize, notNull, listValues, typeMatch.Value, listItems));
        }

        return definitions;
    }

    private (long RowCount, int FilmCount) WriteDataXml(string outputPath, string csvPath,
        IReadOnlyList<FieldDefinition> fields, ConversionSettings settings, List<string>? autoFixes = null)
    {
        using var enumerator = ReadCsvRecords(csvPath, autoFixes,
            removeQuoteCharacters: settings.LegacyTolerantMode).GetEnumerator();
        if (!enumerator.MoveNext())
            throw new ConversionException("The data CSV is empty.", csvPath, 1, "Header");

        var header = enumerator.Current;
        var expected = DataFixedHeader.Concat(fields.Select(x => x.FieldName)).ToArray();
        var headerMap = BuildDataHeaderMap(header, expected, settings, csvPath, autoFixes);

        using var writer = new XmlLineWriter(outputPath);
        writer.WriteLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        writer.WriteLine("<DocumentElement>");

        long rowCount = 0;
        var films = new HashSet<string>(StringComparer.Ordinal);
        var photoPaths = new HashSet<(long FilmType, long FilmId, long PortfolioId)>();

        while (enumerator.MoveNext())
        {
            var row = enumerator.Current;
            var normalized = NormalizeDataRow(row, header.Values, settings, csvPath, autoFixes);
            // In strict mode, preserve the original row-width guard. This also ensures Convert()
            // cannot silently write a row that preflight would classify as structurally damaged.
            if (!settings.LegacyTolerantMode && normalized.Count != expected.Length)
                ValidateColumnCount(new CsvRecord(normalized, row.StartLine), expected.Length, csvPath, expected);
            var values = MapDataRow(normalized, expected, headerMap);
            var filmIdRaw = values[1];
            var filmTypeRaw = values[2];
            var portfolioRaw = values[3];
            var filmIdValid = TryParseNonNegative(filmIdRaw, out var parsedFilmId);
            var filmTypeValid = TryParseNonNegative(filmTypeRaw, out var parsedFilmType);
            var portfolioValid = TryParseNonNegative(portfolioRaw, out var parsedPortfolioId);
            if (!settings.LegacyTolerantMode && (!filmIdValid || !filmTypeValid || !portfolioValid))
            {
                var column = !filmIdValid ? "FilmId" : !filmTypeValid ? "FilmType" : "PortfolioId";
                var raw = !filmIdValid ? filmIdRaw : !filmTypeValid ? filmTypeRaw : portfolioRaw;
                throw new ConversionException($"'{raw}' is not a non-negative integer.", csvPath, row.StartLine, column);
            }

            var filmId = filmIdValid ? parsedFilmId : 0;
            var filmType = filmTypeValid ? parsedFilmType : 0;
            var portfolioId = portfolioValid ? parsedPortfolioId : 0;

            if (filmIdValid && filmTypeValid && portfolioValid && !photoPaths.Add((filmType, filmId, portfolioId)))
            {
                var message = $"PHOTO_PATH \\Scan_Tree\\{filmTypeRaw}\\{filmIdRaw}\\Film\\{portfolioRaw} is used by more than one row.";
                if (settings.RejectDuplicatePhotoPaths && !settings.LegacyTolerantMode)
                    throw new ConversionException(message, csvPath, row.StartLine, "FilmId/PortfolioId");
                autoFixes?.Add($"{Path.GetFileName(csvPath)}: line {row.StartLine}: REVIEW - {message} It is written as-is, like V1.");
            }
            films.Add(settings.LegacyTolerantMode
                ? filmIdRaw
                : filmId.ToString(CultureInfo.InvariantCulture));

            writer.WriteLine("\t<Data>");
            if (headerMap.ContainsKey("FilmId"))
                WriteDataTag(writer, "FILM_NUMBER", filmIdRaw, csvPath, row.StartLine, "FilmId", settings);
            if (headerMap.ContainsKey("FilmType"))
                WriteDataTag(writer, "FILM_TYPE", filmTypeRaw, csvPath, row.StartLine, "FilmType", settings);
            if (headerMap.ContainsKey("PortfolioId"))
            {
                var photoPath = $"\\Scan_Tree\\{filmTypeRaw}\\{filmIdRaw}\\Film\\{portfolioRaw}";
                WriteDataTag(writer, "PHOTO_PATH", photoPath, csvPath, row.StartLine, "PortfolioId", settings);
            }

            for (var index = 0; index < fields.Count; index++)
            {
                var field = fields[index];
                if (!headerMap.ContainsKey(field.FieldName)) continue;
                var value = values[index + DataFixedHeader.Length];
                var rowContext = new DataRowContext(values[0], filmId, filmType, portfolioId, csvPath, row.StartLine);
                foreach (var feature in _features)
                {
                    if ((settings.EnabledFeatureIds ?? []).Contains(feature.Id, StringComparer.OrdinalIgnoreCase))
                        value = feature.Transform(value, field, rowContext);
                }
                if (settings.ValidateFieldSize && !settings.LegacyTolerantMode && value.Length > field.FieldSize)
                    throw new ConversionException($"Value length {value.Length} exceeds FieldSize {field.FieldSize}.",
                        csvPath, row.StartLine, field.FieldName);
                if (settings.ValidateNotNull && !settings.LegacyTolerantMode && field.NotNull && value.Length == 0)
                    throw new ConversionException("The field is marked NotNull but the value is empty.",
                        csvPath, row.StartLine, field.FieldName);
                WriteDataTag(writer, field.FieldName, value, csvPath, row.StartLine, field.FieldName, settings);
            }

            writer.WriteLine("\t</Data>");
            rowCount++;
        }

        writer.WriteLine("</DocumentElement>");
        writer.Flush();
        return (rowCount, films.Count);
    }

    private static IReadOnlyDictionary<string, int> BuildDataHeaderMap(CsvRecord header,
        IReadOnlyList<string> expected, ConversionSettings settings, string path, List<string>? autoFixes)
    {
        if (!settings.LegacyTolerantMode)
        {
            ValidateExactHeader(header, expected, path, "Data CSV");
            return expected.Select((name, index) => (name, index))
                .ToDictionary(item => item.name, item => item.index, StringComparer.Ordinal);
        }

        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < header.Values.Count; index++)
        {
            var name = header.Values[index];
            if (string.IsNullOrWhiteSpace(name))
                throw new ConversionException("Data CSV contains an empty column header.", path, header.StartLine,
                    $"#{index + 1}");
            if (!map.TryAdd(name, index))
                throw new ConversionException($"Data CSV contains duplicate column header '{name}'.",
                    path, header.StartLine, name);
        }

        foreach (var name in expected)
        {
            if (map.ContainsKey(name)) continue;
            autoFixes?.Add($"{Path.GetFileName(path)}: line {header.StartLine}: REVIEW - data column '{name}' is absent; like V1, no XML element will be generated for it.");
        }
        return map;
    }

    private static IReadOnlyList<string> MapDataRow(IReadOnlyList<string> row,
        IReadOnlyList<string> expected, IReadOnlyDictionary<string, int> headerMap)
    {
        var mapped = new string[expected.Count];
        for (var index = 0; index < expected.Count; index++)
            if (headerMap.TryGetValue(expected[index], out var sourceIndex) && sourceIndex < row.Count)
                mapped[index] = row[sourceIndex];
            else
                mapped[index] = string.Empty;
        return mapped;
    }

    /// <summary>
    /// Applies the opt-in row clean-ups chosen in the settings (extra cells, quotation marks) and reports every
    /// change for review. With the default settings the row is returned unchanged.
    /// </summary>
    private static IReadOnlyList<string> NormalizeDataRow(CsvRecord row, IReadOnlyList<string> columns,
        ConversionSettings settings, string path, List<string>? autoFixes)
    {
        IReadOnlyList<string> values = row.Values;
        var width = columns.Count;
        var file = Path.GetFileName(path);

        if (settings.LegacyTolerantMode && values.Count > width)
        {
            var extra = values.Count - width;
            var dropped = string.Join(",", values.Skip(width));
            values = values.Take(width).ToArray();
            autoFixes?.Add($"{file}: line {row.StartLine}: REVIEW - {extra} extra cell(s) were dropped (original V1 behavior); dropped text: {dropped}");
        }
        else if (values.Count > width && settings.ExtraColumnsMode != ExtraColumnsMode.Hold)
        {
            var extra = values.Count - width;
            if (settings.ExtraColumnsMode == ExtraColumnsMode.MergeIntoLastColumn)
            {
                values = values.Take(width - 1).Append(string.Join(",", values.Skip(width - 1))).ToArray();
                autoFixes?.Add($"{file}: line {row.StartLine}: REVIEW - {extra} extra cell(s) were joined into the last column '{columns[width - 1]}'.");
            }
            else
            {
                var dropped = string.Join(",", values.Skip(width));
                values = values.Take(width).ToArray();
                autoFixes?.Add($"{file}: line {row.StartLine}: REVIEW - {extra} extra cell(s) were dropped (V1 behavior); dropped text: {dropped}");
            }
        }

        if (settings.LegacyTolerantMode && values.Count < width)
        {
            var missing = width - values.Count;
            values = values.Concat(Enumerable.Repeat(string.Empty, missing)).ToArray();
            autoFixes?.Add($"{file}: line {row.StartLine}: {missing} missing cell(s) were padded with empty values (original V1 behavior).");
        }

        if (settings.QuoteHandling == QuoteHandling.Remove && values.Count == width)
        {
            List<string>? changed = null;
            var firstValueColumn = settings.LegacyTolerantMode ? 0 : DataFixedHeader.Length;
            for (var index = firstValueColumn; index < values.Count; index++)
            {
                if (!values[index].Contains('"')) continue;
                changed ??= values.ToList();
                changed[index] = values[index].Replace("\"", string.Empty, StringComparison.Ordinal);
                autoFixes?.Add($"{file}: line {row.StartLine}: quotation mark(s) were removed from '{columns[index]}'.");
            }

            if (changed is not null) values = changed;
        }

        return values;
    }

    /// <summary>
    /// ListValues is a JSON array of texts, e.g. ["A","B"]. Exporting tools sometimes wrap the whole cell in CSV
    /// quotes (with doubled inner quotes); that form is accepted too.
    /// </summary>
    private static IReadOnlyList<string> ParseListValues(string raw, string fieldName, string path, int line,
        List<string>? autoFixes)
    {
        var text = raw.Trim();
        if (text.Length == 0) return [];

        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
        {
            text = text[1..^1].Replace("\"\"", "\"", StringComparison.Ordinal);
            autoFixes?.Add($"{Path.GetFileName(path)}: line {line}: ListValues of '{fieldName}' was wrapped in CSV quotes; they were removed.");
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw new ConversionException("ListValues must be a JSON array such as [\"A\",\"B\"].", path, line, "ListValues");

            var items = new List<string>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                var item = element.ValueKind switch
                {
                    JsonValueKind.String => element.GetString() ?? string.Empty,
                    JsonValueKind.Number => element.GetRawText(),
                    _ => throw new ConversionException("Every ListValues item must be text.", path, line, "ListValues")
                };
                XmlText.ValidateXml10(item, path, line, "ListValues");
                items.Add(item);
            }

            return items;
        }
        catch (JsonException ex)
        {
            throw new ConversionException(
                $"ListValues is not a valid JSON array of texts such as [\"A\",\"B\"] ({ex.Message}).",
                path, line, "ListValues", ex);
        }
    }

    /// <summary>
    /// V1 layout (verified against real V1 output): list fields appear in reverse CSV order, each field's items in
    /// reverse list order, and FIELD_LOOKUP_ORDER is the position of the field in that reversed sequence.
    /// With no list items the file is exactly &lt;DocumentElement/&gt;.
    /// </summary>
    private static void WriteLookupXml(string path, IReadOnlyList<FieldDefinition> fields, ConversionSettings settings)
    {
        var lists = fields.Where(field => field.ListItems is { Count: > 0 }).Reverse().ToList();
        if (lists.Count == 0)
        {
            File.WriteAllBytes(path, Encoding.UTF8.GetBytes("<DocumentElement/>"));
            return;
        }

        using var writer = new XmlLineWriter(path);
        writer.WriteLine("<DocumentElement>");
        for (var order = 0; order < lists.Count; order++)
        {
            var field = lists[order];
            foreach (var item in field.ListItems!.Reverse())
            {
                writer.WriteLine("\t<Project_lookup_Fields>");
                WriteTag(writer, 2, "FIELD_LOOKUP_ORDER", order.ToString(CultureInfo.InvariantCulture));
                WriteTag(writer, 2, "FIELD_NAME_ENG", field.FieldName);
                WriteTag(writer, 2, "PROJECT_CODE", settings.ProjectCode.ToString(CultureInfo.InvariantCulture));
                WriteTag(writer, 2, "FIELD_LOOKUP_TEXT", item);
                writer.WriteLine("\t</Project_lookup_Fields>");
            }
        }

        writer.WriteLine("</DocumentElement>");
        writer.Flush();
    }

    private static void WriteProjectXml(string path, string slug, string stamp, ConversionSettings settings)
    {
        using var writer = new XmlLineWriter(path);
        writer.WriteLine("<DocumentElement>");
        writer.WriteLine("\t<Project>");
        WriteTag(writer, 2, "PROJECT_CODE", settings.ProjectCode.ToString(CultureInfo.InvariantCulture));
        WriteTag(writer, 2, "PROJECT_SHORT_DESC", slug);
        WriteTag(writer, 2, "PROJECT_NAME", slug);
        WriteTag(writer, 2, "PROJECT_PATH", settings.ProjectCode.ToString(CultureInfo.InvariantCulture));
        WriteTag(writer, 2, "START_DATE", stamp);
        WriteTag(writer, 2, "END_DATE", stamp);
        WriteTag(writer, 2, "IS_CIVIL", settings.IsCivil.ToString(CultureInfo.InvariantCulture));
        WriteTag(writer, 2, "USER_CREATION_DATE", stamp);
        WriteTag(writer, 2, "USER_MODIFICATION_CODE", settings.UserModificationCode.ToString(CultureInfo.InvariantCulture));
        WriteTag(writer, 2, "IS_STRUCTURE_CREATED", settings.IsStructureCreated.ToString(CultureInfo.InvariantCulture));
        WriteTag(writer, 2, "ORG_CODE", settings.OrgCode.ToString(CultureInfo.InvariantCulture));
        writer.WriteLine("\t</Project>");
        writer.WriteLine("</DocumentElement>");
    }

    private static void WriteFieldsXml(string path, IReadOnlyList<FieldDefinition> fields, ConversionSettings settings)
    {
        using var writer = new XmlLineWriter(path);
        writer.WriteLine("<DocumentElement>");
        WriteFieldBlock(writer, "FILM_NUMBER", "رقم الفيلم", 2, 5, 2, 2, settings);
        WriteFieldBlock(writer, "FILM_TYPE", "نوع الفيلم", 5, 25, 1, 1, settings);
        for (var index = 0; index < fields.Count; index++)
        {
            var field = fields[index];
            WriteFieldBlock(writer, field.FieldName, field.FieldNameAr, field.XmlFieldType,
                field.FieldSize, 3 + index, 0, settings, docKey: 0);
        }
        writer.WriteLine("</DocumentElement>");
    }

    private static void WriteFieldBlock(XmlLineWriter writer, string name, string nameAr, int type,
        int width, int orderPage, int orderTree, ConversionSettings settings, int docKey = 2)
    {
        writer.WriteLine("\t<Project_Fields>");
        WriteTag(writer, 2, "PROJECT_CODE", settings.ProjectCode.ToString(CultureInfo.InvariantCulture));
        WriteTag(writer, 2, "FIELD_NAME_ENG", name);
        WriteTag(writer, 2, "FIELD_NAME_AR", nameAr);
        WriteTag(writer, 2, "FIELD_TYPE", type.ToString(CultureInfo.InvariantCulture));
        WriteTag(writer, 2, "FIELD_WIDTH", width.ToString(CultureInfo.InvariantCulture));
        WriteTag(writer, 2, "IS_UNIQUE", "0");
        WriteTag(writer, 2, "IS_DOC_KEY", docKey.ToString(CultureInfo.InvariantCulture));
        WriteTag(writer, 2, "FIELD_ORDER_PAGE", orderPage.ToString(CultureInfo.InvariantCulture));
        WriteTag(writer, 2, "FIELD_ORDER_TREE", orderTree.ToString(CultureInfo.InvariantCulture));
        WriteTag(writer, 2, "IS_FILE_BARCODE", "0");
        WriteTag(writer, 2, "IS_FILM_BARCODE", "0");
        WriteTag(writer, 2, "IS_SEPARATOR", "0");
        WriteTag(writer, 2, "FIELD_NAME_ENG_DEP", "0");
        writer.WriteLine("\t</Project_Fields>");
    }

    private static void WriteDataTag(XmlLineWriter writer, string name, string value, string filePath,
        int line, string column, ConversionSettings settings)
    {
        XmlText.ValidateXml10(value, filePath, line, column);
        var escaped = XmlText.Escape(value);
        var tag = settings.EmptyValueMode == EmptyValueMode.SelfClosingTags && value.Length == 0
            ? $"\t\t<{name}/>"
            : $"\t\t<{name}>{escaped}</{name}>";
        writer.WriteLine(tag);
    }

    private static void WriteTag(XmlLineWriter writer, int indent, string name, string value)
    {
        var escaped = XmlText.Escape(value);
        writer.WriteLine($"{new string('\t', indent)}<{name}>{escaped}</{name}>");
    }

    private static IEnumerable<CsvRecord> ReadCsvRecords(string path, List<string>? autoFixes = null,
        int rawTailColumn = -1, bool removeQuoteCharacters = false)
    {
        var fileName = Path.GetFileName(path);
        using var enumerator = CsvReader.Read(path, rawTailColumn, removeQuoteCharacters).GetEnumerator();
        while (true)
        {
            bool hasNext;
            try
            {
                hasNext = enumerator.MoveNext();
            }
            catch (CsvFormatException ex)
            {
                throw new ConversionException(ex.Message, ex.FilePath, ex.LineNumber,
                    $"#{ex.ColumnNumber}", ex);
            }
            catch (DecoderFallbackException ex)
            {
                throw new ConversionException("The CSV contains invalid UTF-8 byte sequences.", path, 1, "File", ex);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                throw new ConversionException(ex.Message, path, 1, "File", ex);
            }

            if (!hasNext) yield break;
            if (autoFixes is not null)
                foreach (var note in enumerator.Current.Notes) autoFixes.Add($"{fileName}: {note}");
            yield return enumerator.Current;
        }
    }

    private static void ValidateExactHeader(CsvRecord record, IReadOnlyList<string> expected, string path, string label)
    {
        if (record.Values.Count != expected.Count)
        {
            var column = record.Values.Count > expected.Count
                ? $"#{expected.Count + 1} (unexpected extra column)"
                : $"{expected[record.Values.Count]} (missing)";
            throw new ConversionException($"{label} header has {record.Values.Count} columns; expected {expected.Count}.",
                path, record.StartLine, column);
        }

        for (var index = 0; index < expected.Count; index++)
        {
            if (!string.Equals(record.Values[index], expected[index], StringComparison.Ordinal))
                throw new ConversionException($"Expected header '{expected[index]}' but found '{record.Values[index]}'.",
                    path, record.StartLine, expected[index]);
        }
    }

    private static void ValidateColumnCount(CsvRecord row, int expectedCount, string path, IReadOnlyList<string> columns)
    {
        if (row.Values.Count == expectedCount) return;
        var column = row.Values.Count > expectedCount
            ? $"#{expectedCount + 1} (unexpected extra column)"
            : columns[row.Values.Count];
        throw new ConversionException($"Row has {row.Values.Count} columns; expected {expectedCount}.",
            path, row.StartLine, column);
    }

    private static int ParseInt(string value, string path, int line, string column)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)) return result;
        throw new ConversionException($"'{value}' is not a valid integer.", path, line, column);
    }

    private static long ParseNonNegativeLong(string value, string path, int line, string column)
    {
        if (long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result) && result >= 0) return result;
        throw new ConversionException($"'{value}' is not a non-negative integer.", path, line, column);
    }

    private static void ValidateRequest(ConversionRequest request, ConversionSettings settings)
    {
        if (!File.Exists(request.FieldsCsvPath)) throw new FileNotFoundException("Fields CSV was not found.", request.FieldsCsvPath);
        if (!File.Exists(request.DataCsvPath)) throw new FileNotFoundException("Data CSV was not found.", request.DataCsvPath);
        if (string.IsNullOrWhiteSpace(request.OutputRoot)) throw new ArgumentException("Output folder is required.");
        if (string.IsNullOrWhiteSpace(request.Department.Value)) throw new ArgumentException("Department slug is required.");
        if (!Regex.IsMatch(request.Department.Value, "^[A-Za-z0-9_]+$", RegexOptions.CultureInvariant))
            throw new ArgumentException("Department slug can contain ASCII letters, digits, and underscores only.");
        if (!string.IsNullOrEmpty(request.TimestampOverride) && request.TimestampOverride.Contains('\0'))
            throw new ArgumentException("Timestamp contains an invalid null character.");
        if (string.IsNullOrWhiteSpace(settings.TimestampFormat)) throw new ConversionException("TimestampFormat cannot be empty.");
    }

    private static void ValidateTimestamp(string stamp, string sourceFile)
    {
        if (stamp.Length == 0) throw new ConversionException("Timestamp cannot be empty.", sourceFile, 1, "Timestamp");
        XmlText.ValidateXml10(stamp, sourceFile, 1, "Timestamp");
    }

    private static void ValidateFolderSegment(string folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName) || folderName is "." or "..")
            throw new ConversionException("The output folder name is empty or invalid.");
        if (folderName.EndsWith(' ') || folderName.EndsWith('.'))
            throw new ConversionException("Windows folder names cannot end with a space or a period.");
        if (folderName.IndexOfAny("<>:\"/\\|?*".ToCharArray()) >= 0)
            throw new ConversionException($"The output folder name '{folderName}' contains a character not allowed in a Windows folder name.");
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };
        if (reserved.Contains(folderName.Split('.')[0]))
            throw new ConversionException($"'{folderName}' is a reserved Windows folder name.");
    }

    private static void ValidateExistingOutputDirectory(string path)
    {
        if (!Directory.Exists(path)) return;
        var unexpectedFiles = Directory.EnumerateFiles(path)
            .Select(file => Path.GetFileName(file)!)
            .Where(name => !OutputFiles.Contains(name, StringComparer.Ordinal))
            .ToArray();
        var childDirectories = Directory.EnumerateDirectories(path).ToArray();
        if (unexpectedFiles.Length > 0 || childDirectories.Length > 0)
            throw new ConversionException($"Output folder '{path}' contains unrelated files or subdirectories; it will not be overwritten.");
    }

    private static void CommitDirectory(string stagingDirectory, string targetDirectory, string backupDirectory)
    {
        var hadTarget = Directory.Exists(targetDirectory);
        if (hadTarget) Directory.Move(targetDirectory, backupDirectory);
        try
        {
            Directory.Move(stagingDirectory, targetDirectory);
            if (hadTarget) Directory.Delete(backupDirectory, recursive: true);
        }
        catch
        {
            if (Directory.Exists(targetDirectory)) Directory.Delete(targetDirectory, recursive: true);
            if (hadTarget && Directory.Exists(backupDirectory)) Directory.Move(backupDirectory, targetDirectory);
            throw;
        }
    }
}
