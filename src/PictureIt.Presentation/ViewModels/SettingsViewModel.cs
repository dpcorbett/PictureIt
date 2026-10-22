using CommunityToolkit.Mvvm.ComponentModel;
using PictureIt.Core.Settings;
using PictureIt.Presentation.Services;

namespace PictureIt.Presentation.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsStore _settings;
    private readonly IThemeService _themes;

    public SettingsViewModel(ISettingsStore settings, IThemeService themes, string backupFolder)
    {
        _settings = settings;
        _themes = themes;
        BackupFolder = backupFolder;
        Theme = settings.Load().Theme;
        BackupRetentionDays = settings.Load().BackupRetentionDays;
        _initialized = true;
    }

    private readonly bool _initialized;

    public IReadOnlyList<AppTheme> Themes { get; } = Enum.GetValues<AppTheme>();

    /// <summary>Where copies of files are kept before each save, so a batch can be undone.</summary>
    public string BackupFolder { get; }

    [ObservableProperty]
    public partial AppTheme Theme { get; set; }

    /// <summary>Choices for how long backups are kept; 0 means forever.</summary>
    public IReadOnlyList<int> RetentionChoices { get; } = [7, 30, 90, 365, 0];

    [ObservableProperty]
    public partial int BackupRetentionDays { get; set; }

    partial void OnBackupRetentionDaysChanged(int value)
    {
        if (_initialized)
        {
            _settings.Save(_settings.Load() with { BackupRetentionDays = value });
        }
    }

    partial void OnThemeChanged(AppTheme value)
    {
        if (_initialized)
        {
            _settings.Save(_settings.Load() with { Theme = value });
            _themes.Apply(value);
        }
    }
}
