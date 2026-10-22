using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using PictureIt.Core.Abstractions;
using PictureIt.Core.History;
using PictureIt.Core.Model;
using PictureIt.Core.Search;
using PictureIt.Core.Settings;
using PictureIt.Presentation.Services;

namespace PictureIt.Presentation.ViewModels;

/// <summary>A photo in the library grid. Thumbnails are loaded by the view.</summary>
public sealed class PhotoItemViewModel(string filePath)
{
    public string FilePath { get; } = filePath;

    public string FileName { get; } = Path.GetFileName(filePath);
}

/// <summary>
/// What the library shows (a folder, or the results of a search), its photos, the selection, and the editor for
/// the selection.
/// </summary>
public sealed partial class LibraryViewModel : ObservableObject, IRecipient<FolderChangedMessage>, IRecipient<CatalogChangedMessage>, IDisposable
{
    private readonly IFolderPicker _folderPicker;
    private readonly ISettingsStore _settings;
    private readonly IPhotoSummaryReader _summaryReader;
    private readonly LibraryServices _services;
    private int _summaryVersion;
    private IReadOnlyList<PhotoItemViewModel>? _pendingSelection;
    private IDisposable? _folderWatch;
    private Func<Task<IReadOnlyList<string>>>? _reloadResults;

    public LibraryViewModel(
        IFolderPicker folderPicker,
        ISettingsStore settings,
        IPhotoSummaryReader summaryReader,
        MetadataEditorViewModel editor,
        ToolsViewModel tools,
        OrganiseViewModel organise,
        LibraryServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _folderPicker = folderPicker;
        _settings = settings;
        _summaryReader = summaryReader;
        _services = services;
        Editor = editor;
        Tools = tools;
        Organise = organise;
        services.Messenger.Register<FolderChangedMessage>(this);
        services.Messenger.Register<CatalogChangedMessage>(this);
        RecentFolders = [.. settings.Load().RecentFolders];
        StatusText = string.Empty;
    }

    public MetadataEditorViewModel Editor { get; }

    /// <summary>Bulk tools (templates, find and replace, shift dates...) for the selection.</summary>
    public ToolsViewModel Tools { get; }

    /// <summary>Rename, sort, export and import, and duplicates.</summary>
    public OrganiseViewModel Organise { get; }

    public ObservableCollection<PhotoItemViewModel> Photos { get; } = [];

    public ObservableCollection<string> RecentFolders { get; }

    public IReadOnlyList<PhotoItemViewModel> SelectedPhotos { get; private set; } = [];

    /// <summary>The open folder, or null when showing search results (or nothing).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFolder), nameof(FolderName), nameof(LocationText))]
    public partial string? CurrentFolder { get; private set; }

    /// <summary>What the search results are, e.g. "Canon EOS R5 · no copyright", or null when showing a folder.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFolder), nameof(FolderName), nameof(LocationText), nameof(IsShowingResults))]
    public partial string? ResultsTitle { get; private set; }

    public bool IsShowingResults => ResultsTitle is not null;

    /// <summary>True when a folder or search results are open.</summary>
    public bool HasFolder => CurrentFolder is not null || IsShowingResults;

    public string FolderName => IsShowingResults
        ? "Search results"
        : CurrentFolder is null ? string.Empty : Path.GetFileName(CurrentFolder) is { Length: > 0 } name ? name : CurrentFolder;

    /// <summary>The folder's path, or what the search was.</summary>
    public string LocationText => ResultsTitle ?? CurrentFolder ?? string.Empty;

    /// <summary>Camera and exposure summary for one selected photo, or a count for several.</summary>
    [ObservableProperty]
    public partial string StatusText { get; private set; }

    [RelayCommand]
    private async Task OpenFolderAsync()
    {
        if (await _folderPicker.PickFolderAsync() is { } folder)
        {
            await LoadFolderAsync(folder);
        }
    }

    [RelayCommand]
    private async Task OpenRecentAsync(string folder) => await LoadFolderAsync(folder);

    /// <summary>Opens a folder. Returns false if the user chose to stay with unsaved changes, or the folder is gone.</summary>
    public async Task<bool> LoadFolderAsync(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        if (!await Editor.ConfirmLeaveAsync())
        {
            return false;
        }

        if (!Directory.Exists(folder))
        {
            RemoveRecent(folder);
            StatusText = $"Folder not found: {folder}";
            return false;
        }

        ResultsTitle = null;
        _reloadResults = null;
        CurrentFolder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        var files = await ListFolderAsync(CurrentFolder);
        ShowPhotos(files);
        WatchFolder(CurrentFolder);

        var settings = _settings.Load().WithRecentFolder(CurrentFolder);
        _settings.Save(settings);
        RecentFolders.Clear();
        foreach (var recent in settings.RecentFolders)
        {
            RecentFolders.Add(recent);
        }

        SelectedPhotos = [];
        await Editor.ShowSelectionAsync([]);
        StatusText = Count(files.Count);
        return true;
    }

