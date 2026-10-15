using System.Diagnostics;
using System.Text;

namespace PictureIt.Infrastructure.ExifTool;

/// <summary>Output of one ExifTool command.</summary>
public sealed record ExifToolResult(string Output, string Errors);

/// <summary>
/// A long-lived <c>exiftool -stay_open</c> process. Starting ExifTool costs far more than running a command,
/// so one process serves every request. Commands are serialised; the process is started on first use and
/// restarted automatically if it dies or a command is cancelled mid-flight.
/// </summary>
public sealed class ExifToolProcess : IAsyncDisposable
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly string _executablePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _process;
    private int _sequence;
    private bool _disposed;

    public ExifToolProcess(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        _executablePath = executablePath;
    }

    /// <summary>The ExifTool bundled with the app, in the <c>exiftool</c> folder next to it.</summary>
    public static string DefaultExecutablePath => Path.Combine(AppContext.BaseDirectory, "exiftool", "exiftool.exe");

    /// <summary>Process id of the running ExifTool, or null if it isn't running. Exposed for tests.</summary>
    internal int? ProcessId => _process is { HasExited: false } p ? p.Id : null;

    /// <summary>
    /// Runs one ExifTool command. Each argument becomes one line of ExifTool's argument stream,
    /// so arguments must not contain line breaks.
    /// </summary>
    public async Task<ExifToolResult> ExecuteAsync(IEnumerable<string> arguments, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var command = new StringBuilder();
        foreach (var argument in arguments)
        {
            if (argument.AsSpan().IndexOfAny('\r', '\n') >= 0)
            {
                throw new ArgumentException(
                    "ExifTool arguments cannot contain line breaks; write multi-line values from a file with -TAG<=FILE.",
                    nameof(arguments));
            }

            command.Append(argument).Append('\n');
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var process = EnsureStarted();
            var id = ++_sequence;
            var marker = $"{{ready{id}}}";

            // -echo4 prints the marker to stderr after the command finishes, so both streams have a known end.
            command.Append("-echo4\n").Append(marker).Append('\n').Append("-execute").Append(id).Append('\n');

            try
            {
                await process.StandardInput.WriteAsync(command, cancellationToken).ConfigureAwait(false);
                await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);

                var output = ReadUntilMarkerAsync(process.StandardOutput, marker, cancellationToken);
                var errors = ReadUntilMarkerAsync(process.StandardError, marker, cancellationToken);
                await Task.WhenAll(output, errors).ConfigureAwait(false);

                return new ExifToolResult(await output.ConfigureAwait(false), await errors.ConfigureAwait(false));
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or ExifToolException)
            {
                // The streams are now mid-command and can't be trusted; start fresh next time.
                StopProcess();
                if (ex is IOException)
                {
                    throw new ExifToolException("Lost contact with ExifTool.", ex);
                }

                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_process is { HasExited: false } process)
            {
                try
                {
                    await process.StandardInput.WriteAsync("-stay_open\nFalse\n").ConfigureAwait(false);
                    await process.StandardInput.FlushAsync().ConfigureAwait(false);
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or OperationCanceledException)
                {
                    // Fall through to a forced stop.
                }
            }

            StopProcess();
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private Process EnsureStarted()
    {
        if (_process is { HasExited: false } running)
        {
            return running;
        }

        StopProcess();

        if (!File.Exists(_executablePath))
        {
            throw new ExifToolException($"ExifTool was not found at '{_executablePath}'.");
        }

        var startInfo = new ProcessStartInfo(_executablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Utf8NoBom,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        // Read commands from stdin; treat file names as UTF-8 so non-ASCII paths work on Windows.
        foreach (var argument in (string[])["-stay_open", "True", "-@", "-", "-common_args", "-charset", "filename=utf8"])
        {
            startInfo.ArgumentList.Add(argument);
        }

        _process = Process.Start(startInfo) ?? throw new ExifToolException("ExifTool failed to start.");
        _sequence = 0;
        return _process;
    }

    private void StopProcess()
    {
        if (_process is null)
        {
            return;
        }

        try
        {
            if (!_process.HasExited)
            {
                // The Windows build is a launcher around a bundled Perl, so take down the whole tree.
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }
        finally
        {
            _process.Dispose();
            _process = null;
        }
    }

    private static async Task<string> ReadUntilMarkerAsync(StreamReader reader, string marker, CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new ExifToolException("ExifTool exited unexpectedly.");

            if (line.TrimEnd() == marker)
            {
                return text.ToString();
            }

            text.Append(line).Append('\n');
        }
    }
}
