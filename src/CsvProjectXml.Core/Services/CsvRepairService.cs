using System.Text;
using CsvProjectXml.Core.Models;

namespace CsvProjectXml.Core.Services;

/// <summary>
/// Writes a repaired copy of a CSV. The source file is opened read-only and is never modified.
/// Only records included in <paramref name="corrections"/> are changed; all other cell values
/// are round-tripped without trimming or Unicode normalization.
/// </summary>
public sealed class CsvRepairService
{
    public string WriteCorrectedCopy(
        string sourcePath,
        string destinationPath,
        IReadOnlyDictionary<long, IReadOnlyList<string>> corrections)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(corrections);
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("The source CSV was not found.", sourcePath);
        if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(destinationPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("A repaired CSV must be written to a separate path; the original file is read-only.");

        var destinationDirectory = Path.GetDirectoryName(Path.GetFullPath(destinationPath));
        if (destinationDirectory is not null) Directory.CreateDirectory(destinationDirectory);

        var applied = new HashSet<long>();
        using var writer = new StreamWriter(destinationPath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true))
        {
            NewLine = "\r\n"
        };

        long recordNumber = 0;
        var isHeader = true;
        foreach (var record in CsvReader.Read(sourcePath))
        {
            IReadOnlyList<string> values = record.Values;
            if (!isHeader)
            {
                recordNumber++;
                if (corrections.TryGetValue(recordNumber, out var correctedValues))
                {
                    values = correctedValues;
                    applied.Add(recordNumber);
                }
            }

            WriteRecord(writer, values);
            isHeader = false;
        }

        var missing = corrections.Keys.Except(applied).Order().ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"Could not apply corrections: CSV data record(s) {string.Join(", ", missing)} no longer exist.");

        return destinationPath;
    }

    private static void WriteRecord(TextWriter writer, IReadOnlyList<string> values)
    {
        for (var index = 0; index < values.Count; index++)
        {
            if (index > 0) writer.Write(',');
            var value = values[index] ?? string.Empty;
            var requiresQuotes = value.Contains(',') || value.Contains('"') || value.Contains('\r') || value.Contains('\n');
            if (!requiresQuotes)
            {
                writer.Write(value);
                continue;
            }

            writer.Write('"');
            writer.Write(value.Replace("\"", "\"\"", StringComparison.Ordinal));
            writer.Write('"');
        }
        writer.Write("\r\n");
    }
}
