using CsvProjectXml.Core.Models;

namespace CsvProjectXml.Core.Services;

public sealed class BatchConversionService(CsvProjectConverter converter)
{
    public BatchConversionSummary ConvertFolder(string inputFolder, string outputRoot,
        IReadOnlyList<DepartmentEntry> departments, ConversionSettings settings,
        OutputFolderNameMode folderNameMode = OutputFolderNameMode.LegacySlug,
        string? timestampOverride = null)
    {
        if (!Directory.Exists(inputFolder)) throw new DirectoryNotFoundException($"Input folder was not found: {inputFolder}");
        if (string.IsNullOrWhiteSpace(outputRoot)) throw new ArgumentException("Output folder is required.", nameof(outputRoot));
        NamesRegistryService.Validate(departments);

        var files = Directory.EnumerateFiles(inputFolder, "*.csv", SearchOption.TopDirectoryOnly).ToArray();
        var pairs = new Dictionary<string, InputPair>(StringComparer.OrdinalIgnoreCase);
        var orphanFiles = new List<string>();

        foreach (var file in files)
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            string? slug = null;
            InputKind? kind = null;
            if (fileName.EndsWith("_fields", StringComparison.OrdinalIgnoreCase))
            {
                slug = fileName[..^"_fields".Length];
                kind = InputKind.Fields;
            }
            else if (fileName.EndsWith("_data", StringComparison.OrdinalIgnoreCase))
            {
                slug = fileName[..^"_data".Length];
                kind = InputKind.Data;
            }

            if (slug is null || slug.Length == 0)
            {
                orphanFiles.Add(file);
                continue;
            }

            if (!pairs.TryGetValue(slug, out var pair))
                pairs[slug] = pair = new InputPair();

            if (kind == InputKind.Fields)
            {
                if (pair.FieldsPath is not null) orphanFiles.Add(file);
                else pair.FieldsPath = file;
            }
            else
            {
                if (pair.DataPath is not null) orphanFiles.Add(file);
                else pair.DataPath = file;
            }
        }

        var departmentSlugs = departments.Select(x => x.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in pairs)
        {
            if (!departmentSlugs.Contains(pair.Key))
            {
                if (pair.Value.FieldsPath is not null) orphanFiles.Add(pair.Value.FieldsPath);
                if (pair.Value.DataPath is not null) orphanFiles.Add(pair.Value.DataPath);
            }
        }

        var collidingOutputNames = departments
            .GroupBy(x => folderNameMode == OutputFolderNameMode.ArabicDisplayName ? x.DisplayName : x.Value,
                StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var results = new List<DepartmentBatchItem>(departments.Count);
        foreach (var department in departments.OrderBy(x => x.Index))
        {
            if (!pairs.TryGetValue(department.Value, out var pair) ||
                (pair.FieldsPath is null && pair.DataPath is null))
            {
                results.Add(new DepartmentBatchItem(department.Value, department.DisplayName,
                    DepartmentBatchStatus.MissingInput, Message: "No matching CSV files were found."));
                continue;
            }

            if (pair.FieldsPath is null || pair.DataPath is null)
            {
                results.Add(new DepartmentBatchItem(department.Value, department.DisplayName,
                    DepartmentBatchStatus.IncompleteInput,
                    Message: pair.FieldsPath is null ? "The *_fields.csv file is missing." : "The *_data.csv file is missing."));
                continue;
            }

            var outputName = folderNameMode == OutputFolderNameMode.ArabicDisplayName
                ? department.DisplayName : department.Value;
            if (collidingOutputNames.Contains(outputName))
            {
                results.Add(new DepartmentBatchItem(department.Value, department.DisplayName,
                    DepartmentBatchStatus.Failed, Message: $"Multiple departments resolve to the same output folder '{outputName}'."));
                continue;
            }

            try
            {
                var converted = converter.Convert(new ConversionRequest(pair.FieldsPath, pair.DataPath,
                    outputRoot, department, folderNameMode, timestampOverride), settings);
                results.Add(new DepartmentBatchItem(department.Value, department.DisplayName,
                    DepartmentBatchStatus.Converted, converted));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                results.Add(new DepartmentBatchItem(department.Value, department.DisplayName,
                    DepartmentBatchStatus.Failed, Message: ex is ConversionException ce ? ce.ToString() : ex.Message));
            }
        }

        return new BatchConversionSummary(results, orphanFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private sealed class InputPair
    {
        public string? FieldsPath { get; set; }
        public string? DataPath { get; set; }
    }

    private enum InputKind { Fields, Data }
}
