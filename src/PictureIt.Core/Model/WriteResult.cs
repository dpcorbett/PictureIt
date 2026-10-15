namespace PictureIt.Core.Model;

/// <summary>Outcome of writing one file in a batch.</summary>
public sealed record FileWriteResult(string FilePath, bool Succeeded, string? Error, IReadOnlyList<string> Warnings);

/// <summary>Outcome of applying an <see cref="EditSet"/>.</summary>
public sealed record WriteResult(Guid BatchId, IReadOnlyList<FileWriteResult> Files, bool BackedUp)
{
    public int SucceededCount => Files.Count(f => f.Succeeded);

    public int FailedCount => Files.Count(f => !f.Succeeded);
}

/// <summary>Progress report while a batch is being written.</summary>
public sealed record WriteProgress(int Completed, int Total, string CurrentFile);
