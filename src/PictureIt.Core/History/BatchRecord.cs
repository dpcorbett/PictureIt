namespace PictureIt.Core.History;

/// <summary>What a batch did, which decides how it's undone.</summary>
public enum BatchKind
{
    /// <summary>Changed metadata; undone from the backup.</summary>
    Metadata,

    /// <summary>Renamed files; undone by renaming them back.</summary>
    Rename,

    /// <summary>Moved files to other folders; undone by moving them back.</summary>
    Move,

    /// <summary>Copied files; undone by deleting the copies.</summary>
    Copy,

    /// <summary>Sent files to the Recycle Bin; restored from there, not from History.</summary>
    Recycle,
}

/// <summary>A file a batch renamed, moved or copied.</summary>
public sealed record FileMove(string From, string To);

/// <summary>A batch that was written, kept so it can be reviewed and undone.</summary>
public sealed record BatchRecord(
    Guid BatchId,
    string Description,
    DateTimeOffset CreatedAt,
    IReadOnlyList<string> FilePaths,
    int SucceededCount,
    int FailedCount,
    bool BackedUp)
{
    public BatchKind Kind { get; init; } = BatchKind.Metadata;

    /// <summary>For renames, moves and copies: where each file went (successful ones only).</summary>
    public IReadOnlyList<FileMove> Moves { get; init; } = [];

    /// <summary>Folders the batch created, in the order it created them; undo removes them if they're empty.</summary>
    public IReadOnlyList<string> CreatedFolders { get; init; } = [];

    public DateTimeOffset? RevertedAt { get; init; }

    /// <summary>The backup was removed by clean-up, so the batch can no longer be undone.</summary>
    public bool BackupDeleted { get; init; }

    public bool CanUndo => RevertedAt is null && SucceededCount > 0 && Kind switch
    {
        BatchKind.Metadata => BackedUp && !BackupDeleted,
        BatchKind.Rename or BatchKind.Move or BatchKind.Copy => Moves.Count > 0,
        _ => false,
    };

    /// <summary>True for batches that change where files are, rather than what's in them.</summary>
    public bool MovesFiles => Kind is BatchKind.Rename or BatchKind.Move;
}

/// <summary>Most recent first.</summary>
public interface IHistoryStore
{
    IReadOnlyList<BatchRecord> Load();

    void Save(IReadOnlyList<BatchRecord> records);
}
