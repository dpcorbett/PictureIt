using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using PictureIt.App.Services;
using PictureIt.Presentation.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace PictureIt.App.Views;

public sealed partial class LibraryPage : Page
{
    private readonly ThumbnailCache _thumbnails = new();
    private List<PhotoItemViewModel> _selection = [];
    private bool _restoringSelection;
    private int _previewVersion;
    private IReadOnlyList<PhotoItemViewModel>? _deferredSelection;

    public LibraryPage()
    {
        ViewModel = App.Services.GetRequiredService<LibraryViewModel>();
        InitializeComponent();
        ViewModel.SelectionRequested += OnSelectionRequested;
        ViewModel.Tools.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ToolsViewModel.CopiedFrom))
            {
                PasteButton.Label = $"Paste from {ViewModel.Tools.CopiedFrom}";
            }
        };
        PhotoGrid.Loaded += (_, _) =>
        {
            if ((_deferredSelection ?? ViewModel.TakePendingSelection()) is { } pending)
            {
                _deferredSelection = null;
                OnSelectionRequested(this, pending);
            }
        };
        ViewModel.Photos.CollectionChanged += (_, _) =>
        {
            // A new folder: drop old thumbnails and the preview.
            if (ViewModel.Photos.Count == 0)
            {
                _thumbnails.Clear();
                _selection = [];
                ShowPreview(null);
            }
        };
    }

    public LibraryViewModel ViewModel { get; }

    private async void OnPhotoSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_restoringSelection)
        {
            return;
        }

        var selection = PhotoGrid.SelectedItems.Cast<PhotoItemViewModel>().ToList();
        var previous = _selection;
        _selection = selection;

        if (!await ViewModel.SelectAsync(selection))
        {
            // The user chose to stay with their unsaved changes: put the old selection back.
            _selection = previous;
            _restoringSelection = true;
            PhotoGrid.SelectedItems.Clear();
            foreach (var item in previous)
            {
                PhotoGrid.SelectedItems.Add(item);
            }

            _restoringSelection = false;
            return;
        }

        ShowPreview(selection.Count == 1 ? selection[0].FilePath : null);
    }

    private async void ShowPreview(string? path)
    {
        var version = ++_previewVersion;
        PreviewImage.Source = null;
        PreviewScroller.ChangeView(0, 0, 1, disableAnimation: true);
        PreviewHint.Text = path is null && _selection.Count > 1 ? $"{_selection.Count} photos selected" : string.Empty;
        if (path is null)
        {
            PreviewProgress.IsActive = false;
            return;
        }

        PreviewProgress.IsActive = true;
        var image = await ThumbnailCache.GetPreviewAsync(path);
        if (version == _previewVersion)
        {
            PreviewImage.Source = image;
            PreviewProgress.IsActive = false;
        }
    }

    private void OnPreviewSizeChanged(object sender, SizeChangedEventArgs e)
    {
        PreviewHost.Width = e.NewSize.Width;
        PreviewHost.Height = e.NewSize.Height;
    }

    /// <summary>Loads thumbnails as items scroll into view (phase 1), so the grid stays responsive.</summary>
    private void OnPhotoContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
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
            args.RegisterUpdateCallback(OnPhotoContainerContentChanging);
            return;
        }

        if (args.Item is PhotoItemViewModel photo)
        {
            LoadThumbnail(thumb, args.ItemContainer, photo);
        }
    }

    private async void LoadThumbnail(Image thumb, SelectorItem container, PhotoItemViewModel photo)
    {
        var image = await _thumbnails.GetThumbnailAsync(photo.FilePath);

        // The container may have been recycled for another photo while loading.
        if (ReferenceEquals(container.Content, photo))
        {
            thumb.Source = image;
        }
    }

    /// <summary>Saved templates to apply, then "save as" and "manage".</summary>
    private void OnTemplatesMenuOpening(object sender, object e)
    {
        var tools = ViewModel.Tools;
        tools.RefreshTemplates();
        TemplatesMenu.Items.Clear();

        if (tools.Templates.Count == 0)
        {
            TemplatesMenu.Items.Add(new MenuFlyoutItem { Text = "No templates yet", IsEnabled = false });
        }

        foreach (var template in tools.Templates)
        {
            TemplatesMenu.Items.Add(new MenuFlyoutItem
            {
                Text = $"Apply \"{template.Name}\"",
                Command = tools.ApplyTemplateCommand,
                CommandParameter = template,
                Icon = new FontIcon { Glyph = "" },
            });
        }

        TemplatesMenu.Items.Add(new MenuFlyoutSeparator());
        TemplatesMenu.Items.Add(new MenuFlyoutItem
        {
            Text = "Save this photo as a template…",
            Command = tools.SaveAsTemplateCommand,
            Icon = new FontIcon { Glyph = "" },
        });
        var manage = new MenuFlyoutItem { Text = "Manage templates", Icon = new FontIcon { Glyph = "" } };
        manage.Click += (_, _) => App.MainWindow?.Navigate(MainWindow.TemplatesTag);
        TemplatesMenu.Items.Add(manage);
    }

    private void OnRecentMenuOpening(object sender, object e)
    {
        RecentMenu.Items.Clear();
        if (ViewModel.RecentFolders.Count == 0)
        {
            RecentMenu.Items.Add(new MenuFlyoutItem { Text = "No recent folders", IsEnabled = false });
            return;
        }

        foreach (var folder in ViewModel.RecentFolders)
        {
            RecentMenu.Items.Add(new MenuFlyoutItem
            {
                Text = folder,
                Command = ViewModel.OpenRecentCommand,
                CommandParameter = folder,
                Icon = new FontIcon { Glyph = "" },
            });
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Link;
            e.DragUIOverride.Caption = "Open in PictureIt";
        }
    }

    /// <summary>Dropping a folder opens it; dropping photos opens their folder and selects them.</summary>
    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            var items = await e.DataView.GetStorageItemsAsync();
            await ViewModel.OpenPathsAsync([.. items.Select(i => i.Path)]);
        }
    }

    private void OnSelectionRequested(object? sender, IReadOnlyList<PhotoItemViewModel> photos)
    {
        // Until the grid has loaded it hasn't taken in its items, and selecting them would be ignored.
        if (!PhotoGrid.IsLoaded)
        {
            _deferredSelection = photos;
            return;
        }

        PhotoGrid.SelectedItems.Clear();
        foreach (var photo in photos)
        {
            PhotoGrid.SelectedItems.Add(photo);
        }

        PhotoGrid.ScrollIntoView(photos[0]);
    }
}
