namespace CsvProjectXml.Core.Models;

public enum EmptyValueMode
{
    PairedTags,
    SelfClosingTags
}

/// <summary>What to do with a data row that has more cells than the header (usually an unquoted comma in text).</summary>
public enum ExtraColumnsMode
{
    /// <summary>Hold the row back as a conflict for manual correction (default; nothing is guessed).</summary>
    Hold,

    /// <summary>
    /// Join the extra cells (with commas, exactly as typed) into the last column and report each row for review.
    /// Correct when the comma was typed in the last column; wrong when it was typed in an earlier text column.
    /// </summary>
    MergeIntoLastColumn,

    /// <summary>
    /// Keep only the first cells and drop the rest. This is what V1 did; it silently loses the dropped text,
    /// so each dropped value is listed in the report for review.
    /// </summary>
    DropExtra
}

/// <summary>What to do with quotation marks (") that are part of a data value, e.g. Ringer"s.</summary>
public enum QuoteHandling
{
    /// <summary>Keep them exactly as typed (default).</summary>
    Keep,

    /// <summary>Delete them from data values. This is what V1 did.</summary>
    Remove
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
    public EmptyValueMode EmptyValueMode { get; set; } = EmptyValueMode.SelfClosingTags;
    public ExtraColumnsMode ExtraColumnsMode { get; set; } = ExtraColumnsMode.Hold;

    /// <summary>
    /// Reproduces the original converter's forgiving data-row behavior: remove all double quotes before parsing,
    /// drop excess cells, pad missing cells with empty strings, and do not reject non-numeric system identifiers
    /// or values longer than FieldSize. This can be lossy; every structural adjustment is reported.
    /// </summary>
    public bool LegacyTolerantMode { get; set; }

    /// <summary>
    /// V1 wrote several rows that share the same PHOTO_PATH (FilmType, FilmId, PortfolioId). By default that is
    /// reported for review but not blocked; set to true to hold such rows back.
    /// </summary>
    public bool RejectDuplicatePhotoPaths { get; set; }

    public QuoteHandling QuoteHandling { get; set; } = QuoteHandling.Keep;

    /// <summary>
    /// A FieldType with no entry in <see cref="FieldTypeMap"/> (for example INT or DATE) normally stops the department.
    /// When true, such fields are written with the NVARCHAR code instead (values are plain text in the XML either way)
    /// and each field is listed for review. Prefer adding the real code to the map once it is known.
    /// </summary>
    public bool UnmappedFieldTypeAsText { get; set; }

    public bool ValidateNotNull { get; set; }
    public bool ValidateFieldSize { get; set; } = true;
    public List<string> EnabledFeatureIds { get; set; } = [];
    /// <summary>
    /// Reproduces the original V1 converter's forgiving data-row behavior. This is intentionally lossy and
    /// should be selected only when matching the old executable is more important than preserving malformed input.
    /// </summary>
    public static ConversionSettings V1Compatible() => new()
    {
        LegacyTolerantMode = true,
        ExtraColumnsMode = ExtraColumnsMode.DropExtra,
        QuoteHandling = QuoteHandling.Remove,
        ValidateFieldSize = false,
        UnmappedFieldTypeAsText = true,
        RejectDuplicatePhotoPaths = false
    };

    public Dictionary<string, int> FieldTypeMap { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NVARCHAR"] = 1,
        ["LIST"] = 5
    };
}
