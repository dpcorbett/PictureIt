using System.Text.Json;
using PictureIt.Core.Abstractions;

namespace PictureIt.Infrastructure.Backup;

/// <summary>
/// Stores backups as plain file copies, one folder per batch, with a <c>manifest.json</c> recording where each
/// file came from. Files keep their original names (with an index prefix) so a backup can be recovered by hand.
/// </summary>
public sealed class FileBackupStore(string rootDirectory, TimeProvider? timeProvider = null) : IBackupStore
{
    private const string ManifestFileName = "manifest.json";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public static string DefaultRootDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PictureIt", "backups");

    public string RootDirectory { get; } = rootDirectory;

    public async Task<BackupManifest> CreateAsync(
        Guid batchId,
        IReadOnlyCollection<string> filePaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePaths);
        var batchDirectory = BatchDirectory(batchId);
        if (Directory.Exists(batchDirectory))
        {
            throw new InvalidOperationException($"A backup for batch {batchId} already exists.");
        }

        Directory.CreateDirectory(batchDirectory);
        try
        {
            var entries = new List<BackupEntry>(filePaths.Count);
            var index = 0;
            foreach (var path in filePaths.Select(Path.GetFullPath))
            {
                var backupPath = Path.Combine(batchDirectory, $"{index++:D5}_{Path.GetFileName(path)}");
                await CopyAsync(path, backupPath, cancellationToken).ConfigureAwait(false);
                entries.Add(new BackupEntry(path, backupPath, new FileInfo(backupPath).Length));
            }

            var manifest = new BackupManifest(batchId, _time.GetUtcNow(), entries);
            await using var stream = File.Create(Path.Combine(batchDirectory, ManifestFileName));
            await JsonSerializer.SerializeAsync(stream, manifest, JsonOptions, cancellationToken).ConfigureAwait(false);
            return manifest;
        }
        catch
        {
            // A half-made backup is worse than none: it looks restorable but isn't.
            Directory.Delete(batchDirectory, recursive: true);
            throw;
        }
    }

    public async Task<BackupManifest?> GetAsync(Guid batchId, CancellationToken cancellationToken = default)
    {
        var manifestPath = Path.Combine(BatchDirectory(batchId), ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(manifestPath);
        return await JsonSerializer.DeserializeAsync<BackupManifest>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<BackupManifest>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(RootDirectory))
        {
            return [];
        }

        var manifests = new List<BackupManifest>();
        foreach (var directory in Directory.EnumerateDirectories(RootDirectory))
        {
            if (Guid.TryParseExact(Path.GetFileName(directory), "N", out var batchId)
                && await GetAsync(batchId, cancellationToken).ConfigureAwait(false) is { } manifest)
            {
                manifests.Add(manifest);
            }
        }

        return manifests;
    }

    public async Task RestoreAsync(Guid batchId, CancellationToken cancellationToken = default)
    {
        var manifest = await GetAsync(batchId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"No backup exists for batch {batchId}.");

        foreach (var entry in manifest.Entries)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(entry.OriginalPath)!);
            await CopyAsync(entry.BackupPath, entry.OriginalPath, cancellationToken).ConfigureAwait(false);
        }
    }

    public Task DeleteAsync(Guid batchId, CancellationToken cancellationToken = default)
    {
        var batchDirectory = BatchDirectory(batchId);
        if (Directory.Exists(batchDirectory))
        {
            Directory.Delete(batchDirectory, recursive: true);
        }

        return Task.CompletedTask;
    }

    private string BatchDirectory(Guid batchId) => Path.Combine(RootDirectory, batchId.ToString("N"));

    private static async Task CopyAsync(string source, string destination, CancellationToken cancellationToken)
    {
        await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
        await using (var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        }

        // Keep timestamps so a restored file is indistinguishable from the original.
        File.SetCreationTimeUtc(destination, File.GetCreationTimeUtc(source));
        File.SetLastWriteTimeUtc(destination, File.GetLastWriteTimeUtc(source));
    }
}
