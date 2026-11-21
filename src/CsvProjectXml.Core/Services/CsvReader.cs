using System.Text;

namespace CsvProjectXml.Core.Services;

/// <summary>
/// One CSV record. <see cref="Notes"/> lists every automatic, lossless correction the reader
/// applied while producing this record (for example "a stray quote was kept as a literal character").
/// </summary>
public sealed record CsvRecord(IReadOnlyList<string> Values, int StartLine)
{
    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Tolerant CSV reader for hand-typed data. For input that is valid RFC-4180 CSV the result is
/// identical to a strict reader. For malformed input it recovers the record structure without
/// ever changing cell text, and describes each recovery in <see cref="CsvRecord.Notes"/>:
///
/// * a quote only opens a quoted cell at the very start of a cell, and only closes it when followed
///   by a delimiter or end of line; any other quote is kept as a literal character;
/// * a quote that never closes (or swallows an unreasonable number of lines) is re-read as a
///   literal character, so one stray quote can no longer swallow the rest of the file;
/// * the header width is used as an oracle: if a record that spans lines has the wrong number of
///   cells, the record is re-read without multi-line quotes and that reading is used if it fits;
/// * extra trailing empty cells (",,,") and rows made only of empty cells are dropped;
/// * encoding: UTF-8 (with or without BOM), UTF-16 with BOM, otherwise Windows-1256;
/// * delimiter: comma, or semicolon / tab when the header line contains no comma.
/// </summary>
public static class CsvReader
{
    private const int MaxQuotedLineBreaks = 50;

    public static IEnumerable<CsvRecord> Read(string path)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 64 * 1024, FileOptions.SequentialScan);

