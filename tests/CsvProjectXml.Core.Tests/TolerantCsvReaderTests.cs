using System.Text;
using CsvProjectXml.Core.Models;
using CsvProjectXml.Core.Services;
using Xunit;

namespace CsvProjectXml.Core.Tests;

/// <summary>
/// Hand-typed data is never clean. These tests pin down how the reader recovers the record
/// structure without changing cell text, and that anything it cannot recover is held back.
/// </summary>
public sealed class TolerantCsvReaderTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("CsvProjectXmlTolerantTests_").FullName;
    private const string Slug = "tolerant_test_hyeh_altnmya_alsnaaya";
    private const string Stamp = "11/11/2026 09:52:32 AM";
    private const string FieldsHeader = "ID,FieldName,FieldNameAr,FieldType,FieldSize,NotNull,ListValues\r\n";

    private static string FieldsFile(params string[] names) =>
        FieldsHeader + string.Concat(names.Select((name, i) => $"{i + 1},{name},{name},NVARCHAR,200,False,\r\n"));

    // ---------------------------------------------------------------- reader level

    [Fact]
    public void ValidRfc4180InputIsReadExactlyAsBefore()
    {
        var path = Write("a.csv", "h1,h2,h3\r\n1,\"x, y\",\"he said \"\"hi\"\"\"\r\n2,\"line1\r\nline2\",z\r\n");
        var records = CsvReader.Read(path).ToList();

        Assert.Equal(3, records.Count);
        Assert.Equal(["1", "x, y", "he said \"hi\""], records[1].Values);
        Assert.Equal(["2", "line1\r\nline2", "z"], records[2].Values);
        Assert.All(records, record => Assert.Empty(record.Notes));
    }

    [Fact]
    public void StrayQuoteInsideUnquotedValueIsKeptAsLiteral()
    {
        var path = Write("a.csv", "h1,h2,h3\r\n1,مبنى \"الإدارة\" الجديد,12\"\r\n");
        var record = CsvReader.Read(path).Skip(1).Single();

        Assert.Equal(["1", "مبنى \"الإدارة\" الجديد", "12\""], record.Values);
        Assert.NotEmpty(record.Notes);
    }

    [Fact]
    public void QuoteFollowedByTextInsideQuotedValueIsKeptAsLiteral()
    {
        var path = Write("a.csv", "h1,h2\r\n1,\"abc\" def\r\n2,next\r\n");
        var records = CsvReader.Read(path).Skip(1).ToList();

        Assert.Equal(2, records.Count);
        Assert.Equal("\"abc\" def", records[0].Values[1]);
        Assert.Equal("next", records[1].Values[1]);
    }

    [Fact]
    public void QuoteThatNeverClosesDoesNotSwallowTheRestOfTheFile()
    {
        var rows = string.Concat(Enumerable.Range(2, 200).Select(i => $"{i},row {i}\r\n"));
        var path = Write("a.csv", "h1,h2\r\n1,\"opening quote only\r\n" + rows);
        var records = CsvReader.Read(path).Skip(1).ToList();

        Assert.Equal(201, records.Count);
        Assert.Equal("\"opening quote only", records[0].Values[1]);
        Assert.Equal("row 201", records[^1].Values[1]);
        Assert.Contains(records[0].Notes, note => note.Contains("never closed", StringComparison.Ordinal)
            || note.Contains("too many lines", StringComparison.Ordinal));
    }

    [Fact]
    public void StrayOpeningQuoteThatIsClosedByALaterRowIsSplitBackIntoRows()
    {
        // The quote in row 1 is "closed" by the quote in row 3, which would merge rows 1-3 into one record
        // with the wrong number of cells. The header width tells the reader to read them as separate rows.
        var path = Write("a.csv", "h1,h2,h3\r\n1,\"stray,x\r\n2,b,c\r\n3,d,\"e\"\r\n4,f,g\r\n");
        var records = CsvReader.Read(path).Skip(1).ToList();

        Assert.Equal(4, records.Count);
        Assert.Equal(["1", "\"stray", "x"], records[0].Values);
        Assert.Equal(["2", "b", "c"], records[1].Values);
        Assert.Equal(["3", "d", "e"], records[2].Values);
        Assert.Equal(["4", "f", "g"], records[3].Values);
        Assert.Contains(records[0].Notes, note => note.Contains("restore the expected 3 columns", StringComparison.Ordinal));
    }

    [Fact]
    public void AmbiguousMergedRowsWithTheRightWidthAreKeptButFlaggedForReview()
    {
        // Both readings have 2 cells, so nothing proves the quote was a mistake: keep the faithful reading
        // and flag it instead of guessing.
        var path = Write("a.csv", "h1,h2\r\n1,\"stray\r\n2,middle\r\n3,end\"\r\n4,last\r\n");
        var records = CsvReader.Read(path).Skip(1).ToList();

        Assert.Equal(2, records.Count);
        Assert.Contains(records[0].Notes, note => note.Contains("REVIEW", StringComparison.Ordinal));
    }

    [Fact]
    public void GenuineMultiLineValueWithCorrectWidthIsStillKept()
    {
        var path = Write("a.csv", "h1,h2\r\n1,\"first\r\nsecond\"\r\n2,x\r\n");
        var records = CsvReader.Read(path).Skip(1).ToList();

        Assert.Equal(2, records.Count);
        Assert.Equal("first\r\nsecond", records[0].Values[1]);
    }

    [Fact]
    public void TrailingEmptyColumnsAndAllEmptyRowsAreIgnored()
    {
        var path = Write("a.csv", "h1,h2\r\n1,a,,,\r\n,,,,\r\n\r\n2,b\r\n");
        var records = CsvReader.Read(path).Skip(1).ToList();

        Assert.Equal(2, records.Count);
        Assert.Equal(["1", "a"], records[0].Values);
        Assert.Equal(["2", "b"], records[1].Values);
    }

    [Fact]
    public void ExtraNonEmptyColumnIsNotHiddenOrMerged()
    {
        var path = Write("a.csv", "h1,h2\r\n1,a,b\r\n");
        var record = CsvReader.Read(path).Skip(1).Single();

        Assert.Equal(3, record.Values.Count);
    }

    [Fact]
    public void SemicolonDelimitedFileIsRecognised()
    {
        var path = Write("a.csv", "h1;h2;h3\r\n1;مرحبا, يا عالم;3\r\n");
        var records = CsvReader.Read(path).ToList();

        Assert.Equal(["h1", "h2", "h3"], records[0].Values);
        Assert.Equal(["1", "مرحبا, يا عالم", "3"], records[1].Values);
        Assert.Contains(records[0].Notes, note => note.Contains("';'", StringComparison.Ordinal));
    }

    [Fact]
    public void Windows1256FileIsDecodedAndReported()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var bytes = Encoding.GetEncoding(1256).GetBytes("h1,h2\r\n1,القاهرة\r\n");
        var path = Path.Combine(_root, "ansi.csv");
        File.WriteAllBytes(path, bytes);

        var records = CsvReader.Read(path).ToList();

        Assert.Equal("القاهرة", records[1].Values[1]);
        Assert.Contains(records[0].Notes, note => note.Contains("1256", StringComparison.Ordinal));
    }

    [Fact]
    public void Utf16FileWithBomIsDecodedAndReported()
    {
        var path = Path.Combine(_root, "utf16.csv");
        File.WriteAllText(path, "h1,h2\r\n1,القاهرة\r\n", new UnicodeEncoding(false, true));

        var records = CsvReader.Read(path).ToList();

        Assert.Equal("القاهرة", records[1].Values[1]);
        Assert.Contains(records[0].Notes, note => note.Contains("UTF-16", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------- converter level

    [Fact]
    public void ConversionSucceedsWithStrayQuotesInDataAndFieldsFiles_AndReportsAutoFixes()
    {
        var (fieldsPath, dataPath) = CreateInput(
            FieldsHeader + "1,area,المنطقة 12\",NVARCHAR,200,False,\r\n",
            "ID,FilmId,FilmType,PortfolioId,area\r\n1,1,16,1,شارع \"النصر\" الرئيسي\r\n2,1,16,2,عادي\r\n");

        var result = Convert(fieldsPath, dataPath);
        var xml = File.ReadAllText(Path.Combine(result.OutputDirectory, "Project_Data.xml"), Encoding.UTF8);

        Assert.Equal(2, result.RowCount);
        Assert.Contains("<area>شارع \"النصر\" الرئيسي</area>", xml);
        Assert.NotNull(result.AutoFixes);
        Assert.Contains(result.AutoFixes!, fix => fix.Contains("_data.csv", StringComparison.Ordinal));
        Assert.Contains(result.AutoFixes!, fix => fix.Contains("_fields.csv", StringComparison.Ordinal));
    }

    [Fact]
    public void CleanFilesProduceNoAutoFixes()
    {
        var (fieldsPath, dataPath) = CreateInput(FieldsFile("area"),
            "ID,FilmId,FilmType,PortfolioId,area\r\n1,1,16,1,Cairo\r\n");

        var result = Convert(fieldsPath, dataPath);

        Assert.Empty(result.AutoFixes!);
    }

    [Fact]
    public void RowThatCannotBeRecoveredIsHeldBackAndNothingIsWritten()
    {
        // An unquoted comma inside free text is structural damage the reader must not guess about.
        var (fieldsPath, dataPath) = CreateInput(FieldsFile("area"),
            "ID,FilmId,FilmType,PortfolioId,area\r\n1,1,16,1,Cairo\r\n2,1,16,2,Giza, Dokki\r\n");
        var request = Request(fieldsPath, dataPath);
        var converter = new CsvProjectConverter();

        var preflight = converter.Validate(request, new ConversionSettings());

        var conflict = Assert.Single(preflight.Conflicts);
        Assert.Equal(2, conflict.RecordNumber);
        Assert.Contains(conflict.Issues, issue => issue.Code == "COLUMN_COUNT");
        Assert.Throws<ConversionException>(() => converter.Convert(request, new ConversionSettings()));
        Assert.False(Directory.Exists(Path.Combine(_root, Slug)));
    }

    [Fact]
    public void UnmappedFieldTypeStopsTheDepartmentUnlessTreatedAsText()
    {
        var (fieldsPath, dataPath) = CreateInput(
            FieldsHeader + "1,when,التاريخ,DATE,255,False,\r\n",
            "ID,FilmId,FilmType,PortfolioId,when\r\n1,1,16,1,1/27/2016 12:00:00 AM\r\n");

        Assert.Throws<ConversionException>(() => Convert(fieldsPath, dataPath));

        var result = new CsvProjectConverter().Convert(Request(fieldsPath, dataPath),
            new ConversionSettings { UnmappedFieldTypeAsText = true });
        Assert.Contains(result.AutoFixes!, fix => fix.Contains("REVIEW", StringComparison.Ordinal)
            && fix.Contains("'DATE'", StringComparison.Ordinal));
        var xml = File.ReadAllText(Path.Combine(result.OutputDirectory, "Project_Data.xml"), Encoding.UTF8);
        Assert.Contains("<when>1/27/2016 12:00:00 AM</when>", xml);
    }

    [Fact]
    public void RepeatedFieldNameWithoutAnArabicNameIsIgnoredButAnyOtherDuplicateStillFails()
    {
        var (fieldsPath, dataPath) = CreateInput(
            FieldsHeader + "1,addr,العنوان,NVARCHAR,255,False,\r\n2,addr,,NVARCHAR,255,False,\r\n",
            "ID,FilmId,FilmType,PortfolioId,addr\r\n1,1,16,1,Cairo\r\n");
        var result = Convert(fieldsPath, dataPath);
        Assert.Equal(1, result.FieldCount);
        Assert.Contains(result.AutoFixes!, fix => fix.Contains("REVIEW", StringComparison.Ordinal)
            && fix.Contains("'addr'", StringComparison.Ordinal));

        File.WriteAllText(fieldsPath, FieldsHeader + "1,addr,العنوان,NVARCHAR,255,False,\r\n2,addr,عنوان آخر,NVARCHAR,255,False,\r\n",
            new UTF8Encoding(true));
        Assert.Throws<ConversionException>(() => Convert(fieldsPath, dataPath));
    }

    // ---------------------------------------------------------------- helpers

    private string Write(string name, string content)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    private (string FieldsPath, string DataPath) CreateInput(string fields, string data)
    {
        var fieldsPath = Path.Combine(_root, $"{Slug}_fields.csv");
        var dataPath = Path.Combine(_root, $"{Slug}_data.csv");
        var encoding = new UTF8Encoding(true);
        File.WriteAllText(fieldsPath, fields, encoding);
        File.WriteAllText(dataPath, data, encoding);
        return (fieldsPath, dataPath);
    }

    private ConversionRequest Request(string fieldsPath, string dataPath) =>
        new(fieldsPath, dataPath, _root,
            new DepartmentEntry { Index = 1, Value = Slug, Text = "إدارة الاختبار" },
            OutputFolderNameMode.LegacySlug, Stamp);

    private DepartmentConversionResult Convert(string fieldsPath, string dataPath) =>
        new CsvProjectConverter().Convert(Request(fieldsPath, dataPath), new ConversionSettings());

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
