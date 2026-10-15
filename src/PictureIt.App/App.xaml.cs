using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using PictureIt.App.Services;
using PictureIt.Core.Abstractions;
using PictureIt.Core.History;
using PictureIt.Core.Settings;
using PictureIt.Core.Templates;
using PictureIt.Infrastructure.Backup;
using PictureIt.Infrastructure.ExifTool;
using PictureIt.Infrastructure.Settings;
using PictureIt.Infrastructure.Summary;
using PictureIt.Presentation.Services;
using PictureIt.Presentation.ViewModels;

namespace PictureIt.App;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
    }

    /// <summary>The app's services. Available once <see cref="OnLaunched"/> has run.</summary>
    public static IServiceProvider Services { get; private set; } = null!;

    public static MainWindow? MainWindow => (Current as App)?._window;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var windowContext = new WindowContext();
        Services = ConfigureServices(windowContext);

        _window = new MainWindow(Services.GetRequiredService<LibraryViewModel>());
        windowContext.Window = _window;
        var settings = Services.GetRequiredService<ISettingsStore>().Load();
        Services.GetRequiredService<IThemeService>().Apply(settings.Theme);

        _window.Closed += async (_, _) =>
        {
            // Stops the shared ExifTool process cleanly.
            if (Services is IAsyncDisposable disposable)
            {
                await disposable.DisposeAsync();
            }
        };
        _window.Activate();

        // Expired backups are removed quietly in the background; a failure here mustn't affect the session.
        _ = CleanUpBackupsAsync(settings.BackupRetentionDays);

        // "PictureIt.exe <folder or photos>" — also what Explorer's "Open with" passes.
        var paths = Environment.GetCommandLineArgs().Skip(1).ToList();
        if (paths.Count > 0)
        {
            _ = Services.GetRequiredService<LibraryViewModel>().OpenPathsAsync(paths);
        }
    }

    private static async Task CleanUpBackupsAsync(int retentionDays)
    {
        try
        {
            await Task.Run(() => Services.GetRequiredService<BackupCleanup>().RunAsync(retentionDays));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Try again next launch.
        }
    }

    private static ServiceProvider ConfigureServices(WindowContext windowContext)
    {
        var services = new ServiceCollection();

        // Engine and storage
        services.AddSingleton(_ => new ExifToolProcess(ExifToolProcess.DefaultExecutablePath));
        services.AddSingleton<IMetadataReader, ExifToolReader>();
        services.AddSingleton<IBackupStore>(_ => new FileBackupStore(FileBackupStore.DefaultRootDirectory));
        services.AddSingleton<IMetadataWriter>(sp => new ExifToolWriter(sp.GetRequiredService<ExifToolProcess>(), sp.GetRequiredService<IBackupStore>()));
        services.AddSingleton<IPhotoSummaryReader, MetadataExtractorSummaryReader>();
        services.AddSingleton<ISettingsStore>(_ => new JsonSettingsStore(JsonSettingsStore.DefaultFilePath));
        services.AddSingleton<IHistoryStore>(_ => new JsonHistoryStore(JsonHistoryStore.DefaultFilePath));
        services.AddSingleton<ITemplateStore>(_ => new JsonTemplateStore(JsonTemplateStore.DefaultFilePath));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IMessenger>(WeakReferenceMessenger.Default);

        // Windows-specific UI services
        services.AddSingleton(windowContext);
        services.AddSingleton<FolderPickerService>();
        services.AddSingleton<IFolderPicker>(sp => sp.GetRequiredService<FolderPickerService>());
        services.AddSingleton<IFilePicker>(sp => sp.GetRequiredService<FolderPickerService>());
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IThemeService, ThemeService>();

        // Presentation
        services.AddSingleton<BatchRunner>();
        services.AddSingleton<BackupCleanup>();
        services.AddSingleton<MetadataEditorViewModel>();
        services.AddSingleton<ToolsViewModel>();
        services.AddSingleton<LibraryViewModel>();
        services.AddSingleton<HistoryViewModel>();
        services.AddTransient<TemplatesViewModel>();
        services.AddTransient(sp => new SettingsViewModel(
            sp.GetRequiredService<ISettingsStore>(),
            sp.GetRequiredService<IThemeService>(),
            FileBackupStore.DefaultRootDirectory));

        return services.BuildServiceProvider();
    }
}
