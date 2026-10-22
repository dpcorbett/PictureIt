using PictureIt.Core.Settings;

namespace PictureIt.Core.Tests.Settings;

public class AppSettingsTests
{
    [Fact]
    public void Moves_a_reopened_folder_to_the_top_without_duplicating_it()
    {
        var settings = new AppSettings()
            .WithRecentFolder(@"C:\a")
            .WithRecentFolder(@"C:\b")
            .WithRecentFolder(@"C:\A\");

        settings.RecentFolders.ShouldBe([@"C:\A", @"C:\b"]);
    }

    [Fact]
    public void Keeps_a_limited_number_of_recent_folders()
    {
        var settings = new AppSettings();
        for (var i = 0; i < AppSettings.MaxRecentFolders + 5; i++)
        {
            settings = settings.WithRecentFolder($@"C:\folder{i}");
        }

        settings.RecentFolders.Count.ShouldBe(AppSettings.MaxRecentFolders);
        settings.RecentFolders[0].ShouldBe($@"C:\folder{AppSettings.MaxRecentFolders + 4}");
    }
}
