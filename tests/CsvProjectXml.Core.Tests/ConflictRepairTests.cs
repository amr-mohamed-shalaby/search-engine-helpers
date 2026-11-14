using System.Text;
using CsvProjectXml.Core.Models;
using CsvProjectXml.Core.Services;
using Xunit;

namespace CsvProjectXml.Core.Tests;

public sealed class ConflictRepairTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("CsvProjectXmlRepairTests_").FullName;
    private const string Slug = "repair_test_hyeh_altnmya_alsnaaya";

    [Fact]
    public void PreflightCollectsExtraColumnAndRepairWritesNewCsvWithoutChangingOriginal()
    {
        var fieldsPath = Path.Combine(_root, $"{Slug}_fields.csv");
        var dataPath = Path.Combine(_root, $"{Slug}_data.csv");
        var fields = "ID,FieldName,FieldNameAr,FieldType,FieldSize,NotNull,ListValues\r\n1,area,المنطقة,NVARCHAR,200,True,\r\n";
        var data = "ID,FilmId,FilmType,PortfolioId,area\r\n105,18,16,4,العبور الصناعية ب , ج صناعات صغيرة\r\n";
        var encoding = new UTF8Encoding(true);
        File.WriteAllText(fieldsPath, fields, encoding);
        File.WriteAllText(dataPath, data, encoding);
        var originalBytes = File.ReadAllBytes(dataPath);

        var department = new DepartmentEntry { Index = 1, Value = Slug, Text = "إدارة اختبار" };
        var request = new ConversionRequest(fieldsPath, dataPath, _root, department,
            OutputFolderNameMode.LegacySlug, "11/11/2026 09:52:32 AM");
        var converter = new CsvProjectConverter();
        var preflight = converter.Validate(request, new ConversionSettings());

        Assert.True(preflight.HasConflicts);
        var conflict = Assert.Single(preflight.Conflicts);
        Assert.Equal(1, conflict.RecordNumber);
        Assert.Equal(2, conflict.StartLine);
        Assert.Contains(conflict.Issues, issue => issue.Code == "COLUMN_COUNT");
        Assert.Equal(6, conflict.Cells.Count);

        var repairedPath = Path.Combine(_root, "repaired", "data.csv");
        new CsvRepairService().WriteCorrectedCopy(dataPath, repairedPath,
            new Dictionary<long, IReadOnlyList<string>>
            {
                [1] = ["105", "18", "16", "4", "العبور الصناعية ب , ج صناعات صغيرة"]
            });

        Assert.Equal(originalBytes, File.ReadAllBytes(dataPath));
        var repairedRequest = request with { DataCsvPath = repairedPath };
        Assert.True(converter.Validate(repairedRequest, new ConversionSettings()).IsValid);
        var result = converter.Convert(repairedRequest, new ConversionSettings());
        var xml = File.ReadAllText(Path.Combine(result.OutputDirectory, "Project_Data.xml"), Encoding.UTF8);
        Assert.Contains("<area>العبور الصناعية ب , ج صناعات صغيرة</area>", xml);
    }

    [Fact]
    public void PreflightCollectsMultipleRowConflictsInsteadOfStoppingAtFirst()
    {
        var fieldsPath = Path.Combine(_root, $"{Slug}_fields.csv");
        var dataPath = Path.Combine(_root, $"{Slug}_data.csv");
        File.WriteAllText(fieldsPath,
            "ID,FieldName,FieldNameAr,FieldType,FieldSize,NotNull,ListValues\r\n1,area,المنطقة,NVARCHAR,200,True,\r\n",
            new UTF8Encoding(true));
        File.WriteAllText(dataPath,
            "ID,FilmId,FilmType,PortfolioId,area\r\n1,1,16,1,first,extra\r\n2,not-number,16,2,second\r\n",
            new UTF8Encoding(true));

        var request = new ConversionRequest(fieldsPath, dataPath, _root,
            new DepartmentEntry { Index = 1, Value = Slug, Text = "إدارة اختبار" });
        var result = new CsvProjectConverter().Validate(request, new ConversionSettings());

        Assert.Equal(2, result.Conflicts.Count);
        Assert.Contains(result.Conflicts[0].Issues, issue => issue.Code == "COLUMN_COUNT");
        Assert.Contains(result.Conflicts[1].Issues, issue => issue.Code == "INVALID_FILM_ID");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
