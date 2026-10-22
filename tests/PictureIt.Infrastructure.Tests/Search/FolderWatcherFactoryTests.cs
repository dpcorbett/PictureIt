using PictureIt.Infrastructure.Search;

namespace PictureIt.Infrastructure.Tests.Search;

public sealed class FolderWatcherFactoryTests : IDisposable
{
    private readonly TestFiles _files = new();
    private readonly FolderWatcherFactory _factory = new() { QuietPeriod = TimeSpan.FromMilliseconds(200) };

    [Fact]
    public async Task Reports_photo_changes_together_once_things_are_quiet()
    {
        var reports = new List<IReadOnlyList<string>>();
        var reported = new TaskCompletionSource();
        Directory.CreateDirectory(_files.PathFor("sub"));
        using var watch = _factory.Watch(_files.Directory, includeSubfolders: true, paths =>
        {
            lock (reports)
            {
                reports.Add(paths);
            }

            reported.TrySetResult();
        });

        _files.Copy(TestFiles.SampleJpeg, "a.jpg");
        _files.Copy(TestFiles.SamplePng, @"sub\b.png");
        await File.WriteAllTextAsync(_files.PathFor("notes.txt"), "ignored", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(_files.PathFor("a.jpg_exiftool_tmp"), "ignored", TestContext.Current.CancellationToken);

        await reported.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await Task.Delay(500, TestContext.Current.CancellationToken);

        lock (reports)
        {
            reports.SelectMany(r => r).Distinct().Select(Path.GetFileName).Order().ShouldBe(["a.jpg", "b.png"]);
        }
    }

    [Fact]
    public async Task Stops_reporting_once_disposed()
    {
        var called = false;
        var watch = _factory.Watch(_files.Directory, includeSubfolders: false, _ => called = true);
        watch.Dispose();

        _files.Copy(TestFiles.SampleJpeg, "a.jpg");
        await Task.Delay(600, TestContext.Current.CancellationToken);

        called.ShouldBeFalse();
    }

    public void Dispose() => _files.Dispose();
}
