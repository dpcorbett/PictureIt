using PictureIt.Core.Catalog;
using PictureIt.Core.Settings;
using PictureIt.Infrastructure.Settings;

namespace PictureIt.Infrastructure.Tests.Settings;

public sealed class JsonSettingsStoreTests : IDisposable
{
    private readonly TestFiles _files = new();

    [Fact]
    public void Round_trips_settings()
    {
        var store = new JsonSettingsStore(_files.PathFor(@"nested\settings.json"));
        var settings = new AppSettings
        {
            Theme = AppTheme.Dark,
            ShowTagKeys = true,
            ShowAllTags = false,
            CollapsedSections = [TagCategory.Camera],
        }.WithRecentFolder(@"C:\photos");

        store.Save(settings);
        var loaded = store.Load();

        loaded.Theme.ShouldBe(AppTheme.Dark);
        loaded.ShowTagKeys.ShouldBeTrue();
        loaded.ShowAllTags.ShouldBeFalse();
        loaded.CollapsedSections.ShouldBe([TagCategory.Camera]);
        loaded.RecentFolders.ShouldBe([@"C:\photos"]);
    }

    [Fact]
    public void Returns_defaults_when_there_is_no_file()
    {
        var settings = new JsonSettingsStore(_files.PathFor("missing.json")).Load();

        settings.Theme.ShouldBe(AppTheme.System);
        settings.RecentFolders.ShouldBeEmpty();
        settings.ShowAllTags.ShouldBeTrue();
        settings.CollapsedSections.ShouldBe([TagCategory.MakerNotes, TagCategory.File]);
    }

    [Fact]
    public void Returns_defaults_when_the_file_is_corrupt()
    {
        var path = _files.PathFor("settings.json");
        File.WriteAllText(path, "{ not json");

        new JsonSettingsStore(path).Load().Theme.ShouldBe(AppTheme.System);
    }

    public void Dispose() => _files.Dispose();
}
