using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PictureIt.Presentation.ViewModels;

namespace PictureIt.App.Views;

public sealed partial class TemplatesPage : Page
{
    public TemplatesPage()
    {
        ViewModel = App.Services.GetRequiredService<TemplatesViewModel>();
        InitializeComponent();
    }

    public TemplatesViewModel ViewModel { get; }

    private void OnRenameClick(object sender, RoutedEventArgs e) => ViewModel.RenameCommand.Execute(Item(sender));

    private void OnExportClick(object sender, RoutedEventArgs e) => ViewModel.ExportCommand.Execute(Item(sender));

    private void OnDeleteClick(object sender, RoutedEventArgs e) => ViewModel.DeleteCommand.Execute(Item(sender));

    private static TemplateItemViewModel? Item(object sender) => (sender as FrameworkElement)?.DataContext as TemplateItemViewModel;
}
