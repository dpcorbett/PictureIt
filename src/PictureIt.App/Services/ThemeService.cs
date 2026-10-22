using Microsoft.UI.Xaml;
using PictureIt.Core.Settings;
using PictureIt.Presentation.Services;

namespace PictureIt.App.Services;

public sealed class ThemeService(WindowContext window) : IThemeService
{
    public void Apply(AppTheme theme)
    {
        if (window.RequiredWindow.Content is FrameworkElement root)
        {
            root.RequestedTheme = theme switch
            {
                AppTheme.Light => ElementTheme.Light,
                AppTheme.Dark => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };
        }
    }
}
