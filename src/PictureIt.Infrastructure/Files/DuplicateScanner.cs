using System.Security.Cryptography;
using PictureIt.Core.Abstractions;
using PictureIt.Core.Model;
using PictureIt.Core.Organise;

namespace PictureIt.Infrastructure.Files;

/// <summary>
/// Fingerprints photos for <see cref="DuplicateFinder"/>. Only files that share a size can be identical, so only
/// those are hashed; capture time and camera come from the fast summary reader.
/// </summary>
public sealed class DuplicateScanner(IPhotoSummaryReader summaries) : IDuplicateScanner
{
    public Task<IReadOnlyList<DuplicateGroup>> ScanAsync(
        IReadOnlyList<string> filePaths,
        bool includeSameMoment,
        IProgress<WriteProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePaths);
        return Task.Run(() => Scan(filePaths, includeSameMoment, progress, cancellationToken), cancellationToken);
    }

    private IReadOnlyList<DuplicateGroup> Scan(
        IReadOnlyList<string> filePaths,
        bool includeSameMoment,
        IProgress<WriteProgress>? progress,
        CancellationToken cancellationToken)
    {
        var files = filePaths
            .Select(p => new FileInfo(p))
            .Where(f => f.Exists)
            .ToList();
        var sharedSizes = files.GroupBy(f => f.Length).Where(g => g.Count() > 1).SelectMany(g => g).Select(f => f.FullName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var total = sharedSizes.Count + (includeSameMoment ? files.Count : 0);
        var done = 0;
        var fingerprints = new List<PhotoFingerprint>();
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? hash = null;
            if (sharedSizes.Contains(file.FullName))
            {
                progress?.Report(new WriteProgress(done++, total, file.FullName));
                hash = Hash(file.FullName);
            }

            DateTime? taken = null;
            string? camera = null;
            if (includeSameMoment)
            {
                progress?.Report(new WriteProgress(done++, total, file.FullName));
                var summary = summaries.ReadSummary(file.FullName);
                taken = summary?.DateTaken;
                camera = $"{summary?.CameraMake} {summary?.CameraModel}".Trim() is { Length: > 0 } c ? c : null;
            }

            fingerprints.Add(new PhotoFingerprint(file.FullName, file.Length, hash, taken, camera));
        }

        progress?.Report(new WriteProgress(total, total, string.Empty));
        return DuplicateFinder.Find(fingerprints, includeSameMoment);
    }

    /// <summary>Null if the file can't be read; it then can't match anything.</summary>
    private static string? Hash(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
