using System.Text;

namespace CsvProjectXml.Core.Services;

/// <summary>
/// Writes UTF-8 bytes directly. Each structural line is separated by literal CRLF,
/// and the final line has no terminator. No BOM or platform newline translation is used.
/// </summary>
internal sealed class XmlLineWriter : IDisposable
{
    private static readonly byte[] CrLf = [0x0D, 0x0A];
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private readonly FileStream _stream;
    private bool _first = true;

    public XmlLineWriter(string path)
    {
        _stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            64 * 1024, FileOptions.SequentialScan);
    }

    public void WriteLine(string value)
    {
        if (!_first) _stream.Write(CrLf);
        var bytes = Utf8.GetBytes(value);
        _stream.Write(bytes);
        _first = false;
    }

    public void Flush() => _stream.Flush(flushToDisk: false);
    public void Dispose() => _stream.Dispose();
}
