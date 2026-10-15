using Microsoft.UI.Xaml.Controls;
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
