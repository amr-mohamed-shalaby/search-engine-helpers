using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using CsvProjectXml.Core.Models;

namespace CsvProjectXml.Core.Services;

public sealed class NamesRegistryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = false
    };

    private static readonly Regex ValidSlug = new("^[A-Za-z0-9_]+$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public async Task<IReadOnlyList<DepartmentEntry>> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("names.json was not found.", path);

        // Read through StreamReader so a UTF-8 BOM, when present, is consumed safely.
        var json = await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken);
        var entries = JsonSerializer.Deserialize<List<DepartmentEntry>>(json, JsonOptions)
                      ?? throw new InvalidDataException("names.json contains null instead of a JSON array.");
        Validate(entries, path);
        return entries.OrderBy(x => x.Index).ToArray();
    }

    public async Task SaveAsync(string path, IReadOnlyCollection<DepartmentEntry> entries,
        CancellationToken cancellationToken = default)
    {
        Validate(entries, path);
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException("Invalid names.json path.");
        Directory.CreateDirectory(directory);
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             32 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, entries.OrderBy(x => x.Index).ToArray(), JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            if (File.Exists(fullPath))
                File.Move(tempPath, fullPath, overwrite: true);
            else
                File.Move(tempPath, fullPath);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    public DepartmentEntry Create(string arabicName, IReadOnlyCollection<DepartmentEntry> existing)
    {
        var text = arabicName.Trim();
        if (text.Length == 0) throw new ArgumentException("The department name cannot be empty.", nameof(arabicName));
        var slug = DepartmentSlugGenerator.Generate(text);
        if (existing.Any(x => string.Equals(x.Value, slug, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"The generated slug '{slug}' already exists. Choose another name or edit the slug.");

        return new DepartmentEntry
        {
            Index = existing.Count == 0 ? 1 : existing.Max(x => x.Index) + 1,
            Value = slug,
            Text = text
        };
    }

    public static void Validate(IReadOnlyCollection<DepartmentEntry> entries, string? path = null)
    {
        var indices = new HashSet<int>();
        var slugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (entry.Index < 1 || !indices.Add(entry.Index))
                throw new InvalidDataException($"{path ?? "names.json"}: index values must be positive and unique (problem at index {entry.Index}).");
            if (string.IsNullOrWhiteSpace(entry.Value) || !ValidSlug.IsMatch(entry.Value))
                throw new InvalidDataException($"{path ?? "names.json"}: '{entry.Value}' is not a valid ASCII department slug.");
            if (!slugs.Add(entry.Value))
                throw new InvalidDataException($"{path ?? "names.json"}: duplicate slug '{entry.Value}'.");
            if (string.IsNullOrWhiteSpace(entry.Text))
                throw new InvalidDataException($"{path ?? "names.json"}: department text for slug '{entry.Value}' cannot be empty.");
        }
    }
}
