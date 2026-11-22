using CsvProjectXml.Core.Models;

namespace CsvProjectXml.Core.Services;

/// <summary>
/// A conflicting CSV row that a person is editing. Structural edits (merge, remove, insert, append) are
/// recorded so the same steps can be replayed on other rows; typing into a cell is not recorded.
/// </summary>
public sealed class EditableConflictRow
{
    private readonly List<CellOperation> _operations = [];

    public EditableConflictRow(DataRowConflict conflict)
    {
        RecordNumber = conflict.RecordNumber;
        StartLine = conflict.StartLine;
        Issues = conflict.Issues;
        OriginalCells = conflict.Cells.ToArray();
        Cells = conflict.Cells.ToList();
    }

    public long RecordNumber { get; }
    public int StartLine { get; }
    public IReadOnlyList<RowValidationIssue> Issues { get; }
    public IReadOnlyList<string> OriginalCells { get; }

    /// <summary>The cells being edited. The UI binds its text boxes to this list.</summary>
    public List<string> Cells { get; }

    public IReadOnlyList<CellOperation> Operations => _operations;
    public bool IsEdited => !Cells.SequenceEqual(OriginalCells, StringComparer.Ordinal);

    public bool MergeWithNext(int index)
    {
        if (index < 0 || index >= Cells.Count - 1) return false;
        // Keep the exact comma and spacing that the CSV parser split on.
        Cells[index] += "," + Cells[index + 1];
        Cells.RemoveAt(index + 1);
        _operations.Add(new CellOperation.MergeWithNext(index));
        return true;
    }

    public bool Remove(int index)
    {
        if (Cells.Count <= 1 || index < 0 || index >= Cells.Count) return false;
        Cells.RemoveAt(index);
        _operations.Add(new CellOperation.Remove(index));
        return true;
    }

    public bool Insert(int index)
    {
        if (index < 0 || index > Cells.Count) return false;
        Cells.Insert(index, string.Empty);
        _operations.Add(new CellOperation.Insert(index));
        return true;
    }

    public void Append()
    {
        Cells.Add(string.Empty);
        _operations.Add(new CellOperation.Append());
    }

    /// <summary>Puts the row back exactly as it was read, and forgets the recorded steps.</summary>
    public void Reset()
    {
        Cells.Clear();
        Cells.AddRange(OriginalCells);
        _operations.Clear();
    }
}
