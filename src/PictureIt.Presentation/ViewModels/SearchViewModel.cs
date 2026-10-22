using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using PictureIt.Core.Search;
using PictureIt.Presentation.Services;

namespace PictureIt.Presentation.ViewModels;

/// <summary>A catalogue folder on the Search page.</summary>
public sealed class CatalogFolderViewModel(CatalogFolder folder)
{
    public string Path { get; } = folder.Path;

    public string Name { get; } = System.IO.Path.GetFileName(folder.Path) is { Length: > 0 } name ? name : folder.Path;

    public string Summary { get; } =
        $"{(folder.PhotoCount == 1 ? "1 photo" : $"{folder.PhotoCount:N0} photos")} · " +
        (folder.ScannedAt is { } at ? $"scanned {at.ToLocalTime().ToString("d MMM, HH:mm", CultureInfo.CurrentCulture)}" : "not scanned yet");

    public string RescanLabel => $"Rescan {Name}";

    public string RemoveLabel => $"Remove {Name} from the catalogue";
}

/// <summary>
/// Searches the catalogue across folders: free text plus filters, updating as they change. Results can be shown
/// in the Library, where every editing tool works on them. Also manages saved searches, statistics and the
/// catalogue's folders.
/// </summary>
public sealed partial class SearchViewModel : ObservableObject, IRecipient<CatalogChangedMessage>
{
    /// <summary>The first choice in the camera, lens and keyword lists, meaning "don't filter".</summary>
    public const string Any = "Any";

    /// <summary>How many results are listed on the page (all of them go to the Library).</summary>
    public const int PreviewLimit = 300;

    private readonly IPhotoCatalog _catalog;
    private readonly SearchServices _services;
    private readonly bool _initialized;
    private int _searchVersion;
    private bool _applying;

    public SearchViewModel(IPhotoCatalog catalog, SearchServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _catalog = catalog;
        _services = services;
        Text = string.Empty;
        Camera = Any;
        Lens = Any;
        Keyword = Any;
        ClearRanges();
        Summary = string.Empty;
        SavedSearches = [.. services.SavedSearches.Load()];
        services.Messenger.Register(this);
        PropertyChanged += OnFilterChanged;
        _initialized = true;
    }

    /// <summary>How long to wait after typing before searching. Zero in tests.</summary>
    public TimeSpan SearchDelay { get; set; } = TimeSpan.FromMilliseconds(250);

    public ObservableCollection<CatalogFolderViewModel> Folders { get; } = [];

    public bool HasFolders => Folders.Count > 0;

    public ObservableCollection<string> Cameras { get; } = [Any];

    public ObservableCollection<string> Lenses { get; } = [Any];

    public ObservableCollection<string> Keywords { get; } = [Any];

    public ObservableCollection<SavedSearch> SavedSearches { get; }

    public ObservableCollection<CatalogEntry> Results { get; } = [];

    // Filters

    [ObservableProperty]
    public partial string Text { get; set; }

    [ObservableProperty]
    public partial string Camera { get; set; }

    [ObservableProperty]
    public partial string Lens { get; set; }

    [ObservableProperty]
    public partial string Keyword { get; set; }

    [ObservableProperty]
    public partial DateTimeOffset? TakenFrom { get; set; }

    [ObservableProperty]
    public partial DateTimeOffset? TakenTo { get; set; }

    /// <summary>0 = any, otherwise the fewest stars.</summary>
    [ObservableProperty]
    public partial int MinRating { get; set; }

    // Ranges are doubles because they're bound to NumberBoxes (NaN when empty).

    [ObservableProperty]
    public partial double MinFocalLength { get; set; }

    [ObservableProperty]
    public partial double MaxFocalLength { get; set; }

    [ObservableProperty]
    public partial double MinFNumber { get; set; }

    [ObservableProperty]
    public partial double MaxFNumber { get; set; }

    [ObservableProperty]
    public partial double MinIso { get; set; }

    [ObservableProperty]
    public partial double MaxIso { get; set; }

    /// <summary>0 = any, 1 = has, 2 = missing (bound to a ComboBox).</summary>
    [ObservableProperty]
    public partial int LocationIndex { get; set; }

    [ObservableProperty]
    public partial int DateTakenIndex { get; set; }

