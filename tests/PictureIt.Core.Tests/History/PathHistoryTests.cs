using PictureIt.Core.History;

namespace PictureIt.Core.Tests.History;

public sealed class PathHistoryTests
{
    private static readonly DateTimeOffset Start = new(2024, 6, 15, 12, 0, 0, TimeSpan.Zero);

    private static BatchRecord Moves(BatchKind kind, int minutesAfterStart, params (string From, string To)[] moves) =>
        new(Guid.NewGuid(), kind.ToString(), Start.AddMinutes(minutesAfterStart), [.. moves.Select(m => m.From)], moves.Length, 0, BackedUp: false)
        {
            Kind = kind,
            Moves = [.. moves.Select(m => new FileMove(m.From, m.To))],
        };

    [Fact]
    public void Follows_later_renames_and_moves()
    {
        BatchRecord[] history =
        [
            Moves(BatchKind.Move, 20, (@"C:\b.jpg", @"D:\2024\b.jpg")),
            Moves(BatchKind.Rename, 10, (@"C:\a.jpg", @"C:\b.jpg")),
        ];

        PathHistory.CurrentPaths([@"C:\a.jpg", @"C:\z.jpg"], Start, history)
            .ShouldBe(new Dictionary<string, string> { [@"C:\a.jpg"] = @"D:\2024\b.jpg", [@"C:\z.jpg"] = @"C:\z.jpg" });
    }

    [Fact]
    public void Ignores_moves_from_before()
    {
        var earlier = Moves(BatchKind.Rename, -10, (@"C:\a.jpg", @"C:\b.jpg"));

        PathHistory.CurrentPaths([@"C:\a.jpg"], Start, [earlier])[@"C:\a.jpg"].ShouldBe(@"C:\a.jpg");
    }

    [Fact]
    public void Applies_undone_moves_in_reverse_when_they_were_undone()
    {
        var renamed = Moves(BatchKind.Rename, 10, (@"C:\a.jpg", @"C:\b.jpg")) with { RevertedAt = Start.AddMinutes(30) };

        PathHistory.CurrentPaths([@"C:\a.jpg"], Start, [renamed])[@"C:\a.jpg"].ShouldBe(@"C:\a.jpg");

        // Edited after the rename (as b.jpg), and the rename was undone later: the photo is back at a.jpg.
        PathHistory.CurrentPaths([@"C:\b.jpg"], Start.AddMinutes(20), [renamed])[@"C:\b.jpg"].ShouldBe(@"C:\a.jpg");
    }

    [Fact]
    public void A_batch_that_swaps_names_moves_each_file_once()
    {
        var swap = Moves(BatchKind.Rename, 10, (@"C:\a.jpg", @"C:\b.jpg"), (@"C:\b.jpg", @"C:\a.jpg"));

        PathHistory.CurrentPaths([@"C:\a.jpg", @"C:\b.jpg"], Start, [swap])
            .ShouldBe(new Dictionary<string, string> { [@"C:\a.jpg"] = @"C:\b.jpg", [@"C:\b.jpg"] = @"C:\a.jpg" });
    }

    [Fact]
    public void Copies_do_not_move_anything()
    {
        var copy = Moves(BatchKind.Copy, 10, (@"C:\a.jpg", @"D:\a.jpg"));

        PathHistory.CurrentPaths([@"C:\a.jpg"], Start, [copy])[@"C:\a.jpg"].ShouldBe(@"C:\a.jpg");
    }

    [Theory]
    [InlineData(BatchKind.Rename, true)]
    [InlineData(BatchKind.Move, true)]
    [InlineData(BatchKind.Copy, true)]
    [InlineData(BatchKind.Recycle, false)]
    public void File_batches_can_be_undone_once_unless_recycled(BatchKind kind, bool canUndo)
    {
        var record = Moves(kind, 0, (@"C:\a.jpg", @"C:\b.jpg"));

        record.CanUndo.ShouldBe(canUndo);
        (record with { RevertedAt = Start }).CanUndo.ShouldBeFalse();
    }
}
