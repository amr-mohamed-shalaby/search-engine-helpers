using CsvProjectXml.Core.Models;

namespace CsvProjectXml.Core.Services;

/// <summary>
/// One department's conflicts being fixed by hand. Edits stay in memory and in a separate corrected copy of
/// the data CSV (the original is never touched); nothing is converted until <see cref="ConvertAsync"/>.
/// </summary>
public sealed class DepartmentRepair
{
    private readonly IReadOnlyList<DataRowConflict> _initialConflicts;
    private readonly IReadOnlyList<string> _initialHeaders;

    public DepartmentRepair(Guid id, ConversionRequest request, ConversionSettings settings,
        IReadOnlyList<string> expectedHeaders, IReadOnlyList<DataRowConflict> conflicts)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(settings);
        Id = id;
        OriginalRequest = request;
        CurrentRequest = request;
        Settings = settings;
        _initialHeaders = expectedHeaders;
        _initialConflicts = conflicts;
        ExpectedHeaders = expectedHeaders;
        Rows = conflicts.Select(conflict => new EditableConflictRow(conflict)).ToList();
    }

    public Guid Id { get; }
    public ConversionRequest OriginalRequest { get; }

    /// <summary>The request pointing at the latest corrected copy (or the original file before any check).</summary>
    public ConversionRequest CurrentRequest { get; private set; }

    public ConversionSettings Settings { get; }
    public IReadOnlyList<string> ExpectedHeaders { get; private set; }
    public List<EditableConflictRow> Rows { get; private set; }
    public int InitialConflictCount => _initialConflicts.Count;
    public int Revision { get; private set; }
    public DepartmentRepairState State { get; private set; } = DepartmentRepairState.Editing;

    /// <summary>The result of the last check, for display.</summary>
    public string? Message { get; private set; }

    public string Slug => OriginalRequest.Department.Value;
    public string DisplayName => OriginalRequest.Department.DisplayName;
    public int ExpectedWidth => ExpectedHeaders.Count;
    public bool HasEdits => Revision > 0 || Rows.Any(row => row.IsEdited);

    private string StagingDirectory => Path.Combine(OriginalRequest.OutputRoot, "_repaired-inputs", Slug, Id.ToString("N"));

    /// <summary>
    /// Learns what the person did to the first row they fixed and, with <see cref="ApplyRuleToAll"/>, replays the
    /// structural steps on every other row that has the same kind of problem.
    /// </summary>
    public RepairRule? LearnRule()
    {
        var learned = Rows.Where(row => row.Operations.Count > 0)
            .Select(row => RepairRule.FromRow(row, ExpectedWidth))
            .FirstOrDefault(rule => rule is not null);
        if (learned is not null) LastRule = learned;
        return learned ?? LastRule;
    }

    /// <summary>
    /// The last rule learned from a fixed row. It outlives the row itself, which disappears from the list once
    /// a check accepts it, so the rule can still be applied to the conflicts that remain.
    /// </summary>
    public RepairRule? LastRule { get; private set; }

    public RuleApplicationSummary ApplyRuleToAll(RepairRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        LastRule = rule;
        var results = new List<RuleRowResult>();
        foreach (var row in Rows)
        {
            // Rows the person already fixed by hand (including the one the rule was learned from) are left alone.
            if (row.Operations.Count > 0) continue;
            results.Add(new RuleRowResult(row.RecordNumber, rule.ApplyTo(row, ExpectedWidth)));
        }

        MarkEdited();
        return new RuleApplicationSummary(results);
    }

    /// <summary>Call after any edit so a previously verified department is checked again.</summary>
    public void MarkEdited()
    {
        if (State == DepartmentRepairState.Converted) return;
        if (State == DepartmentRepairState.Verified) State = DepartmentRepairState.Editing;
    }

    /// <summary>
    /// Writes a corrected copy of the data CSV with the current edits and checks it again.
    /// Returns true when no conflicts are left.
    /// </summary>
    public async Task<bool> VerifyAsync(CsvRepairService repair, CsvProjectConverter converter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repair);
        ArgumentNullException.ThrowIfNull(converter);

        LearnRule(); // remember what was done before the fixed rows leave the list

        var corrections = Rows.Where(row => row.IsEdited)
            .ToDictionary(row => row.RecordNumber, row => (IReadOnlyList<string>)row.Cells.ToArray());

        Directory.CreateDirectory(StagingDirectory);
        var revision = Revision + 1;
        var correctedPath = Path.Combine(StagingDirectory, $"data-revision-{revision}.csv");
        repair.WriteCorrectedCopy(CurrentRequest.DataCsvPath, correctedPath, corrections);

        var candidate = CurrentRequest with { DataCsvPath = correctedPath };
        var preflight = await Task.Run(() => converter.Validate(candidate, Settings), cancellationToken);

        CurrentRequest = candidate;
        Revision = revision;
        if (preflight.HasConflicts)
        {
            ExpectedHeaders = ReadHeaders(correctedPath);
            Rows = preflight.Conflicts.Select(conflict => new EditableConflictRow(conflict)).ToList();
            State = DepartmentRepairState.HasRemainingConflicts;
            Message = $"{preflight.Conflicts.Count} conflict(s) remain.";
            return false;
        }

        Rows = [];
        State = DepartmentRepairState.Verified;
        Message = null;
        return true;
    }

    /// <summary>Forgets every edit and check and goes back to the conflicts as they were first found.</summary>
    public void Reset()
    {
        if (State == DepartmentRepairState.Converted)
            throw new InvalidOperationException("A converted department cannot be reverted.");
        DeleteStaging();
        CurrentRequest = OriginalRequest;
        ExpectedHeaders = _initialHeaders;
        Rows = _initialConflicts.Select(conflict => new EditableConflictRow(conflict)).ToList();
        Revision = 0;
        State = DepartmentRepairState.Editing;
        Message = null;
        LastRule = null;
    }

    /// <summary>Removes the corrected copies this repair wrote. The original CSV is never involved.</summary>
    public void Discard() => DeleteStaging();

    public async Task<DepartmentConversionResult> ConvertAsync(CsvProjectConverter converter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(converter);
        if (State != DepartmentRepairState.Verified)
            throw new InvalidOperationException("Only a department whose conflicts are all fixed can be converted.");
        var request = CurrentRequest;
        var result = await Task.Run(() => converter.Convert(request, Settings), cancellationToken);
        Result = result;
        State = DepartmentRepairState.Converted;
        return result;
    }

    /// <summary>The conversion result once <see cref="ConvertAsync"/> has succeeded.</summary>
    public DepartmentConversionResult? Result { get; private set; }

    private static IReadOnlyList<string> ReadHeaders(string path)
    {
        using var enumerator = CsvReader.Read(path).GetEnumerator();
        return enumerator.MoveNext() ? enumerator.Current.Values.ToArray() : [];
    }

    private void DeleteStaging()
    {
        try
        {
            if (Directory.Exists(StagingDirectory)) Directory.Delete(StagingDirectory, recursive: true);
            // Remove the department folder and the shared _repaired-inputs folder too, once they are empty.
            var directory = Path.GetDirectoryName(StagingDirectory);
            for (var level = 0; level < 2 && directory is not null && Directory.Exists(directory)
                                && !Directory.EnumerateFileSystemEntries(directory).Any(); level++)
            {
                Directory.Delete(directory);
                directory = Path.GetDirectoryName(directory);
            }
        }
        catch (IOException)
        {
            // Best effort: leftover staging files are harmless and are never the original CSV.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
