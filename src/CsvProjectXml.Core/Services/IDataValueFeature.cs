using CsvProjectXml.Core.Models;

namespace CsvProjectXml.Core.Services;

/// <summary>
/// Extension point for future opt-in field transformations. A feature is inert unless
/// its Id is listed in ConversionSettings.EnabledFeatureIds.
/// </summary>
public interface IDataValueFeature
{
    string Id { get; }
    string DisplayName { get; }
    string Transform(string value, FieldDefinition field, DataRowContext row);
}

public sealed record DataRowContext(
    string SourceRowId,
    long FilmId,
    long FilmType,
    long PortfolioId,
    string SourceFile,
    int SourceLine);
