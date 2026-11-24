using System.Globalization;
using System.Xml;

namespace CsvProjectXml.Core.Services;

/// <summary>A run of consecutive film numbers that are absent from the data (From..To inclusive).</summary>
public sealed record FilmGap(int From, int To)
{
    public int Count => To - From + 1;
    public override string ToString() => From == To ? From.ToString(CultureInfo.InvariantCulture) : $"{From}–{To}";
}

public sealed record BarcodeEntry(string Value, int FilmNumber);

public sealed record BarcodeDuplicate(string Value, IReadOnlyList<int> FilmNumbers);

/// <summary>Film presence and barcode facts read back from a generated Project_Data.xml.</summary>
public sealed record ProjectDataAnalysis(
    int NodeCount,
    int FilmCount,
    int MaxFilmNumber,
    IReadOnlyList<FilmGap> MissingFilms,
    int NonNumericFilmNumbers,
    string? BarcodeElement,
    IReadOnlyList<BarcodeEntry> Barcodes)
{
    public int MissingFilmCount => MissingFilms.Sum(gap => gap.Count);
    public bool HasBarcodes => BarcodeElement is not null;

    /// <summary>Barcodes used by more than one record (compared by barcode value, not by the whole record).</summary>
    public IReadOnlyList<BarcodeDuplicate> DuplicateBarcodes() => Barcodes
        .Where(entry => entry.Value.Length > 0)
        .GroupBy(entry => entry.Value, StringComparer.Ordinal)
        .Where(group => group.Count() > 1)
        .Select(group => new BarcodeDuplicate(group.Key, group.Select(e => e.FilmNumber).Distinct().Order().ToArray()))
        .ToArray();

    /// <summary>Records whose barcode is empty or does not start with <paramref name="prefix"/>.</summary>
    public IReadOnlyList<BarcodeEntry> BarcodesNotStartingWith(string prefix) => Barcodes
        .Where(entry => !entry.Value.StartsWith(prefix, StringComparison.Ordinal))
        .ToArray();
}

public static class ProjectDataAnalyzer
{
    /// <summary>
    /// Streams the file, so large departments stay cheap. Film numbers are expected to be 1..max with no holes;
    /// holes are reported as ranges rather than one line per film.
    /// </summary>
    public static ProjectDataAnalysis Analyze(string projectDataXmlPath)
    {
        var films = new HashSet<int>();
        var barcodes = new List<BarcodeEntry>();
        string? barcodeElement = null;
        var nodes = 0;
        var nonNumeric = 0;

        using var reader = XmlReader.Create(projectDataXmlPath, new XmlReaderSettings { IgnoreWhitespace = true });
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.Name != "Data" || reader.Depth != 1) continue;

            nodes++;
            int? film = null;
            string? barcode = null;
            using var node = reader.ReadSubtree();
            node.Read();
            while (node.Read())
            {
                if (node.NodeType != XmlNodeType.Element || node.Depth != 1) continue;
                var name = node.Name;
                var isEmpty = node.IsEmptyElement;
                var text = isEmpty ? string.Empty : node.ReadElementContentAsString();
                if (name == "FILM_NUMBER")
                {
                    if (int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var number)) film = number;
                    else nonNumeric++;
                }
                else if (string.Equals(name, "barcode", StringComparison.OrdinalIgnoreCase))
                {
                    barcodeElement ??= name;
                    barcode = text.Trim();
                }
            }

            if (film is { } value) films.Add(value);
            if (barcode is not null) barcodes.Add(new BarcodeEntry(barcode, film ?? 0));
        }

        var max = films.Count == 0 ? 0 : films.Max();
        var gaps = new List<FilmGap>();
        int? gapStart = null;
        for (var number = 1; number <= max; number++)
        {
            if (!films.Contains(number)) gapStart ??= number;
            else if (gapStart is { } start)
            {
                gaps.Add(new FilmGap(start, number - 1));
                gapStart = null;
            }
        }

        return new ProjectDataAnalysis(nodes, films.Count, max, gaps, nonNumeric, barcodeElement, barcodes);
    }
}
