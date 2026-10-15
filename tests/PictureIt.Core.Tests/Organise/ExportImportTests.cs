using PictureIt.Core.Catalog;
using PictureIt.Core.Model;
using PictureIt.Core.Organise;

namespace PictureIt.Core.Tests.Organise;

public sealed class CsvTests
{
    [Fact]
    public void Round_trips_awkward_values()
    {
        IReadOnlyList<string>[] rows =
        [
            ["plain", "with, comma", "with \"quotes\"", "two\r\nlines", " padded ", "semi;colon", string.Empty],
        ];

        var text = Csv.Write(rows);

        Csv.Read(text).Single().ShouldBe(rows[0]);
    }

    [Fact]
    public void Reads_semicolon_and_tab_separated_files()
    {
        Csv.Read("SourceFile;XMP-dc:Title\r\na.jpg;Hello, world\r\n")[1].ShouldBe(["a.jpg", "Hello, world"]);
        Csv.Read("SourceFile\tXMP-dc:Title\na.jpg\tHi\n")[1].ShouldBe(["a.jpg", "Hi"]);
    }

    [Fact]
    public void Skips_blank_lines_and_handles_a_missing_final_newline()
    {
        Csv.Read("a,b\n\n1,2").ShouldBe([["a", "b"], ["1", "2"]]);
    }

    [Fact]
    public void Rejects_an_unclosed_quote()
    {
        Should.Throw<InvalidDataException>(() => Csv.Read("a,\"b\n"));
    }
}

public sealed class ExportImportTests
{
    private static readonly ImageMetadata Photo = TestMetadata.Create(
        @"C:\photos\a.jpg",
        ("XMP-dc:Title", "Big Ben", null),
        ("MWG:Keywords", "London; Clock", null),
        ("IFD0:Model", "Canon EOS R5", null),
        ("File:FileSize", "1 MB", null));

    [Fact]
    public void Common_columns_are_always_all_the_common_fields()
    {
        MetadataExport.Columns([Photo], ExportFields.Common).ShouldBe(TagCatalog.CommonFields.Select(f => f.Key));
    }

    [Fact]
    public void All_columns_list_common_fields_first_then_every_other_tag()
    {
        var columns = MetadataExport.Columns([Photo], ExportFields.All);

        columns.Take(TagCatalog.CommonFields.Count).ShouldBe(TagCatalog.CommonFields.Select(f => f.Key));
        columns.Skip(TagCatalog.CommonFields.Count).Select(c => c.ToString()).ShouldBe(["IFD0:Model", "File:FileSize"]);
    }

    [Fact]
    public void Csv_has_a_SourceFile_column_and_joins_lists()
    {
        var csv = MetadataExport.ToCsv([Photo], [TagKey.Parse("XMP-dc:Title"), TagKey.Parse("MWG:Keywords"), TagKey.Parse("MWG:City")]);

        Csv.Read(csv).ShouldBe([
            ["SourceFile", "XMP-dc:Title", "MWG:Keywords", "MWG:City"],
            [@"C:\photos\a.jpg", "Big Ben", "London; Clock", string.Empty],
        ]);
    }

    [Fact]
    public void Json_writes_lists_as_arrays_and_leaves_out_missing_tags()
    {
        var json = MetadataExport.ToJson([Photo], [TagKey.Parse("MWG:Keywords"), TagKey.Parse("MWG:City")]);

        json.ShouldContain("\"MWG:Keywords\": [");
        json.ShouldNotContain("MWG:City");
        MetadataImport.ParseJson(json).Rows.Single().Values[TagKey.Parse("MWG:Keywords")].ShouldBe(["London", "Clock"]);
    }

    [Theory]
    [InlineData(ExportFormat.Csv, ".csv")]
    [InlineData(ExportFormat.Json, ".json")]
    public void Exported_files_import_back_unchanged(ExportFormat format, string extension)
    {
        var columns = MetadataExport.Columns([Photo], ExportFields.All);
        var table = MetadataImport.Parse(MetadataExport.Write([Photo], columns, format), extension);

        var row = table.Rows.Single();
        row.SourceFile.ShouldBe(Photo.FilePath);

        // Nothing differs, so nothing to write.
        MetadataImport.EditsFor(row, Photo).Edits.ShouldBeEmpty();
    }

    [Fact]
    public void Only_changed_cells_become_edits()
    {
        var table = MetadataImport.ParseCsv(
            "SourceFile,XMP-dc:Title,MWG:Keywords,MWG:City,IFD0:Model\r\n" +
            "a.jpg,Big Ben at night,London; Clock,London,\r\n");

        var edits = MetadataImport.EditsFor(table.Rows.Single(), Photo);

        edits.Edits.ShouldBe([
            MetadataEdit.Set(TagKey.Parse("XMP-dc:Title"), "Big Ben at night"),
            MetadataEdit.Set(TagKey.Parse("MWG:City"), "London"),
        ]);
        edits.Problems.ShouldBeEmpty();
    }

    [Fact]
    public void Lists_are_set_as_lists()
    {
        var table = MetadataImport.ParseCsv("SourceFile,MWG:Keywords\r\na.jpg,London; Night ;; Clock\r\n");

        MetadataImport.EditsFor(table.Rows.Single(), Photo).Edits
            .ShouldBe([MetadataEdit.Set(TagKey.Parse("MWG:Keywords"), ["London", "Night", "Clock"])]);
    }

