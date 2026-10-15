using CommunityToolkit.Mvvm.Messaging;
using PictureIt.Core.Abstractions;
using PictureIt.Core.History;
using PictureIt.Core.Model;
using PictureIt.Core.Settings;
using PictureIt.Core.Templates;
using PictureIt.Presentation.Services;
using PictureIt.Presentation.ViewModels;
using PictureIt.Presentation.ViewModels.Tools;

namespace PictureIt.Presentation.Tests;

internal sealed class InMemorySettingsStore : ISettingsStore
{
    public AppSettings Current { get; set; } = new();

    public int SaveCount { get; private set; }

    public AppSettings Load() => Current;

    public void Save(AppSettings settings)
    {
        Current = settings;
        SaveCount++;
    }
}

internal sealed class InMemoryHistoryStore : IHistoryStore
{
    public List<BatchRecord> Records { get; set; } = [];

    public IReadOnlyList<BatchRecord> Load() => Records;

    public void Save(IReadOnlyList<BatchRecord> records) => Records = [.. records];
}

internal sealed class InMemoryTemplateStore : ITemplateStore
{
    public List<MetadataTemplate> Templates { get; set; } = [];

    public Dictionary<string, MetadataTemplate> Files { get; } = [];

    public IReadOnlyList<MetadataTemplate> Load() => Templates;

    public void Save(IReadOnlyList<MetadataTemplate> templates) => Templates = [.. templates];

    public void Export(MetadataTemplate metadataTemplate, string path) => Files[path] = metadataTemplate;

    public MetadataTemplate Import(string path) =>
        Files.TryGetValue(path, out var template) ? template : throw new InvalidDataException("The file isn't a PictureIt template.");
}

/// <summary>Serves metadata from memory and counts reads. A path can be made slow or failing.</summary>
internal sealed class FakeMetadataReader : IMetadataReader
{
    public Dictionary<string, ImageMetadata> Photos { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> Errors { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, TaskCompletionSource> Gates { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> ReadPaths { get; } = [];

    public void Add(ImageMetadata metadata) => Photos[metadata.FilePath] = metadata;

    public async Task<ImageMetadata> ReadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var result = (await ReadManyAsync([filePath], cancellationToken))[0];
        return result.Metadata ?? throw new IOException(result.Error);
    }

    public async Task<IReadOnlyList<MetadataReadResult>> ReadManyAsync(IEnumerable<string> filePaths, CancellationToken cancellationToken = default)
    {
        var results = new List<MetadataReadResult>();
        foreach (var path in filePaths)
        {
            if (Gates.TryGetValue(path, out var gate))
            {
                await gate.Task;
            }

            ReadPaths.Add(path);
            results.Add(Errors.TryGetValue(path, out var error) || !Photos.TryGetValue(path, out var photo)
                ? new MetadataReadResult(path, null, error ?? "File not found.")
                : new MetadataReadResult(path, photo, null));
        }

        return results;
    }
}

/// <summary>Everything a view-model test needs, wired the way the app wires it.</summary>
internal sealed class Harness
{
    public Harness()
    {
        Dialogs.ConfirmChangesAsync(Arg.Any<ChangePreview>()).Returns(true);
        Dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        Dialogs.ShowToolAsync(Arg.Any<ToolViewModel>()).Returns(true);
        Dialogs.RunWithProgressAsync(Arg.Any<string>(), Arg.Any<Func<IProgress<WriteProgress>, CancellationToken, Task<WriteResult>>>())
            .Returns(call => call.Arg<Func<IProgress<WriteProgress>, CancellationToken, Task<WriteResult>>>()(new Progress<WriteProgress>(), CancellationToken.None));
        Writer.ApplyAsync(Arg.Any<EditSet>(), Arg.Any<IProgress<WriteProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(call => Succeeded(call.Arg<EditSet>()));

        Runner = new BatchRunner(Writer, Dialogs, History, Messenger, Time);
        Editor = new MetadataEditorViewModel(Reader, Runner, Dialogs, Settings, Messenger) { SelectionDelay = TimeSpan.Zero };
        Tools = new ToolsViewModel(Editor, Runner, Dialogs, FolderPicker, Templates);
    }

    public FakeMetadataReader Reader { get; } = new();

    public IMetadataWriter Writer { get; } = Substitute.For<IMetadataWriter>();

    public IDialogService Dialogs { get; } = Substitute.For<IDialogService>();

    public IFolderPicker FolderPicker { get; } = Substitute.For<IFolderPicker>();

    public IFilePicker FilePicker { get; } = Substitute.For<IFilePicker>();

    public IBackupStore Backups { get; } = Substitute.For<IBackupStore>();

    public InMemorySettingsStore Settings { get; } = new();

    public InMemoryHistoryStore History { get; } = new();

    public InMemoryTemplateStore Templates { get; } = new();

    public IMessenger Messenger { get; } = new WeakReferenceMessenger();

    public TimeProvider Time { get; } = TimeProvider.System;

    public BatchRunner Runner { get; }

    public MetadataEditorViewModel Editor { get; }

    public ToolsViewModel Tools { get; }

    /// <summary>The edit sets passed to the writer, in order.</summary>
    public IReadOnlyList<EditSet> Written =>
        [.. Writer.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(IMetadataWriter.ApplyAsync)).Select(c => (EditSet)c.GetArguments()[0]!)];

    public static WriteResult Succeeded(EditSet editSet) =>
        new(editSet.BatchId, [.. editSet.Files.Select(f => new FileWriteResult(f.FilePath, true, null, []))], true);
}

internal static class Metadata
{
    public const string Path = @"C:\photos\a.jpg";

    /// <summary>Builds metadata from "Group:Name" → value pairs; values containing "; " become lists.</summary>
    public static ImageMetadata Create(string path, params (string Key, string Value)[] tags) =>
        new(path, tags.Select(t =>
        {
            var key = TagKey.Parse(t.Key);
            var values = t.Value.Split("; ");
            var readOnly = key.Group is "File" or "Composite" or "System";
            return new MetadataTag(key, key.Name, values, null, IsList: values.Length > 1 || Core.Catalog.TagCatalog.IsListTag(key), IsWritable: !readOnly);
        }));

    public static ImageMetadata Sample(string path = Path) => Create(
        path,
        ("IFD0:Model", "Canon EOS R5"),
        ("MWG:Keywords", "London; Big Ben"),
        ("MWG:Description", "Old description"),
        ("ExifIFD:DateTimeOriginal", "2024:06:15 14:32:10"),
        ("ExifIFD:ExposureTime", "1/250"),
        ("File:FileType", "JPEG"),
        ("Composite:GPSPosition", "51 deg N, 0 deg W"));
}
