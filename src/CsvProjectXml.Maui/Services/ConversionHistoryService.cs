using System.Text.Json;
using Microsoft.Maui.Storage;

namespace CsvProjectXml.Maui.Services;

/// <summary>
/// Persists conversion attempts under the app-private data directory.
/// Each attempt is a separate JSON file, so one damaged record cannot corrupt the log.
/// </summary>
public sealed class ConversionHistoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public string HistoryDirectory =>
        Path.Combine(FileSystem.AppDataDirectory, "converting_history");

    public async Task<string> SaveAsync(
        ConversionHistoryEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Directory.CreateDirectory(HistoryDirectory);

        var fileName =
            $"conversion_{entry.StartedAt:yyyyMMdd_HHmmss}_{entry.Id:N}.json";
        var destination = Path.Combine(HistoryDirectory, fileName);
        var temporary = destination + ".tmp";

        try
        {
            await using (var stream = new FileStream(
                temporary,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(
                    stream, entry, JsonOptions, cancellationToken);

                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporary, destination, overwrite: true);
            return destination;
        }
        catch
        {
            try
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
            catch
            {
                // Preserve the original persistence exception.
            }

            throw;
        }
    }

    public IReadOnlyList<ConversionHistoryEntry> ReadAll()
    {
        if (!Directory.Exists(HistoryDirectory))
            return [];

        var entries = new List<ConversionHistoryEntry>();

        foreach (var file in Directory.EnumerateFiles(
                     HistoryDirectory,
                     "conversion_*.json",
                     SearchOption.TopDirectoryOnly))
        {
            try
            {
                var json = File.ReadAllText(file);
                var entry =
                    JsonSerializer.Deserialize<ConversionHistoryEntry>(
                        json, JsonOptions);

                if (entry is not null)
                    entries.Add(entry);
            }
            catch (JsonException)
            {
                // Ignore an individual malformed record.
            }
            catch (IOException)
            {
                // A file may be temporarily unavailable.
            }
            catch (UnauthorizedAccessException)
            {
                // Skip files that cannot be accessed.
            }
        }

        return entries
            .OrderByDescending(x => x.CompletedAt)
            .ToArray();
    }
}
