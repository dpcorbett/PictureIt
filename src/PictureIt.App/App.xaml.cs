using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using PictureIt.App.Services;
using PictureIt.Core.Abstractions;
using PictureIt.Core.Settings;
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

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var windowContext = new WindowContext();
        Services = ConfigureServices(windowContext);

        _window = new MainWindow(Services.GetRequiredService<LibraryViewModel>());
        windowContext.Window = _window;
        Services.GetRequiredService<IThemeService>().Apply(Services.GetRequiredService<ISettingsStore>().Load().Theme);

        _window.Closed += async (_, _) =>
        {
            // Stops the shared ExifTool process cleanly.
            if (Services is IAsyncDisposable disposable)
            {
                await disposable.DisposeAsync();
            }
        };
        _window.Activate();

        // "PictureIt.exe <folder or photos>" — also what Explorer's "Open with" passes.
        var paths = Environment.GetCommandLineArgs().Skip(1).ToList();
        if (paths.Count > 0)
        {
            _ = Services.GetRequiredService<LibraryViewModel>().OpenPathsAsync(paths);
        }
    }

    private static ServiceProvider ConfigureServices(WindowContext windowContext)
    {
        var services = new ServiceCollection();

        // Engine
        services.AddSingleton(_ => new ExifToolProcess(ExifToolProcess.DefaultExecutablePath));
        services.AddSingleton<IMetadataReader, ExifToolReader>();
        services.AddSingleton<IBackupStore>(_ => new FileBackupStore(FileBackupStore.DefaultRootDirectory));
        services.AddSingleton<IMetadataWriter>(sp => new ExifToolWriter(sp.GetRequiredService<ExifToolProcess>(), sp.GetRequiredService<IBackupStore>()));
        services.AddSingleton<IPhotoSummaryReader, MetadataExtractorSummaryReader>();
        services.AddSingleton<ISettingsStore>(_ => new JsonSettingsStore(JsonSettingsStore.DefaultFilePath));

        // Windows-specific UI services
        services.AddSingleton(windowContext);
        services.AddSingleton<IFolderPicker, FolderPickerService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IThemeService, ThemeService>();

        // View-models
        services.AddSingleton<MetadataEditorViewModel>();
        services.AddSingleton<LibraryViewModel>();
        services.AddTransient(sp => new SettingsViewModel(
            sp.GetRequiredService<ISettingsStore>(),
            sp.GetRequiredService<IThemeService>(),
            FileBackupStore.DefaultRootDirectory));

        return services.BuildServiceProvider();
    }
}
