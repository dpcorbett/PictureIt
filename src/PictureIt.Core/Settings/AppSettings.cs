namespace PictureIt.Core.Settings;

public enum AppTheme
{
    System,
    Light,
    Dark,
}

/// <summary>User preferences, persisted between runs.</summary>
public sealed record AppSettings
{
    public const int MaxRecentFolders = 10;

    public AppTheme Theme { get; init; } = AppTheme.System;

    /// <summary>Most recent first.</summary>
    public IReadOnlyList<string> RecentFolders { get; init; } = [];

    /// <summary>Show "IFD0:Model" style keys next to friendly names in the editor.</summary>
    public bool ShowTagKeys { get; init; }

    /// <summary>Returns settings with <paramref name="folder"/> moved to the top of the recent list.</summary>
    public AppSettings WithRecentFolder(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        return this with
        {
            RecentFolders = [normalized, .. RecentFolders
                .Where(f => !string.Equals(f, normalized, StringComparison.OrdinalIgnoreCase))
                .Take(MaxRecentFolders - 1)],
        };
    }
}

public interface ISettingsStore
{
    AppSettings Load();

    void Save(AppSettings settings);
}
