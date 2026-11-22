using System.Security.Cryptography;
using System.Text;
using CsvProjectXml.Core.Models;
using CsvProjectXml.Core.Services;
using Xunit;

namespace CsvProjectXml.Core.Tests;

public sealed class ConflictWizardTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("CsvProjectXmlWizardTests_").FullName;
    private readonly CsvProjectConverter _converter = new();
    private readonly CsvRepairService _repair = new();
    private const string Stamp = "11/11/2026 09:52:32 AM";
    private const string Fields = "ID,FieldName,FieldNameAr,FieldType,FieldSize,NotNull,ListValues\r\n" +
                                  "1,area,المنطقة,NVARCHAR,200,False,\r\n2,num,الرقم,NVARCHAR,200,False,\r\n";

    // Row 1: one extra cell, row 2: two extra cells, row 3: fine, row 4: one cell missing, row 5: FilmId is not a number.
    private const string Data = "ID,FilmId,FilmType,PortfolioId,area,num\r\n" +
                                "1,4,16,4,محافظه قنا,407 , 410\r\n" +
                                "2,5,16,5,محافظه سوهاج,164 , 165 , 166\r\n" +
                                "3,6,16,6,قنا,ok\r\n" +
                                "4,7,16,7,only\r\n" +
                                "5,x,16,8,a,b\r\n";

    private DepartmentRepair CreateDepartment(string slug, string data = Data)
    {
        var fieldsPath = Path.Combine(_root, $"{slug}_fields.csv");
        var dataPath = Path.Combine(_root, $"{slug}_data.csv");
        File.WriteAllText(fieldsPath, Fields, new UTF8Encoding(true));
        File.WriteAllText(dataPath, data, new UTF8Encoding(true));
        var request = new ConversionRequest(fieldsPath, dataPath, _root,
            new DepartmentEntry { Index = 1, Value = slug, Text = "إدارة " + slug }, OutputFolderNameMode.LegacySlug, Stamp);
        var settings = new ConversionSettings();
        var preflight = _converter.Validate(request, settings);
        var headers = CsvReader.Read(dataPath).First().Values.ToArray();
        return new DepartmentRepair(Guid.NewGuid(), request, settings, headers, preflight.Conflicts);
    }

    // ---------------------------------------------------------------- rows and rules

    [Fact]
    public void ConflictsAreFoundAsExpected()
    {
        var department = CreateDepartment("dept_a");

        Assert.Equal([1L, 2L, 4L, 5L], department.Rows.Select(row => row.RecordNumber).ToArray());
        Assert.Equal(6, department.ExpectedWidth);
    }

    [Fact]
    public void RowRecordsItsStructuralStepsAndCanBeReset()
    {
        var row = CreateDepartment("dept_a").Rows[0];

        Assert.True(row.MergeWithNext(5));
        Assert.Equal("407 , 410", row.Cells[5]);
        Assert.Equal([new CellOperation.MergeWithNext(5)], row.Operations);
        Assert.True(row.IsEdited);

        row.Reset();
        Assert.False(row.IsEdited);
        Assert.Empty(row.Operations);
        Assert.Equal(7, row.Cells.Count);
    }

    [Fact]
    public void RuleLearnedFromTheFirstFixedRowIsReplayedOnRowsWithTheSameProblem()
    {
        var department = CreateDepartment("dept_a");
        department.Rows[0].MergeWithNext(5);

        var rule = department.LearnRule();
        Assert.NotNull(rule);
        Assert.Equal(RuleDirection.TooManyCells, rule!.Direction);

        var summary = department.ApplyRuleToAll(rule);

        // Row 2 had two extra cells: the same step is repeated until it fits. Row 4 has too few cells and row 5
        // has the right number of cells (its problem is the text), so neither is touched.
        Assert.Equal(1, summary.Applied);
        Assert.Equal(2, summary.NotApplicable);
        Assert.Equal(0, summary.Partial);
        Assert.Equal("164 , 165 , 166", department.Rows[1].Cells[5]);
        Assert.Equal(6, department.Rows[1].Cells.Count);
        Assert.False(department.Rows[3].IsEdited);
    }

    [Fact]
    public void RuleDoesNotOvershootWhenAnotherRowNeedsFewerSteps()
    {
        var department = CreateDepartment("dept_a");
        // Learn from the row with two extra cells (two merges)...
        department.Rows[1].MergeWithNext(5);
        department.Rows[1].MergeWithNext(5);
        var rule = department.LearnRule()!;

        // ...and replay on the row with one extra cell: it must stop at the expected width.
        var outcome = rule.ApplyTo(department.Rows[0], department.ExpectedWidth);

        Assert.Equal(RuleOutcome.Applied, outcome);
        Assert.Equal(6, department.Rows[0].Cells.Count);
        Assert.Equal("407 , 410", department.Rows[0].Cells[5]);
    }

    [Fact]
    public void TextOnlyEditsAndEqualWidthRowsDoNotProduceARule()
    {
        var department = CreateDepartment("dept_a");
        var numberRow = department.Rows[3];                 // right width, wrong text
        numberRow.Cells[1] = "8";
        Assert.Null(department.LearnRule());

        numberRow.Remove(0);                                 // a structural step on an equal-width row
        numberRow.Insert(0);
        Assert.Null(RepairRule.FromRow(numberRow, department.ExpectedWidth));
    }

    [Fact]
    public void MissingCellsRuleAppendsEmptyCellsToOtherShortRows()
    {
        var short1 = "ID,FilmId,FilmType,PortfolioId,area,num\r\n1,4,16,4,a\r\n2,5,16,5,b\r\n3,6,16,6,c,d\r\n";
        var department = CreateDepartment("dept_short", short1);
        department.Rows[0].Append();

        var summary = department.ApplyRuleToAll(department.LearnRule()!);

        Assert.Equal(1, summary.Applied);
        Assert.Equal(["2", "5", "16", "5", "b", ""], department.Rows[1].Cells);
    }

    // ---------------------------------------------------------------- verify, convert, reset

    [Fact]
    public async Task FixingEveryRowThenVerifyingMakesTheDepartmentReadyAndConvertible()
    {
        var department = CreateDepartment("dept_a");
        department.Rows[0].MergeWithNext(5);
        department.ApplyRuleToAll(department.LearnRule()!);
        department.Rows[2].Append();                 // row 4: missing cell
        department.Rows[3].Cells[1] = "8";           // row 5: FilmId text

        var verified = await department.VerifyAsync(_repair, _converter);

        Assert.True(verified);
        Assert.Equal(DepartmentRepairState.Verified, department.State);
        Assert.Empty(department.Rows);

        var result = await department.ConvertAsync(_converter);
        Assert.Equal(5, result.RowCount);
        var xml = File.ReadAllText(Path.Combine(result.OutputDirectory, "Project_Data.xml"), Encoding.UTF8);
        Assert.Contains("<num>407 , 410</num>", xml);
        Assert.Contains("<num>164 , 165 , 166</num>", xml);
    }

    [Fact]
    public async Task VerifyKeepsTheRemainingConflictsAndTheOriginalFileIsNeverChanged()
    {
        var department = CreateDepartment("dept_a");
        var originalHash = Hash(department.OriginalRequest.DataCsvPath);
        department.Rows[0].MergeWithNext(5);

        var verified = await department.VerifyAsync(_repair, _converter);

        Assert.False(verified);
        Assert.Equal(DepartmentRepairState.HasRemainingConflicts, department.State);
        Assert.Equal([2L, 4L, 5L], department.Rows.Select(row => row.RecordNumber).ToArray());
        Assert.NotEqual(department.OriginalRequest.DataCsvPath, department.CurrentRequest.DataCsvPath);
        Assert.Equal(originalHash, Hash(department.OriginalRequest.DataCsvPath));
        await Assert.ThrowsAsync<InvalidOperationException>(() => department.ConvertAsync(_converter));
    }

    [Fact]
    public async Task ResetGoesBackToTheConflictsAsFirstFoundAndRemovesCorrectedCopies()
    {
        var department = CreateDepartment("dept_a");
        department.Rows[0].MergeWithNext(5);
        await department.VerifyAsync(_repair, _converter);
        var staged = department.CurrentRequest.DataCsvPath;
        Assert.True(File.Exists(staged));

        department.Reset();

        Assert.False(File.Exists(staged));
        Assert.Equal(department.OriginalRequest.DataCsvPath, department.CurrentRequest.DataCsvPath);
        Assert.Equal(4, department.Rows.Count);
        Assert.All(department.Rows, row => Assert.False(row.IsEdited));
        Assert.Equal(DepartmentRepairState.Editing, department.State);
    }

    // ---------------------------------------------------------------- wizard

    [Fact]
    public void NavigationKeepsEditsMadeOnEarlierSlides()
    {
        var wizard = new ConflictWizard([CreateDepartment("dept_a"), CreateDepartment("dept_b")]);
        Assert.Equal(3, wizard.SlideCount);
        Assert.False(wizard.CanGoBack);

        wizard.Current!.Rows[0].MergeWithNext(5);
        wizard.Next();
        Assert.Equal("إدارة dept_b", wizard.Current!.DisplayName);
        wizard.Next();
        Assert.True(wizard.IsReviewSlide);
        Assert.False(wizard.CanGoNext);
        wizard.Back();
        wizard.Back();

        Assert.Equal("إدارة dept_a", wizard.Current!.DisplayName);
        Assert.Equal("407 , 410", wizard.Current.Rows[0].Cells[5]);
    }

    [Fact]
    public async Task FinishAdvancesOnlyWhenTheDepartmentIsFullyFixed()
    {
        var a = CreateDepartment("dept_a");
        var wizard = new ConflictWizard([a, CreateDepartment("dept_b")]);

        a.Rows[0].MergeWithNext(5);
        Assert.False(await wizard.FinishCurrentAsync(_repair, _converter));
        Assert.Equal(0, wizard.CurrentIndex);
        Assert.Equal([2L, 4L, 5L], a.Rows.Select(row => row.RecordNumber).ToArray());

        // The fixed row is gone, but the rule learned from it is still available for the rows that remain.
        var summary = a.ApplyRuleToAll(a.LearnRule()!);
        Assert.Equal(1, summary.Applied);
        a.Rows[1].Append();
        a.Rows[2].Cells[1] = "8";
        Assert.True(await wizard.FinishCurrentAsync(_repair, _converter));
        Assert.Equal(1, wizard.CurrentIndex);
    }

    [Fact]
    public async Task CompleteConvertsOnlyTheDepartmentsThatAreReady()
    {
        var a = CreateDepartment("dept_a");
        var b = CreateDepartment("dept_b");
        var wizard = new ConflictWizard([a, b]);
        a.Rows[0].MergeWithNext(5);
        a.ApplyRuleToAll(a.LearnRule()!);
        a.Rows[2].Append();                 // record 4: missing cell
        a.Rows[3].Cells[1] = "8";           // record 5: FilmId text
        Assert.True(await wizard.FinishCurrentAsync(_repair, _converter));

        var outcomes = await wizard.CompleteAsync(_converter);

        var outcome = Assert.Single(outcomes);
        Assert.Equal("dept_a", outcome.Slug);
        Assert.NotNull(outcome.Result);
        Assert.Null(outcome.Error);
        Assert.Equal(1, wizard.VerifiedCount);
        Assert.False(Directory.Exists(Path.Combine(_root, "dept_b")));
    }

    [Fact]
    public async Task CancelRemovesEveryCorrectedCopyAndLeavesTheOriginalsUntouched()
    {
        var a = CreateDepartment("dept_a");
        var originalHash = Hash(a.OriginalRequest.DataCsvPath);
        var wizard = new ConflictWizard([a, CreateDepartment("dept_b")]);
        a.Rows[0].MergeWithNext(5);
        await a.VerifyAsync(_repair, _converter);
        Assert.True(Directory.Exists(Path.Combine(_root, "_repaired-inputs")));

        wizard.Cancel();

        Assert.False(Directory.Exists(Path.Combine(_root, "_repaired-inputs")));
        Assert.Equal(originalHash, Hash(a.OriginalRequest.DataCsvPath));
    }

    // ---------------------------------------------------------------- statistics grouping

    [Fact]
    public void AutoFixNotesAreGroupedByTopic()
    {
        var groups = AutoFixSummary.Group(
        [
            "x_data.csv: line 3, column 5: a quote inside an unquoted value was kept as a literal character.",
            "x_data.csv: line 9: quotation mark(s) were removed from 'name'.",
            "x_data.csv: line 4: REVIEW - 1 extra cell(s) were joined into the last column 'num'.",
            "x_data.csv: line 7: REVIEW - PHOTO_PATH \\Scan_Tree\\16\\1\\Film\\1 is used by more than one row. It is written as-is, like V1.",
            "file is not valid UTF-8; it was decoded as Windows-1256 (Arabic). Check that the text looks right.",
            "something else"
        ]);

        Assert.Equal(2, groups.Single(g => g.Kind == AutoFixKind.Quotes).Count);
        Assert.Equal(1, groups.Single(g => g.Kind == AutoFixKind.ExtraCells).NeedsReview);
        Assert.Equal(1, groups.Single(g => g.Kind == AutoFixKind.DuplicatePhotoPath).Count);
        Assert.Equal(1, groups.Single(g => g.Kind == AutoFixKind.Encoding).Count);
        Assert.Equal(1, groups.Single(g => g.Kind == AutoFixKind.Other).Count);
        Assert.Empty(AutoFixSummary.Group(null));
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
