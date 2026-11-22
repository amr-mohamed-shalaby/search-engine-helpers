using System.Diagnostics;

namespace CsvProjectXml.Maui.Services;

public sealed class WindowsFolderOpener : IFolderOpener
{
    public bool TryOpen(string path, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            error = $"المجلد غير موجود: {path}";
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
