namespace PictureIt.Core.History;

/// <summary>
/// Follows files through the renames and moves recorded in history, so undoing an older metadata change restores
/// each photo where it is now rather than bringing back a copy under its old name.
/// </summary>
public static class PathHistory
{
    /// <summary>
    /// Maps each of <paramref name="paths"/> (as they were at <paramref name="since"/>) to where it is now, applying
    /// later renames and moves, and the undoing of them, in the order they happened. Paths that didn't move map to
    /// themselves.
    /// </summary>
    public static IReadOnlyDictionary<string, string> CurrentPaths(
        IEnumerable<string> paths,
        DateTimeOffset since,
        IEnumerable<BatchRecord> history)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(history);

        // Each move batch is an event when it ran and, if undone, a reverse event when it was undone.
        var events = new List<(DateTimeOffset At, IReadOnlyList<FileMove> Moves)>();
        foreach (var record in history.Where(r => r.MovesFiles && r.Moves.Count > 0))
        {
            if (record.CreatedAt > since)
            {
                events.Add((record.CreatedAt, record.Moves));
            }

            if (record.RevertedAt is { } reverted && reverted > since)
            {
                events.Add((reverted, [.. record.Moves.Select(m => new FileMove(m.To, m.From))]));
            }
        }

        var current = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToDictionary(p => p, p => p, StringComparer.OrdinalIgnoreCase);
        foreach (var (_, moves) in events.OrderBy(e => e.At))
        {
            // All moves in a batch happen together (a batch can swap two names), so look them all up first.
            var map = moves.ToDictionary(m => m.From, m => m.To, StringComparer.OrdinalIgnoreCase);
            foreach (var original in current.Keys.ToList())
            {
                if (map.TryGetValue(current[original], out var moved))
                {
                    current[original] = moved;
                }
            }
        }

        return current;
    }
}
