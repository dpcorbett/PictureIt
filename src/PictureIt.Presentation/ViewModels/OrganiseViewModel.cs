using System.ComponentModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using PictureIt.Core.Abstractions;
using PictureIt.Core.History;
using PictureIt.Core.Model;
using PictureIt.Core.Organise;
using PictureIt.Presentation.Services;
using PictureIt.Presentation.ViewModels.Tools;

namespace PictureIt.Presentation.ViewModels;

/// <summary>
/// Organising tools: rename and sort the selected photos, export their metadata, import metadata into the folder's
/// photos, and find duplicates in the folder.
/// </summary>
public sealed partial class OrganiseViewModel : ObservableObject
{
    private const int MaxListed = 8;

    private readonly MetadataEditorViewModel _editor;
    private readonly OrganiseServices _services;
    private IReadOnlyList<string> _folderPhotos = [];

    public OrganiseViewModel(MetadataEditorViewModel editor, OrganiseServices services)
    {
        ArgumentNullException.ThrowIfNull(editor);
        _editor = editor;
        _services = services;
        editor.PropertyChanged += OnEditorPropertyChanged;
    }

    /// <summary>The open folder, or null.</summary>
    public string? CurrentFolder { get; private set; }

    private bool HasPhotos => _editor.HasPhoto && !_editor.IsLoading;

    /// <summary>True when a folder or search results with photos are open.</summary>
    private bool HasFolderPhotos => _folderPhotos.Count > 0;

