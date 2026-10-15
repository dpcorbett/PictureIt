using PictureIt.Core.Organise;
using PictureIt.Infrastructure.Files;
using PictureIt.Infrastructure.Summary;

namespace PictureIt.Infrastructure.Tests.Files;

public sealed class DuplicateScannerTests : IDisposable
{
    private readonly TestFiles _files = new();
    private readonly DuplicateScanner _scanner = new(new MetadataExtractorSummaryReader());

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Finds_identical_copies_but_not_same_size_strangers()
    {
        var original = _files.Copy(TestFiles.SampleJpeg, "original.jpg");
        var copy = _files.Copy(TestFiles.SampleJpeg, "original - Copy.jpg");
        var png = _files.Copy(TestFiles.SamplePng);

        // Same size as the JPEG, different bytes.
        var stranger = _files.PathFor("stranger.jpg");
        File.WriteAllBytes(stranger, new byte[new FileInfo(original).Length]);

        var groups = await _scanner.ScanAsync([original, copy, png, stranger], includeSameMoment: false, cancellationToken: Ct);

        var group = groups.ShouldHaveSingleItem();
        group.Kind.ShouldBe(DuplicateKind.Identical);
        group.FilePaths.ShouldBe([copy, original]);
    }

    [Fact]
    public async Task Finds_different_files_from_the_same_moment()
    {
        var original = _files.Copy(TestFiles.SampleJpeg, "original.jpg");
        var edited = _files.Copy(TestFiles.SampleJpeg, "edited.jpg");

        // Different bytes, same camera and capture time.
        await File.AppendAllTextAsync(edited, "x", Ct);

        (await _scanner.ScanAsync([original, edited], includeSameMoment: false, cancellationToken: Ct)).ShouldBeEmpty();

        var group = (await _scanner.ScanAsync([original, edited], includeSameMoment: true, cancellationToken: Ct)).ShouldHaveSingleItem();
        group.Kind.ShouldBe(DuplicateKind.SameMoment);
    }

    [Fact]
    public async Task Ignores_files_that_are_gone()
    {
        var original = _files.Copy(TestFiles.SampleJpeg);

        (await _scanner.ScanAsync([original, _files.PathFor("gone.jpg")], includeSameMoment: true, cancellationToken: Ct)).ShouldBeEmpty();
    }

    public void Dispose() => _files.Dispose();
}
