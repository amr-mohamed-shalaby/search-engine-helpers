using System.Text;
using CsvProjectXml.Core.Models;
using CsvProjectXml.Core.Services;
using Xunit;

namespace CsvProjectXml.Core.Tests;

public sealed class ConverterTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("CsvProjectXmlTests_").FullName;
    private readonly CsvProjectConverter _converter = new();
    private const string Stamp = "11/11/2026 09:52:32 AM";
    private const string Slug = "test_department_hyeh_altnmya_alsnaaya";

    [Fact]
    public void ReadsUtf8BomAndPreservesCrLfAndTrailingTabs()
    {
        var input = CreateInput(
            "ID,FieldName,FieldNameAr,FieldType,FieldSize,NotNull,ListValues\r\n1,city,المدينة,NVARCHAR,100,True,\r\n",
            "ID,FilmId,FilmType,PortfolioId,city\r\n3,1,16,1,القاهرة\t \r\n17,1,16,2,الجيزة\t\r\n");

        var result = Convert(input);
        var bytes = File.ReadAllBytes(Path.Combine(result.OutputDirectory, "Project_Data.xml"));
        var xml = Encoding.UTF8.GetString(bytes);

        Assert.False(bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
        Assert.Contains("\r\n", xml);
        Assert.DoesNotContain("\n", xml.Replace("\r\n", string.Empty, StringComparison.Ordinal));
        Assert.Contains("<city>القاهرة\t </city>", xml);
        Assert.Contains("<city>الجيزة\t</city>", xml);
        Assert.EndsWith("</DocumentElement>", xml, StringComparison.Ordinal);
        Assert.False(xml.EndsWith("\r\n", StringComparison.Ordinal));
        Assert.Equal(2, result.RowCount);
    }

    [Fact]
    public void EscapesXmlTextButDoesNotEscapeQuotesOrArabic()
    {
        var input = CreateInput(
            "ID,FieldName,FieldNameAr,FieldType,FieldSize,NotNull,ListValues\r\n1,company,الشركة,NVARCHAR,100,True,\r\n",
            "ID,FilmId,FilmType,PortfolioId,company\r\n1,2,35,1,A&B <C> \"IDG\" 'مصر'\r\n");

        var result = Convert(input);
        var xml = File.ReadAllText(Path.Combine(result.OutputDirectory, "Project_Data.xml"), Encoding.UTF8);
        Assert.Contains("<company>A&amp;B &lt;C&gt; \"IDG\" 'مصر'</company>", xml);
    }

    [Fact]
    public void EmptyValuesUseExplicitPairedTagsByDefault()
    {
        var input = CreateInput(
            "ID,FieldName,FieldNameAr,FieldType,FieldSize,NotNull,ListValues\r\n1,city,المدينة,NVARCHAR,100,True,\r\n",
            "ID,FilmId,FilmType,PortfolioId,city\r\n1,1,16,1,\r\n");

        var result = Convert(input);
        var xml = File.ReadAllText(Path.Combine(result.OutputDirectory, "Project_Data.xml"), Encoding.UTF8);
        Assert.Contains("<city></city>", xml);
        Assert.DoesNotContain("<city/>", xml);
    }

    [Fact]
    public void IgnoredSourceIdsAndNotNullMetadataDoNotBlockTheLegacyDefaultPath()
    {
        var input = CreateInput(
            "ID,FieldName,FieldNameAr,FieldType,FieldSize,NotNull,ListValues\r\nnot-an-id,city,المدينة,NVARCHAR,100,not-a-boolean,\r\n",
            "ID,FilmId,FilmType,PortfolioId,city\r\nsource-id-is-opaque,1,16,1,Cairo\r\n");

        var result = Convert(input);

        Assert.Equal(1, result.RowCount);
        var xml = File.ReadAllText(Path.Combine(result.OutputDirectory, "Project_Data.xml"), Encoding.UTF8);
        Assert.Contains("<city>Cairo</city>", xml);
        Assert.DoesNotContain("source-id-is-opaque", xml);
    }

    [Fact]
    public void UnknownFieldTypeFailsWithExactFieldLocationAndDoesNotCommitOutput()
    {
        var input = CreateInput(
            "ID,FieldName,FieldNameAr,FieldType,FieldSize,NotNull,ListValues\r\n1,when,التاريخ,DATE,30,True,\r\n",
            "ID,FilmId,FilmType,PortfolioId,when\r\n1,1,16,1,2026-01-01\r\n");

        var error = Assert.Throws<ConversionException>(() => Convert(input));
        Assert.Equal(input.FieldsPath, error.FilePath);
        Assert.Equal(2, error.LineNumber);
        Assert.Equal("FieldType", error.ColumnName);
        Assert.False(Directory.Exists(Path.Combine(_root, Slug)));
    }

    [Fact]
    public void ExtraDataHeaderColumnIsRejected()
    {
        var input = CreateInput(
            "ID,FieldName,FieldNameAr,FieldType,FieldSize,NotNull,ListValues\r\n1,city,المدينة,NVARCHAR,100,True,\r\n",
            "ID,FilmId,FilmType,PortfolioId,city,EXTRA\r\n1,1,16,1,Cairo,x\r\n");

        var error = Assert.Throws<ConversionException>(() => Convert(input));
        Assert.Equal(input.DataPath, error.FilePath);
        Assert.Equal(1, error.LineNumber);
    }

    [Fact]
    public void MissingDataRowColumnIsRejected()
    {
        var input = CreateInput(
            "ID,FieldName,FieldNameAr,FieldType,FieldSize,NotNull,ListValues\r\n1,city,المدينة,NVARCHAR,100,True,\r\n",
            "ID,FilmId,FilmType,PortfolioId,city\r\n1,1,16\r\n");

        var error = Assert.Throws<ConversionException>(() => Convert(input));
        Assert.Equal(input.DataPath, error.FilePath);
        Assert.Equal(2, error.LineNumber);
        Assert.Equal("PortfolioId", error.ColumnName);
    }

    [Fact]
    public void SourceIdGapsAreIgnoredAndRowsRemainInOriginalOrder()
    {
        var input = CreateInput(
            "ID,FieldName,FieldNameAr,FieldType,FieldSize,NotNull,ListValues\r\n1,city,المدينة,NVARCHAR,100,True,\r\n",
            "ID,FilmId,FilmType,PortfolioId,city\r\n3,1,16,1,first\r\n17,2,16,1,second\r\n5,1,16,2,third\r\n");

        var result = Convert(input);
        var xml = File.ReadAllText(Path.Combine(result.OutputDirectory, "Project_Data.xml"), Encoding.UTF8);
        Assert.Equal(3, result.RowCount);
        Assert.True(xml.IndexOf("<city>first</city>", StringComparison.Ordinal) < xml.IndexOf("<city>second</city>", StringComparison.Ordinal));
        Assert.True(xml.IndexOf("<city>second</city>", StringComparison.Ordinal) < xml.IndexOf("<city>third</city>", StringComparison.Ordinal));
        Assert.DoesNotContain("<ID>", xml);
    }

    [Fact]
    public void DuplicateFilmPortfolioPairIsRejectedAndNoPartialFolderIsWritten()
    {
        var input = CreateInput(
            "ID,FieldName,FieldNameAr,FieldType,FieldSize,NotNull,ListValues\r\n1,city,المدينة,NVARCHAR,100,True,\r\n",
            "ID,FilmId,FilmType,PortfolioId,city\r\n1,1,16,1,first\r\n2,1,16,1,duplicate\r\n");

        var error = Assert.Throws<ConversionException>(() => Convert(input));
        Assert.Equal(3, error.LineNumber);
        Assert.Equal("FilmId/PortfolioId", error.ColumnName);
        Assert.False(Directory.Exists(Path.Combine(_root, Slug)));
    }

    [Fact]
    public void ArabicOutputFolderKeepsFrankoNameInsideProjectXml()
    {
        var input = CreateInput(
            "ID,FieldName,FieldNameAr,FieldType,FieldSize,NotNull,ListValues\r\n1,city,المدينة,NVARCHAR,100,True,\r\n",
            "ID,FilmId,FilmType,PortfolioId,city\r\n1,1,16,1,Cairo\r\n");
        var department = new DepartmentEntry { Index = 1, Value = Slug, Text = "الإدارة العامة للاختبار" };
        var result = _converter.Convert(new ConversionRequest(input.FieldsPath, input.DataPath, _root,
            department, OutputFolderNameMode.ArabicDisplayName, Stamp), new ConversionSettings());
        var projectXml = File.ReadAllText(Path.Combine(result.OutputDirectory, "Project.xml"), Encoding.UTF8);
        Assert.EndsWith("الإدارة العامة للاختبار", result.OutputDirectory, StringComparison.Ordinal);
        Assert.Contains($"<PROJECT_NAME>{Slug}</PROJECT_NAME>", projectXml);
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

    private DepartmentConversionResult Convert((string FieldsPath, string DataPath) input)
    {
        return _converter.Convert(new ConversionRequest(input.FieldsPath, input.DataPath, _root,
            new DepartmentEntry { Index = 1, Value = Slug, Text = "إدارة الاختبار" },
            OutputFolderNameMode.LegacySlug, Stamp), new ConversionSettings());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
