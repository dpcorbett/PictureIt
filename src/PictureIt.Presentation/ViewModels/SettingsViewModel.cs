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
        _initialized = true;
    }

    private readonly bool _initialized;

    public IReadOnlyList<AppTheme> Themes { get; } = Enum.GetValues<AppTheme>();

    /// <summary>Where copies of files are kept before each save, so a batch can be undone.</summary>
    public string BackupFolder { get; }

    [ObservableProperty]
    public partial AppTheme Theme { get; set; }

    partial void OnThemeChanged(AppTheme value)
    {
        if (_initialized)
        {
            _settings.Save(_settings.Load() with { Theme = value });
            _themes.Apply(value);
        }
    }
}
