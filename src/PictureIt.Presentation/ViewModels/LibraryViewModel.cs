using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using PictureIt.Core.Abstractions;
using PictureIt.Core.History;
using PictureIt.Core.Model;
using PictureIt.Core.Settings;
using PictureIt.Presentation.Services;

namespace PictureIt.Presentation.ViewModels;

/// <summary>A photo in the library grid. Thumbnails are loaded by the view.</summary>
public sealed class PhotoItemViewModel(string filePath)
{
    public string FilePath { get; } = filePath;

    public string FileName { get; } = Path.GetFileName(filePath);
}

/// <summary>The folder being browsed, its photos, the selection, and the editor for the selection.</summary>
public sealed partial class LibraryViewModel : ObservableObject, IRecipient<FolderChangedMessage>
{
    private readonly IFolderPicker _folderPicker;
    private readonly ISettingsStore _settings;
    private readonly IPhotoSummaryReader _summaryReader;
    private int _summaryVersion;
    private IReadOnlyList<PhotoItemViewModel>? _pendingSelection;

    public LibraryViewModel(
        IFolderPicker folderPicker,
        ISettingsStore settings,
        IPhotoSummaryReader summaryReader,
        MetadataEditorViewModel editor,
        ToolsViewModel tools,
        OrganiseViewModel organise,
        IMessenger messenger)
    {
        ArgumentNullException.ThrowIfNull(messenger);
        _folderPicker = folderPicker;
        _settings = settings;
        _summaryReader = summaryReader;
        Editor = editor;
        Tools = tools;
        Organise = organise;
        messenger.Register(this);
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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFolder), nameof(FolderName))]
    public partial string? CurrentFolder { get; private set; }

    public bool HasFolder => CurrentFolder is not null;

    public string FolderName => CurrentFolder is null ? string.Empty : Path.GetFileName(CurrentFolder) is { Length: > 0 } name ? name : CurrentFolder;

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

        CurrentFolder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        var files = await FillAsync(CurrentFolder);

        var settings = _settings.Load().WithRecentFolder(CurrentFolder);
        _settings.Save(settings);
        RecentFolders.Clear();
        foreach (var recent in settings.RecentFolders)
        {
            RecentFolders.Add(recent);
        }

        SelectedPhotos = [];
        await Editor.ShowSelectionAsync([]);
        StatusText = files.Count == 1 ? "1 photo" : $"{files.Count} photos";
        return true;
    }

    /// <summary>
    /// Re-reads the open folder after files were renamed, moved, copied or deleted, keeping the selection on the same
    /// photos (under their new names) where they're still in the folder.
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

    public async Task RefreshFolderAsync(IReadOnlyList<FileMove> moves)
    {
        ArgumentNullException.ThrowIfNull(moves);
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

        var moved = moves.GroupBy(m => m.From, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Last().To, StringComparer.OrdinalIgnoreCase);
        var keep = SelectedPhotos.Select(p => moved.GetValueOrDefault(p.FilePath) ?? p.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var files = await FillAsync(CurrentFolder);
        SelectedPhotos = [];
        await Editor.ShowSelectionAsync([]);
        StatusText = files.Count == 1 ? "1 photo" : $"{files.Count} photos";

        var reselect = Photos.Where(p => keep.Contains(p.FilePath)).ToList();
        if (reselect.Count > 0)
        {
            RequestSelection(reselect);
        }
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

    /// <summary>Lists the folder's photos into <see cref="Photos"/>.</summary>
    private async Task<List<string>> FillAsync(string folder)
    {
        var files = await Task.Run(() => Directory
            .EnumerateFiles(folder)
            .Where(SupportedFormats.IsSupported)
            .Order(StringComparer.CurrentCultureIgnoreCase)
            .ToList());

        Photos.Clear();
        foreach (var file in files)
        {
            Photos.Add(new PhotoItemViewModel(file));
        }

        Organise.SetFolder(folder, files);
        return files;
    }

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
