using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PictureIt.App.Services;
using PictureIt.Presentation.ViewModels.Tools;

namespace PictureIt.App.Dialogs;

public sealed partial class FindReplaceView : UserControl
{
    public FindReplaceView(FindReplaceToolViewModel tool)
    {
        Tool = tool;
        InitializeComponent();
    }

    public FindReplaceToolViewModel Tool { get; }
}

public sealed partial class RemoveMetadataView : UserControl
{
    public RemoveMetadataView(RemoveMetadataToolViewModel tool)
    {
        Tool = tool;
        InitializeComponent();
    }

    public RemoveMetadataToolViewModel Tool { get; }
}

public sealed partial class PasteMetadataView : UserControl
{
    public PasteMetadataView(PasteMetadataToolViewModel tool)
    {
        Tool = tool;
        InitializeComponent();
    }

    public PasteMetadataToolViewModel Tool { get; }
}

public sealed partial class ShiftDatesView : UserControl
{
    public ShiftDatesView(ShiftDatesToolViewModel tool)
    {
        Tool = tool;
        InitializeComponent();

        // Date and time pickers use nullable/offset types, so they're synced by hand rather than bound.
        ModeChoice.SelectedIndex = tool.UseReference ? 1 : 0;
        CorrectDatePicker.Date = new DateTimeOffset(tool.CorrectDate);
        CorrectTimePicker.SelectedTime = tool.CorrectTime;
    }

    public ShiftDatesToolViewModel Tool { get; }

    private void OnModeChanged(object sender, SelectionChangedEventArgs e) => Tool.UseReference = ModeChoice.SelectedIndex == 1;

    private void OnCorrectDateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
    {
        if (args.NewDate is { } date)
        {
            Tool.CorrectDate = date.Date;
        }
    }

    private void OnCorrectTimeChanged(TimePicker sender, TimePickerSelectedValueChangedEventArgs args)
    {
        if (args.NewTime is { } time)
        {
            Tool.CorrectTime = time;
        }
    }
}

public sealed partial class RenameView : UserControl
{
    public RenameView(RenameToolViewModel tool)
    {
        Tool = tool;
        InitializeComponent();
        FilePlanPreview.FillTokenMenu(TokenMenu, tool);
    }

    public RenameToolViewModel Tool { get; }
}

public sealed partial class SortView : UserControl
{
    public SortView(SortToolViewModel tool)
    {
        Tool = tool;
        InitializeComponent();
        FilePlanPreview.FillTokenMenu(TokenMenu, tool);
    }

    public SortToolViewModel Tool { get; }
}

/// <summary>The rename/sort preview list, summary and error.</summary>
public sealed partial class FilePlanPreview : UserControl
{
    public static readonly DependencyProperty ToolProperty =
        DependencyProperty.Register(nameof(Tool), typeof(FilePlanToolViewModel), typeof(FilePlanPreview), new PropertyMetadata(null));

    public static readonly DependencyProperty ListHeightProperty =
        DependencyProperty.Register(nameof(ListHeight), typeof(double), typeof(FilePlanPreview), new PropertyMetadata(240d));

    public FilePlanPreview()
    {
        InitializeComponent();
    }

    public FilePlanToolViewModel Tool
    {
        get => (FilePlanToolViewModel)GetValue(ToolProperty);
        set => SetValue(ToolProperty, value);
    }

    public double ListHeight
    {
        get => (double)GetValue(ListHeightProperty);
        set => SetValue(ListHeightProperty, value);
    }

    /// <summary>Fills an "Insert" menu with every token; choosing one appends it to the pattern.</summary>
    public static void FillTokenMenu(MenuFlyout menu, FilePlanToolViewModel tool)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(tool);
        foreach (var token in FilePlanToolViewModel.Tokens)
        {
            menu.Items.Add(new MenuFlyoutItem
            {
                Text = $"{token.DisplayName}   {token.Example}",
                Command = tool.InsertTokenCommand,
                CommandParameter = token,
            });
        }
    }
}

public sealed partial class ExportView : UserControl
{
    public ExportView(ExportToolViewModel tool)
    {
        Tool = tool;
        InitializeComponent();
    }

    public ExportToolViewModel Tool { get; }
}

public sealed partial class FindDuplicatesView : UserControl
{
    public FindDuplicatesView(FindDuplicatesToolViewModel tool)
    {
        Tool = tool;
        InitializeComponent();
    }

    public FindDuplicatesToolViewModel Tool { get; }
}

public sealed partial class DuplicatesView : UserControl
{
    private readonly ThumbnailCache _thumbnails = new();

    public DuplicatesView(DuplicatesViewModel tool)
    {
        Tool = tool;
        InitializeComponent();
    }

    public DuplicatesViewModel Tool { get; }

    private async void OnThumbnailLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Image { Tag: string path } image)
        {
            image.Source = await _thumbnails.GetThumbnailAsync(path);
        }
    }
}
