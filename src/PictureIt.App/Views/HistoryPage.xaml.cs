using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PictureIt.Presentation.ViewModels;

namespace PictureIt.App.Views;

public sealed partial class HistoryPage : Page
{
    public HistoryPage()
    {
        ViewModel = App.Services.GetRequiredService<HistoryViewModel>();
        InitializeComponent();
        Loaded += (_, _) => ViewModel.Refresh();
    }

    public HistoryViewModel ViewModel { get; }

    private void OnUndoClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: BatchRecordViewModel record })
        {
            ViewModel.UndoCommand.Execute(record);
        }
    }
}
