using Xunit;

namespace CsvProjectXml.Core.Tests;

public sealed class GoldenFactAttribute : FactAttribute
{
    public GoldenFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CSV_XML_GOLDEN_DIR")))
            Skip = "Set CSV_XML_GOLDEN_DIR to the original V1 CSV/XML fixture directory.";
    }
}
