using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using PictureIt.Core.Abstractions;
using PictureIt.Core.Catalog;
using PictureIt.Core.Model;
using PictureIt.Core.Settings;
using PictureIt.Presentation.Services;

namespace PictureIt.Presentation.ViewModels;

/// <summary>
/// Shows the tags of the selected photos, grouped into sections, and saves edits through <see cref="BatchRunner"/>.
/// With several photos selected, fields that differ are "mixed" (see <see cref="TagFieldViewModel"/>), and an edit
/// applies to every selected photo.
/// </summary>
public sealed partial class MetadataEditorViewModel : ObservableObject, IRecipient<FilesChangedMessage>
{
    private readonly IMetadataReader _reader;
    private readonly BatchRunner _runner;
    private readonly IDialogService _dialogs;
    private readonly ISettingsStore _settings;
    private readonly List<TagFieldViewModel> _allFields = [];
    private readonly Dictionary<string, (DateTime Stamp, ImageMetadata Metadata)> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly bool _initialized;
    private readonly HashSet<TagCategory> _collapsed;
    private IReadOnlyList<ImageMetadata> _photos = [];
    private int _loadVersion;

    public MetadataEditorViewModel(
        IMetadataReader reader,
        BatchRunner runner,
        IDialogService dialogs,
        ISettingsStore settings,
        IMessenger messenger)
    {
        ArgumentNullException.ThrowIfNull(messenger);
        _reader = reader;
        _runner = runner;
        _dialogs = dialogs;
        _settings = settings;
        FilterText = string.Empty;
        var saved = settings.Load();
        ShowTagKeys = saved.ShowTagKeys;
        ShowAllTags = saved.ShowAllTags;
        _collapsed = [.. saved.CollapsedSections];
        messenger.Register(this);
        _initialized = true;
    }

