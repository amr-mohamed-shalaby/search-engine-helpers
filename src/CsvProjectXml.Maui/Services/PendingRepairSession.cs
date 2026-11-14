using CsvProjectXml.Core.Models;

namespace CsvProjectXml.Maui.Services;

public sealed record PendingRepairSession(
    Guid Id,
    ConversionRequest CurrentRequest,
    string OriginalDataCsvPath,
    ConversionSettings Settings,
    IReadOnlyList<string> ExpectedHeaders,
    IReadOnlyList<DataRowConflict> Conflicts,
    int Revision);
