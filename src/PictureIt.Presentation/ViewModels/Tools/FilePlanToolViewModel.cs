using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PictureIt.Core.Model;
using PictureIt.Core.Organise;

namespace PictureIt.Presentation.ViewModels.Tools;

/// <summary>One line of a rename or sort preview.</summary>
public sealed record FilePlanRow(string From, string To, string? Note, bool IsUnchanged)
{
    public bool HasNote => Note is not null;
}

/// <summary>
/// A tool that renames, moves or copies files from a <see cref="NamePattern"/>, with a preview that updates as the
/// options change. The dialog's confirm button carries the plan out, so the preview is the confirmation.
/// </summary>
public abstract partial class FilePlanToolViewModel : ToolViewModel
{
    /// <summary>The preview shows at most this many rows; the summary still counts them all.</summary>
    public const int MaxPreviewRows = 500;

    private string? _patternError;

    protected FilePlanToolViewModel(IReadOnlyList<ImageMetadata> photos, Func<string, bool> fileExists, string pattern)
        : base(photos.Count)
    {
        Photos = photos;
        FileExists = fileExists;
        Pattern = pattern;
    }

    public static IReadOnlyList<NameToken> Tokens => NamePattern.Tokens;

    protected IReadOnlyList<ImageMetadata> Photos { get; }

    protected Func<string, bool> FileExists { get; }

    [ObservableProperty]
    public partial string Pattern { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<FilePlanRow> Rows { get; private set; } = [];

    [ObservableProperty]
    public partial string Summary { get; private set; } = string.Empty;

    /// <summary>The current plan, or null while the options are invalid.</summary>
    public FilePlan? Plan { get; private set; }

    public override string Description => Plan?.Description ?? Title;

    /// <summary>Adds a token at the end of the pattern (the menu next to the pattern box).</summary>
    [RelayCommand]
    private void InsertToken(NameToken token)
    {
        ArgumentNullException.ThrowIfNull(token);
        Pattern += token.Example;
    }

    partial void OnPatternChanged(string value) => Refresh();

    /// <summary>Rebuilds the plan and preview. Call when an option changes.</summary>
    protected void Refresh()
    {
        Plan = null;
        _patternError = null;
        if (!NamePattern.TryParse(Pattern, out var pattern, out var error))
        {
            _patternError = error;
        }
        else
        {
            try
            {
                Plan = BuildPlan(pattern);
            }
            catch (ArgumentException ex)
            {
                _patternError = ex.Message;
            }
        }

        Rows = Plan is null ? [] : [.. Plan.Operations.Take(MaxPreviewRows).Select(ToRow)];
        Summary = Plan is null ? string.Empty : SummaryOf(Plan);
        OnPropertyChanged(nameof(Plan));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(ConfirmText));
    }

    /// <returns>The plan; throws <see cref="ArgumentException"/> if the options don't make sense.</returns>
    protected abstract FilePlan BuildPlan(NamePattern pattern);

    protected abstract FilePlanRow ToRow(PlannedFileOperation operation);

    protected abstract string SummaryOf(FilePlan plan);

    protected override string? Validate() =>
        _patternError ?? (Plan is { ChangeCount: 0 } ? "Nothing would change." : null);

    protected static string PhotosText(int count) => count == 1 ? "1 photo" : $"{count} photos";
}

/// <summary>Renames photos from their metadata, keeping each one's extension.</summary>
public sealed partial class RenameToolViewModel : FilePlanToolViewModel
{
    public const string DefaultPattern = "{DateTaken:yyyy-MM-dd}_{Seq:000}";

    private readonly bool _initialized;

    public RenameToolViewModel(IReadOnlyList<ImageMetadata> photos, Func<string, bool> fileExists, string pattern = DefaultPattern)
        : base(photos, fileExists, pattern)
    {
        OrderByDateTaken = true;
        FirstNumber = 1;
        _initialized = true;
        Refresh();
    }

    public override string Title => "Rename";

    public override string ConfirmText => Plan is { ChangeCount: > 0 } plan ? $"Rename {PhotosText(plan.ChangeCount)}" : "Rename";

    [ObservableProperty]
    public partial bool OrderByDateTaken { get; set; }