    [ObservableProperty]
    public partial int CopyrightIndex { get; set; }

    [ObservableProperty]
    public partial int KeywordsIndex { get; set; }

    // Results

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResults))]
    [NotifyCanExecuteChangedFor(nameof(ShowInLibraryCommand))]
    public partial int ResultCount { get; private set; }

    public bool HasResults => ResultCount > 0;

    /// <summary>"124 photos · Canon EOS R5 · no copyright".</summary>
    [ObservableProperty]
    public partial string Summary { get; private set; }

    /// <summary>A scan's outcome or a problem, shown above the results.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    public partial string? Message { get; private set; }

    public bool HasMessage => !string.IsNullOrEmpty(Message);

    [ObservableProperty]
    public partial bool IsSearching { get; private set; }

    /// <summary>Charts for the current results, once <see cref="LoadStatisticsCommand"/> has run.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatistics))]
    public partial CatalogStatistics? Statistics { get; private set; }

    public bool HasStatistics => Statistics is not null;

    /// <summary>Show charts of the results instead of the photos.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPhotos))]
    public partial bool ShowStatistics { get; set; }

    public bool ShowPhotos => !ShowStatistics;

    partial void OnShowStatisticsChanged(bool value)
    {
        if (value && _initialized)
        {
            LoadStatisticsCommand.Execute(null);
        }
    }

    public PhotoQuery Query => new()
    {
        Text = string.IsNullOrWhiteSpace(Text) ? null : Text.Trim(),
        Camera = Filter(Camera),
        Lens = Filter(Lens),
        Keyword = Filter(Keyword),
        TakenFrom = TakenFrom is { } from ? DateOnly.FromDateTime(from.Date) : null,
        TakenTo = TakenTo is { } to ? DateOnly.FromDateTime(to.Date) : null,
        MinRating = MinRating > 0 ? MinRating : null,
        MinFocalLength = Number(MinFocalLength),
        MaxFocalLength = Number(MaxFocalLength),
        MinFNumber = Number(MinFNumber),
        MaxFNumber = Number(MaxFNumber),
        MinIso = Number(MinIso) is { } minIso ? (int)minIso : null,
        MaxIso = Number(MaxIso) is { } maxIso ? (int)maxIso : null,
        Location = (Presence)LocationIndex,
        DateTaken = (Presence)DateTakenIndex,
        Copyright = (Presence)CopyrightIndex,
        Keywords = (Presence)KeywordsIndex,
    };

    /// <summary>Loads folders, filter lists and results. Call when the page opens.</summary>
    public async Task LoadAsync()
    {
        await RefreshCatalogInfoAsync();
        await SearchAsync();
    }

    public async void Receive(CatalogChangedMessage message)
    {
        await RefreshCatalogInfoAsync();
        await SearchAsync();
    }

    /// <summary>Runs the search now (the page also searches as filters change).</summary>
    public async Task SearchAsync()
    {
        var version = ++_searchVersion;
        var query = Query;
        IsSearching = true;
        try
        {
            var count = await _catalog.CountAsync(query);
            var preview = await _catalog.SearchAsync(query, PreviewLimit);
            if (version != _searchVersion)
            {
                return;
            }

            ResultCount = count;
            Summary = $"{(count == 1 ? "1 photo" : $"{count:N0} photos")} · {query.Describe()}";
            Results.Clear();
            foreach (var entry in preview)
            {
                Results.Add(entry);
            }

            if (ShowStatistics)
            {
                await LoadStatisticsAsync();
            }
            else
            {
                Statistics = null;
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.Data.Common.DbException)
        {
            Message = $"Couldn't search the catalogue: {ex.Message}";
        }
        finally
        {
            if (version == _searchVersion)
            {
                IsSearching = false;
            }
        }
    }

    [RelayCommand]
    private void ClearFilters()
    {
        Apply(PhotoQuery.All);
    }

    [RelayCommand]
    private async Task AddFolderAsync()
    {
        if (await _services.FolderPicker.PickFolderAsync() is { } folder)
        {
            await AddFolderAsync(folder);
        }
    }

    /// <summary>Adds a folder (and everything below it) to the catalogue and scans it.</summary>
    public async Task AddFolderAsync(string folder)
    {
        var root = await _catalog.AddFolderAsync(folder);
        if (!string.Equals(root, Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)), StringComparison.OrdinalIgnoreCase))
        {
            Message = $"{folder} is already in the catalogue, inside {root}.";
        }

        await ScanAsync(root);
        await _services.Sync.WatchFoldersAsync();
    }

    [RelayCommand]
    private async Task RescanAsync(CatalogFolderViewModel folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        await ScanAsync(folder.Path);
    }

    [RelayCommand]
    private async Task RescanAllAsync()
    {
        foreach (var folder in Folders.ToList())
        {
            if (!await ScanAsync(folder.Path))
            {
                break;
            }
        }
    }

    [RelayCommand]
    private async Task RemoveFolderAsync(CatalogFolderViewModel folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        if (!await _services.Dialogs.ConfirmAsync(
                $"Remove {folder.Name} from the catalogue?",
                "Its photos won't show up in searches any more. Nothing on disk is changed.",
                "Remove"))
        {
            return;
        }

        await _catalog.RemoveFolderAsync(folder.Path);
        await _services.Sync.WatchFoldersAsync();
        Message = null;
        await LoadAsync();
    }

    [RelayCommand(CanExecute = nameof(HasResults))]
    private async Task ShowInLibraryAsync()
    {
        var query = Query;
        async Task<IReadOnlyList<string>> Find() =>
            [.. (await _catalog.SearchAsync(query)).Select(e => e.FilePath).Where(File.Exists)];

        if (await _services.Library.ShowResultsAsync(query.Describe(), await Find(), Find))
        {
            _services.Messenger.Send(new NavigateMessage(NavigateMessage.Library));
        }
    }

    [RelayCommand]
    private async Task SaveSearchAsync()
    {
        var name = await _services.Dialogs.PromptAsync("Save search", "Name", Query.Describe());
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var searches = SavedSearches.Where(s => !s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Append(new SavedSearch(name, Query)).ToList();
        _services.SavedSearches.Save(searches);
        SavedSearches.Clear();
        foreach (var search in searches)
        {
            SavedSearches.Add(search);
        }
    }

    [RelayCommand]
    private void ApplySavedSearch(SavedSearch search)
    {
        ArgumentNullException.ThrowIfNull(search);
        Apply(search.Query);
    }

    [RelayCommand]
    private void DeleteSavedSearch(SavedSearch search)
    {
        ArgumentNullException.ThrowIfNull(search);
        SavedSearches.Remove(search);
        _services.SavedSearches.Save([.. SavedSearches]);
    }

    [RelayCommand]
    private async Task LoadStatisticsAsync()
    {
        var all = await _catalog.SearchAsync(Query);
        Statistics = await Task.Run(() => CatalogStatistics.From(all, CultureInfo.CurrentCulture));
    }

    /// <summary>Sets every filter from a query (e.g. a saved search) and searches once.</summary>
    public void Apply(PhotoQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        _applying = true;
        try
        {
            Text = query.Text ?? string.Empty;
            Camera = Choose(Cameras, query.Camera);
            Lens = Choose(Lenses, query.Lens);
            Keyword = Choose(Keywords, query.Keyword);
            TakenFrom = query.TakenFrom is { } from ? new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue)) : null;
            TakenTo = query.TakenTo is { } to ? new DateTimeOffset(to.ToDateTime(TimeOnly.MinValue)) : null;
            MinRating = query.MinRating ?? 0;
            MinFocalLength = query.MinFocalLength ?? double.NaN;
            MaxFocalLength = query.MaxFocalLength ?? double.NaN;
            MinFNumber = query.MinFNumber ?? double.NaN;
            MaxFNumber = query.MaxFNumber ?? double.NaN;
            MinIso = query.MinIso ?? double.NaN;
            MaxIso = query.MaxIso ?? double.NaN;
            LocationIndex = (int)query.Location;
            DateTakenIndex = (int)query.DateTaken;
            CopyrightIndex = (int)query.Copyright;
            KeywordsIndex = (int)query.Keywords;
        }
        finally
        {
            _applying = false;
        }

        _ = SearchAsync();
    }

    /// <returns>False if the scan was cancelled or failed.</returns>
    private async Task<bool> ScanAsync(string folder)
    {
        try
        {
            var result = await _services.Dialogs.RunWithProgressAsync(
                $"Scanning {Path.GetFileName(folder)}",
                (progress, cancellationToken) => _catalog.ScanAsync(folder, progress, cancellationToken));
            Message = Describe(folder, result);
            return true;
        }
        catch (OperationCanceledException)
        {
            Message = $"Scan of {Path.GetFileName(folder)} stopped. Photos read so far are in the catalogue; rescan to finish.";
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Data.Common.DbException)
        {
            Message = $"Couldn't scan {folder}: {ex.Message}";
            return false;
        }
        finally
        {
            await LoadAsync();
        }
    }

    private static string Describe(string folder, ScanResult result)
    {
        var name = Path.GetFileName(folder);
        var text = result.Changed == 0
            ? $"{name} is up to date."
            : $"Scanned {name}: {result.Added:N0} new, {result.Updated:N0} updated, {result.Removed:N0} removed.";
        return result.Failed.Count == 0 ? text : $"{text} {result.Failed.Count} couldn't be read (they're listed by file name only).";
    }

    private async Task RefreshCatalogInfoAsync()
    {
        try
        {
            var folders = await _catalog.GetFoldersAsync();
            Folders.Clear();
            foreach (var folder in folders)
            {
                Folders.Add(new CatalogFolderViewModel(folder));
            }

            OnPropertyChanged(nameof(HasFolders));

            var facets = await _catalog.GetFacetsAsync();
            _applying = true;
            try
            {
                Camera = Reset(Cameras, facets.Cameras, Camera);
                Lens = Reset(Lenses, facets.Lenses, Lens);
                Keyword = Reset(Keywords, facets.Keywords, Keyword);
            }
            finally
            {
                _applying = false;
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.Data.Common.DbException)
        {
            Message = $"Couldn't read the catalogue: {ex.Message}";
        }
    }

    /// <summary>Refills a filter list, keeping the current choice if it's still there.</summary>
    private static string Reset(ObservableCollection<string> list, IReadOnlyList<FacetValue> values, string current)
    {
        var wanted = values.Select(v => v.Value).ToList();
        if (!list.Skip(1).SequenceEqual(wanted))
        {
            list.Clear();
            list.Add(Any);
            foreach (var value in wanted)
            {
                list.Add(value);
            }
        }

        return list.Contains(current) ? current : Any;
    }

    private static string Choose(ObservableCollection<string> list, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Any;
        }

        // A saved search may name something no longer in the catalogue; keep it so the search means the same.
        if (!list.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            list.Add(value);
        }

        return list.First(v => v.Equals(value, StringComparison.OrdinalIgnoreCase));
    }

    private static string? Filter(string value) => string.IsNullOrWhiteSpace(value) || value == Any ? null : value;

    private static double? Number(double value) => double.IsNaN(value) ? null : value;

    private void ClearRanges()
    {
        MinFocalLength = MaxFocalLength = MinFNumber = MaxFNumber = MinIso = MaxIso = double.NaN;
    }

    private async void OnFilterChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_initialized || _applying || e.PropertyName is not (
                nameof(Text) or nameof(Camera) or nameof(Lens) or nameof(Keyword) or nameof(TakenFrom) or nameof(TakenTo)
                or nameof(MinRating) or nameof(MinFocalLength) or nameof(MaxFocalLength) or nameof(MinFNumber) or nameof(MaxFNumber)
                or nameof(MinIso) or nameof(MaxIso) or nameof(LocationIndex) or nameof(DateTakenIndex) or nameof(CopyrightIndex)
                or nameof(KeywordsIndex)))
        {
            return;
        }

        // Typing searches once it pauses.
        var version = ++_searchVersion;
        if (SearchDelay > TimeSpan.Zero)
        {
            await Task.Delay(SearchDelay);
        }

        if (version == _searchVersion)
        {
            await SearchAsync();
        }
    }
}

/// <summary>The services <see cref="SearchViewModel"/> uses, gathered so its constructor stays readable.</summary>
public sealed record SearchServices(
    LibraryViewModel Library,
    CatalogSync Sync,
    ISavedSearchStore SavedSearches,
    IDialogService Dialogs,
    IFolderPicker FolderPicker,
    IMessenger Messenger);
