using CsvProjectXml.Core.Models;

namespace CsvProjectXml.Maui.Services;

/// <summary>
/// Holds the conversion page's in-memory state while navigation temporarily replaces
/// the Convert component with the repair page.
/// </summary>
public sealed record ConversionPageSnapshot(
    string Mode,
    string NamesPath,
    string FieldsCsvPath,
    string DataCsvPath,
    string InputFolder,
    string OutputFolder,
    string TimestampOverride,
    bool UseArabicFolderName,
    string ErrorMessage,
    string ReportPath,
    DepartmentEntry? MatchedDepartment,
    DepartmentConversionResult? SingleResult,
    BatchConversionSummary? BatchResult,
    Guid? PendingRepairSessionId);

public sealed class ConversionPageState
{
    public ConversionPageSnapshot? Current { get; private set; }

    public void Save(ConversionPageSnapshot snapshot) => Current = snapshot;

    public void Clear() => Current = null;
}