    /// <summary>Called by the library when it shows a folder (or search results, with no folder) or re-reads it.</summary>
    public void SetFolder(string? folder, IReadOnlyList<string> photos)
    {
        CurrentFolder = folder;
        _folderPhotos = photos ?? [];
        ImportMetadataCommand.NotifyCanExecuteChanged();
        FindDuplicatesCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(HasPhotos))]
    private async Task RenameAsync()
    {
        if (!await _editor.ConfirmLeaveAsync())
        {
            return;
        }

        var tool = new RenameToolViewModel(SelectedPhotos(), File.Exists);
        if (await _services.Dialogs.ShowToolAsync(tool) && tool.Plan is { } plan)
        {
            await _services.FileRunner.RunAsync(plan);
        }
    }

    [RelayCommand(CanExecute = nameof(HasPhotos))]
    private async Task SortIntoFoldersAsync()
    {
        if (!await _editor.ConfirmLeaveAsync())
        {
            return;
        }

        var photos = SelectedPhotos();
        var destination = CurrentFolder ?? Path.GetDirectoryName(photos[0].FilePath)!;
        var tool = new SortToolViewModel(photos, File.Exists, _services.FolderPicker, destination);
        if (await _services.Dialogs.ShowToolAsync(tool) && tool.Plan is { } plan)
        {
            await _services.FileRunner.RunAsync(plan);
        }
    }

    [RelayCommand(CanExecute = nameof(HasPhotos))]
    private async Task ExportMetadataAsync()
    {
        var photos = SelectedPhotos();
        var tool = new ExportToolViewModel(photos.Count);
        if (!await _services.Dialogs.ShowToolAsync(tool))
        {
            return;
        }

        var name = CurrentFolder is { } folder ? $"{Path.GetFileName(folder)} metadata" : "Photo metadata";
        if (await _services.FilePicker.PickFileToSaveAsync(name, tool.TypeName, tool.Extension) is not { } path)
        {
            return;
        }

        var text = MetadataExport.Write(photos, MetadataExport.Columns(photos, tool.Fields), tool.Format);
        try
        {
            // Excel needs the byte-order mark to read a CSV as UTF-8.
            await File.WriteAllTextAsync(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: tool.Format == ExportFormat.Csv));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _services.Dialogs.ShowErrorAsync("Couldn't export", ex.Message);
            return;
        }

        await _services.Dialogs.ShowInfoAsync("Exported", $"Saved the metadata of {Count(photos.Count)} to {path}.");
    }

    [RelayCommand(CanExecute = nameof(HasFolderPhotos))]
    private async Task ImportMetadataAsync()
    {
        if (!await _editor.ConfirmLeaveAsync() || await _services.FilePicker.PickFileToOpenAsync([".csv", ".json"]) is not { } path)
        {
            return;
        }

        ImportTable table;
        try
        {
            table = MetadataImport.Parse(await File.ReadAllTextAsync(path), Path.GetExtension(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            await _services.Dialogs.ShowErrorAsync("Couldn't import", ex.Message);
            return;
        }

        var match = MetadataImport.Match(table, _folderPhotos);
        if (match.Matched.Count == 0)
        {
            await _services.Dialogs.ShowErrorAsync(
                "Nothing to import",
                $"None of the {Count(table.Rows.Count)} in {Path.GetFileName(path)} are in this folder. The SourceFile column should name each photo.");
            return;
        }

        var reads = await _services.Reader.ReadManyAsync(match.Matched.Select(m => m.FilePath));
        var current = reads.Where(r => r.Metadata is not null).ToDictionary(r => r.FilePath, r => r.Metadata!, StringComparer.OrdinalIgnoreCase);
        var problems = reads.Where(r => r.Metadata is null).Select(r => $"{Path.GetFileName(r.FilePath)}: {r.Error}").ToList();
        var files = new List<FileEdits>();
        foreach (var (file, row) in match.Matched.Where(m => current.ContainsKey(m.FilePath)))
        {
            var edits = MetadataImport.EditsFor(row, current[file]);
            problems.AddRange(edits.Problems);
            files.Add(new FileEdits(file, edits.Edits));
        }

        var notes = new List<string>();
        if (match.Unmatched.Count > 0)
        {
            notes.Add($"{Count(match.Unmatched.Count)} in the file aren't in this folder and will be skipped: {List(match.Unmatched.Select(Path.GetFileName)!)}");
        }

        if (table.ReadOnlyColumns.Count > 0)
        {
            notes.Add($"These columns can't be written and will be ignored: {List(table.ReadOnlyColumns.Select(c => c.ToString()))}");
        }

        if (problems.Count > 0)
        {
            notes.Add($"These values will be skipped: {Environment.NewLine}{string.Join(Environment.NewLine, problems.Take(MaxListed))}{(problems.Count > MaxListed ? $"{Environment.NewLine}…and {problems.Count - MaxListed} more" : string.Empty)}");
        }

        if (notes.Count > 0 && !await _services.Dialogs.ConfirmAsync(
                $"Import from {Path.GetFileName(path)}?",
                string.Join(Environment.NewLine + Environment.NewLine, [$"{Count(match.Matched.Count)} matched.", .. notes]),
                "Continue"))
        {
            return;
        }

        await _services.BatchRunner.RunAsync(EditSet.Create($"Import metadata from {Path.GetFileName(path)}", files), current);
    }

    [RelayCommand(CanExecute = nameof(HasFolderPhotos))]
    private async Task FindDuplicatesAsync()
    {
        var photos = _folderPhotos;
        var tool = new FindDuplicatesToolViewModel(photos.Count);
        if (!await _services.Dialogs.ShowToolAsync(tool))
        {
            return;
        }

        IReadOnlyList<DuplicateGroup> groups;
        try
        {
            groups = await _services.Dialogs.RunWithProgressAsync(
                "Finding duplicates",
                (progress, cancellationToken) => _services.Scanner.ScanAsync(photos, tool.IncludeSameMoment, progress, cancellationToken));
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (groups.Count == 0)
        {
            await _services.Dialogs.ShowInfoAsync("No duplicates", $"None of the {Count(photos.Count)} in this folder are duplicates.");
            return;
        }

        var review = await Task.Run(() => new DuplicatesViewModel([.. groups.Select(g => new DuplicateGroupViewModel(g.Kind, [.. g.FilePaths.Select(Describe)]))]));
        if (!await _services.Dialogs.ShowToolAsync(review))
        {
            return;
        }

        await RecycleAsync(review.Description, [.. review.Marked.Select(i => i.FilePath)]);
    }

    private async Task RecycleAsync(string description, IReadOnlyList<string> paths)
    {
        var batchId = Guid.NewGuid();
        var result = await _services.Dialogs.RunWithProgressAsync(description, async (progress, cancellationToken) =>
        {
            var files = new List<FileWriteResult>();
            foreach (var path in paths)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                progress.Report(new WriteProgress(files.Count, paths.Count, path));
                var error = await _services.RecycleBin.RecycleAsync(path);
                files.Add(new FileWriteResult(path, error is null, error, []));
            }

            progress.Report(new WriteProgress(paths.Count, paths.Count, string.Empty));
            return new WriteResult(batchId, files, BackedUp: false);
        });

        if (result.Files.Count > 0)
        {
            var record = new BatchRecord(batchId, description, _services.Time.GetUtcNow(), [.. result.Files.Select(f => f.FilePath)], result.SucceededCount, result.FailedCount, BackedUp: false)
            {
                Kind = BatchKind.Recycle,
            };
            _services.History.Save([record, .. _services.History.Load()]);
        }

        _services.Messenger.Send(new FolderChangedMessage([]));
        if (result.FailedCount > 0)
        {
            await _services.Dialogs.ShowWriteFailuresAsync(result);
        }
    }

    private DuplicateItemViewModel Describe(string path)
    {
        var info = new FileInfo(path);
        return new DuplicateItemViewModel(path, info.Exists ? info.Length : 0, info.LastWriteTime, _services.Summaries.ReadSummary(path));
    }

    /// <summary>The selected photos that could be read, in selection order.</summary>
    private List<ImageMetadata> SelectedPhotos() =>
        [.. _editor.RequestedPaths.Select(p => _editor.LoadedMetadata.GetValueOrDefault(p)).OfType<ImageMetadata>()];

    private static string Count(int count) => count == 1 ? "1 photo" : $"{count} photos";

    private static string List(IEnumerable<string> items)
    {
        var list = items.ToList();
        var shown = string.Join(", ", list.Take(MaxListed));
        return list.Count > MaxListed ? $"{shown} and {list.Count - MaxListed} more." : $"{shown}.";
    }

    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MetadataEditorViewModel.HasPhoto) or nameof(MetadataEditorViewModel.IsLoading))
        {
            RenameCommand.NotifyCanExecuteChanged();
            SortIntoFoldersCommand.NotifyCanExecuteChanged();
            ExportMetadataCommand.NotifyCanExecuteChanged();
        }
    }
}

/// <summary>The services <see cref="OrganiseViewModel"/> uses, gathered so its constructor stays readable.</summary>
public sealed record OrganiseServices(
    BatchRunner BatchRunner,
    FileOperationRunner FileRunner,
    IDialogService Dialogs,
    IFolderPicker FolderPicker,
    IFilePicker FilePicker,
    IMetadataReader Reader,
    IPhotoSummaryReader Summaries,
    IDuplicateScanner Scanner,
    IRecycleBin RecycleBin,
    IHistoryStore History,
    IMessenger Messenger,
    TimeProvider Time);
