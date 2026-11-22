using CsvProjectXml.Core.Models;

namespace CsvProjectXml.Core.Services;

/// <summary>
/// The structural steps a person took to fix one row, generalised so they can be replayed on other rows.
/// Only the cell structure is replayed, never typed text. A rule applies only to rows with the same kind of
/// problem (too many cells, or too few) and, where the steps merge or remove cells, it repeats the last step
/// as often as the other row needs.
/// </summary>
public sealed record RepairRule(RuleDirection Direction, IReadOnlyList<CellOperation> Steps)
{
    /// <summary>
    /// Learns a rule from a row the person has already fixed. Returns null when there is nothing to learn:
    /// no structural steps, or the row already had the right number of cells (its steps only moved text around).
    /// </summary>
    public static RepairRule? FromRow(EditableConflictRow row, int expectedWidth)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.Operations.Count == 0) return null;

        var original = row.OriginalCells.Count;
        if (original == expectedWidth) return null;

        return new RepairRule(
            original > expectedWidth ? RuleDirection.TooManyCells : RuleDirection.TooFewCells,
            row.Operations.ToArray());
    }

    public RuleOutcome ApplyTo(EditableConflictRow row, int expectedWidth)
    {
        ArgumentNullException.ThrowIfNull(row);
        var count = row.Cells.Count;
        if (count == expectedWidth) return RuleOutcome.NotApplicable;
        var direction = count > expectedWidth ? RuleDirection.TooManyCells : RuleDirection.TooFewCells;
        if (direction != Direction) return RuleOutcome.NotApplicable;

        foreach (var step in Steps)
        {
            // Do not overshoot: a step that removes cells is skipped once the row is no longer too long.
            if (Shrinks(step) && row.Cells.Count <= expectedWidth) continue;
            if (!Shrinks(step) && row.Cells.Count >= expectedWidth) continue;
            Apply(row, step);
        }

        // The row may have needed more of the same step than the row the rule was learned from.
        var last = Steps[^1];
        while (Shrinks(last) ? row.Cells.Count > expectedWidth : row.Cells.Count < expectedWidth)
        {
            if (!Apply(row, last)) break;
        }

        return row.Cells.Count == expectedWidth ? RuleOutcome.Applied : RuleOutcome.Partial;
    }

    private static bool Shrinks(CellOperation step) => step is CellOperation.MergeWithNext or CellOperation.Remove;

    private static bool Apply(EditableConflictRow row, CellOperation step) => step switch
    {
        CellOperation.MergeWithNext merge => row.MergeWithNext(merge.Index),
        CellOperation.Remove remove => row.Remove(remove.Index),
        CellOperation.Insert insert => row.Insert(insert.Index),
        CellOperation.Append => AppendAndReport(row),
        _ => false
    };

    private static bool AppendAndReport(EditableConflictRow row)
    {
        row.Append();
        return true;
    }
}
