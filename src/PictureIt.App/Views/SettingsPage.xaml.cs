using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PictureIt.Core.Settings;
using PictureIt.Presentation.ViewModels;

namespace PictureIt.App.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        ViewModel = App.Services.GetRequiredService<SettingsViewModel>();
        InitializeComponent();
        ThemeBox.SelectedIndex = (int)ViewModel.Theme;
        RetentionBox.SelectedItem = RetentionBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(i => (string)i.Tag == ViewModel.BackupRetentionDays.ToString(System.Globalization.CultureInfo.InvariantCulture))
            ?? RetentionBox.Items[1];
    }

    private void OnRetentionSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RetentionBox.SelectedItem is ComboBoxItem { Tag: string tag }
            && int.TryParse(tag, System.Globalization.CultureInfo.InvariantCulture, out var days))
        {
            ViewModel.BackupRetentionDays = days;
        }
    }

    public SettingsViewModel ViewModel { get; }

    private void OnThemeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeBox.SelectedItem is ComboBoxItem { Tag: string tag } && Enum.TryParse<AppTheme>(tag, out var theme))
        {
            ViewModel.Theme = theme;
        }
    }

    private void OnOpenBackupFolder(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(ViewModel.BackupFolder);
        using var _ = Process.Start(new ProcessStartInfo("explorer.exe", $"\"{ViewModel.BackupFolder}\"") { UseShellExecute = true });
    }
}
