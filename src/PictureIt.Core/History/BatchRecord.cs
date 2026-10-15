namespace PictureIt.Core.History;

/// <summary>A batch of edits that was written, kept so it can be reviewed and undone from its backup.</summary>
public sealed record BatchRecord(
    Guid BatchId,
    string Description,
    DateTimeOffset CreatedAt,
    IReadOnlyList<string> FilePaths,
    int SucceededCount,
    int FailedCount,
    bool BackedUp)
{
    public DateTimeOffset? RevertedAt { get; init; }

    /// <summary>The backup was removed by clean-up, so the batch can no longer be undone.</summary>
    public bool BackupDeleted { get; init; }

    public bool CanUndo => BackedUp && RevertedAt is null && !BackupDeleted && SucceededCount > 0;
}

/// <summary>Most recent first.</summary>
public interface IHistoryStore
{
    IReadOnlyList<BatchRecord> Load();

    void Save(IReadOnlyList<BatchRecord> records);
}
