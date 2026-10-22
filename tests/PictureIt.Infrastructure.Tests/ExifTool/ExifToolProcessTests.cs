using System.Diagnostics;
using PictureIt.Infrastructure.ExifTool;

namespace PictureIt.Infrastructure.Tests.ExifTool;

[Collection(ExifToolCollectionDefinition.Name)]
public sealed class ExifToolProcessTests(ExifToolFixture exifTool) : IDisposable
{
    private readonly TestFiles _files = new();

    [Fact]
    public async Task Runs_the_bundled_version()
    {
        var result = await exifTool.Process.ExecuteAsync(["-ver"], TestContext.Current.CancellationToken);

        result.Output.Trim().ShouldBe("13.59");
        result.Errors.ShouldBeEmpty();
    }

    [Fact]
    public async Task Serialises_concurrent_commands()
    {
        var path = _files.Copy(TestFiles.SampleJpeg);

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ =>
            exifTool.Process.ExecuteAsync(["-s3", "-IFD0:Model", path], TestContext.Current.CancellationToken)));

        results.ShouldAllBe(r => r.Output.Trim() == "Canon EOS R5");
    }

    [Fact]
    public async Task Restarts_after_the_process_dies()
    {
        await using var process = new ExifToolProcess(ExifToolProcess.DefaultExecutablePath);
        await process.ExecuteAsync(["-ver"], TestContext.Current.CancellationToken);
        var firstId = process.ProcessId.ShouldNotBeNull();

        using (var running = Process.GetProcessById(firstId))
        {
            running.Kill(entireProcessTree: true);
            await running.WaitForExitAsync(TestContext.Current.CancellationToken);
        }

        var result = await process.ExecuteAsync(["-ver"], TestContext.Current.CancellationToken);

        result.Output.Trim().ShouldBe("13.59");
        process.ProcessId.ShouldNotBe(firstId);
    }

    [Fact]
    public async Task Recovers_after_a_cancelled_command()
    {
        await using var process = new ExifToolProcess(ExifToolProcess.DefaultExecutablePath);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => process.ExecuteAsync(["-ver"], cancelled.Token));

        var result = await process.ExecuteAsync(["-ver"], TestContext.Current.CancellationToken);
        result.Output.Trim().ShouldBe("13.59");
    }

    [Fact]
    public async Task Rejects_arguments_with_line_breaks()
    {
        await Should.ThrowAsync<ArgumentException>(() =>
            exifTool.Process.ExecuteAsync(["-XMP-dc:Title=one\ntwo"], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Reports_a_missing_executable()
    {
        await using var process = new ExifToolProcess(_files.PathFor("missing.exe"));

        await Should.ThrowAsync<ExifToolException>(() => process.ExecuteAsync(["-ver"], TestContext.Current.CancellationToken));
    }

    public void Dispose() => _files.Dispose();
}
