using CommunityToolkit.Mvvm.ComponentModel;
using PictureIt.Core.Model;
using PictureIt.Core.Operations;

namespace PictureIt.Presentation.ViewModels.Tools;

public sealed partial class FindReplaceToolViewModel : MetadataToolViewModel
{
    public FindReplaceToolViewModel(int photoCount)
        : base(photoCount)
    {
        Find = string.Empty;
        Replace = string.Empty;
    }

    public override string Title => "Find and replace";

    [ObservableProperty]
    public partial string Find { get; set; }

    [ObservableProperty]
    public partial string Replace { get; set; }

    [ObservableProperty]
    public partial bool UseRegex { get; set; }

    [ObservableProperty]
    public partial bool MatchCase { get; set; }

    /// <summary>Search every text tag rather than only the common fields.</summary>
    [ObservableProperty]
    public partial bool AllTextTags { get; set; }

    public override string Description => $"Replace \"{Find}\" with \"{Replace}\" in {PhotoCountText}";

    private FindReplaceOptions Options =>
        new(Find, Replace, UseRegex, MatchCase, AllTextTags ? FindReplaceScope.AllTextTags : FindReplaceScope.CommonFields);

    public override IReadOnlyList<MetadataEdit> EditsFor(ImageMetadata metadata) => FindReplace.EditsFor(metadata, Options);

    protected override string? Validate() => Options.Validate();
}
