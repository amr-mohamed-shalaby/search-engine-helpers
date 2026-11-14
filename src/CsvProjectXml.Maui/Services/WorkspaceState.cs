using Microsoft.Maui.Storage;

namespace CsvProjectXml.Maui.Services;

public sealed class WorkspaceState
{
    private const string NamesPathKey = "csv-project-xml.names-path";
    private const string OutputPathKey = "csv-project-xml.output-path";

    public string NamesFilePath { get; private set; } = Preferences.Default.Get(NamesPathKey, string.Empty);
    public string OutputFolderPath { get; private set; } = Preferences.Default.Get(OutputPathKey, string.Empty);

    public void SetNamesFilePath(string path)
    {
        NamesFilePath = path;
        Preferences.Default.Set(NamesPathKey, path);
    }

    public void SetOutputFolderPath(string path)
    {
        OutputFolderPath = path;
        Preferences.Default.Set(OutputPathKey, path);
    }

    public string SettingsFilePath => string.IsNullOrWhiteSpace(NamesFilePath)
        ? string.Empty
        : Path.Combine(Path.GetDirectoryName(NamesFilePath)!, "conversion-settings.json");
}
