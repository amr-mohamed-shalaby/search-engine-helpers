using System.Text;
using CsvProjectXml.Core.Models;

namespace CsvProjectXml.Core.Services;

public static class XmlText
{
    public static void ValidateXml10(string value, string filePath, int lineNumber, string columnName)
    {
        for (var index = 0; index < value.Length; index++)
        {
            var ch = value[index];
            var valid = ch is '\t' or '\n' or '\r' ||
                        ch is >= '\u0020' and <= '\uD7FF' ||
                        ch is >= '\uE000' and <= '\uFFFD';

            if (char.IsHighSurrogate(ch))
            {
                if (index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]))
                {
                    index++;
                    continue;
                }
                valid = false;
            }
            else if (char.IsLowSurrogate(ch)) valid = false;

            if (!valid)
                throw new ConversionException($"Character U+{(int)ch:X4} is not permitted by XML 1.0.",
                    filePath, lineNumber, columnName);
        }
    }

    // XML text-node escaping only: quotes and apostrophes deliberately remain unchanged.
    public static string Escape(string value) => value
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal);
}
