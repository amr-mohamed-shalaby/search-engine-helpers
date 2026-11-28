namespace CsvProjectXml.Maui.Services;

/// <summary>A compact, durable record of one conversion attempt.</summary>
public sealed record ConversionHistoryEntry(
    Guid Id,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    string Mode,
    string Status,
    string? NamesFilePath,
    string? FieldsCsvPath,
    string? DataCsvPath,
    string? InputFolder,
    string? OutputFolder,
    string? ReportPath,
    string? Summary,
    string? Error);
