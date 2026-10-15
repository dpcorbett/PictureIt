using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PictureIt.Core.Abstractions;
using PictureIt.Core.Catalog;
using PictureIt.Core.Model;
using PictureIt.Core.Settings;
using PictureIt.Presentation.Services;

namespace PictureIt.Presentation.ViewModels;

/// <summary>
/// Shows one photo's tags grouped into sections, tracks edits, and saves them through the standard pipeline:
/// build an EditSet, preview it, confirm, then write (with backup).
/// </summary>
public sealed partial class MetadataEditorViewModel : ObservableObject
{
    private readonly IMetadataReader _reader;
    private readonly IMetadataWriter _writer;
    private readonly IDialogService _dialogs;
    private readonly ISettingsStore _settings;
    private readonly List<TagFieldViewModel> _allFields = [];
    private ImageMetadata? _metadata;
    private int _loadVersion;

    public MetadataEditorViewModel(
        IMetadataReader reader,
        IMetadataWriter writer,
        IDialogService dialogs,
        ISettingsStore settings)
    {
        _reader = reader;
        _writer = writer;
        _dialogs = dialogs;
        _settings = settings;
        FilterText = string.Empty;
        ShowTagKeys = settings.Load().ShowTagKeys;
        _initialized = true;
    }

    private readonly bool _initialized;

    /// <summary>Sections with the fields that pass <see cref="FilterText"/>.</summary>
    public ObservableCollection<TagGroupViewModel> Groups { get; } = [];

    public string? FilePath => _metadata?.FilePath;

    public string FileName => FilePath is null ? string.Empty : Path.GetFileName(FilePath);

    public bool HasPhoto => _metadata is not null;

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    /// <summary>Explains why there's nothing to edit: no selection, several photos, or a read error.</summary>
    [ObservableProperty]
    public partial string? Message { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(RevertCommand))]
    public partial bool IsDirty { get; private set; }

    [ObservableProperty]
    public partial bool ShowTagKeys { get; set; }

    [ObservableProperty]
    public partial string FilterText { get; set; }

    /// <summary>Shows the metadata for the selected photos; editing needs exactly one.</summary>
    public async Task ShowSelectionAsync(IReadOnlyList<string> filePaths)
    {
        ArgumentNullException.ThrowIfNull(filePaths);
        switch (filePaths.Count)
        {
            case 0:
                Clear("Select a photo to see its metadata.");
                break;
            case 1:
                await LoadAsync(filePaths[0]);
                break;
            default:
                Clear($"{filePaths.Count} photos selected. Select a single photo to edit its metadata.");
                break;
        }
    }

    public async Task LoadAsync(string filePath)
    {
        var version = ++_loadVersion;
        IsLoading = true;
        try
        {
            var metadata = await _reader.ReadAsync(filePath);

            // A newer selection arrived while this one was loading.
            if (version != _loadVersion)
            {
                return;
            }

            Show(metadata);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (version == _loadVersion)
            {
                Clear($"Couldn't read {Path.GetFileName(filePath)}: {ex.Message}");
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

    /// <summary>
    /// Call before moving away from the current photo. Asks about unsaved changes; returns false if the user
    /// cancels (or saving fails), in which case the caller should stay put.
    /// </summary>
    public async Task<bool> ConfirmLeaveAsync()
    {
        if (!IsDirty)
        {
            return true;
        }

        switch (await _dialogs.AskToSaveChangesAsync(FileName))
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
        if (_metadata is null)
        {
            return true;
        }

        var dirty = _allFields.Where(f => f.IsDirty).ToList();
        if (dirty.FirstOrDefault(f => f.HasError) is { } invalid)
        {
            await _dialogs.ShowErrorAsync("Can't save yet", $"{invalid.DisplayName}: {invalid.Error}");
            return false;
        }

        var path = _metadata.FilePath;
        var editSet = EditSet.Create($"Edit {FileName}", [new FileEdits(path, [.. dirty.Select(f => f.ToEdit())])]);
        var preview = ChangePreview.Create(editSet, new Dictionary<string, ImageMetadata> { [path] = _metadata });

        // Only formatting differed (e.g. "a;b" vs "a; b"): nothing to write.
        if (preview.IsEmpty)
        {
            dirty.ForEach(f => f.Revert());
            return true;
        }

        if (!await _dialogs.ConfirmChangesAsync(preview))
        {
            return false;
        }

        IsLoading = true;
        try
        {
            var result = await _writer.ApplyAsync(editSet);
            var file = result.Files.Single();
            if (!file.Succeeded)
            {
                await _dialogs.ShowErrorAsync($"Couldn't save {FileName}", file.Error ?? "ExifTool did not update the file.");
                return false;
            }
        }
        finally
        {
            IsLoading = false;
        }

        // Re-read so the editor shows exactly what's in the file now (including MWG-synced copies).
        await LoadAsync(path);
        return true;
    }

    private void Show(ImageMetadata metadata)
    {
        _metadata = metadata;
        Message = null;
        _allFields.Clear();
        _allFields.AddRange(BuildFields(metadata));
        _allFields.ForEach(f => f.ShowKey = ShowTagKeys);
        RefreshGroups();
        UpdateDirty();
        OnPropertyChanged(nameof(FilePath));
        OnPropertyChanged(nameof(FileName));
        OnPropertyChanged(nameof(HasPhoto));
    }

    private void Clear(string message)
    {
        ++_loadVersion;
        IsLoading = false;
        _metadata = null;
        _allFields.Clear();
        Groups.Clear();
        Message = message;
        UpdateDirty();
        OnPropertyChanged(nameof(FilePath));
        OnPropertyChanged(nameof(FileName));
        OnPropertyChanged(nameof(HasPhoto));
    }

    private IEnumerable<TagFieldViewModel> BuildFields(ImageMetadata metadata)
    {
        // The common fields always appear, so you can fill in a copyright the file doesn't have yet.
        foreach (var common in TagCatalog.CommonFields)
        {
            var value = metadata.TryGetTag(common.Key, out var tag) ? Display(tag) : string.Empty;
            yield return new TagFieldViewModel(common.Key, common.DisplayName, TagCategory.Common, common.Editor, value, isReadOnly: false, UpdateDirty);
        }

        foreach (var tag in metadata.Tags)
        {
            if (TagCatalog.FindCommonField(tag.Key) is not null)
            {
                continue;
            }

            yield return new TagFieldViewModel(
                tag.Key,
                tag.Description,
                TagCatalog.CategoryOf(tag.Key),
                TagCatalog.EditorFor(tag.Key, tag.IsList),
                Display(tag),
                isReadOnly: !tag.IsWritable,
                UpdateDirty);
        }
    }

    private static string Display(MetadataTag tag) => TagCatalog.JoinList(tag.Values);

    private void RefreshGroups()
    {
        var filter = FilterText.Trim();
        var visible = filter.Length == 0 ? _allFields : _allFields.Where(f => f.Matches(filter));

        Groups.Clear();
        foreach (var group in visible
            .GroupBy(f => f.Category)
            .OrderBy(g => g.Key))
        {
            // Common fields keep the catalog's order; other sections are alphabetical.
            var fields = group.Key == TagCategory.Common
                ? (IEnumerable<TagFieldViewModel>)group
                : group.OrderBy(f => f.DisplayName, StringComparer.CurrentCultureIgnoreCase).ThenBy(f => f.KeyText, StringComparer.Ordinal);
            Groups.Add(new TagGroupViewModel(group.Key, fields));
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
