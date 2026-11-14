namespace CsvProjectXml.Maui.Services;

public interface IPathPicker
{
    Task<string?> PickFileAsync(string title, params string[] extensions);
    Task<string?> PickFolderAsync(string title);
}
