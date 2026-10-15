using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using PictureIt.App.Views;
using PictureIt.Presentation.ViewModels;
using Windows.Graphics;

namespace PictureIt.App;

public sealed partial class MainWindow : Window
{
    private readonly LibraryViewModel _library;
    private bool _closeConfirmed;

    public MainWindow(LibraryViewModel library)
    {
        _library = library;
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.Resize(new SizeInt32(1480, 920));
        AppWindow.Closing += OnClosing;

        Navigation.SelectedItem = LibraryItem;
    }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var page = args.IsSettingsSelected ? typeof(SettingsPage) : typeof(LibraryPage);

        // Keep a single Library page alive (it's cached), so navigating away and back keeps your place.
        if (ContentFrame.CurrentSourcePageType != page)
        {
            ContentFrame.Navigate(page, null, new EntranceNavigationTransitionInfo());
        }
    }

    private async void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_closeConfirmed || !_library.Editor.IsDirty)
        {
            return;
        }

        // Ask first; close for real only if the user saves or discards.
        args.Cancel = true;
        if (await _library.Editor.ConfirmLeaveAsync())
        {
            _closeConfirmed = true;
            Close();
        }
    }
}
