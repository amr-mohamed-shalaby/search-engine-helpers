using System.Text.Json;
using CsvProjectXml.Core.Models;
using CsvProjectXml.Core.Services;
using Xunit;

namespace CsvProjectXml.Core.Tests;

public sealed class GoldenCompatibilityTests
{
    [GoldenFact]
    public async Task AllSuppliedV1XmlFilesAreByteIdentical()
    {
        var goldenRoot = Environment.GetEnvironmentVariable("CSV_XML_GOLDEN_DIR")!;

        var namesPath = Path.Combine(goldenRoot, "names.json");
        var stampsPath = Path.Combine(goldenRoot, "golden-stamps.json");
        var expectedRoot = Path.Combine(goldenRoot, "expected");
        Assert.True(File.Exists(namesPath), $"Missing {namesPath}");
        Assert.True(File.Exists(stampsPath), $"Missing {stampsPath}");

        var names = await new NamesRegistryService().LoadAsync(namesPath);
        var stamps = JsonSerializer.Deserialize<Dictionary<string, string>>(await File.ReadAllTextAsync(stampsPath))
                     ?? throw new InvalidDataException("golden-stamps.json is invalid.");
        var outputRoot = Directory.CreateTempSubdirectory("CsvProjectXmlGolden_").FullName;
        try
        {
            var converter = new CsvProjectConverter();
            // V1 dropped extra cells and removed quotation marks; verify against its output with the same behavior.
            var settings = ConversionSettings.V1Compatible();
            var compared = 0;
            foreach (var department in names)
            {
                var fields = Path.Combine(goldenRoot, $"{department.Value}_fields.csv");
                var data = Path.Combine(goldenRoot, $"{department.Value}_data.csv");
                var expectedDirectory = Path.Combine(expectedRoot, department.Value);
                if (!File.Exists(fields) && !File.Exists(data) && !Directory.Exists(expectedDirectory)) continue;
                Assert.True(File.Exists(fields), $"Missing fields CSV: {fields}");
                Assert.True(File.Exists(data), $"Missing data CSV: {data}");
                Assert.True(Directory.Exists(expectedDirectory), $"Missing expected XML directory: {expectedDirectory}");
                Assert.True(stamps.TryGetValue(department.Value, out var stamp), $"No fixed timestamp for {department.Value}");

                var result = converter.Convert(new ConversionRequest(fields, data, outputRoot,
                    department, OutputFolderNameMode.LegacySlug, stamp), settings);
                foreach (var fileName in new[] { "Project.xml", "Project_Fields.xml", "Project_Data.xml", "Project_lookup_Fields.xml" })
                {
                    var actual = File.ReadAllBytes(Path.Combine(result.OutputDirectory, fileName));
                    var expected = File.ReadAllBytes(Path.Combine(expectedDirectory, fileName));
                    Assert.True(actual.SequenceEqual(expected), $"Byte mismatch: {department.Value}/{fileName}");
                }
                compared++;
            }
            Assert.True(compared > 0, "No complete golden department pair was found in the configured fixture directory.");
        }
        finally
        {
            if (Directory.Exists(outputRoot)) Directory.Delete(outputRoot, recursive: true);
        }
    }
}
