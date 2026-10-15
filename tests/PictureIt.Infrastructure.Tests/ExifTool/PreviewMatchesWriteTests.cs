using PictureIt.Core.Catalog;
using PictureIt.Core.Model;
using PictureIt.Core.Operations;
using PictureIt.Core.Templates;
using PictureIt.Infrastructure.Backup;
using PictureIt.Infrastructure.ExifTool;

namespace PictureIt.Infrastructure.Tests.ExifTool;

/// <summary>
/// The promise behind every bulk tool: what the preview shows is exactly what ends up in the file.
/// Each test predicts with <see cref="ChangePreview"/>, writes with ExifTool, re-reads and compares.
/// </summary>
[Collection(ExifToolCollectionDefinition.Name)]
public sealed class PreviewMatchesWriteTests : IDisposable
{
    // Groups that are derived from other tags or the file, and tags ExifTool adds as bookkeeping when it writes.
    private static readonly HashSet<string> IgnoredGroups = new(StringComparer.OrdinalIgnoreCase) { "Composite", "MWG", "File", "System", "ExifTool" };
    private static readonly HashSet<string> IgnoredTags = new(StringComparer.OrdinalIgnoreCase) { "XMP-x:XMPToolkit", "Photoshop:IPTCDigest" };

    // When ExifTool creates an EXIF block (e.g. in a PNG that had none) it adds the tags the EXIF standard requires.
    private static readonly HashSet<string> MandatoryExifTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "IFD0:XResolution", "IFD0:YResolution", "IFD0:ResolutionUnit", "IFD0:YCbCrPositioning",
        "ExifIFD:ExifVersion", "ExifIFD:ComponentsConfiguration", "ExifIFD:FlashpixVersion", "ExifIFD:ColorSpace",
        "GPS:GPSVersionID",
    };

    private readonly TestFiles _files = new();
    private readonly ExifToolReader _reader;
    private readonly ExifToolWriter _writer;

    public PreviewMatchesWriteTests(ExifToolFixture exifTool)
    {
        _reader = new ExifToolReader(exifTool.Process);
        _writer = new ExifToolWriter(exifTool.Process, new FileBackupStore(Path.Combine(_files.Directory, "_backups")));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(PrivacyOptions.GpsLocation)]
    [InlineData(PrivacyOptions.PlaceNames)]
    [InlineData(PrivacyOptions.CameraIdentifiers)]
    [InlineData(PrivacyOptions.Everything)]
    [InlineData(PrivacyOptions.GpsLocation | PrivacyOptions.CameraIdentifiers)]
    public async Task Privacy_strip(PrivacyOptions options)
    {
        var path = _files.Copy(TestFiles.SampleJpeg);

        // The sample has no place names, so add some to remove.
        await _writer.ApplyAsync(
            EditSet.Create("setup", [new FileEdits(path, [MetadataEdit.Set(TagKey.Parse("MWG:City"), "London"), MetadataEdit.Set(TagKey.Parse("MWG:Country"), "UK")])]),
            cancellationToken: Ct);

        await Apply(path, m => PrivacyStrip.EditsFor(m, options));
    }

    [Theory]
    [InlineData(10, null)]
    [InlineData(-36, "+10:00")]
    [InlineData(0, "-05:00")]
    public async Task Date_shift(int hours, string? zone)
    {
        var path = _files.Copy(TestFiles.SampleJpeg);
        await Apply(path, m => DateShift.EditsFor(m, new DateShiftOptions(TimeSpan.FromHours(hours), zone)));
    }

    [Theory]
    [InlineData(FindReplaceScope.CommonFields)]
    [InlineData(FindReplaceScope.AllTextTags)]
    public async Task Find_and_replace(FindReplaceScope scope)
    {
        var path = _files.Copy(TestFiles.SampleJpeg);
        await Apply(path, m => FindReplace.EditsFor(m, new FindReplaceOptions("test", "Demo", UseRegex: false, MatchCase: false, scope)));
    }

    [Fact]
    public async Task Copy_metadata_from_another_photo()
    {
        var source = _files.Copy(TestFiles.SampleJpeg, "source.jpg");
        var target = _files.Copy(TestFiles.SamplePng, "target.png");
        await _writer.ApplyAsync(
            EditSet.Create("setup", [new FileEdits(source, [
                MetadataEdit.SetRaw(TagKey.Parse("GPS:GPSLatitude"), "48.8584"),
                MetadataEdit.Set(TagKey.Parse("MWG:City"), "Paris"),
                MetadataEdit.Set(TagKey.Parse("MWG:Rating"), "5"),
            ])]),
            cancellationToken: Ct);
        var sourceMetadata = await _reader.ReadAsync(source, Ct);

        await Apply(target, _ => CopyMetadata.EditsFrom(sourceMetadata, MetadataGroups.All), expectChanges: true);
    }

    [Fact]
    public async Task Apply_a_template_that_adds_keywords()
    {
        var path = _files.Copy(TestFiles.SampleJpeg);
        var template = new MetadataTemplate("Trip",
        [
            new TemplateField(TagKey.Parse("MWG:Keywords"), AddToList: true, ["Thames", "London"]),
            new TemplateField(TagKey.Parse("MWG:Copyright"), AddToList: false, ["© 2026 Dave"]),
        ]);

        await Apply(path, _ => template.ToEdits());
    }

    private async Task Apply(string path, Func<ImageMetadata, IReadOnlyList<MetadataEdit>> buildEdits, bool expectChanges = true)
    {
        var before = await _reader.ReadAsync(path, Ct);
        var editSet = EditSet.Create("test", [new FileEdits(path, buildEdits(before))]);
        var preview = ChangePreview.Create(editSet, new Dictionary<string, ImageMetadata>(StringComparer.OrdinalIgnoreCase) { [path] = before });
        if (expectChanges)
        {
            preview.IsEmpty.ShouldBeFalse("the operation should change something in the sample");
        }

        var result = await _writer.ApplyAsync(editSet, cancellationToken: Ct);
        result.Files.ShouldHaveSingleItem().Error.ShouldBeNull();
        var after = await _reader.ReadAsync(path, Ct);

        var predicted = preview.Files[0].Changes.ToDictionary(c => c.Key);
        foreach (var change in predicted.Values)
        {
            if (change.NewValue is null)
            {
                after.TryGetTag(change.Key, out _).ShouldBeFalse($"{change.Key} should have been removed");
            }
            else
            {
                after.TryGetTag(change.Key, out var tag).ShouldBeTrue($"{change.Key} should exist");
                new[] { tag.DisplayValue, tag.RawValue }.ShouldContain(change.NewValue, $"{change.Key} should be '{change.NewValue}'");
            }
        }

        // Nothing the preview didn't mention may change. A change to an MWG field covers the standard tags it writes.
        var covered = predicted.Keys.Concat(predicted.Keys.SelectMany(TagCatalog.MwgComponents)).ToHashSet();
        var keys = before.Tags.Select(t => t.Key).Union(after.Tags.Select(t => t.Key))
            .Where(k => !covered.Contains(k) && !IgnoredGroups.Contains(k.Group) && !IgnoredTags.Contains(k.ToString()))
            .Where(k => !(MandatoryExifTags.Contains(k.ToString()) && before.GetValue(k) is null));
        foreach (var key in keys)
        {
            after.GetValue(key).ShouldBe(before.GetValue(key), $"{key} changed but wasn't in the preview");
        }
    }

    public void Dispose() => _files.Dispose();
}
