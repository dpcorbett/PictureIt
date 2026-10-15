using PictureIt.Core.History;
using PictureIt.Core.Model;
using PictureIt.Core.Organise;

namespace PictureIt.Core.Abstractions;

/// <summary>What happened to one file in a <see cref="FilePlan"/>.</summary>
public sealed record FileOperationOutcome(string SourcePath, string TargetPath, bool Succeeded, string? Error);

/// <summary>The result of carrying out (or undoing) a <see cref="FilePlan"/>.</summary>
/// <param name="BatchId">The plan's batch.</param>
/// <param name="Files">Every file that was attempted. Files not reached before a cancel aren't listed.</param>
/// <param name="CreatedFolders">Folders created for the targets, parents first.</param>
public sealed record FileOperationResult(Guid BatchId, IReadOnlyList<FileOperationOutcome> Files, IReadOnlyList<string> CreatedFolders)
{
    public int SucceededCount => Files.Count(f => f.Succeeded);

    public int FailedCount => Files.Count(f => !f.Succeeded);

    public IReadOnlyList<FileMove> Moves => [.. Files.Where(f => f.Succeeded).Select(f => new FileMove(f.SourcePath, f.TargetPath))];

    /// <summary>The same outcome in the shape the failure report takes.</summary>
    public WriteResult ToWriteResult() =>
        new(BatchId, [.. Files.Select(f => new FileWriteResult(f.SourcePath, f.Succeeded, f.Error, []))], BackedUp: false);
}

/// <summary>Renames, moves and copies files, never overwriting anything.</summary>
public interface IFileOrganiser
{
    Task<FileOperationResult> ExecuteAsync(FilePlan plan, IProgress<WriteProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Undoes a rename, move or copy batch: moves files back, or deletes the copies, then removes the folders the
    /// batch created if they're empty.
    /// </summary>
    Task<FileOperationResult> UndoAsync(BatchRecord record, CancellationToken cancellationToken = default);
}

/// <summary>Sends files to the Recycle Bin.</summary>
public interface IRecycleBin
{
    /// <summary>Returns null on success, or why the file couldn't be recycled.</summary>
    Task<string?> RecycleAsync(string filePath);
}
