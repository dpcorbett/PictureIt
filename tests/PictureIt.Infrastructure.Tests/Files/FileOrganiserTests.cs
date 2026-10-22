using PictureIt.Core.Abstractions;
using PictureIt.Core.History;
using PictureIt.Core.Model;
using PictureIt.Core.Organise;
using PictureIt.Infrastructure.Files;

namespace PictureIt.Infrastructure.Tests.Files;

public sealed class FileOrganiserTests : IDisposable
{
    private readonly TestFiles _files = new();
    private readonly FileOrganiser _organiser = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Write(string name, string content)
    {
        var path = _files.PathFor(name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private FilePlan Plan(FileOperationKind kind, params (string From, string To)[] moves) =>
        new(Guid.NewGuid(), kind, "test", [.. moves.Select(m => new PlannedFileOperation(_files.PathFor(m.From), _files.PathFor(m.To), null))]);

    private static BatchRecord Record(FilePlan plan, FileOperationResult result) =>
        new(plan.BatchId, "test", DateTimeOffset.UtcNow, [], result.SucceededCount, result.FailedCount, false)
        {
            Kind = plan.Kind switch { FileOperationKind.Copy => BatchKind.Copy, FileOperationKind.Move => BatchKind.Move, _ => BatchKind.Rename },
            Moves = result.Moves,
            CreatedFolders = result.CreatedFolders,
        };

    [Fact]
    public async Task Renames_and_undoes()
    {
        Write("a.jpg", "A");
        var plan = Plan(FileOperationKind.Rename, ("a.jpg", "b.jpg"));

        var result = await _organiser.ExecuteAsync(plan, cancellationToken: Ct);

        result.SucceededCount.ShouldBe(1);
        File.ReadAllText(_files.PathFor("b.jpg")).ShouldBe("A");
        File.Exists(_files.PathFor("a.jpg")).ShouldBeFalse();

        var undo = await _organiser.UndoAsync(Record(plan, result), Ct);

        undo.FailedCount.ShouldBe(0);
        File.ReadAllText(_files.PathFor("a.jpg")).ShouldBe("A");
        File.Exists(_files.PathFor("b.jpg")).ShouldBeFalse();
    }

    [Fact]
    public async Task Swaps_names_within_a_batch()
    {
        Write("a.jpg", "A");
        Write("b.jpg", "B");
        var plan = Plan(FileOperationKind.Rename, ("a.jpg", "b.jpg"), ("b.jpg", "a.jpg"));

        var result = await _organiser.ExecuteAsync(plan, cancellationToken: Ct);

        result.SucceededCount.ShouldBe(2);
        File.ReadAllText(_files.PathFor("a.jpg")).ShouldBe("B");
        File.ReadAllText(_files.PathFor("b.jpg")).ShouldBe("A");
        Directory.GetFiles(_files.Directory).Length.ShouldBe(2);

        await _organiser.UndoAsync(Record(plan, result), Ct);
        File.ReadAllText(_files.PathFor("a.jpg")).ShouldBe("A");
        File.ReadAllText(_files.PathFor("b.jpg")).ShouldBe("B");
    }

    [Fact]
    public async Task Changes_only_the_case_of_a_name()
    {
        Write("IMG.jpg", "A");
        var result = await _organiser.ExecuteAsync(Plan(FileOperationKind.Rename, ("IMG.jpg", "img.jpg")), cancellationToken: Ct);

        result.SucceededCount.ShouldBe(1);
        Path.GetFileName(Directory.GetFiles(_files.Directory).Single()).ShouldBe("img.jpg");
    }

    [Fact]
    public async Task Never_overwrites_a_file_that_appeared_since_planning()
    {
        Write("a.jpg", "A");
        Write("b.jpg", "already here");

        var result = await _organiser.ExecuteAsync(Plan(FileOperationKind.Rename, ("a.jpg", "b.jpg")), cancellationToken: Ct);

        result.FailedCount.ShouldBe(1);
        result.Files.Single().Error.ShouldNotBeNull().ShouldContain("b.jpg already exists");
        File.ReadAllText(_files.PathFor("a.jpg")).ShouldBe("A");
        File.ReadAllText(_files.PathFor("b.jpg")).ShouldBe("already here");
    }

    [Fact]
    public async Task Moves_into_new_folders_and_undo_removes_them()
    {
        Write("a.jpg", "A");
        var plan = Plan(FileOperationKind.Move, ("a.jpg", @"2024\06\a.jpg"));

        var result = await _organiser.ExecuteAsync(plan, cancellationToken: Ct);

        File.ReadAllText(_files.PathFor(@"2024\06\a.jpg")).ShouldBe("A");
        result.CreatedFolders.ShouldBe([_files.PathFor("2024"), _files.PathFor(@"2024\06")]);

        await _organiser.UndoAsync(Record(plan, result), Ct);

        File.ReadAllText(_files.PathFor("a.jpg")).ShouldBe("A");
        Directory.Exists(_files.PathFor("2024")).ShouldBeFalse();
    }

    [Fact]
    public async Task Undo_keeps_created_folders_that_now_hold_other_files()
    {
        Write("a.jpg", "A");
        var plan = Plan(FileOperationKind.Move, ("a.jpg", @"2024\a.jpg"));
        var result = await _organiser.ExecuteAsync(plan, cancellationToken: Ct);
        Write(@"2024\mine.txt", "added later");

        await _organiser.UndoAsync(Record(plan, result), Ct);

        File.Exists(_files.PathFor(@"2024\mine.txt")).ShouldBeTrue();
    }

    [Fact]
    public async Task Copies_and_undo_deletes_only_the_copies()
    {
        Write("a.jpg", "A");
        var plan = Plan(FileOperationKind.Copy, ("a.jpg", @"out\a.jpg"));

        var result = await _organiser.ExecuteAsync(plan, cancellationToken: Ct);

        File.ReadAllText(_files.PathFor(@"out\a.jpg")).ShouldBe("A");
        File.Exists(_files.PathFor("a.jpg")).ShouldBeTrue();

        await _organiser.UndoAsync(Record(plan, result), Ct);

        File.Exists(_files.PathFor("a.jpg")).ShouldBeTrue();
        Directory.Exists(_files.PathFor("out")).ShouldBeFalse();
    }

    [Fact]
    public async Task Carries_on_past_failures_and_reports_progress()
    {
        Write("a.jpg", "A");
        Write("c.jpg", "C");
        var reports = new List<WriteProgress>();
        var plan = Plan(FileOperationKind.Rename, ("a.jpg", "a2.jpg"), ("missing.jpg", "m2.jpg"), ("c.jpg", "c2.jpg"));

        var result = await _organiser.ExecuteAsync(plan, new SynchronousProgress(reports.Add), Ct);

        result.Files.Select(f => f.Succeeded).ShouldBe([true, false, true]);
        reports.Last().Completed.ShouldBe(3);
    }

    [Fact]
    public async Task Skips_unchanged_operations()
    {
        Write("a.jpg", "A");
        var result = await _organiser.ExecuteAsync(Plan(FileOperationKind.Rename, ("a.jpg", "a.jpg")), cancellationToken: Ct);

        result.Files.ShouldBeEmpty();
    }

    public void Dispose() => _files.Dispose();

    private sealed class SynchronousProgress(Action<WriteProgress> report) : IProgress<WriteProgress>
    {
        public void Report(WriteProgress value) => report(value);
    }
}
