using CsvProjectXml.Core.Services;
using CsvProjectXml.Maui.Services;
using Microsoft.Extensions.Logging;

namespace CsvProjectXml.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                // Uses the platform's default sans-serif font. Add bundled fonts here if desired.
            });

        builder.Services.AddMauiBlazorWebView();
#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif
        builder.Services.AddSingleton<CsvProjectConverter>();
        builder.Services.AddSingleton<BatchConversionService>();
        builder.Services.AddSingleton<NamesRegistryService>();
        builder.Services.AddSingleton<ConversionSettingsStore>();
        builder.Services.AddSingleton<ConversionReportWriter>();
        builder.Services.AddSingleton<CsvRepairService>();
        builder.Services.AddSingleton<RepairSessionStore>();
        builder.Services.AddSingleton<ConversionPageState>();
        builder.Services.AddSingleton<WorkspaceState>();
        builder.Services.AddSingleton<ConflictWizardState>();
        builder.Services.AddSingleton<IPathPicker, WindowsPathPicker>();
        builder.Services.AddSingleton<IFolderOpener, WindowsFolderOpener>();
        return builder.Build();
    }
}
