using CommunityToolkit.Mvvm.Messaging;
using PictureIt.Core.Abstractions;
using PictureIt.Core.History;
using PictureIt.Core.Model;
using PictureIt.Core.Organise;
using PictureIt.Presentation.Services;
using PictureIt.Presentation.ViewModels.Tools;

namespace PictureIt.Presentation.Tests;

public sealed class OrganiseViewModelTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "PictureIt.Tests", Guid.NewGuid().ToString("N"));
    private readonly Harness _h = new();
    private readonly List<FolderChangedMessage> _folderChanges = [];

    public OrganiseViewModelTests()
    {
        Directory.CreateDirectory(_folder);
        _h.Messenger.Register<FolderChangedMessage>(this, (_, m) => _folderChanges.Add(m));
    }

    private string PathFor(string name) => Path.Combine(_folder, name);

    private async Task SelectAsync(params ImageMetadata[] photos)
    {
        foreach (var photo in photos)
        {
            _h.Reader.Add(photo);
        }

        await _h.Editor.ShowSelectionAsync([.. photos.Select(p => p.FilePath)]);
    }

    private static ImageMetadata Photo(string path, string taken) =>
        Metadata.Create(path, ("ExifIFD:DateTimeOriginal", taken), ("IFD0:Model", "Canon EOS R5"), ("XMP-dc:Title", "Old title"));

    [Fact]
    public async Task Rename_runs_the_plan_and_records_it_for_undo()
    {
        await SelectAsync(Photo(PathFor("b.jpg"), "2024:06:15 12:00:00"), Photo(PathFor("a.jpg"), "2024:06:15 09:00:00"));

        await _h.Organise.RenameCommand.ExecuteAsync(null);

        var plan = _h.Executed.ShouldHaveSingleItem();
        plan.Kind.ShouldBe(FileOperationKind.Rename);
        plan.Operations.Select(o => (Path.GetFileName(o.SourcePath), Path.GetFileName(o.TargetPath)))
            .ShouldBe([("a.jpg", "2024-06-15_001.jpg"), ("b.jpg", "2024-06-15_002.jpg")]);

        var record = _h.History.Records.ShouldHaveSingleItem();
        record.Kind.ShouldBe(BatchKind.Rename);
        record.Moves.Count.ShouldBe(2);
        record.CanUndo.ShouldBeTrue();
        _folderChanges.ShouldHaveSingleItem().Moves.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Rename_does_nothing_when_cancelled()
    {
        _h.Dialogs.ShowToolAsync(Arg.Any<ToolViewModel>()).Returns(false);
        await SelectAsync(Photo(PathFor("a.jpg"), "2024:06:15 09:00:00"));

        await _h.Organise.RenameCommand.ExecuteAsync(null);

        _h.Executed.ShouldBeEmpty();
        _h.History.Records.ShouldBeEmpty();
    }

    [Fact]
    public async Task Sort_defaults_to_the_open_folder_and_can_copy()
    {
        await SelectAsync(Photo(PathFor("a.jpg"), "2024:06:15 09:00:00"));
        _h.Organise.SetFolder(_folder, [PathFor("a.jpg")]);
        _h.Dialogs.ShowToolAsync(Arg.Do<ToolViewModel>(t => ((SortToolViewModel)t).Copy = true)).Returns(true);

        await _h.Organise.SortIntoFoldersCommand.ExecuteAsync(null);

        var plan = _h.Executed.ShouldHaveSingleItem();
        plan.Kind.ShouldBe(FileOperationKind.Copy);
        plan.Operations.Single().TargetPath.ShouldBe(Path.Combine(_folder, "2024", "06", "a.jpg"));
        _h.History.Records.Single().Kind.ShouldBe(BatchKind.Copy);

        // Copies don't move anything the library shows.
        _folderChanges.Single().Moves.ShouldBeEmpty();
    }

    [Fact]
    public async Task File_failures_are_reported()
    {
        await SelectAsync(Photo(PathFor("a.jpg"), "2024:06:15 09:00:00"));
        _h.Organiser.ExecuteAsync(Arg.Any<FilePlan>(), Arg.Any<IProgress<WriteProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var plan = call.Arg<FilePlan>();
                return new FileOperationResult(plan.BatchId, [.. plan.Changes.Select(o => new FileOperationOutcome(o.SourcePath, o.TargetPath, false, "Locked"))], []);
            });

        await _h.Organise.RenameCommand.ExecuteAsync(null);

        await _h.Dialogs.Received(1).ShowWriteFailuresAsync(Arg.Is<WriteResult>(r => r.FailedCount == 1));
        _h.History.Records.Single().CanUndo.ShouldBeFalse();
    }

    [Fact]
    public async Task Export_writes_a_csv_of_the_selection()
    {
        await SelectAsync(Photo(PathFor("a.jpg"), "2024:06:15 09:00:00"));
        var output = PathFor("out.csv");
        _h.FilePicker.PickFileToSaveAsync(Arg.Any<string>(), Arg.Any<string>(), ".csv").Returns(output);

        await _h.Organise.ExportMetadataCommand.ExecuteAsync(null);

        var rows = Csv.Read(await File.ReadAllTextAsync(output, TestContext.Current.CancellationToken));
        rows[0][0].ShouldBe("SourceFile");
        rows[1][0].ShouldBe(PathFor("a.jpg"));
        rows[1][rows[0].ToList().IndexOf("XMP-dc:Title")].ShouldBe("Old title");
    }

    [Fact]
    public async Task Import_previews_and_writes_only_changed_cells()
    {
        var a = Photo(PathFor("a.jpg"), "2024:06:15 09:00:00");
        _h.Reader.Add(a);
        _h.Organise.SetFolder(_folder, [a.FilePath]);
        var csv = PathFor("edits.csv");
        await File.WriteAllTextAsync(csv, "SourceFile,XMP-dc:Title,IFD0:Model\r\na.jpg,New title,Canon EOS R5\r\n", TestContext.Current.CancellationToken);
        _h.FilePicker.PickFileToOpenAsync(Arg.Any<IReadOnlyList<string>>()).Returns(csv);

        await _h.Organise.ImportMetadataCommand.ExecuteAsync(null);

        var written = _h.Written.ShouldHaveSingleItem();
        written.Description.ShouldBe("Import metadata from edits.csv");
        written.Files.Single().Edits.ShouldBe([MetadataEdit.Set(TagKey.Parse("XMP-dc:Title"), "New title")]);

        // Nothing to warn about, so straight to the preview.
        await _h.Dialogs.DidNotReceive().ConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
        await _h.Dialogs.Received(1).ConfirmChangesAsync(Arg.Any<ChangePreview>());
    }

    [Fact]
    public async Task Import_warns_about_unmatched_rows_and_stops_if_declined()
    {
        var a = Photo(PathFor("a.jpg"), "2024:06:15 09:00:00");
        _h.Reader.Add(a);
        _h.Organise.SetFolder(_folder, [a.FilePath]);
        var csv = PathFor("edits.csv");
        await File.WriteAllTextAsync(csv, "SourceFile,XMP-dc:Title\r\na.jpg,New\r\nzzz.jpg,Other\r\n", TestContext.Current.CancellationToken);
        _h.FilePicker.PickFileToOpenAsync(Arg.Any<IReadOnlyList<string>>()).Returns(csv);
        _h.Dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(false);

        await _h.Organise.ImportMetadataCommand.ExecuteAsync(null);

        await _h.Dialogs.Received(1).ConfirmAsync(Arg.Any<string>(), Arg.Is<string>(m => m.Contains("zzz.jpg", StringComparison.Ordinal)), "Continue");
        _h.Written.ShouldBeEmpty();
    }

    [Fact]
    public async Task Import_reports_a_file_it_cannot_read()
    {
        _h.Organise.SetFolder(_folder, [PathFor("a.jpg")]);
        var csv = PathFor("bad.csv");
        await File.WriteAllTextAsync(csv, "Title\r\nx\r\n", TestContext.Current.CancellationToken);
        _h.FilePicker.PickFileToOpenAsync(Arg.Any<IReadOnlyList<string>>()).Returns(csv);

        await _h.Organise.ImportMetadataCommand.ExecuteAsync(null);

        await _h.Dialogs.Received(1).ShowErrorAsync("Couldn't import", Arg.Is<string>(m => m.Contains("SourceFile", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Duplicates_are_reviewed_then_recycled_and_recorded()
    {
        var keep = PathFor("IMG_1.jpg");
        var copy = PathFor("IMG_1 - Copy.jpg");
        _h.Organise.SetFolder(_folder, [keep, copy]);
        _h.Scanner.ScanAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<bool>(), Arg.Any<IProgress<WriteProgress>?>(), Arg.Any<CancellationToken>())
            .Returns([new DuplicateGroup(DuplicateKind.Identical, [copy, keep])]);
        DuplicatesViewModel? review = null;
        _h.Dialogs.ShowToolAsync(Arg.Do<ToolViewModel>(t => review ??= t as DuplicatesViewModel)).Returns(true);

        await _h.Organise.FindDuplicatesCommand.ExecuteAsync(null);

        review.ShouldNotBeNull().Marked.Select(i => i.FilePath).ShouldBe([copy]);
        await _h.RecycleBin.Received(1).RecycleAsync(copy);
        await _h.RecycleBin.DidNotReceive().RecycleAsync(keep);
        var record = _h.History.Records.ShouldHaveSingleItem();
        record.Kind.ShouldBe(BatchKind.Recycle);
        record.CanUndo.ShouldBeFalse();
        _folderChanges.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task No_duplicates_says_so()
    {
        _h.Organise.SetFolder(_folder, [PathFor("a.jpg"), PathFor("b.jpg")]);
        _h.Scanner.ScanAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<bool>(), Arg.Any<IProgress<WriteProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<DuplicateGroup>());

        await _h.Organise.FindDuplicatesCommand.ExecuteAsync(null);

        await _h.Dialogs.Received(1).ShowInfoAsync("No duplicates", Arg.Any<string>());
    }

    [Fact]
    public async Task Commands_need_a_selection_or_a_folder()
    {
        _h.Organise.RenameCommand.CanExecute(null).ShouldBeFalse();
        _h.Organise.ImportMetadataCommand.CanExecute(null).ShouldBeFalse();

        await SelectAsync(Photo(PathFor("a.jpg"), "2024:06:15 09:00:00"));
        _h.Organise.SetFolder(_folder, [PathFor("a.jpg")]);

        _h.Organise.RenameCommand.CanExecute(null).ShouldBeTrue();
        _h.Organise.ImportMetadataCommand.CanExecute(null).ShouldBeTrue();
        _h.Organise.FindDuplicatesCommand.CanExecute(null).ShouldBeTrue();
    }

    public void Dispose()
    {
        _h.Messenger.UnregisterAll(this);
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

public sealed class FileToolViewModelTests
{
    private static readonly ImageMetadata[] Photos =
    [
        Metadata.Create(@"C:\photos\a.jpg", ("ExifIFD:DateTimeOriginal", "2024:06:15 09:00:00")),
        Metadata.Create(@"C:\photos\b.jpg"),
    ];

    [Fact]
    public void Rename_preview_follows_the_pattern()
    {
        var tool = new RenameToolViewModel(Photos, _ => false, "{Name}-x");

        tool.Rows.Select(r => r.To).ShouldBe(["a-x.jpg", "b-x.jpg"]);
        tool.ConfirmText.ShouldBe("Rename 2 photos");
        tool.CanApply.ShouldBeTrue();

        tool.Pattern = "{DateTaken:yyyy|undated}";
        tool.Rows.Select(r => r.To).ShouldBe(["2024.jpg", "undated.jpg"]);
        tool.Rows[1].Note.ShouldBe("No date taken.");
    }

    [Fact]
    public void Rename_explains_invalid_patterns_and_no_change()
    {
        var tool = new RenameToolViewModel(Photos, _ => false, "{Nope}");
        tool.CanApply.ShouldBeFalse();
        tool.Error.ShouldNotBeNull().ShouldContain("{Nope}");
        tool.Rows.ShouldBeEmpty();

        tool.Pattern = "{Name}";
        tool.Error.ShouldBe("Nothing would change.");

        tool.Pattern = @"x\{Name}";
        tool.Error.ShouldNotBeNull().ShouldContain("can't contain");
    }

    [Fact]
    public void Rename_options_update_the_preview()
    {
        var tool = new RenameToolViewModel(Photos, _ => false, "{Seq}");
        tool.OrderByDateTaken = false;
        tool.FirstNumber = 10;

        tool.Rows.Select(r => r.To).ShouldBe(["10.jpg", "11.jpg"]);
    }

    [Fact]
    public void Inserting_a_token_appends_it()
    {
        var tool = new RenameToolViewModel(Photos, _ => false, "x_");
        tool.InsertTokenCommand.Execute(NamePattern.Tokens.First(t => t.Name == "Camera"));

        tool.Pattern.ShouldBe("x_{Camera}");
    }

    [Fact]
    public void Sort_needs_a_destination_and_shows_relative_targets()
    {
        var tool = new SortToolViewModel(Photos, _ => false, Substitute.For<IFolderPicker>(), string.Empty);
        tool.Error.ShouldBe("Choose a folder to sort into.");

        tool.Destination = @"D:\Sorted";
        tool.Rows.Select(r => r.To).ShouldBe([@"2024\06\a.jpg", @"Undated\b.jpg"]);
        tool.ConfirmText.ShouldBe("Move 2 photos");
        tool.Summary.ShouldBe("2 photos will be moved into 2 folders.");

        tool.Copy = true;
        tool.ConfirmText.ShouldBe("Copy 2 photos");
        tool.Plan.ShouldNotBeNull().Kind.ShouldBe(FileOperationKind.Copy);
    }

    [Fact]
    public void Duplicates_keep_one_identical_copy_and_tick_nothing_taken_at_the_same_moment()
    {
        var review = new DuplicatesViewModel(
        [
            new DuplicateGroupViewModel(DuplicateKind.Identical,
            [
                new DuplicateItemViewModel(@"C:\IMG_1 - Copy.jpg", 10, DateTime.Now, null),
                new DuplicateItemViewModel(@"C:\IMG_1.jpg", 10, DateTime.Now, null),
            ]),
            new DuplicateGroupViewModel(DuplicateKind.SameMoment,
            [
                new DuplicateItemViewModel(@"C:\x.jpg", 10, DateTime.Now, null),
                new DuplicateItemViewModel(@"C:\x.cr3", 30, DateTime.Now, null),
            ]),
        ]);

        review.Marked.Select(i => i.FileName).ShouldBe(["IMG_1 - Copy.jpg"]);
        review.ConfirmText.ShouldBe("Send 1 file to Recycle Bin");
        review.CanApply.ShouldBeTrue();

        // Ticking every file in a set isn't allowed.
        review.Groups[0].Items[1].IsMarked = true;
        review.CanApply.ShouldBeFalse();
        review.Error.ShouldNotBeNull().ShouldContain("Keep at least one");

        review.Groups[0].Items[0].IsMarked = false;
        review.Groups[0].Items[1].IsMarked = false;
        review.Error.ShouldBe("Tick the files to send to the Recycle Bin.");
    }
}
