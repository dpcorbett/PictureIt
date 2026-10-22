using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PictureIt.Core.Model;

namespace PictureIt.Presentation.ViewModels.Tools;

/// <summary>A tool's options, shown in a dialog with a confirm button that's enabled once they're valid.</summary>
public abstract partial class ToolViewModel : ObservableObject
{
    protected ToolViewModel(int photoCount)
    {
        PhotoCount = photoCount;
        PropertyChanged += OnAnyPropertyChanged;
    }

    public abstract string Title { get; }

    public virtual string ConfirmText => "Preview changes";

    public int PhotoCount { get; }

    public string PhotoCountText => PhotoCount == 1 ? "1 photo" : $"{PhotoCount} photos";

    /// <summary>Why the options can't be used yet, or null.</summary>
    public string? Error => Validate();

    public bool CanApply => Error is null;

    /// <summary>The description recorded in history, e.g. "Shift dates by +1 h on 12 photos".</summary>
    public abstract string Description { get; }

    protected abstract string? Validate();

    private void OnAnyPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(Error) or nameof(CanApply)))
        {
            OnPropertyChanged(nameof(Error));
            OnPropertyChanged(nameof(CanApply));
        }
    }
}

/// <summary>
/// A bulk metadata tool. Once confirmed, it turns each photo's current metadata into edits, which then go through the
/// usual preview and write.
/// </summary>
public abstract class MetadataToolViewModel(int photoCount) : ToolViewModel(photoCount)
{
    public abstract IReadOnlyList<MetadataEdit> EditsFor(ImageMetadata metadata);
}
