using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using PictureIt.Core.Model;
using PictureIt.Core.Organise;

namespace PictureIt.Presentation.ViewModels.Tools;

/// <summary>One file in a duplicate group, with a tick for "send this one to the Recycle Bin".</summary>
public sealed partial class DuplicateItemViewModel : ObservableObject
{
    public DuplicateItemViewModel(string filePath, long length, DateTime modified, PhotoSummary? summary)
    {
        FilePath = filePath;
        FileName = Path.GetFileName(filePath);
        Folder = Path.GetDirectoryName(filePath) ?? string.Empty;

        var details = new List<string> { SizeText(length) };
        if (summary is { Width: { } width, Height: { } height })
        {
            details.Add($"{width} × {height}");
        }

        details.Add(summary?.DateTaken is { } taken
            ? $"taken {taken.ToString("d MMM yyyy, HH:mm:ss", CultureInfo.CurrentCulture)}"
            : $"modified {modified.ToString("d MMM yyyy, HH:mm", CultureInfo.CurrentCulture)}");
        Details = string.Join(" · ", details);
    }

    public string FilePath { get; }

    public string FileName { get; }

    public string Folder { get; }

    public string Details { get; }

    [ObservableProperty]
    public partial bool IsMarked { get; set; }

    /// <summary>Accessible name for the tick box.</summary>
    public string MarkLabel => $"Send {FileName} to the Recycle Bin";

    private static string SizeText(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} bytes",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / (1024.0 * 1024):0.#} MB",
    };
}

public sealed class DuplicateGroupViewModel(DuplicateKind kind, IReadOnlyList<DuplicateItemViewModel> items)
{
    public DuplicateKind Kind { get; } = kind;

    public string Title => Kind == DuplicateKind.Identical
        ? $"{Items.Count} identical files"
        : $"{Items.Count} photos taken at the same moment";

    public string Hint => Kind == DuplicateKind.Identical
        ? "Byte-for-byte copies. It's safe to keep just one."
        : "Different files from the same camera and second: perhaps an edited copy, a RAW+JPEG pair or a burst. Compare before removing.";

    public IReadOnlyList<DuplicateItemViewModel> Items { get; } = items;
}

/// <summary>
/// Duplicate groups to review. In identical groups every copy but the one with the shortest name starts ticked; in
/// same-moment groups nothing is ticked, since those are different pictures. At least one file in each group must
/// stay unticked.
/// </summary>
public sealed class DuplicatesViewModel : ToolViewModel
{
    public DuplicatesViewModel(IReadOnlyList<DuplicateGroupViewModel> groups)
        : base(groups.Sum(g => g.Items.Count))
    {
        Groups = groups;
        foreach (var group in groups)
        {
            if (group.Kind == DuplicateKind.Identical)
            {
                var keep = group.Items.OrderBy(i => i.FileName.Length).ThenBy(i => i.FileName, StringComparer.CurrentCultureIgnoreCase).First();
                foreach (var item in group.Items)
                {
                    item.IsMarked = item != keep;
                }
            }

            foreach (var item in group.Items)
            {
                item.PropertyChanged += OnItemChanged;
            }
        }
    }

    public IReadOnlyList<DuplicateGroupViewModel> Groups { get; }

    public override string Title => "Duplicates";

    public IReadOnlyList<DuplicateItemViewModel> Marked => [.. Groups.SelectMany(g => g.Items).Where(i => i.IsMarked)];

    public override string ConfirmText => Marked.Count switch
    {
        0 => "Send to Recycle Bin",
        1 => "Send 1 file to Recycle Bin",
        var n => $"Send {n} files to Recycle Bin",
    };

    public override string Description => Marked.Count == 1 ? "Send 1 duplicate to the Recycle Bin" : $"Send {Marked.Count} duplicates to the Recycle Bin";

    public string Summary
    {
        get
        {
            var identical = Groups.Count(g => g.Kind == DuplicateKind.Identical);
            var sameMoment = Groups.Count - identical;
            var parts = new List<string>();
            if (identical > 0)
            {
                parts.Add(identical == 1 ? "1 set of identical files" : $"{identical} sets of identical files");
            }

            if (sameMoment > 0)
            {
                parts.Add(sameMoment == 1 ? "1 set taken at the same moment" : $"{sameMoment} sets taken at the same moment");
            }

            return $"Found {string.Join(" and ", parts)}. Tick the files to send to the Recycle Bin.";
        }
    }

    protected override string? Validate()
    {
        if (Groups.FirstOrDefault(g => g.Items.All(i => i.IsMarked)) is { } all)
        {
            return $"Every file in a set is ticked ({all.Items[0].FileName}…). Keep at least one.";
        }

        return Marked.Count == 0 ? "Tick the files to send to the Recycle Bin." : null;
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(Marked));
        OnPropertyChanged(nameof(ConfirmText));
        OnPropertyChanged(nameof(Description));
    }
}
