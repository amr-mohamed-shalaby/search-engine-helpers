using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace CsvProjectXml.Maui.Services;

public sealed class WindowsPathPicker : IPathPicker
{
    public async Task<string?> PickFileAsync(string title, params string[] extensions)
    {
        var fileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            [DevicePlatform.WinUI] = extensions.Length == 0 ? [".*"] : extensions
        });
        var result = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = title,
            FileTypes = fileTypes
        });
        return result?.FullPath;
    }

    public async Task<string?> PickFolderAsync(string title)
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        var mauiWindow = Application.Current?.Windows.FirstOrDefault();
        if (mauiWindow?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window nativeWindow)
            throw new InvalidOperationException("The native Windows window is not available for the folder picker.");

        var windowHandle = WindowNative.GetWindowHandle(nativeWindow);
        InitializeWithWindow.Initialize(picker, windowHandle);
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }
}
