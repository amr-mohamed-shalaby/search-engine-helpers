using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using CsvProjectXml.Core.Models;

namespace CsvProjectXml.Core.Services;

public sealed class ConversionReportWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public string SaveSingle(string outputRoot, DepartmentConversionResult result,
        string fieldsCsvPath, string dataCsvPath)
    {
        var report = new
        {
            GeneratedAtLocal = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
            Mode = "Single",
            Input = new { FieldsCsv = fieldsCsvPath, DataCsv = dataCsvPath },
            Department = result
        };
        return Save(outputRoot, report);
    }

    public string SaveBatch(string outputRoot, BatchConversionSummary result)
    {
        var report = new
        {
            GeneratedAtLocal = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
            Mode = "Batch",
            Summary = new
            {
                result.ConvertedCount,
                result.FailedCount,
                result.IncompleteCount,
                result.MissingCount,
                OrphanCsvCount = result.OrphanCsvFiles.Count
            },
            Departments = result.Departments,
            OrphanCsvFiles = result.OrphanCsvFiles
        };
        return Save(outputRoot, report);
    }

    private static string Save(string outputRoot, object report)
    {
        Directory.CreateDirectory(outputRoot);
        var fileName = $"conversion-report-{DateTime.Now:yyyyMMdd-HHmmssfff}.json";
        var target = Path.Combine(outputRoot, fileName);
        var temp = target + ".tmp";
        try
        {
            var json = JsonSerializer.Serialize(report, Options);
            File.WriteAllText(temp, json, new UTF8Encoding(false));
            File.Move(temp, target, overwrite: true);
            return target;
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