    /// <summary>Where {Seq} starts. A double because it's bound to a NumberBox.</summary>
    [ObservableProperty]
    public partial double FirstNumber { get; set; }

    partial void OnOrderByDateTakenChanged(bool value) => RefreshIfReady();

    partial void OnFirstNumberChanged(double value) => RefreshIfReady();

    protected override FilePlan BuildPlan(NamePattern pattern)
    {
        var first = double.IsNaN(FirstNumber) ? 1 : (int)Math.Clamp(FirstNumber, 0, 999_999);
        return FilePlanner.Rename(Photos, new RenameOptions(pattern, OrderByDateTaken, first), FileExists, CultureInfo.CurrentCulture);
    }

    protected override FilePlanRow ToRow(PlannedFileOperation operation) =>
        new(Path.GetFileName(operation.SourcePath), Path.GetFileName(operation.TargetPath), operation.Note, operation.IsUnchanged);

    protected override string SummaryOf(FilePlan plan)
    {
        var unchanged = plan.Operations.Count - plan.ChangeCount;
        return unchanged == 0
            ? $"{PhotosText(plan.ChangeCount)} will be renamed."
            : $"{PhotosText(plan.ChangeCount)} will be renamed; {unchanged} already {(unchanged == 1 ? "has" : "have")} that name.";
    }

    private void RefreshIfReady()
    {
        if (_initialized)
        {
            Refresh();
        }
    }
}

/// <summary>Moves or copies photos into folders built from their metadata, e.g. one per year and month.</summary>
public sealed partial class SortToolViewModel : FilePlanToolViewModel
{
    public const string DefaultPattern = @"{DateTaken:yyyy|Undated}\{DateTaken:MM}";

    private readonly Services.IFolderPicker _folderPicker;
    private readonly bool _initialized;

    public SortToolViewModel(
        IReadOnlyList<ImageMetadata> photos,
        Func<string, bool> fileExists,
        Services.IFolderPicker folderPicker,
        string destination,
        string pattern = DefaultPattern)
        : base(photos, fileExists, pattern)
    {
        _folderPicker = folderPicker;
        Destination = destination;
        _initialized = true;
        Refresh();
    }

    public override string Title => "Sort into folders";

    public override string ConfirmText => Plan is { ChangeCount: > 0 } plan ? $"{(Copy ? "Copy" : "Move")} {PhotosText(plan.ChangeCount)}" : "Sort";

    [ObservableProperty]
    public partial string Destination { get; set; }

    /// <summary>Copy, leaving the originals where they are, rather than move.</summary>
    [ObservableProperty]
    public partial bool Copy { get; set; }

    partial void OnDestinationChanged(string value)
    {
        if (_initialized)
        {
            Refresh();
        }
    }

    partial void OnCopyChanged(bool value)
    {
        if (_initialized)
        {
            Refresh();
        }
    }

    [RelayCommand]
    private async Task ChooseDestinationAsync()
    {
        if (await _folderPicker.PickFolderAsync() is { } folder)
        {
            Destination = folder;
        }
    }

    protected override FilePlan BuildPlan(NamePattern pattern)
    {
        if (string.IsNullOrWhiteSpace(Destination) || !Path.IsPathFullyQualified(Destination))
        {
            throw new ArgumentException("Choose a folder to sort into.");
        }

        return FilePlanner.Sort(Photos, new SortOptions(pattern, Destination, Copy), FileExists, CultureInfo.CurrentCulture);
    }

    protected override FilePlanRow ToRow(PlannedFileOperation operation)
    {
        var relative = Path.GetRelativePath(Destination, operation.TargetPath);
        return new FilePlanRow(Path.GetFileName(operation.SourcePath), relative, operation.Note, operation.IsUnchanged);
    }

    protected override string SummaryOf(FilePlan plan)
    {
        var folders = plan.Changes.Select(o => Path.GetDirectoryName(o.TargetPath)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var verb = Copy ? "copied" : "moved";
        var unchanged = plan.Operations.Count - plan.ChangeCount;
        var text = $"{PhotosText(plan.ChangeCount)} will be {verb} into {folders} {(folders == 1 ? "folder" : "folders")}.";
        return unchanged == 0 ? text : $"{text} {unchanged} {(unchanged == 1 ? "is" : "are")} already in the right place.";
    }
}
