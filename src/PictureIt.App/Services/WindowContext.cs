using Microsoft.UI.Xaml;

namespace PictureIt.App.Services;

/// <summary>
/// Gives services access to the main window, which is created after the service provider.
/// </summary>
public sealed class WindowContext
{
    public Window? Window { get; set; }

    public Window RequiredWindow => Window ?? throw new InvalidOperationException("The main window hasn't been created yet.");

    public XamlRoot XamlRoot => RequiredWindow.Content.XamlRoot;

    public nint Handle => WinRT.Interop.WindowNative.GetWindowHandle(RequiredWindow);
}
