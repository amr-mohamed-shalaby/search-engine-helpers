using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;
using CsvProjectXml.Core.Models;

namespace CsvProjectXml.Core.Services;

public sealed class ConversionSettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<ConversionSettings> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) return new ConversionSettings();
        await using var stream = File.OpenRead(path);
        var settings = await JsonSerializer.DeserializeAsync<ConversionSettings>(stream, Options, cancellationToken)
                       ?? new ConversionSettings();
        settings.FieldTypeMap ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["NVARCHAR"] = 1 };
        settings.EnabledFeatureIds ??= [];
        return settings;
    }

    public async Task SaveAsync(string path, ConversionSettings settings, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        var temp = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             16 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, settings, Options, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            File.Move(temp, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
