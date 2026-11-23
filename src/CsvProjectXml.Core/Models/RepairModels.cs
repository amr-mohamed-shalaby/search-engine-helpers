namespace CsvProjectXml.Core.Models;

/// <summary>A structural edit a person made to the cells of one conflicting row.</summary>
public abstract record CellOperation
{
    /// <summary>Joined the cell at <see cref="Index"/> with the next one (keeping the comma that separated them).</summary>
    public sealed record MergeWithNext(int Index) : CellOperation;

    /// <summary>Deleted the cell at <see cref="Index"/>.</summary>
    public sealed record Remove(int Index) : CellOperation;

    /// <summary>Inserted an empty cell at <see cref="Index"/>.</summary>
    public sealed record Insert(int Index) : CellOperation;

    /// <summary>Added an empty cell at the end.</summary>
    public sealed record Append : CellOperation;
}

public enum RuleDirection
{
    /// <summary>The row had more cells than the header.</summary>
    TooManyCells,

    /// <summary>The row had fewer cells than the header.</summary>
    TooFewCells
}

public enum RuleOutcome
{
    /// <summary>The row now has the expected number of cells.</summary>
    Applied,

    /// <summary>The steps were replayed but the row still has the wrong number of cells.</summary>
    Partial,

    /// <summary>The row is a different kind of conflict, so the steps were not replayed.</summary>
    NotApplicable
}

public sealed record RuleRowResult(long RecordNumber, RuleOutcome Outcome);

public sealed record RuleApplicationSummary(IReadOnlyList<RuleRowResult> Rows)
{
    public int Applied => Rows.Count(row => row.Outcome == RuleOutcome.Applied);
    public int Partial => Rows.Count(row => row.Outcome == RuleOutcome.Partial);
    public int NotApplicable => Rows.Count(row => row.Outcome == RuleOutcome.NotApplicable);
}

public enum DepartmentRepairState
{
    /// <summary>Edits (if any) have not been checked yet.</summary>
    Editing,

    /// <summary>The corrected copy was checked and has no conflicts left; it is ready to convert.</summary>
    Verified,

    /// <summary>The corrected copy was checked and some conflicts remain.</summary>
    HasRemainingConflicts,

    /// <summary>The department was converted; its XML files exist and it is no longer editable.</summary>
    Converted
}

public sealed record WizardConversionOutcome(
    Guid DepartmentRepairId,
    string Slug,
    string DisplayName,
    DepartmentConversionResult? Result,
    string? Error);