    /// <summary>
    /// Shows search results (photos from any folders) so the editor and every tool work on them.
    /// <paramref name="reload"/> re-runs the search when the catalogue changes.
    /// </summary>
    public async Task<bool> ShowResultsAsync(string title, IReadOnlyList<string> paths, Func<Task<IReadOnlyList<string>>> reload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(paths);
        if (!await Editor.ConfirmLeaveAsync())
        {
            return false;
        }

        StopWatching();
        CurrentFolder = null;
        ResultsTitle = title;
        _reloadResults = reload;
        ShowPhotos(paths);
        SelectedPhotos = [];
        await Editor.ShowSelectionAsync([]);
        StatusText = Count(paths.Count);
        return true;
    }

    /// <summary>
    /// Re-reads the open folder (or updates the results) after files were renamed, moved, copied or deleted, keeping
    /// the selection on the same photos (under their new names) where they're still listed.
    /// </summary>
    public async void Receive(FolderChangedMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        try
        {
            await RefreshFolderAsync(message.Moves);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText = $"Couldn't re-read the folder: {ex.Message}";
        }
    }

    /// <summary>Re-runs the search shown, after the catalogue changed.</summary>
    public async void Receive(CatalogChangedMessage message)
    {
        if (_reloadResults is not { } reload || Editor.IsDirty)
        {
            return;
        }

        try
        {
            var paths = await reload();
            if (_reloadResults == reload)
            {
                await ReplacePhotosAsync([.. paths.Where(File.Exists)], new Dictionary<string, string>());
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            StatusText = $"Couldn't update the results: {ex.Message}";
        }
    }

    public async Task RefreshFolderAsync(IReadOnlyList<FileMove> moves)
    {
        ArgumentNullException.ThrowIfNull(moves);
        var moved = moves.GroupBy(m => m.From, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Last().To, StringComparer.OrdinalIgnoreCase);

        if (IsShowingResults)
        {
            // Results follow their photos wherever they went; deleted ones drop out.
            var paths = Photos.Select(p => moved.GetValueOrDefault(p.FilePath) ?? p.FilePath).Where(File.Exists).ToList();
            await ReplacePhotosAsync(paths, moved);
            return;
        }

        if (CurrentFolder is null)
        {
            return;
        }

        if (!Directory.Exists(CurrentFolder))
        {
            Photos.Clear();
            Organise.SetFolder(null, []);
            StatusText = $"Folder not found: {CurrentFolder}";
            return;
        }

        await ReplacePhotosAsync(await ListFolderAsync(CurrentFolder), moved);
    }

    public void Dispose()
    {
        StopWatching();
        _services.Messenger.UnregisterAll(this);
    }

    /// <summary>Raised when the view should select these photos (after a drop or a command-line open).</summary>
    public event EventHandler<IReadOnlyList<PhotoItemViewModel>>? SelectionRequested;

    /// <summary>
    /// Opens dropped or command-line paths: a folder opens as-is; photos open their folder and get selected.
    /// </summary>
    public async Task OpenPathsAsync(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (paths.Count == 0)
        {
            return;
        }

        if (Directory.Exists(paths[0]))
        {
            await LoadFolderAsync(paths[0]);
            return;
        }

        var files = paths.Where(File.Exists).Select(Path.GetFullPath).ToList();
        if (files.Count == 0 || Path.GetDirectoryName(files[0]) is not { } folder || !await LoadFolderAsync(folder))
        {
            return;
        }

        var photos = Photos.Where(p => files.Contains(p.FilePath, StringComparer.OrdinalIgnoreCase)).ToList();
        if (photos.Count == 0)
        {
            return;
        }

        RequestSelection(photos);
    }

    private void RequestSelection(IReadOnlyList<PhotoItemViewModel> photos)
    {
        // At startup the view may not exist yet; it collects the request with TakePendingSelection when it does.
        if (SelectionRequested is { } handler)
        {
            handler(this, photos);
        }
        else
        {
            _pendingSelection = photos;
        }
    }

    /// <summary>Shows a new list of photos, keeping the selected ones (following <paramref name="moved"/>) selected.</summary>
    private async Task ReplacePhotosAsync(List<string> paths, IReadOnlyDictionary<string, string> moved)
    {
        // Nothing changed (e.g. the catalogue updated after a save): leave the grid and selection alone.
        if (paths.SequenceEqual(Photos.Select(p => p.FilePath), StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        var keep = SelectedPhotos.Select(p => moved.GetValueOrDefault(p.FilePath) ?? p.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        ShowPhotos(paths);
        SelectedPhotos = [];
        await Editor.ShowSelectionAsync([]);
        StatusText = Count(paths.Count);

        var reselect = Photos.Where(p => keep.Contains(p.FilePath)).ToList();
        if (reselect.Count > 0)
        {
            RequestSelection(reselect);
        }
    }

    private void ShowPhotos(IReadOnlyList<string> paths)
    {
        Photos.Clear();
        foreach (var path in paths)
        {
            Photos.Add(new PhotoItemViewModel(path));
        }

        Organise.SetFolder(CurrentFolder, paths);
    }

    private static Task<List<string>> ListFolderAsync(string folder) =>
        Task.Run(() => Directory
            .EnumerateFiles(folder)
            .Where(SupportedFormats.IsSupported)
            .Order(StringComparer.CurrentCultureIgnoreCase)
            .ToList());

    /// <summary>Notices photos added, removed or renamed in the open folder by other apps.</summary>
    private void WatchFolder(string folder)
    {
        StopWatching();
        try
        {
            _folderWatch = _services.Watchers.Watch(folder, includeSubfolders: false, changed => _services.UiThread.Post(() => _ = OnFolderWatchAsync(folder)));
        }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        {
            // Some locations (e.g. some network shares) can't be watched; the library still works without it.
            _folderWatch = null;
        }
    }

    private async Task OnFolderWatchAsync(string folder)
    {
        // Edits in progress win; the next change (or reopening the folder) catches up.
        if (CurrentFolder != folder || Editor.IsDirty)
        {
            return;
        }

        try
        {
            await ReplacePhotosAsync(await ListFolderAsync(folder), new Dictionary<string, string>());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText = $"Couldn't re-read the folder: {ex.Message}";
        }
    }

    private void StopWatching()
    {
        _folderWatch?.Dispose();
        _folderWatch = null;
    }

    private static string Count(int count) => count == 1 ? "1 photo" : $"{count} photos";

    /// <summary>Returns (and forgets) a selection requested before the view was listening.</summary>
    public IReadOnlyList<PhotoItemViewModel>? TakePendingSelection()
    {
        var pending = _pendingSelection;
        _pendingSelection = null;
        return pending;
    }

    /// <summary>
    /// Called by the view when the selection changes. Returns false if the user cancelled because of unsaved
    /// changes; the view should then put the previous selection back.
    /// </summary>
    public async Task<bool> SelectAsync(IReadOnlyList<PhotoItemViewModel> selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        var stayingOnSamePhoto = selection.Select(p => p.FilePath).SequenceEqual(Editor.RequestedPaths, StringComparer.OrdinalIgnoreCase);
        if (!stayingOnSamePhoto && !await Editor.ConfirmLeaveAsync())
        {
            return false;
        }

        SelectedPhotos = selection;
        if (!stayingOnSamePhoto)
        {
            await Task.WhenAll(Editor.ShowSelectionAsync([.. selection.Select(p => p.FilePath)]), UpdateStatusAsync(selection));
        }

        return true;
    }

    private async Task UpdateStatusAsync(IReadOnlyList<PhotoItemViewModel> selection)
    {
        var version = ++_summaryVersion;
        if (selection.Count != 1)
        {
            StatusText = selection.Count == 0 ? $"{Photos.Count} photos" : $"{selection.Count} of {Photos.Count} photos selected";
            return;
        }

        var summary = await Task.Run(() => _summaryReader.ReadSummary(selection[0].FilePath));
        if (version == _summaryVersion)
        {
            StatusText = summary is null ? selection[0].FileName : PhotoSummaryFormatter.Describe(summary) is { Length: > 0 } text ? text : selection[0].FileName;
        }
    }

    private void RemoveRecent(string folder)
    {
        var settings = _settings.Load();
        var remaining = settings.RecentFolders.Where(f => !string.Equals(f, folder, StringComparison.OrdinalIgnoreCase)).ToList();
        _settings.Save(settings with { RecentFolders = remaining });
        RecentFolders.Remove(folder);
    }
}

/// <summary>Services the library uses to keep up with changes, gathered so its constructor stays readable.</summary>
public sealed record LibraryServices(IMessenger Messenger, IFolderWatcherFactory Watchers, IUiThread UiThread);
