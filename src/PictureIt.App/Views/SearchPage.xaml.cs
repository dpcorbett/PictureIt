using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PictureIt.App.Services;
using PictureIt.Core.Search;
using PictureIt.Presentation.Services;
using PictureIt.Presentation.ViewModels;

namespace PictureIt.App.Views;

public sealed partial class SearchPage : Page
{
    private readonly ThumbnailCache _thumbnails = new();
    private readonly LibraryViewModel _library;

    public SearchPage()
    {
        ViewModel = App.Services.GetRequiredService<SearchViewModel>();
        _library = App.Services.GetRequiredService<LibraryViewModel>();
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public SearchViewModel ViewModel { get; }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Offer the folder open in the Library as a quick start for an empty catalogue.
        var folder = _library.CurrentFolder;
        AddLibraryFolderButton.Visibility = folder is null ? Visibility.Collapsed : Visibility.Visible;
        AddLibraryFolderButton.Content = folder is null ? null : $"Add {Path.GetFileName(folder)}";
        await ViewModel.LoadAsync();
    }

    private async void OnAddLibraryFolderClick(object sender, RoutedEventArgs e)
    {
        if (_library.CurrentFolder is { } folder)
        {
            await ViewModel.AddFolderAsync(folder);
        }
    }

    private void OnRescanClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CatalogFolderViewModel folder })
        {
            ViewModel.RescanCommand.Execute(folder);
        }
    }

    private void OnRemoveFolderClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CatalogFolderViewModel folder })
        {
            ViewModel.RemoveFolderCommand.Execute(folder);
        }
    }

    /// <summary>Clicking a result opens its folder in the Library with the photo selected.</summary>
    private async void OnResultClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is CatalogEntry entry && File.Exists(entry.FilePath))
        {
            App.Services.GetRequiredService<IMessenger>().Send(new NavigateMessage(NavigateMessage.Library));
            await _library.OpenPathsAsync([entry.FilePath]);
        }
    }

    private void OnSavedMenuOpening(object sender, object e)
    {
        SavedMenu.Items.Clear();
        if (ViewModel.SavedSearches.Count == 0)
        {
            SavedMenu.Items.Add(new MenuFlyoutItem { Text = "No saved searches yet", IsEnabled = false });
            return;
        }

        foreach (var search in ViewModel.SavedSearches)
        {
            SavedMenu.Items.Add(new MenuFlyoutItem
            {
                Text = search.Name,
                Command = ViewModel.ApplySavedSearchCommand,
                CommandParameter = search,
                Icon = new FontIcon { Glyph = "" },
            });
        }

        var delete = new MenuFlyoutSubItem { Text = "Delete", Icon = new FontIcon { Glyph = "" } };
        foreach (var search in ViewModel.SavedSearches)
        {
            delete.Items.Add(new MenuFlyoutItem { Text = search.Name, Command = ViewModel.DeleteSavedSearchCommand, CommandParameter = search });
        }

        SavedMenu.Items.Add(new MenuFlyoutSeparator());
        SavedMenu.Items.Add(delete);
    }

    /// <summary>Loads thumbnails as results scroll into view (phase 1), so the grid stays responsive.</summary>
    private void OnResultContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.ItemContainer.ContentTemplateRoot is not Grid root || root.FindName("Thumb") is not Image thumb)
        {
            return;
        }

        if (args.InRecycleQueue)
        {
            thumb.Source = null;
            return;
        }

        if (args.Phase == 0)
        {
            thumb.Source = null;
            args.RegisterUpdateCallback(OnResultContainerContentChanging);
            return;
        }

        if (args.Item is CatalogEntry entry)
        {
            LoadThumbnail(thumb, args.ItemContainer, entry);
        }
    }

    private async void LoadThumbnail(Image thumb, Microsoft.UI.Xaml.Controls.Primitives.SelectorItem container, CatalogEntry entry)
    {
        var image = await _thumbnails.GetThumbnailAsync(entry.FilePath);
        if (ReferenceEquals(container.Content, entry))
        {
            thumb.Source = image;
        }
    }
}
