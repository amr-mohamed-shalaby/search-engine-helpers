namespace CsvProjectXml.Core.Models;

public enum EmptyValueMode
{
    PairedTags,
    SelfClosingTags
}

public enum OutputFolderNameMode
{
    LegacySlug,
    ArabicDisplayName
}

/// <summary>
/// V1-compatible defaults. Unknown target-system values remain explicit and editable.
/// </summary>
public sealed class ConversionSettings
{
    public int ProjectCode { get; set; } = 444;
    public int OrgCode { get; set; } = 37;
    public int UserModificationCode { get; set; } = 71;
    public int IsCivil { get; set; } = 1;
    public int IsStructureCreated { get; set; } = 1;
    public string TimestampFormat { get; set; } = "MM/dd/yyyy hh:mm:ss tt";
    public EmptyValueMode EmptyValueMode { get; set; } = EmptyValueMode.PairedTags;
    public bool ValidateNotNull { get; set; }
    public bool ValidateFieldSize { get; set; } = true;
    public List<string> EnabledFeatureIds { get; set; } = [];
    public Dictionary<string, int> FieldTypeMap { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NVARCHAR"] = 1
    };
}
