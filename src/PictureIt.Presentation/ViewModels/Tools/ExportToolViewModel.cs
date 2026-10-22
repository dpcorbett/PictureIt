using CommunityToolkit.Mvvm.ComponentModel;
using PictureIt.Core.Organise;

namespace PictureIt.Presentation.ViewModels.Tools;

/// <summary>Options for exporting the selected photos' metadata to a CSV or JSON file.</summary>
public sealed partial class ExportToolViewModel : ToolViewModel
{
    public ExportToolViewModel(int photoCount)
        : base(photoCount)
    {
    }

    public override string Title => "Export metadata";

    public override string ConfirmText => "Export…";

    public override string Description => $"Export metadata of {PhotoCountText}";

    /// <summary>0 = CSV, 1 = JSON (bound to a radio button group).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Format))]
    public partial int FormatIndex { get; set; }

    /// <summary>0 = common fields, 1 = every tag.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Fields))]
    public partial int FieldsIndex { get; set; }

    public ExportFormat Format => FormatIndex == 1 ? ExportFormat.Json : ExportFormat.Csv;

    public ExportFields Fields => FieldsIndex == 1 ? ExportFields.All : ExportFields.Common;

    public string Extension => Format == ExportFormat.Json ? ".json" : ".csv";

    public string TypeName => Format == ExportFormat.Json ? "JSON" : "CSV spreadsheet";

    protected override string? Validate() => null;
}

/// <summary>Options for a duplicate scan of the open folder.</summary>
public sealed partial class FindDuplicatesToolViewModel : ToolViewModel
{
    public FindDuplicatesToolViewModel(int photoCount)
        : base(photoCount)
    {
    }

    public override string Title => "Find duplicates";

    public override string ConfirmText => "Search";

    public override string Description => $"Find duplicates among {PhotoCountText}";

    /// <summary>Also group different files with the same camera and time taken.</summary>
    [ObservableProperty]
    public partial bool IncludeSameMoment { get; set; }

    protected override string? Validate() => PhotoCount < 2 ? "The folder needs at least two photos." : null;
}
