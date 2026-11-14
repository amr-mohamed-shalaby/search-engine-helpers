using System.Text.Json.Serialization;

namespace CsvProjectXml.Core.Models;

public sealed class DepartmentEntry
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonIgnore]
    public string DisplayName => Text.Trim();
}
