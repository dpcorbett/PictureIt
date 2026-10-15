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
    public const string LibraryTag = "library";
    public const string TemplatesTag = "templates";
    public const string HistoryTag = "history";

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

    /// <summary>Switches to a page by its tag (see the *Tag constants).</summary>
    public void Navigate(string tag) => Navigation.SelectedItem = tag switch
    {
        TemplatesTag => TemplatesItem,
        HistoryTag => HistoryItem,
        _ => LibraryItem,
    };

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var page = args.IsSettingsSelected
            ? typeof(SettingsPage)
            : (args.SelectedItem as NavigationViewItem)?.Tag switch
            {
                TemplatesTag => typeof(TemplatesPage),
                HistoryTag => typeof(HistoryPage),
                _ => typeof(LibraryPage),
            };

        // The Library page is cached, so navigating away and back keeps your place.
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
