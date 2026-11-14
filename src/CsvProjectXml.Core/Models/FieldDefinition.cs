namespace CsvProjectXml.Core.Models;

public sealed record FieldDefinition(
    string SourceId,
    string FieldName,
    string FieldNameAr,
    string FieldType,
    int FieldSize,
    bool NotNull,
    string ListValues,
    int XmlFieldType);
