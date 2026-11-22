using System.Text;
using CsvProjectXml.Core.Models;
using CsvProjectXml.Core.Services;
using Xunit;

namespace CsvProjectXml.Core.Tests;

/// <summary>
/// Rules observed in real V1 output (list fields and lookup XML, field type codes, duplicate PHOTO_PATH, and the
/// way V1 handled extra cells and quotation marks), plus the opt-in settings that control the risky ones.
/// </summary>
public sealed class V1CompatibilityTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("CsvProjectXmlV1Tests_").FullName;
    private const string Slug = "v1_test_hyeh_altnmya_alsnaaya";
    private const string Stamp = "11/11/2026 09:52:32 AM";
    private const string FieldsHeader = "ID,FieldName,FieldNameAr,FieldType,FieldSize,NotNull,ListValues\r\n";

    // ---------------------------------------------------------------- lists and lookup XML

    [Fact]
    public void ListFieldsUseTypeFiveAndWriteTheV1LookupLayout()
    {
        // ListValues is a JSON array typed straight into the CSV: unquoted, with commas and quotes inside.
        var fields = FieldsHeader +
                     "1,a,A,NVARCHAR,255,False,\r\n" +
                     "2,fra,فرع,LIST,4000,False,[\"x\",\"y\"]\r\n" +
                     "3,b,B,LIST,4000,True,[\"p\",\"q\",\"r s\"]\r\n";
        var (fieldsPath, dataPath) = Write(fields, "ID,FilmId,FilmType,PortfolioId,a,fra,b\r\n1,1,16,1,t,x,p\r\n");

        var result = Convert(fieldsPath, dataPath, new ConversionSettings());

        var fieldsXml = File.ReadAllText(Path.Combine(result.OutputDirectory, "Project_Fields.xml"), Encoding.UTF8);
        Assert.Contains("<FIELD_NAME_ENG>fra</FIELD_NAME_ENG>\r\n\t\t<FIELD_NAME_AR>فرع</FIELD_NAME_AR>\r\n\t\t<FIELD_TYPE>5</FIELD_TYPE>\r\n\t\t<FIELD_WIDTH>4000</FIELD_WIDTH>", fieldsXml);

        // Verified against real V1 output: list fields in reverse CSV order, items in reverse list order,
        // FIELD_LOOKUP_ORDER = position in that reversed sequence.
        var expected = "<DocumentElement>" + string.Concat(new[]
            {
                ("b", 0, "r s"), ("b", 0, "q"), ("b", 0, "p"), ("fra", 1, "y"), ("fra", 1, "x")
            }.Select(e =>
                $"\r\n\t<Project_lookup_Fields>\r\n\t\t<FIELD_LOOKUP_ORDER>{e.Item2}</FIELD_LOOKUP_ORDER>\r\n\t\t<FIELD_NAME_ENG>{e.Item1}</FIELD_NAME_ENG>\r\n\t\t<PROJECT_CODE>444</PROJECT_CODE>\r\n\t\t<FIELD_LOOKUP_TEXT>{e.Item3}</FIELD_LOOKUP_TEXT>\r\n\t</Project_lookup_Fields>")) +
                       "\r\n</DocumentElement>";
        var actual = File.ReadAllText(Path.Combine(result.OutputDirectory, "Project_lookup_Fields.xml"), new UTF8Encoding(false));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void NoListValuesStillWritesTheEmptyLookupFile()
    {
        var (fieldsPath, dataPath) = Write(FieldsHeader + "1,a,A,NVARCHAR,255,False,\r\n",
            "ID,FilmId,FilmType,PortfolioId,a\r\n1,1,16,1,t\r\n");

        var result = Convert(fieldsPath, dataPath, new ConversionSettings());

        Assert.Equal("<DocumentElement/>", File.ReadAllText(Path.Combine(result.OutputDirectory, "Project_lookup_Fields.xml")));
    }

    [Fact]
    public void ListValuesWrappedInCsvQuotesByExcelAreAccepted()
    {
        var fields = FieldsHeader + "1,fra,فرع,LIST,4000,False,\"[\"\"x\"\",\"\"y\"\"]\"\r\n";
        var (fieldsPath, dataPath) = Write(fields, "ID,FilmId,FilmType,PortfolioId,fra\r\n1,1,16,1,x\r\n");

        var result = Convert(fieldsPath, dataPath, new ConversionSettings());

        var lookup = File.ReadAllText(Path.Combine(result.OutputDirectory, "Project_lookup_Fields.xml"));
        Assert.Contains("<FIELD_LOOKUP_TEXT>y</FIELD_LOOKUP_TEXT>", lookup);
        Assert.Contains("<FIELD_LOOKUP_TEXT>x</FIELD_LOOKUP_TEXT>", lookup);
        Assert.Contains(result.AutoFixes!, fix => fix.Contains("wrapped in CSV quotes", StringComparison.Ordinal));
    }

    [Fact]
    public void ListValuesThatAreNotAJsonArrayFailWithTheExactLocation()
    {
        var (fieldsPath, dataPath) = Write(FieldsHeader + "1,fra,فرع,LIST,4000,False,[x,y\r\n",
            "ID,FilmId,FilmType,PortfolioId,fra\r\n1,1,16,1,x\r\n");

        var error = Assert.Throws<ConversionException>(() => Convert(fieldsPath, dataPath, new ConversionSettings()));

        Assert.Equal("ListValues", error.ColumnName);
        Assert.Equal(2, error.LineNumber);
    }

    // ---------------------------------------------------------------- extra cells

    private (string Fields, string Data) ExtraCellInput() => Write(
        FieldsHeader + "1,area,المنطقة,NVARCHAR,200,False,\r\n2,num,الرقم,NVARCHAR,200,False,\r\n",
        "ID,FilmId,FilmType,PortfolioId,area,num\r\n1,4,16,4,محافظه قنا,407 , 410\r\n");

    [Fact]
    public void ExtraCellsAreHeldBackByDefault()
    {
        var (fieldsPath, dataPath) = ExtraCellInput();
        var preflight = new CsvProjectConverter().Validate(Request(fieldsPath, dataPath), new ConversionSettings());

        var conflict = Assert.Single(preflight.Conflicts);
        Assert.Contains(conflict.Issues, issue => issue.Code == "COLUMN_COUNT");
    }

    [Fact]
    public void ExtraCellsCanBeJoinedIntoTheLastColumnAndAreReported()
    {
        var (fieldsPath, dataPath) = ExtraCellInput();
        var settings = new ConversionSettings { ExtraColumnsMode = ExtraColumnsMode.MergeIntoLastColumn };

        var result = Convert(fieldsPath, dataPath, settings);

        var xml = File.ReadAllText(Path.Combine(result.OutputDirectory, "Project_Data.xml"), Encoding.UTF8);
        Assert.Contains("<num>407 , 410</num>", xml);
        Assert.Contains(result.AutoFixes!, fix => fix.Contains("REVIEW", StringComparison.Ordinal) && fix.Contains("joined", StringComparison.Ordinal));
    }

    [Fact]
    public void ExtraCellsCanBeDroppedLikeV1_AndTheDroppedTextIsReported()
    {
        var (fieldsPath, dataPath) = ExtraCellInput();
        var settings = new ConversionSettings { ExtraColumnsMode = ExtraColumnsMode.DropExtra };

        var result = Convert(fieldsPath, dataPath, settings);

        var xml = File.ReadAllText(Path.Combine(result.OutputDirectory, "Project_Data.xml"), Encoding.UTF8);
        Assert.Contains("<num>407 </num>", xml);
        Assert.Contains(result.AutoFixes!, fix => fix.Contains("dropped text:  410", StringComparison.Ordinal));
    }

    [Fact]
    public void MissingCellsAreStillHeldBackInEveryMode()
    {
        var (fieldsPath, dataPath) = Write(
            FieldsHeader + "1,area,المنطقة,NVARCHAR,200,False,\r\n2,num,الرقم,NVARCHAR,200,False,\r\n",
            "ID,FilmId,FilmType,PortfolioId,area,num\r\n1,4,16,4,محافظه قنا\r\n");
        var settings = ConversionSettings.V1Compatible();

        var preflight = new CsvProjectConverter().Validate(Request(fieldsPath, dataPath), settings);

        Assert.Contains(Assert.Single(preflight.Conflicts).Issues, issue => issue.Code == "COLUMN_COUNT");
    }

    // ---------------------------------------------------------------- quotation marks

    [Fact]
    public void QuotationMarksInsideValuesAreKeptByDefaultAndRemovedOnRequest()
    {
        var (fieldsPath, dataPath) = Write(FieldsHeader + "1,name,الاسم,NVARCHAR,200,False,\r\n",
            "ID,FilmId,FilmType,PortfolioId,name\r\n1,1,16,1,Ringer\"s Solution\r\n");

        var kept = Convert(fieldsPath, dataPath, new ConversionSettings());
        Assert.Contains("<name>Ringer\"s Solution</name>",
            File.ReadAllText(Path.Combine(kept.OutputDirectory, "Project_Data.xml"), Encoding.UTF8));

        var removed = Convert(fieldsPath, dataPath, new ConversionSettings { QuoteHandling = QuoteHandling.Remove },
            Path.Combine(_root, "removed"));
        Assert.Contains("<name>Ringers Solution</name>",
            File.ReadAllText(Path.Combine(removed.OutputDirectory, "Project_Data.xml"), Encoding.UTF8));
        Assert.Contains(removed.AutoFixes!, fix => fix.Contains("quotation mark", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------- duplicate PHOTO_PATH

    [Fact]
    public void SameFilmAndPortfolioWithADifferentFilmTypeIsNotADuplicate()
    {
        // This was reported as a conflict before; PHOTO_PATH differs because it contains FilmType.
        var (fieldsPath, dataPath) = Write(FieldsHeader + "1,name,الاسم,NVARCHAR,200,False,\r\n",
            "ID,FilmId,FilmType,PortfolioId,name\r\n1,1,16,1,a\r\n2,1,35,1,b\r\n");

        var preflight = new CsvProjectConverter().Validate(Request(fieldsPath, dataPath), new ConversionSettings());

        Assert.False(preflight.HasConflicts);
        Assert.DoesNotContain(preflight.AutoFixes!, fix => fix.Contains("PHOTO_PATH", StringComparison.Ordinal));
    }

    [Fact]
    public void IdenticalPhotoPathIsWrittenAsInV1ButReported_OrHeldBackOnRequest()
    {
        var (fieldsPath, dataPath) = Write(FieldsHeader + "1,name,الاسم,NVARCHAR,200,False,\r\n",
            "ID,FilmId,FilmType,PortfolioId,name\r\n1,81,16,57,first\r\n2,81,16,57,second\r\n");

        var result = Convert(fieldsPath, dataPath, new ConversionSettings());
        Assert.Equal(2, result.RowCount);
        Assert.Contains(result.AutoFixes!, fix => fix.Contains("PHOTO_PATH", StringComparison.Ordinal));

        var strict = new ConversionSettings { RejectDuplicatePhotoPaths = true };
        var preflight = new CsvProjectConverter().Validate(Request(fieldsPath, dataPath), strict);
        Assert.Contains(Assert.Single(preflight.Conflicts).Issues, issue => issue.Code == "DUPLICATE_PHOTO_PATH");
    }

    // ---------------------------------------------------------------- helpers

    private (string Fields, string Data) Write(string fields, string data)
    {
        var fieldsPath = Path.Combine(_root, $"{Slug}_fields.csv");
        var dataPath = Path.Combine(_root, $"{Slug}_data.csv");
        var encoding = new UTF8Encoding(true);
        File.WriteAllText(fieldsPath, fields, encoding);
        File.WriteAllText(dataPath, data, encoding);
        return (fieldsPath, dataPath);
    }

    private ConversionRequest Request(string fieldsPath, string dataPath, string? outputRoot = null) =>
        new(fieldsPath, dataPath, outputRoot ?? _root,
            new DepartmentEntry { Index = 1, Value = Slug, Text = "إدارة الاختبار" },
            OutputFolderNameMode.LegacySlug, Stamp);

    private DepartmentConversionResult Convert(string fieldsPath, string dataPath, ConversionSettings settings,
        string? outputRoot = null) =>
        new CsvProjectConverter().Convert(Request(fieldsPath, dataPath, outputRoot), settings);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
