using PictureIt.Core.Abstractions;
using PictureIt.Core.History;
using PictureIt.Core.Model;
using PictureIt.Core.Organise;

namespace PictureIt.Infrastructure.Files;

/// <summary>
/// Carries out <see cref="FilePlan"/>s on disk. Never overwrites: if a target appears after the plan was made, that
/// file fails instead. Renames that swap or shift names within the batch (a → b while b → c) go through temporary
/// names first.
/// </summary>
public sealed class FileOrganiser : IFileOrganiser
{
    private const string TempSuffix = ".pictureit-tmp";

    public Task<FileOperationResult> ExecuteAsync(FilePlan plan, IProgress<WriteProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return Task.Run(() => Execute(plan, progress, cancellationToken), CancellationToken.None);
    }

    public Task<FileOperationResult> UndoAsync(BatchRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        return Task.Run(() => Undo(record, cancellationToken), CancellationToken.None);
    }

    private static FileOperationResult Execute(FilePlan plan, IProgress<WriteProgress>? progress, CancellationToken cancellationToken)
    {
        var operations = plan.Changes.ToList();
        var created = new List<string>();
        var outcomes = plan.Kind == FileOperationKind.Copy
            ? Copy(operations, created, progress, cancellationToken)
            : Move(operations, created, progress, cancellationToken);
        return new FileOperationResult(plan.BatchId, outcomes, created);
    }

    private static List<FileOperationOutcome> Copy(
        List<PlannedFileOperation> operations,
        List<string> created,
        IProgress<WriteProgress>? progress,
        CancellationToken cancellationToken)
    {
        var outcomes = new List<FileOperationOutcome>();
        foreach (var operation in operations)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            progress?.Report(new WriteProgress(outcomes.Count, operations.Count, operation.SourcePath));
            outcomes.Add(Try(operation, () =>
            {
                EnsureFolder(Path.GetDirectoryName(operation.TargetPath)!, created);
                File.Copy(operation.SourcePath, operation.TargetPath, overwrite: false);
            }));
        }

        progress?.Report(new WriteProgress(operations.Count, operations.Count, string.Empty));
        return outcomes;
    }

    private static List<FileOperationOutcome> Move(
        List<PlannedFileOperation> operations,
        List<string> created,
        IProgress<WriteProgress>? progress,
        CancellationToken cancellationToken)
    {
        // A target that's currently another source in the batch (or the same file in a different case) is only free
        // once that file has moved, so everything goes via a temporary name.
        var sources = operations.Select(o => o.SourcePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var viaTemp = operations.Any(o => sources.Contains(o.TargetPath));
        return viaTemp
            ? MoveViaTemp(operations, created, progress)
            : MoveDirect(operations, created, progress, cancellationToken);
    }

    private static List<FileOperationOutcome> MoveDirect(
        List<PlannedFileOperation> operations,
        List<string> created,
        IProgress<WriteProgress>? progress,
        CancellationToken cancellationToken)
    {
        var outcomes = new List<FileOperationOutcome>();
        foreach (var operation in operations)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            progress?.Report(new WriteProgress(outcomes.Count, operations.Count, operation.SourcePath));
            outcomes.Add(Try(operation, () =>
            {
                EnsureFolder(Path.GetDirectoryName(operation.TargetPath)!, created);
                File.Move(operation.SourcePath, operation.TargetPath, overwrite: false);
            }));
        }

        progress?.Report(new WriteProgress(operations.Count, operations.Count, string.Empty));
        return outcomes;
    }

    /// <summary>
    /// Moves every file to a temporary name, then to its target. Not cancellable once started, so a batch is never
    /// left half-way with files under temporary names.
    /// </summary>
    private static List<FileOperationOutcome> MoveViaTemp(
        List<PlannedFileOperation> operations,
        List<string> created,
        IProgress<WriteProgress>? progress)
    {
        var outcomes = new List<FileOperationOutcome>();
        var parked = new List<(PlannedFileOperation Operation, string Temp)>();
        foreach (var operation in operations)
        {
            var temp = Path.Combine(Path.GetDirectoryName(operation.SourcePath)!, $"{Path.GetFileName(operation.SourcePath)}.{Guid.NewGuid():N}{TempSuffix}");
            var outcome = Try(operation, () => File.Move(operation.SourcePath, temp));
            if (outcome.Succeeded)
            {
                parked.Add((operation, temp));
            }
            else
            {
                outcomes.Add(outcome);
            }
        }

        foreach (var (operation, temp) in parked)
        {
            progress?.Report(new WriteProgress(outcomes.Count, operations.Count, operation.SourcePath));
            var outcome = Try(operation, () =>
            {
                EnsureFolder(Path.GetDirectoryName(operation.TargetPath)!, created);
                File.Move(temp, operation.TargetPath, overwrite: false);
            });

            if (!outcome.Succeeded)
            {
                // Put it back where it was.
                var restored = Try(operation, () => File.Move(temp, operation.SourcePath, overwrite: false));
                outcome = outcome with { Error = restored.Succeeded ? outcome.Error : $"{outcome.Error} It was left as {Path.GetFileName(temp)}." };
            }

            outcomes.Add(outcome);
        }

        progress?.Report(new WriteProgress(operations.Count, operations.Count, string.Empty));
        return outcomes;
    }

    private static FileOperationResult Undo(BatchRecord record, CancellationToken cancellationToken)
    {
        List<FileOperationOutcome> outcomes;
        if (record.Kind == BatchKind.Copy)
        {
            outcomes = [];
            foreach (var move in record.Moves)
            {
                var operation = new PlannedFileOperation(move.To, move.To, null);
                outcomes.Add(Try(operation, () => File.Delete(move.To)));
            }
        }
        else if (record.MovesFiles)
        {
            var back = record.Moves.Select(m => new PlannedFileOperation(m.To, m.From, null)).ToList();
            outcomes = Move(back, [], progress: null, cancellationToken);
        }
        else
        {
            throw new ArgumentException($"A {record.Kind} batch can't be undone by moving files.", nameof(record));
        }

        // Newest (deepest) first, and only if nothing else has been put in them.
        foreach (var folder in record.CreatedFolders.Reverse())
        {
            try
            {
                if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
                {
                    Directory.Delete(folder);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Leaving an empty folder behind is harmless.
            }
        }

        return new FileOperationResult(record.BatchId, outcomes, []);
    }

    private static FileOperationOutcome Try(PlannedFileOperation operation, Action action)
    {
        try
        {
            action();
            return new FileOperationOutcome(operation.SourcePath, operation.TargetPath, true, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var error = ex is IOException && File.Exists(operation.TargetPath) && !string.Equals(operation.SourcePath, operation.TargetPath, StringComparison.OrdinalIgnoreCase)
                ? $"{Path.GetFileName(operation.TargetPath)} already exists in {Path.GetDirectoryName(operation.TargetPath)}."
                : ex.Message;
            return new FileOperationOutcome(operation.SourcePath, operation.TargetPath, false, error);
        }
    }

    /// <summary>Creates a folder and any missing parents, recording each one created (parents first).</summary>
    private static void EnsureFolder(string folder, List<string> created)
    {
        var missing = new Stack<string>();
        for (var current = folder; !string.IsNullOrEmpty(current) && !Directory.Exists(current); current = Path.GetDirectoryName(current))
        {
            missing.Push(current);
        }

        while (missing.TryPop(out var next))
        {
            Directory.CreateDirectory(next);
            created.Add(next);
        }
    }
}
