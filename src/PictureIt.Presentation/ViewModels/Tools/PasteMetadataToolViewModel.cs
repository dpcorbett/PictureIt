using CommunityToolkit.Mvvm.ComponentModel;
using PictureIt.Core.Model;
using PictureIt.Core.Operations;

namespace PictureIt.Presentation.ViewModels.Tools;

/// <summary>Pastes chosen groups of fields from a copied photo onto the selection.</summary>
public sealed partial class PasteMetadataToolViewModel : MetadataToolViewModel
{
    private readonly ImageMetadata _source;

    public PasteMetadataToolViewModel(int photoCount, ImageMetadata source)
        : base(photoCount)
    {
        _source = source;

        // Start with whatever the source photo actually has.
        var present = CopyMetadata.GroupsPresentIn(source);
        IncludeDescription = present.HasFlag(MetadataGroups.Description);
        IncludeRights = present.HasFlag(MetadataGroups.Rights);
        IncludeLocation = present.HasFlag(MetadataGroups.Location);
        IncludeDateTaken = false;
        IncludeRating = present.HasFlag(MetadataGroups.Rating);
    }

    public override string Title => "Paste metadata";

    public string SourceName => Path.GetFileName(_source.FilePath);

    /// <summary>Title, description and keywords.</summary>
    [ObservableProperty]
    public partial bool IncludeDescription { get; set; }

    [ObservableProperty]
    public partial bool IncludeRights { get; set; }

    [ObservableProperty]
    public partial bool IncludeLocation { get; set; }

    /// <summary>Off by default: a date taken rarely belongs on other photos.</summary>
    [ObservableProperty]
    public partial bool IncludeDateTaken { get; set; }

    [ObservableProperty]
    public partial bool IncludeRating { get; set; }

    public MetadataGroups Groups =>
        (IncludeDescription ? MetadataGroups.Description : MetadataGroups.None)
        | (IncludeRights ? MetadataGroups.Rights : MetadataGroups.None)
        | (IncludeLocation ? MetadataGroups.Location : MetadataGroups.None)
        | (IncludeDateTaken ? MetadataGroups.DateTaken : MetadataGroups.None)
        | (IncludeRating ? MetadataGroups.Rating : MetadataGroups.None);

    public override string Description => $"Paste metadata from {SourceName} onto {PhotoCountText}";

    public override IReadOnlyList<MetadataEdit> EditsFor(ImageMetadata metadata) => CopyMetadata.EditsFrom(_source, Groups);

    protected override string? Validate() => Groups == MetadataGroups.None ? "Choose what to paste." : null;
}
