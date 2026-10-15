using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PictureIt.Core.Model;
using PictureIt.Core.Operations;
using PictureIt.Presentation.Services;

namespace PictureIt.Presentation.ViewModels.Tools;

/// <summary>Privacy clean-up before sharing, either in place or on copies in another folder.</summary>
public sealed partial class RemoveMetadataToolViewModel : MetadataToolViewModel
{
    private readonly IFolderPicker _folderPicker;

    public RemoveMetadataToolViewModel(int photoCount, IFolderPicker folderPicker)
        : base(photoCount)
    {
        _folderPicker = folderPicker;
        GpsLocation = true;
        CopyFolder = string.Empty;
    }

    public override string Title => "Remove metadata";

    [ObservableProperty]
    public partial bool GpsLocation { get; set; }

    [ObservableProperty]
    public partial bool PlaceNames { get; set; }

    [ObservableProperty]
    public partial bool CameraIdentifiers { get; set; }

    /// <summary>Removes all metadata except orientation and colour profile; the other options don't apply.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PartialOptionsEnabled))]
    public partial bool Everything { get; set; }

    public bool PartialOptionsEnabled => !Everything;

    /// <summary>Write cleaned copies to <see cref="CopyFolder"/> and leave the originals alone.</summary>
    [ObservableProperty]
    public partial bool SaveAsCopies { get; set; }

    [ObservableProperty]
    public partial string CopyFolder { get; set; }

    public PrivacyOptions Options =>
        Everything
            ? PrivacyOptions.Everything
            : (GpsLocation ? PrivacyOptions.GpsLocation : PrivacyOptions.None)
              | (PlaceNames ? PrivacyOptions.PlaceNames : PrivacyOptions.None)
              | (CameraIdentifiers ? PrivacyOptions.CameraIdentifiers : PrivacyOptions.None);

    public override string Description => SaveAsCopies
        ? $"Remove metadata from copies of {PhotoCountText} in {Path.GetFileName(CopyFolder)}"
        : $"Remove metadata from {PhotoCountText}";

    public override IReadOnlyList<MetadataEdit> EditsFor(ImageMetadata metadata) => PrivacyStrip.EditsFor(metadata, Options);

    protected override string? Validate()
    {
        if (Options == PrivacyOptions.None)
        {
            return "Choose what to remove.";
        }

        if (SaveAsCopies && string.IsNullOrWhiteSpace(CopyFolder))
        {
            return "Choose a folder for the copies.";
        }

        return null;
    }

    [RelayCommand]
    private async Task ChooseCopyFolderAsync()
    {
        if (await _folderPicker.PickFolderAsync() is { } folder)
        {
            CopyFolder = folder;
            SaveAsCopies = true;
        }
    }
}
