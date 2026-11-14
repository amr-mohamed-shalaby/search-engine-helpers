using System.Text;

namespace CsvProjectXml.Core.Services;

public sealed record CsvRecord(IReadOnlyList<string> Values, int StartLine);

/// <summary>
/// Small strict RFC-4180-style CSV reader. It handles quoted commas, escaped quotes,
/// and quoted line breaks while preserving all cell characters, including TABs/spaces.
/// </summary>
public static class CsvReader
{
    public static IEnumerable<CsvRecord> Read(string path)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 64 * 1024, FileOptions.SequentialScan);

        try
        {
            var signature = new byte[4];
            var read = stream.Read(signature);
            if (read >= 2 && ((signature[0] == 0xFF && signature[1] == 0xFE) ||
                              (signature[0] == 0xFE && signature[1] == 0xFF)))
                throw new InvalidDataException("CSV must be UTF-8 (with or without a UTF-8 BOM), not UTF-16.");
            if (read >= 4 && ((signature[0] == 0x00 && signature[1] == 0x00 && signature[2] == 0xFE && signature[3] == 0xFF) ||
                              (signature[0] == 0xFF && signature[1] == 0xFE && signature[2] == 0x00 && signature[3] == 0x00)))
                throw new InvalidDataException("CSV must be UTF-8 (with or without a UTF-8 BOM), not UTF-32.");

            var hasUtf8Bom = read >= 3 && signature[0] == 0xEF && signature[1] == 0xBB && signature[2] == 0xBF;
            stream.Position = hasUtf8Bom ? 3 : 0;
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true),
                detectEncodingFromByteOrderMarks: false, bufferSize: 64 * 1024, leaveOpen: false);
            foreach (var record in Parse(reader, path))
                yield return record;
        }
        finally
        {
            stream.Dispose();
        }
    }

    private static IEnumerable<CsvRecord> Parse(TextReader reader, string path)
    {
        var cells = new List<string>();
        var field = new StringBuilder();
        var line = 1;
        var recordStartLine = 1;
        var inQuotes = false;
        var afterQuote = false;
        var touched = false;
        var character = reader.Read();

        while (character != -1)
        {
            var ch = (char)character;

            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (reader.Peek() == '"')
                    {
                        reader.Read();
                        field.Append('"');
                    }
                    else
                    {
                        inQuotes = false;
                        afterQuote = true;
                    }
                }
                else if (ch == '\r')
                {
                    if (reader.Peek() == '\n')
                    {
                        reader.Read();
                        field.Append("\r\n");
                    }
                    else field.Append('\r');
                    line++;
                }
                else if (ch == '\n')
                {
                    field.Append('\n');
                    line++;
                }
                else field.Append(ch);
            }
            else
            {
                if (afterQuote && ch != ',' && ch != '\r' && ch != '\n')
                    throw new CsvFormatException("Unexpected character after a closing quote.", path, line, cells.Count + 1);

                if (ch == '"')
                {
                    if (field.Length != 0 || afterQuote)
                        throw new CsvFormatException("A quote appeared inside an unquoted CSV value.", path, line, cells.Count + 1);
                    inQuotes = true;
                    touched = true;
                }
                else if (ch == ',')
                {
                    cells.Add(field.ToString());
                    field.Clear();
                    afterQuote = false;
                    touched = true;
                }
                else if (ch == '\r' || ch == '\n')
                {
                    if (ch == '\r' && reader.Peek() == '\n') reader.Read();
                    if (touched || cells.Count > 0 || field.Length > 0)
                    {
                        cells.Add(field.ToString());
                        yield return new CsvRecord(cells.ToArray(), recordStartLine);
                    }
                    cells.Clear();
                    field.Clear();
                    afterQuote = false;
                    touched = false;
                    line++;
                    recordStartLine = line;
                }
                else
                {
                    field.Append(ch);
                    touched = true;
                }
            }

            character = reader.Read();
        }

        if (inQuotes)
            throw new CsvFormatException("The file ended inside a quoted CSV value.", path, recordStartLine, cells.Count + 1);

        if (touched || cells.Count > 0 || field.Length > 0)
        {
            cells.Add(field.ToString());
            yield return new CsvRecord(cells.ToArray(), recordStartLine);
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
