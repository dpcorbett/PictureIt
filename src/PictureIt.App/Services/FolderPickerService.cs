using PictureIt.Presentation.Services;
using Windows.Storage.Pickers;

namespace PictureIt.App.Services;

public sealed class FolderPickerService(WindowContext window) : IFolderPicker
{
    public async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
        picker.FileTypeFilter.Add("*");

        // Unpackaged desktop apps must tell the picker which window owns it.
        WinRT.Interop.InitializeWithWindow.Initialize(picker, window.Handle);

        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }
}
