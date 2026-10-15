using Microsoft.UI.Xaml;

namespace PictureIt.App.Views;

/// <summary>Small conversion helpers for use as x:Bind functions.</summary>
public static class Ui
{
    public static Visibility VisibleIf(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility CollapsedIf(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility VisibleIfText(string? value) => string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;

    public static bool Not(bool value) => !value;
}
