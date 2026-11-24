using System.Text;
using CsvProjectXml.Core.Services;
using Xunit;

namespace CsvProjectXml.Core.Tests;

public sealed class ProjectDataAnalyzerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("CsvProjectXmlAnalyzerTests_").FullName;

    private string Xml(params (string Film, string? Barcode)[] rows)
    {
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<DocumentElement>\n");
        foreach (var (film, barcode) in rows)
        {
            sb.Append($"\t<Data>\n\t\t<FILM_NUMBER>{film}</FILM_NUMBER>\n\t\t<FILM_TYPE>16</FILM_TYPE>\n");
            if (barcode is not null) sb.Append($"\t\t<barcode>{barcode}</barcode>\n");
            sb.Append("\t</Data>\n");
        }

        sb.Append("</DocumentElement>\n");
        var path = Path.Combine(_root, "Project_Data.xml");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        return path;
    }

    [Fact]
    public void ReportsNodesFilmsAndMissingRanges()
    {
        var result = ProjectDataAnalyzer.Analyze(Xml(("1", null), ("2", null), ("2", null), ("5", null), ("6", null), ("9", null)));

        Assert.Equal(6, result.NodeCount);
        Assert.Equal(5, result.FilmCount);
        Assert.Equal(9, result.MaxFilmNumber);
        Assert.Equal(["3–4", "7–8"], result.MissingFilms.Select(g => g.ToString()).ToArray());
        Assert.Equal(4, result.MissingFilmCount);
        Assert.False(result.HasBarcodes);
    }

    [Fact]
    public void ContinuousFilmsHaveNoGaps()
    {
        var result = ProjectDataAnalyzer.Analyze(Xml(("1", null), ("2", null), ("3", null)));
        Assert.Empty(result.MissingFilms);
    }

    [Fact]
    public void BarcodeDuplicatesAreFoundByValueAndPrefixIsChecked()
    {
        var result = ProjectDataAnalyzer.Analyze(Xml(("1", "AB100"), ("2", "AB100"), ("3", "AB200"), ("4", "ZZ300")));

        Assert.True(result.HasBarcodes);
        var duplicate = Assert.Single(result.DuplicateBarcodes());
        Assert.Equal("AB100", duplicate.Value);
        Assert.Equal([1, 2], duplicate.FilmNumbers.ToArray());
        var wrong = Assert.Single(result.BarcodesNotStartingWith("AB"));
        Assert.Equal("ZZ300", wrong.Value);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