    [Fact]
    public void Read_only_columns_are_ignored_and_reported()
    {
        var table = MetadataImport.ParseCsv("SourceFile,File:FileSize,Composite:GPSPosition\r\na.jpg,2 MB,1 2\r\n");

        table.ReadOnlyColumns.Select(c => c.ToString()).ShouldBe(["File:FileSize", "Composite:GPSPosition"]);
        MetadataImport.EditsFor(table.Rows.Single(), Photo).Edits.ShouldBeEmpty();
    }

    [Fact]
    public void Invalid_values_are_skipped_with_a_reason()
    {
        // As Excel might save a date.
        var table = MetadataImport.ParseCsv("SourceFile,MWG:DateTimeOriginal,MWG:Rating\r\na.jpg,15/06/2024 14:32,9\r\n");

        var edits = MetadataImport.EditsFor(table.Rows.Single(), Photo);

        edits.Edits.ShouldBeEmpty();
        edits.Problems.Count.ShouldBe(2);
        edits.Problems[0].ShouldStartWith("a.jpg, MWG:DateTimeOriginal: \"15/06/2024 14:32\".");
    }

    [Theory]
    [InlineData("XMP-dc:Title\r\nx\r\n", "SourceFile")]
    [InlineData("SourceFile,Title\r\na.jpg,x\r\n", "\"Title\" isn't a tag")]
    [InlineData("", "empty")]
    public void Rejects_files_it_cannot_use(string csv, string message)
    {
        Should.Throw<InvalidDataException>(() => MetadataImport.ParseCsv(csv)).Message.ShouldContain(message);
    }

    [Theory]
    [InlineData("{}", "list of photos")]
    [InlineData("[{\"XMP-dc:Title\": \"x\"}]", "SourceFile")]
    [InlineData("[{", "valid JSON")]
    public void Rejects_json_it_cannot_use(string json, string message)
    {
        Should.Throw<InvalidDataException>(() => MetadataImport.ParseJson(json)).Message.ShouldContain(message);
    }

    [Fact]
    public void Matches_rows_by_path_then_by_file_name()
    {
        var table = MetadataImport.ParseCsv(
            "SourceFile,XMP-dc:Title\r\n" +
            "C:\\photos\\a.jpg,1\r\n" +
            "D:\\elsewhere\\B.JPG,2\r\n" +
            "c.jpg,3\r\n" +
            "a.jpg,again\r\n");

        var match = MetadataImport.Match(table, [@"C:\photos\a.jpg", @"C:\photos\b.jpg"]);

        match.Matched.Select(m => (m.FilePath, m.Row.SourceFile)).ShouldBe([(@"C:\photos\a.jpg", @"C:\photos\a.jpg"), (@"C:\photos\b.jpg", @"D:\elsewhere\B.JPG")]);

        // c.jpg isn't in the folder, and a.jpg was already matched.
        match.Unmatched.ShouldBe(["c.jpg", "a.jpg"]);
    }
}

public sealed class DuplicateFinderTests
{
    private static readonly DateTime Noon = new(2024, 6, 15, 12, 0, 0, DateTimeKind.Unspecified);

    [Fact]
    public void Groups_identical_files()
    {
        var groups = DuplicateFinder.Find(
            [
                new PhotoFingerprint(@"C:\a.jpg", 10, "H1", null, null),
                new PhotoFingerprint(@"C:\a - Copy.jpg", 10, "H1", null, null),
                new PhotoFingerprint(@"C:\b.jpg", 10, "H2", null, null),
                new PhotoFingerprint(@"C:\c.jpg", 20, null, null, null),
            ],
            includeSameMoment: false);

        groups.ShouldHaveSingleItem().Kind.ShouldBe(DuplicateKind.Identical);
        groups[0].FilePaths.ShouldBe([@"C:\a - Copy.jpg", @"C:\a.jpg"]);
    }

    [Fact]
    public void Same_moment_groups_need_the_option_and_merge_with_identical_ones()
    {
        PhotoFingerprint[] photos =
        [
            new(@"C:\a.jpg", 10, "H1", Noon, "Canon EOS R5"),
            new(@"C:\a copy.jpg", 10, "H1", Noon, "Canon EOS R5"),
            new(@"C:\a edited.jpg", 12, null, Noon, "canon eos r5 "),
            new(@"C:\other camera.jpg", 12, null, Noon, "Nikon Z8"),
            new(@"C:\no camera.jpg", 12, null, Noon, null),
        ];

        DuplicateFinder.Find(photos, includeSameMoment: false).ShouldHaveSingleItem().FilePaths.Count.ShouldBe(2);

        var group = DuplicateFinder.Find(photos, includeSameMoment: true).ShouldHaveSingleItem();
        group.Kind.ShouldBe(DuplicateKind.SameMoment);
        group.FilePaths.ShouldBe([@"C:\a copy.jpg", @"C:\a edited.jpg", @"C:\a.jpg"]);
    }

    [Fact]
    public void Largest_groups_come_first()
    {
        var groups = DuplicateFinder.Find(
            [
                new PhotoFingerprint(@"C:\x1.jpg", 1, "X", null, null),
                new PhotoFingerprint(@"C:\x2.jpg", 1, "X", null, null),
                new PhotoFingerprint(@"C:\y1.jpg", 2, "Y", null, null),
                new PhotoFingerprint(@"C:\y2.jpg", 2, "Y", null, null),
                new PhotoFingerprint(@"C:\y3.jpg", 2, "Y", null, null),
            ],
            includeSameMoment: false);

        groups.Select(g => g.FilePaths.Count).ShouldBe([3, 2]);
    }
}
