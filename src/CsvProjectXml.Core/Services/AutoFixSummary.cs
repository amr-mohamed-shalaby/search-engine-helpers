namespace CsvProjectXml.Core.Services;

public enum AutoFixKind
{
    DuplicatePhotoPath,
    ExtraCells,
    Quotes,
    Encoding,
    Delimiter,
    EmptyColumns,
    ListValues,
    Other
}

public sealed record AutoFixGroup(AutoFixKind Kind, int Count, int NeedsReview, IReadOnlyList<string> Messages);

/// <summary>Groups the free-text notes the converter reports (<c>AutoFixes</c>) by what they are about.</summary>
public static class AutoFixSummary
{
    public static IReadOnlyList<AutoFixGroup> Group(IEnumerable<string>? notes)
    {
        if (notes is null) return [];
        return notes
            .GroupBy(Classify)
            .OrderBy(group => group.Key)
            .Select(group => new AutoFixGroup(
                group.Key,
                group.Count(),
                group.Count(note => note.Contains("REVIEW", StringComparison.Ordinal)),
                group.ToArray()))
            .ToArray();
    }

    public static AutoFixKind Classify(string note)
    {
        if (note.Contains("PHOTO_PATH", StringComparison.Ordinal)) return AutoFixKind.DuplicatePhotoPath;
        if (note.Contains("extra cell", StringComparison.OrdinalIgnoreCase)) return AutoFixKind.ExtraCells;
        if (note.Contains("quot", StringComparison.OrdinalIgnoreCase)) return AutoFixKind.Quotes;
        if (note.Contains("encoding", StringComparison.OrdinalIgnoreCase) ||
            note.Contains("decoded", StringComparison.OrdinalIgnoreCase)) return AutoFixKind.Encoding;
        if (note.Contains("delimiter", StringComparison.OrdinalIgnoreCase)) return AutoFixKind.Delimiter;
        if (note.Contains("trailing column", StringComparison.OrdinalIgnoreCase)) return AutoFixKind.EmptyColumns;
        if (note.Contains("ListValues", StringComparison.Ordinal)) return AutoFixKind.ListValues;
        return AutoFixKind.Other;
    }
}
