namespace CsvProjectXml.Maui.Services;

/// <summary>Opens a folder in the system file browser.</summary>
public interface IFolderOpener
{
    /// <returns>true when the folder was opened; otherwise <paramref name="error"/> says why.</returns>
    bool TryOpen(string path, out string error);
}
