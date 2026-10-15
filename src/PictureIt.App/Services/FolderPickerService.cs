using PictureIt.Presentation.Services;
using Windows.Storage.Pickers;

namespace PictureIt.App.Services;

public sealed class FolderPickerService(WindowContext window) : IFolderPicker, IFilePicker
{
    public async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
        picker.FileTypeFilter.Add("*");
        Initialize(picker);
        return (await picker.PickSingleFolderAsync())?.Path;
    }

    public async Task<string?> PickFileToOpenAsync(IReadOnlyList<string> extensions)
    {
        ArgumentNullException.ThrowIfNull(extensions);
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        foreach (var extension in extensions)
        {
            picker.FileTypeFilter.Add(extension);
        }

        Initialize(picker);
        return (await picker.PickSingleFileAsync())?.Path;
    }

    public async Task<string?> PickFileToSaveAsync(string suggestedName, string typeName, string extension)
    {
        var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary, SuggestedFileName = suggestedName };
        picker.FileTypeChoices.Add(typeName, [extension]);
        Initialize(picker);
        return (await picker.PickSaveFileAsync())?.Path;
    }

    // Unpackaged desktop apps must tell pickers which window owns them.
    private void Initialize(object picker) => WinRT.Interop.InitializeWithWindow.Initialize(picker, window.Handle);
}
