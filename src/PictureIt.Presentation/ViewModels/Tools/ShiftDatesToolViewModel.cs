using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using PictureIt.Core.Model;
using PictureIt.Core.Operations;

namespace PictureIt.Presentation.ViewModels.Tools;

/// <summary>
/// Fixes photo dates, either by a fixed amount or by saying when one photo was really taken
/// (the same correction then applies to every selected photo).
/// </summary>
public sealed partial class ShiftDatesToolViewModel : MetadataToolViewModel
{
    public ShiftDatesToolViewModel(int photoCount, ImageMetadata? reference)
        : base(photoCount)
    {
        ReferenceName = reference is null ? null : Path.GetFileName(reference.FilePath);
        ReferenceDateTaken = reference is null ? null : DateShift.DateTaken(reference);
        var start = ReferenceDateTaken ?? DateTime.Today;
        CorrectDate = start.Date;
        CorrectTime = start.TimeOfDay;
        TimeZone = "+00:00";
    }

    public override string Title => "Shift dates";

    /// <summary>True: correct by telling us when the reference photo was taken. False: shift by an amount.</summary>
    [ObservableProperty]
    public partial bool UseReference { get; set; }

    [ObservableProperty]
    public partial double Days { get; set; }

    [ObservableProperty]
    public partial double Hours { get; set; }

    [ObservableProperty]
    public partial double Minutes { get; set; }

    [ObservableProperty]
    public partial double Seconds { get; set; }

    /// <summary>The first selected photo, used for "this photo was actually taken at…".</summary>
    public string? ReferenceName { get; }

    public DateTime? ReferenceDateTaken { get; }

    public bool HasReference => ReferenceDateTaken is not null;

    public string ReferenceText => ReferenceDateTaken is { } taken
        ? $"{ReferenceName} says it was taken {taken.ToString("d MMM yyyy, HH:mm:ss", CultureInfo.CurrentCulture)}. When was it really taken?"
        : $"{ReferenceName ?? "The first photo"} has no date taken, so it can't be used as a reference.";

    [ObservableProperty]
    public partial DateTime CorrectDate { get; set; }

    [ObservableProperty]
    public partial TimeSpan CorrectTime { get; set; }

    [ObservableProperty]
    public partial bool SetTimeZone { get; set; }

    [ObservableProperty]
    public partial string TimeZone { get; set; }

    public TimeSpan Offset => UseReference
        ? (ReferenceDateTaken is { } taken ? CorrectDate.Date + CorrectTime - taken : TimeSpan.Zero)
        : TimeSpan.FromDays(Number(Days)) + TimeSpan.FromHours(Number(Hours)) + TimeSpan.FromMinutes(Number(Minutes)) + TimeSpan.FromSeconds(Number(Seconds));

    /// <summary>e.g. "Dates move 1 h 30 min later."</summary>
    public string Summary => Offset == TimeSpan.Zero
        ? (SetTimeZone ? "Dates stay the same; only the time zone changes." : "Dates stay the same.")
        : $"Dates move {Format(Offset.Duration())} {(Offset > TimeSpan.Zero ? "later" : "earlier")}.";

    public override string Description =>
        $"Shift dates {(Offset >= TimeSpan.Zero ? "+" : "-")}{Format(Offset.Duration())}{(SetTimeZone ? $", time zone {TimeZone}" : string.Empty)} on {PhotoCountText}";

    public override IReadOnlyList<MetadataEdit> EditsFor(ImageMetadata metadata) =>
        DateShift.EditsFor(metadata, new DateShiftOptions(Offset, SetTimeZone ? TimeZone.Trim() : null));

    protected override string? Validate()
    {
        if (UseReference && !HasReference)
        {
            return "The first selected photo has no date taken. Shift by an amount instead.";
        }

        if (SetTimeZone && !ExifDateTime.IsValidZone(TimeZone.Trim()))
        {
            return "Enter the time zone as an offset from UTC, like +01:00 or -05:00.";
        }

        return Offset == TimeSpan.Zero && !SetTimeZone ? "Choose how far to move the dates." : null;
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is not (nameof(Summary) or nameof(Offset) or nameof(Error) or nameof(CanApply)))
        {
            base.OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Offset)));
            base.OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Summary)));
        }
    }

    // A cleared number box reports NaN; whole units only.
    private static double Number(double value) => double.IsFinite(value) ? Math.Truncate(value) : 0;

    private static string Format(TimeSpan span)
    {
        var parts = new List<string>();
        if (span.Days > 0)
        {
            parts.Add(span.Days == 1 ? "1 day" : $"{span.Days} days");
        }

        if (span.Hours > 0)
        {
            parts.Add($"{span.Hours} h");
        }

        if (span.Minutes > 0)
        {
            parts.Add($"{span.Minutes} min");
        }

        if (span.Seconds > 0)
        {
            parts.Add($"{span.Seconds} s");
        }

        return parts.Count == 0 ? "0 s" : string.Join(" ", parts);
    }
}
