using CsvProjectXml.Core.Models;

namespace CsvProjectXml.Core.Services;

public sealed record ResolvedDepartment(DepartmentEntry Department, string FieldsCsvPath, string DataCsvPath);

public static class SingleProjectResolver
{
    public static ResolvedDepartment Resolve(string fieldsCsvPath, string dataCsvPath,
        IReadOnlyList<DepartmentEntry> departments)
    {
        var fieldsSlug = ExtractSlug(fieldsCsvPath, "_fields.csv");
        var dataSlug = ExtractSlug(dataCsvPath, "_data.csv");
        if (!string.Equals(fieldsSlug, dataSlug, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"The selected files do not belong to the same department: '{fieldsSlug}' vs '{dataSlug}'.");

        var entry = departments.FirstOrDefault(x => string.Equals(x.Value, fieldsSlug, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
            throw new InvalidOperationException($"No names.json entry matches slug '{fieldsSlug}'. Check the CSV filenames and names.json.");
        return new ResolvedDepartment(entry, fieldsCsvPath, dataCsvPath);
    }

    private static string ExtractSlug(string path, string suffix)
    {
        var name = Path.GetFileName(path);
        if (!name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Expected a file ending with '{suffix}', but selected '{name}'.");
        var slug = name[..^suffix.Length];
        if (slug.Length == 0) throw new InvalidOperationException($"Could not infer department slug from '{name}'.");
        return slug;
    }
}
