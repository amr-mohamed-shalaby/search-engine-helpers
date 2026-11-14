using System.Text;

namespace CsvProjectXml.Core.Services;

public static class DepartmentSlugGenerator
{
    public const string FixedSuffix = "_hyeh_altnmya_alsnaaya";

    private static readonly IReadOnlyDictionary<char, string> Map = new Dictionary<char, string>
    {
        ['ا'] = "a", ['أ'] = "a", ['إ'] = "a", ['آ'] = "a",
        ['ب'] = "b", ['ت'] = "t", ['ث'] = "th", ['ج'] = "j", ['ح'] = "h", ['خ'] = "kh",
        ['د'] = "d", ['ذ'] = "th", ['ر'] = "r", ['ز'] = "z", ['س'] = "s", ['ش'] = "sh",
        ['ص'] = "s", ['ض'] = "d", ['ط'] = "t", ['ظ'] = "z", ['ع'] = "a", ['غ'] = "gh",
        ['ف'] = "f", ['ق'] = "q", ['ك'] = "k", ['ل'] = "l", ['م'] = "m", ['ن'] = "n",
        ['ه'] = "h", ['و'] = "w", ['ي'] = "y", ['ى'] = "a", ['ة'] = "a",
        ['ء'] = "e", ['ئ'] = "e", ['ؤ'] = "e"
    };

    public static string Generate(string arabicName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(arabicName);
        var result = new StringBuilder();
        foreach (var ch in arabicName.Trim())
        {
            if (ch is ' ' or '-' or '_')
                result.Append('_');
            else if (Map.TryGetValue(ch, out var transliteration))
                result.Append(transliteration);
            else if ((ch is >= 'a' and <= 'z') || (ch is >= 'A' and <= 'Z') || (ch is >= '0' and <= '9'))
                result.Append(ch);
            // Arabic marks, punctuation, parentheses, and all other characters are dropped.
        }

        return result + FixedSuffix;
    }
}