        try
        {
            var fileNotes = new List<string>();
            var (encoding, bomLength) = DetectEncoding(stream, fileNotes);
            stream.Position = bomLength;
            using var reader = new StreamReader(stream, encoding,
                detectEncodingFromByteOrderMarks: false, bufferSize: 64 * 1024, leaveOpen: false);

            foreach (var record in Parse(reader, fileNotes))
                yield return record;
        }
        finally
        {
            stream.Dispose();
        }
    }

    // ---------------------------------------------------------------- encoding

    private static (Encoding Encoding, int BomLength) DetectEncoding(FileStream stream, List<string> notes)
    {
        var signature = new byte[4];
        var read = stream.Read(signature);

        if (read >= 4 && ((signature[0] == 0x00 && signature[1] == 0x00 && signature[2] == 0xFE && signature[3] == 0xFF) ||
                          (signature[0] == 0xFF && signature[1] == 0xFE && signature[2] == 0x00 && signature[3] == 0x00)))
            throw new InvalidDataException("CSV must be UTF-8 or UTF-16 (or Windows-1256), not UTF-32.");

        if (read >= 3 && signature[0] == 0xEF && signature[1] == 0xBB && signature[2] == 0xBF)
            return (new UTF8Encoding(false, true), 3);

        if (read >= 2 && signature[0] == 0xFF && signature[1] == 0xFE)
        {
            notes.Add("file encoding is UTF-16 (little endian); it was decoded as UTF-16.");
            return (new UnicodeEncoding(false, false, true), 2);
        }

        if (read >= 2 && signature[0] == 0xFE && signature[1] == 0xFF)
        {
            notes.Add("file encoding is UTF-16 (big endian); it was decoded as UTF-16.");
            return (new UnicodeEncoding(true, false, true), 2);
        }

        if (IsValidUtf8(stream))
            return (new UTF8Encoding(false, true), 0);

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        notes.Add("file is not valid UTF-8; it was decoded as Windows-1256 (Arabic). Check that the text looks right.");
        return (Encoding.GetEncoding(1256), 0);
    }

    private static bool IsValidUtf8(FileStream stream)
    {
        stream.Position = 0;
        var decoder = new UTF8Encoding(false, true).GetDecoder();
        var bytes = new byte[64 * 1024];
        var chars = new char[bytes.Length + 4];
        try
        {
            int count;
            while ((count = stream.Read(bytes, 0, bytes.Length)) > 0)
                decoder.GetChars(bytes, 0, count, chars, 0, flush: false);
            decoder.GetChars(bytes, 0, 0, chars, 0, flush: true);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    // ---------------------------------------------------------------- source with push-back

    /// <summary>Character source that records what it handed out and can give characters back.</summary>
    private sealed class Source(TextReader reader)
    {
        private readonly List<char> _pushback = [];
        public StringBuilder Recorded { get; } = new();

        public int Read()
        {
            int c;
            if (_pushback.Count > 0)
            {
                c = _pushback[^1];
                _pushback.RemoveAt(_pushback.Count - 1);
            }
            else c = reader.Read();

            if (c != -1) Recorded.Append((char)c);
            return c;
        }

        public int Peek() => _pushback.Count > 0 ? _pushback[^1] : reader.Peek();

        /// <summary>Gives back <paramref name="text"/>, which must be the tail of <see cref="Recorded"/>.</summary>
        public void Unread(string text)
        {
            for (var i = text.Length - 1; i >= 0; i--) _pushback.Add(text[i]);
            Recorded.Length -= text.Length;
        }
    }

    // ---------------------------------------------------------------- parsing

    private static IEnumerable<CsvRecord> Parse(TextReader reader, List<string> fileNotes)
    {
        var source = new Source(reader);
        var delimiter = SniffDelimiter(source, fileNotes);
        var line = 1;
        var expectedWidth = 0;
        var first = true;

        while (true)
        {
            source.Recorded.Clear();
            var notes = new List<string>();
            if (first) notes.AddRange(fileNotes);

            var cells = ParseRecord(source, delimiter, allowMultiline: true, line,
                out var startLine, out var endLine, out var usedMultiline, notes);
            if (cells is null) yield break;

            TrimTrailingEmptyCells(cells, expectedWidth, startLine, notes);

            if (expectedWidth > 0 && cells.Count != expectedWidth && usedMultiline)
            {
                // A quoted cell swallowed line breaks and the record has the wrong width: try the
                // same text with every multi-line quote treated as literal.
                var raw = source.Recorded.ToString();
                source.Unread(raw);
                var retryNotes = new List<string>();
                var retry = ParseRecord(source, delimiter, allowMultiline: false, startLine,
                    out var retryStart, out var retryEnd, out _, retryNotes);
                if (retry is not null)
                    TrimTrailingEmptyCells(retry, expectedWidth, retryStart, retryNotes);

                if (retry is not null && retry.Count == expectedWidth)
                {
                    cells = retry;
                    endLine = retryEnd;
                    startLine = retryStart;
                    notes = first ? [.. fileNotes] : [];
                    notes.AddRange(retryNotes);
                    notes.Add($"line {startLine}: a quote that would have merged several lines was kept as a literal character to restore the expected {expectedWidth} columns.");
                }
                else
                {
                    // The retry did not fit either: give back what it consumed and keep the faithful reading.
                    source.Unread(source.Recorded.ToString());
                    notes = [];
                    if (first) notes.AddRange(fileNotes);
                    cells = ParseRecord(source, delimiter, allowMultiline: true, startLine,
                        out startLine, out endLine, out _, notes)!;
                    TrimTrailingEmptyCells(cells, expectedWidth, startLine, notes);
                }
            }

            line = endLine;
            FlagMultiLineValuesThatLookLikeRows(cells, delimiter, expectedWidth, startLine, notes);

            if (cells.All(cell => cell.Length == 0))
            {
                // Nothing but empty cells (",,,,"): there is no data to keep.
                continue;
            }

            if (first)
            {
                expectedWidth = cells.Count;
                first = false;
            }

            yield return new CsvRecord(cells.ToArray(), startLine) { Notes = notes };
        }
    }

    /// <summary>
    /// A quoted value that spans lines is valid CSV, so it is kept. But if one of its extra lines has as many
    /// delimiters as a whole row, an unclosed quote may have merged several rows: say so, so a person checks it.
    /// </summary>
    private static void FlagMultiLineValuesThatLookLikeRows(List<string> cells, char delimiter, int expectedWidth,
        int startLine, List<string> notes)
    {
        if (expectedWidth < 2) return;
        foreach (var cell in cells)
        {
            if (cell.IndexOf('\n') < 0 && cell.IndexOf('\r') < 0) continue;
            var lines = cell.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
            if (lines.Skip(1).Any(text => text.Count(c => c == delimiter) >= expectedWidth - 1))
            {
                notes.Add($"line {startLine}: REVIEW - a quoted value spans several lines and contains text that looks like separate rows; a stray quote may have merged them.");
                return;
            }
        }
    }

    private static void TrimTrailingEmptyCells(List<string> cells, int expectedWidth, int line, List<string> notes)
    {
        if (expectedWidth <= 0 || cells.Count <= expectedWidth) return;
        for (var i = expectedWidth; i < cells.Count; i++)
            if (cells[i].Length != 0) return;

        var removed = cells.Count - expectedWidth;
        cells.RemoveRange(expectedWidth, removed);
        notes.Add($"line {line}: {removed} empty trailing column(s) were ignored.");
    }

    private static char SniffDelimiter(Source source, List<string> notes)
    {
        var line = new StringBuilder();
        int c;
        while ((c = source.Read()) != -1 && c != '\n' && c != '\r') line.Append((char)c);
        if (c != -1) line.Append((char)c);
        source.Unread(line.ToString());
        source.Recorded.Clear();

        int commas = 0, semicolons = 0, tabs = 0;
        var inQuotes = false;
        foreach (var ch in line.ToString())
        {
            if (ch == '"') inQuotes = !inQuotes;
            else if (!inQuotes)
            {
                if (ch == ',') commas++;
                else if (ch == ';') semicolons++;
                else if (ch == '\t') tabs++;
            }
        }

        if (commas > 0 || (semicolons == 0 && tabs == 0)) return ',';
        if (semicolons >= tabs)
        {
            notes.Add("the file uses ';' as its delimiter instead of ','.");
            return ';';
        }

        notes.Add("the file uses TAB as its delimiter instead of ','.");
        return '\t';
    }

    /// <summary>
    /// Parses one record (skipping blank lines). Returns null at end of input.
    /// </summary>
    private static List<string>? ParseRecord(Source source, char delimiter, bool allowMultiline, int line,
        out int startLine, out int endLine, out bool usedMultiline, List<string> notes)
    {
        var cells = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var touched = false;
        var quoteStartPosition = 0;
        var quoteStartLine = 0;
        var quotedLineBreaks = 0;
        var strayQuoteNoted = false;
        startLine = line;
        endLine = line;
        usedMultiline = false;

        // Called when a quoted cell cannot be trusted: the opening quote becomes literal text and
        // everything read after it is parsed again as ordinary cell content.
        void FallBackToLiteralQuote(string reason)
        {
            var consumed = source.Recorded.ToString(quoteStartPosition, source.Recorded.Length - quoteStartPosition);
            source.Unread(consumed);
            field.Clear();
            field.Append('"');
            inQuotes = false;
            line = quoteStartLine;
            quotedLineBreaks = 0;
            notes.Add($"line {quoteStartLine}, column {cells.Count + 1}: {reason}; the quote was kept as a literal character.");
        }

        while (true)
        {
            var character = source.Read();
            if (character == -1)
            {
                if (inQuotes)
                {
                    FallBackToLiteralQuote("a quoted value was never closed");
                    continue;
                }

                if (touched || cells.Count > 0 || field.Length > 0)
                {
                    cells.Add(field.ToString());
                    endLine = line;
                    return cells;
                }

                return null;
            }

            var ch = (char)character;

            if (inQuotes)
            {
                if (ch == '"')
                {
                    var next = source.Peek();
                    if (next == '"')
                    {
                        source.Read();
                        field.Append('"');
                    }
                    else if (next == -1 || next == delimiter || next == '\r' || next == '\n')
                    {
                        inQuotes = false;
                        if (quotedLineBreaks > 0) usedMultiline = true;
                    }
                    else
                    {
                        field.Append('"');
                        notes.Add($"line {line}, column {cells.Count + 1}: a quote inside a quoted value was kept as a literal character.");
                    }
                }
                else if (ch == '\r' || ch == '\n')
                {
                    if (!allowMultiline || quotedLineBreaks >= MaxQuotedLineBreaks)
                    {
                        FallBackToLiteralQuote(allowMultiline
                            ? "a quoted value spans too many lines"
                            : "a quoted value spans several lines");
                        continue;
                    }

                    if (ch == '\r' && source.Peek() == '\n')
                    {
                        source.Read();
                        field.Append("\r\n");
                    }
                    else field.Append(ch);
                    line++;
                    quotedLineBreaks++;
                }
                else field.Append(ch);

                continue;
            }

            if (ch == '"')
            {
                if (field.Length == 0)
                {
                    inQuotes = true;
                    touched = true;
                    quoteStartPosition = source.Recorded.Length;
                    quoteStartLine = line;
                    quotedLineBreaks = 0;
                }
                else
                {
                    field.Append('"');
                    if (!strayQuoteNoted)
                    {
                        notes.Add($"line {line}, column {cells.Count + 1}: a quote inside an unquoted value was kept as a literal character.");
                        strayQuoteNoted = true;
                    }
                }
            }
            else if (ch == delimiter)
            {
                cells.Add(field.ToString());
                field.Clear();
                strayQuoteNoted = false;
                touched = true;
            }
            else if (ch == '\r' || ch == '\n')
            {
                if (ch == '\r' && source.Peek() == '\n') source.Read();
                if (touched || cells.Count > 0 || field.Length > 0)
                {
                    cells.Add(field.ToString());
                    endLine = line + 1;
                    return cells;
                }

                // Blank line: skip it.
                line++;
                startLine = line;
                source.Recorded.Clear();
            }
            else
            {
                field.Append(ch);
                touched = true;
            }
        }
    }
}

public sealed class CsvFormatException : Exception
{
    public string FilePath { get; }
    public int LineNumber { get; }
    public int ColumnNumber { get; }

    public CsvFormatException(string message, string filePath, int lineNumber, int columnNumber)
        : base(message)
    {
        FilePath = filePath;
        LineNumber = lineNumber;
        ColumnNumber = columnNumber;
    }
}
