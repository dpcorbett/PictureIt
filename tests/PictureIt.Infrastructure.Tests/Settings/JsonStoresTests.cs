using PictureIt.Core.History;
using PictureIt.Core.Model;
using PictureIt.Core.Templates;
using PictureIt.Infrastructure.Backup;
using PictureIt.Infrastructure.Settings;

namespace PictureIt.Infrastructure.Tests.Settings;

public sealed class JsonStoresTests : IDisposable
{
    private readonly TestFiles _files = new();

    [Fact]
    public void History_round_trips_and_is_capped()
    {
        var store = new JsonHistoryStore(_files.PathFor("history.json"));
        var records = Enumerable.Range(0, JsonHistoryStore.MaxRecords + 3)
            .Select(i => new BatchRecord(Guid.NewGuid(), $"Batch {i}", DateTimeOffset.UnixEpoch.AddDays(i), [@"C:\a.jpg"], 1, 0, true))
            .ToList();
        records[0] = records[0] with { RevertedAt = DateTimeOffset.UnixEpoch };

        store.Save(records);
        var loaded = store.Load();

        loaded.Count.ShouldBe(JsonHistoryStore.MaxRecords);
        loaded[0].BatchId.ShouldBe(records[0].BatchId);
        loaded[0].Description.ShouldBe("Batch 0");
        loaded[0].FilePaths.ShouldBe([@"C:\a.jpg"]);
        loaded[0].RevertedAt.ShouldBe(DateTimeOffset.UnixEpoch);
        loaded[0].CanUndo.ShouldBeFalse();
        loaded[1].CanUndo.ShouldBeTrue();
    }

    [Fact]
    public void Templates_round_trip_with_readable_tag_keys()
    {
        var path = _files.PathFor("templates.json");
        var store = new JsonTemplateStore(path);
        var template = new MetadataTemplate("Rights", [new TemplateField(TagKey.Parse("MWG:Copyright"), false, ["© Dave"])]);

        store.Save([template]);

        File.ReadAllText(path).ShouldContain("\"MWG:Copyright\"");
        var loaded = store.Load().ShouldHaveSingleItem();
        loaded.Name.ShouldBe("Rights");
        loaded.ToEdits().ShouldBe(template.ToEdits());
    }

    [Fact]
    public void Exports_and_imports_a_single_template()
    {
        var path = _files.PathFor("rights.pictureit-template.json");
        var template = new MetadataTemplate("Rights", [new TemplateField(TagKey.Parse("MWG:Keywords"), true, ["a", "b"])]);

        new JsonTemplateStore(_files.PathFor("store.json")).Export(template, path);

        new JsonTemplateStore(_files.PathFor("store.json")).Import(path).ToEdits().ShouldBe(template.ToEdits());
    }

    [Fact]
    public void Importing_something_else_fails_clearly()
    {
        var path = _files.PathFor("other.json");
        File.WriteAllText(path, "{\"hello\": 1}");

        Should.Throw<InvalidDataException>(() => new JsonTemplateStore(_files.PathFor("store.json")).Import(path));
    }

    [Fact]
    public async Task Backup_store_lists_its_backups()
    {
        var store = new FileBackupStore(_files.PathFor("backups"));
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await store.CreateAsync(first, [_files.Copy(TestFiles.SampleJpeg, "a.jpg")], TestContext.Current.CancellationToken);
        await store.CreateAsync(second, [_files.Copy(TestFiles.SampleJpeg, "b.jpg")], TestContext.Current.CancellationToken);

        var listed = await store.ListAsync(TestContext.Current.CancellationToken);

        listed.Select(m => m.BatchId).ShouldBe([first, second], ignoreOrder: true);
    }

    public void Dispose() => _files.Dispose();
}