    /// <summary>
    /// How long to wait before reading a multi-photo selection, so dragging or shift-clicking across a grid
    /// doesn't queue a read for every intermediate selection.
    /// </summary>
    public TimeSpan SelectionDelay { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>Sections with the fields that pass <see cref="FilterText"/>.</summary>
    public ObservableCollection<TagGroupViewModel> Groups { get; } = [];

    /// <summary>The paths most recently asked for, whether or not they've finished loading.</summary>
    public IReadOnlyList<string> RequestedPaths { get; private set; } = [];

    /// <summary>The loaded photos' metadata by path, for tools that work on the selection.</summary>
    public IReadOnlyDictionary<string, ImageMetadata> LoadedMetadata =>
        _photos.ToDictionary(p => p.FilePath, StringComparer.OrdinalIgnoreCase);

    public int PhotoCount => _photos.Count;

    /// <summary>The single loaded photo's path, or null when none or several are loaded.</summary>
    public string? FilePath => _photos.Count == 1 ? _photos[0].FilePath : null;

    /// <summary>"Big Ben.jpg", or "12 photos".</summary>
    public string Title => _photos.Count switch
    {
        0 => string.Empty,
        1 => Path.GetFileName(_photos[0].FilePath),
        var count => $"{count} photos",
    };

    public bool HasPhoto => _photos.Count > 0;

    public bool IsMultiple => _photos.Count > 1;

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    /// <summary>Explains an empty editor: no selection, or the photos couldn't be read.</summary>
    [ObservableProperty]
    public partial string? Message { get; private set; }

    /// <summary>Some selected photos couldn't be read and are left out; the others are shown.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWarning))]
    public partial string? Warning { get; private set; }

    public bool HasWarning => Warning is not null;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(RevertCommand))]
    public partial bool IsDirty { get; private set; }

    [ObservableProperty]
    public partial bool ShowTagKeys { get; set; }

    [ObservableProperty]
    public partial string FilterText { get; set; }

    /// <summary>Shows the metadata for the selected photos.</summary>
    public async Task ShowSelectionAsync(IReadOnlyList<string> filePaths)
    {
        ArgumentNullException.ThrowIfNull(filePaths);
        var version = ++_loadVersion;
        RequestedPaths = filePaths;
        if (filePaths.Count == 0)
        {
            Clear("Select a photo to see its metadata.");
            return;
        }

        IsLoading = true;
        if (filePaths.Count > 1 && SelectionDelay > TimeSpan.Zero)
        {
            await Task.Delay(SelectionDelay);
            if (version != _loadVersion)
            {
                return;
            }
        }

        await LoadAsync(filePaths, version);
    }

    public Task LoadAsync(string filePath) => ShowSelectionAsync([filePath]);

    /// <summary>
    /// Call before moving away from the current photos. Asks about unsaved changes; returns false if the user
    /// cancels (or saving fails), in which case the caller should stay put.
    /// </summary>
    public async Task<bool> ConfirmLeaveAsync()
    {
        if (!IsDirty)
        {
            return true;
        }

        switch (await _dialogs.AskToSaveChangesAsync(Title))
        {
            case UnsavedChangesChoice.Save:
                return await SaveCoreAsync();
            case UnsavedChangesChoice.Discard:
                Revert();
                return true;
            default:
                return false;
        }
    }

    /// <summary>Files were written elsewhere (a tool, an undo): forget cached reads and reload if they're showing.</summary>
    public void Receive(FilesChangedMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        foreach (var path in message.FilePaths)
        {
            _cache.Remove(path);
        }

        if (RequestedPaths.Count > 0 && message.FilePaths.Intersect(RequestedPaths, StringComparer.OrdinalIgnoreCase).Any())
        {
            _ = ShowSelectionAsync(RequestedPaths);
        }
    }

    [RelayCommand(CanExecute = nameof(IsDirty))]
    private async Task SaveAsync() => await SaveCoreAsync();

    [RelayCommand(CanExecute = nameof(IsDirty))]
    private void Revert()
    {
        foreach (var field in _allFields)
        {
            field.Revert();
        }
    }

    private async Task<bool> SaveCoreAsync()
    {
        if (_photos.Count == 0)
        {
            return true;
        }

        var dirty = _allFields.Where(f => f.IsDirty).ToList();
        if (dirty.FirstOrDefault(f => f.HasError) is { } invalid)
        {
            await _dialogs.ShowErrorAsync("Can't save yet", $"{invalid.DisplayName}: {invalid.Error}");
            return false;
        }

        List<MetadataEdit> edits = [.. dirty.SelectMany(f => f.ToEdits())];
        var description = _photos.Count == 1 ? $"Edit {Title}" : $"Edit {_photos.Count} photos";
        var editSet = EditSet.Create(description, _photos.Select(p => new FileEdits(p.FilePath, edits)));
        var current = LoadedMetadata;

        // Only formatting differed (e.g. "a;b" vs "a; b"): nothing to write, so quietly tidy up.
        if (ChangePreview.Create(editSet, current).IsEmpty)
        {
            dirty.ForEach(f => f.Revert());
            return true;
        }

        // On success the runner announces the changed files, and Receive reloads them.
        var result = await _runner.RunAsync(editSet, current);
        return result is { FailedCount: 0 };
    }

    private async Task LoadAsync(IReadOnlyList<string> filePaths, int version)
    {
        IsLoading = true;
        try
        {
            var results = await ReadAsync(filePaths);

            // A newer selection arrived while this one was loading.
            if (version != _loadVersion)
            {
                return;
            }

            var loaded = results.Where(r => r.Succeeded).Select(r => r.Metadata!).ToList();
            var failed = results.Where(r => !r.Succeeded).ToList();
            if (loaded.Count == 0)
            {
                Clear(failed.Count == 1
                    ? $"Couldn't read {Path.GetFileName(failed[0].FilePath)}: {failed[0].Error}"
                    : $"None of the {failed.Count} photos could be read.");
                return;
            }

            Message = null;
            Warning = failed.Count switch
            {
                0 => null,
                1 => $"Couldn't read {Path.GetFileName(failed[0].FilePath)}, so it isn't included: {failed[0].Error}",
                var count => $"{count} photos couldn't be read and aren't included.",
            };
            Show(loaded);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (version == _loadVersion)
            {
                Clear($"Couldn't read {(filePaths.Count == 1 ? Path.GetFileName(filePaths[0]) : "the photos")}: {ex.Message}");
            }
        }
        finally
        {
            if (version == _loadVersion)
            {
                IsLoading = false;
            }
        }
    }

    /// <summary>Reads files, reusing earlier reads of files that haven't changed on disk since.</summary>
    private async Task<IReadOnlyList<MetadataReadResult>> ReadAsync(IReadOnlyList<string> filePaths)
    {
        var stamps = filePaths.ToDictionary(p => p, File.GetLastWriteTimeUtc, StringComparer.OrdinalIgnoreCase);
        var toRead = filePaths.Where(p => !_cache.TryGetValue(p, out var hit) || hit.Stamp != stamps[p]).ToList();

        var fresh = toRead.Count == 0 ? [] : await _reader.ReadManyAsync(toRead);
        foreach (var result in fresh.Where(r => r.Succeeded))
        {
            _cache[result.FilePath] = (stamps.GetValueOrDefault(result.FilePath), result.Metadata!);
        }

        var freshByPath = fresh.ToDictionary(r => r.FilePath, StringComparer.OrdinalIgnoreCase);
        return [.. filePaths.Select(p => freshByPath.TryGetValue(p, out var result) ? result : new MetadataReadResult(p, _cache[p].Metadata, null))];
    }

    private void Show(IReadOnlyList<ImageMetadata> photos)
    {
        _photos = photos;
        _allFields.Clear();
        _allFields.AddRange(BuildFields(photos));
        _allFields.ForEach(f => f.ShowKey = ShowTagKeys);
        RefreshGroups();
        UpdateDirty();
        NotifySelectionChanged();
    }

    private void Clear(string message)
    {
        IsLoading = false;
        _photos = [];
        _allFields.Clear();
        Groups.Clear();
        Warning = null;
        Message = message;
        UpdateDirty();
        NotifySelectionChanged();
    }

    private void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(FilePath));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(HasPhoto));
        OnPropertyChanged(nameof(IsMultiple));
        OnPropertyChanged(nameof(PhotoCount));
        OnPropertyChanged(nameof(LoadedMetadata));
    }

    private List<TagFieldViewModel> BuildFields(IReadOnlyList<ImageMetadata> photos)
    {
        var fields = new List<TagFieldViewModel>();

        // The common fields always appear, so you can fill in a copyright the photos don't have yet.
        foreach (var common in TagCatalog.CommonFields)
        {
            var (value, mixed) = Merge(photos, common.Key, common.Editor == EditorKind.List);
            fields.Add(new TagFieldViewModel(common.Key, common.DisplayName, TagCategory.Common, common.Editor, value, isReadOnly: false, UpdateDirty, mixed));
        }

        var keys = photos.SelectMany(p => p.Tags).Select(t => t.Key).Distinct().Where(k => TagCatalog.FindCommonField(k) is null);
        foreach (var key in keys)
        {
            var tags = photos.Select(p => p.TryGetTag(key, out var tag) ? tag : null).ToList();
            var first = tags.First(t => t is not null)!;
            var isList = tags.Any(t => t?.IsList == true);
            var (value, mixed) = Merge(photos, key, isList);
            fields.Add(new TagFieldViewModel(
                key,
                first.Description,
                TagCatalog.CategoryOf(key),
                TagCatalog.EditorFor(key, isList),
                value,
                isReadOnly: tags.Any(t => t is { IsWritable: false }),
                UpdateDirty,
                mixed));
        }

        return fields;
    }

    /// <summary>The shared value, or for differing values: empty (text) or the items every photo has (lists).</summary>
    private static (string Value, bool Mixed) Merge(IReadOnlyList<ImageMetadata> photos, TagKey key, bool isList)
    {
        var values = photos.Select(p => p.TryGetTag(key, out var tag) ? tag.Values : (IReadOnlyList<string>)[]).ToList();
        if (values.All(v => v.SequenceEqual(values[0])))
        {
            return (TagCatalog.JoinList(values[0]), false);
        }

        return isList
            ? (TagCatalog.JoinList(values.Skip(1).Aggregate((IEnumerable<string>)values[0], (shared, v) => shared.Intersect(v))), true)
            : (string.Empty, true);
    }

    private void RefreshGroups()
    {
        // A filter searches every tag, and opens every section with a match.
        var filter = FilterText.Trim();
        var filtering = filter.Length > 0;
        var visible = filtering
            ? _allFields.Where(f => f.Matches(filter))
            : ShowAllTags ? _allFields : _allFields.Where(f => f.Category == TagCategory.Common);

        Groups.Clear();
        foreach (var group in visible.GroupBy(f => f.Category).OrderBy(g => g.Key))
        {
            // Common fields keep the catalog's order; other sections are alphabetical.
            var fields = group.Key == TagCategory.Common
                ? (IEnumerable<TagFieldViewModel>)group
                : group.OrderBy(f => f.DisplayName, StringComparer.CurrentCultureIgnoreCase).ThenBy(f => f.KeyText, StringComparer.Ordinal);
            var expanded = filtering || !_collapsed.Contains(group.Key);
            Groups.Add(new TagGroupViewModel(group.Key, fields, expanded, filtering ? null : OnSectionToggled));
        }

        OnPropertyChanged(nameof(HiddenTagCount));
    }

    /// <summary>How many tags "All tags" would add, for the toggle's tooltip.</summary>
    public int HiddenTagCount => ShowAllTags ? 0 : _allFields.Count(f => f.Category != TagCategory.Common);

    /// <summary>Show every tag, or just the common fields.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HiddenTagCount))]
    public partial bool ShowAllTags { get; set; }

    partial void OnShowAllTagsChanged(bool value)
    {
        if (_initialized)
        {
            RefreshGroups();
            _settings.Save(_settings.Load() with { ShowAllTags = value });
        }
    }

    /// <summary>Opens or closes every section except the common fields.</summary>
    [RelayCommand]
    private void SetAllSectionsExpanded(bool expanded)
    {
        foreach (var group in Groups.Where(g => g.Category != TagCategory.Common))
        {
            group.IsExpanded = expanded;
        }
    }

    private void OnSectionToggled(TagGroupViewModel group)
    {
        var changed = group.IsExpanded ? _collapsed.Remove(group.Category) : _collapsed.Add(group.Category);
        if (changed)
        {
            _settings.Save(_settings.Load() with { CollapsedSections = [.. _collapsed.Order()] });
        }
    }

    private void UpdateDirty() => IsDirty = _allFields.Any(f => f.IsDirty);

    partial void OnFilterTextChanged(string value) => RefreshGroups();

    partial void OnShowTagKeysChanged(bool value)
    {
        _allFields.ForEach(f => f.ShowKey = value);
        if (_initialized)
        {
            _settings.Save(_settings.Load() with { ShowTagKeys = value });
        }
    }
}
