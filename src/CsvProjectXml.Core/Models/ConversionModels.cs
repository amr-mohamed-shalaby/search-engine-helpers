namespace CsvProjectXml.Core.Models;

public sealed record ConversionRequest(
    string FieldsCsvPath,
    string DataCsvPath,
    string OutputRoot,
    DepartmentEntry Department,
    OutputFolderNameMode FolderNameMode = OutputFolderNameMode.LegacySlug,
    string? TimestampOverride = null);

public sealed record OutputFileSummary(string FileName, long SizeBytes, string Sha256);

public sealed record DepartmentConversionResult(
    string Slug,
    string DisplayName,
    string OutputDirectory,
    long RowCount,
    int FilmCount,
    int FieldCount,
    IReadOnlyList<OutputFileSummary> Files,
    IReadOnlyList<string>? AutoFixes = null);

public enum DepartmentBatchStatus
{
    Converted,
    MissingInput,
    IncompleteInput,
    Failed,
    Skipped
}

public sealed record RowValidationIssue(string Code, string Message, string? ColumnName);

/// <summary>A data record that needs user attention. RecordNumber is 1-based, excluding the CSV header.</summary>
public sealed record DataRowConflict(
    long RecordNumber,
    int StartLine,
    IReadOnlyList<string> Cells,
    IReadOnlyList<RowValidationIssue> Issues);

/// <param name="Conflicts">Rows that need a person's attention; the department is held back while any remain.</param>
/// <param name="AutoFixes">Lossless corrections the CSV reader applied on its own (kept quotes, ignored empty columns, encoding...).</param>
public sealed record ConversionPreflightResult(IReadOnlyList<DataRowConflict> Conflicts,
    IReadOnlyList<string>? AutoFixes = null)
{
    public bool HasConflicts => Conflicts.Count > 0;
    public bool IsValid => Conflicts.Count == 0;
}

public sealed record DepartmentBatchItem(
    string Slug,
    string DisplayName,
    DepartmentBatchStatus Status,
    DepartmentConversionResult? Result = null,
    string? Message = null,
    IReadOnlyList<DataRowConflict>? Conflicts = null,
    string? FieldsCsvPath = null,
    string? DataCsvPath = null,
    Guid? RepairSessionId = null);

public sealed record BatchConversionSummary(
    IReadOnlyList<DepartmentBatchItem> Departments,
    IReadOnlyList<string> OrphanCsvFiles)
{
    public int ConvertedCount => Departments.Count(x => x.Status == DepartmentBatchStatus.Converted);
    public int FailedCount => Departments.Count(x => x.Status == DepartmentBatchStatus.Failed);
    public int MissingCount => Departments.Count(x => x.Status == DepartmentBatchStatus.MissingInput);
    public int IncompleteCount => Departments.Count(x => x.Status == DepartmentBatchStatus.IncompleteInput);
}

public sealed class ConversionException : Exception
{
    public string? FilePath { get; }
    public int? LineNumber { get; }
    public string? ColumnName { get; }

    public ConversionException(string message, string? filePath = null, int? lineNumber = null,
        string? columnName = null, Exception? innerException = null)
        : base(message, innerException)
    {
        FilePath = filePath;
        LineNumber = lineNumber;
        ColumnName = columnName;
    }

    public override string ToString()
    {
        var location = FilePath is null ? string.Empty : FilePath;
        if (LineNumber is not null) location += $": line {LineNumber}";
        if (!string.IsNullOrWhiteSpace(ColumnName)) location += $", column '{ColumnName}'";
        return string.IsNullOrEmpty(location) ? Message : $"{location}: {Message}";
    }
}
