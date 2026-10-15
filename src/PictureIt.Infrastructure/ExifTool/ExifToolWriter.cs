using System.Text.RegularExpressions;
using PictureIt.Core.Abstractions;
using PictureIt.Core.Model;

namespace PictureIt.Infrastructure.ExifTool;

public sealed record ExifToolWriterOptions
{
    /// <summary>Keep each file's "date modified" unchanged when its metadata is edited.</summary>
    public bool PreserveFileModifyDate { get; init; } = true;
}

/// <summary>
/// Writes edits with ExifTool. Image data is never re-encoded. Every file in the batch is backed up
/// before the first one is changed; one ExifTool command runs per file so failures are reported per file.
/// </summary>
public sealed partial class ExifToolWriter(
    ExifToolProcess exifTool,
    IBackupStore? backupStore,
    ExifToolWriterOptions? options = null) : IMetadataWriter
{
    private readonly ExifToolWriterOptions _options = options ?? new ExifToolWriterOptions();

    public async Task<WriteResult> ApplyAsync(
        EditSet editSet,
        IProgress<WriteProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(editSet);

        var files = editSet.Files
            .Select(f => f with { FilePath = Path.GetFullPath(f.FilePath) })
            .ToArray();

        // Back up first: if this fails, nothing has been touched.
        var backedUp = false;
        if (backupStore is not null)
        {
            var distinct = files.Select(f => f.FilePath).Distinct(StringComparer.OrdinalIgnoreCase).Where(File.Exists).ToArray();
            await backupStore.CreateAsync(editSet.BatchId, distinct, cancellationToken).ConfigureAwait(false);
            backedUp = true;
        }

        var valueFileDirectory = Path.Combine(Path.GetTempPath(), "PictureIt", editSet.BatchId.ToString("N"));
        var results = new List<FileWriteResult>(files.Length);
        try
        {
            for (var i = 0; i < files.Length; i++)
            {
                var file = files[i];
                if (cancellationToken.IsCancellationRequested)
                {
                    results.AddRange(files[i..].Select(f => Failed(f.FilePath, "Cancelled.")));
                    break;
                }

                progress?.Report(new WriteProgress(i, files.Length, file.FilePath));
                results.Add(await WriteFileAsync(file, valueFileDirectory, cancellationToken).ConfigureAwait(false));
            }

            progress?.Report(new WriteProgress(files.Length, files.Length, string.Empty));
        }
        finally
        {
            TryDeleteDirectory(valueFileDirectory);
        }

        return new WriteResult(editSet.BatchId, results, backedUp);
    }

    private async Task<FileWriteResult> WriteFileAsync(FileEdits file, string valueFileDirectory, CancellationToken cancellationToken)
    {
        if (!File.Exists(file.FilePath))
        {
            return Failed(file.FilePath, "File not found.");
        }

        if (file.Edits.Count == 0)
        {
            return new FileWriteResult(file.FilePath, Succeeded: true, Error: null, Warnings: []);
        }

        // In-place keeps the file's identity (creation date, attributes, hard links); our own backup replaces ExifTool's.
        var args = new List<string> { "-overwrite_original_in_place" };
        if (_options.PreserveFileModifyDate)
        {
            args.Add("-P");
        }

        args.AddRange(ExifToolArguments.ForEdits(file.Edits, valueFileDirectory));
        args.Add(file.FilePath);

        ExifToolResult response;
        try
        {
            response = await exifTool.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Failed(file.FilePath, "Cancelled while writing; restore the batch backup if the file looks wrong.");
        }

        return Interpret(file.FilePath, response);
    }

    internal static FileWriteResult Interpret(string filePath, ExifToolResult response)
    {
        var lines = response.Errors.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var errors = lines.Where(l => l.StartsWith("Error", StringComparison.Ordinal)).ToArray();
        var warnings = lines.Where(l => l.StartsWith("Warning", StringComparison.Ordinal)).ToArray();

        var updated = Count(UpdatedPattern(), response.Output);
        var unchanged = Count(UnchangedPattern(), response.Output);

        // "Unchanged" is fine when the values were already right, but not when ExifTool ignored the edit
        // (e.g. an unknown tag), which it reports as a warning.
        var succeeded = errors.Length == 0 && (updated > 0 || (unchanged > 0 && warnings.Length == 0));
        if (succeeded)
        {
            return new FileWriteResult(filePath, Succeeded: true, Error: null, Warnings: warnings);
        }

        var message = errors.FirstOrDefault()
            ?? (warnings.Length > 0 ? string.Join(" ", warnings) : null)
            ?? (string.IsNullOrWhiteSpace(response.Output) ? "ExifTool did not update the file." : response.Output.Trim());
        return new FileWriteResult(filePath, Succeeded: false, Error: message, Warnings: warnings);
    }

    private static int Count(Regex pattern, string output) =>
        pattern.Match(output) is { Success: true } match ? int.Parse(match.Groups[1].ValueSpan, provider: null) : 0;

    private static FileWriteResult Failed(string filePath, string error) => new(filePath, Succeeded: false, error, Warnings: []);

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Temp files; not worth failing the batch over.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [GeneratedRegex(@"(\d+) image files updated")]
    private static partial Regex UpdatedPattern();

    [GeneratedRegex(@"(\d+) image files unchanged")]
    private static partial Regex UnchangedPattern();
}
