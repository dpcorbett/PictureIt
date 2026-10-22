using PictureIt.Core.Model;
using PictureIt.Core.Search;

namespace PictureIt.Infrastructure.Search;

/// <summary>
/// <see cref="FileSystemWatcher"/>-based watching. Events are collected until the folder has been quiet for
/// <see cref="QuietPeriod"/>, then reported together, so a copy of 500 photos is one notification, not 500.
/// Only photo files are reported (ExifTool's temporary files are ignored).
/// </summary>
public sealed class FolderWatcherFactory : IFolderWatcherFactory
{
    public TimeSpan QuietPeriod { get; init; } = TimeSpan.FromSeconds(1.5);

    public IDisposable Watch(string folder, bool includeSubfolders, Action<IReadOnlyList<string>> changed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentNullException.ThrowIfNull(changed);
        return new Watcher(folder, includeSubfolders, QuietPeriod, changed);
    }

    private sealed class Watcher : IDisposable
    {
        private readonly FileSystemWatcher _watcher;
        private readonly Timer _timer;
        private readonly TimeSpan _quiet;
        private readonly Action<IReadOnlyList<string>> _changed;
        private readonly HashSet<string> _pending = new(StringComparer.OrdinalIgnoreCase);
        private readonly Lock _lock = new();
        private bool _overflowed;
        private bool _disposed;

        public Watcher(string folder, bool includeSubfolders, TimeSpan quiet, Action<IReadOnlyList<string>> changed)
        {
            _quiet = quiet;
            _changed = changed;
            _timer = new Timer(_ => Flush());
            _watcher = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = includeSubfolders,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                InternalBufferSize = 64 * 1024,
            };
            _watcher.Created += (_, e) => Add(e.FullPath);
            _watcher.Deleted += (_, e) => Add(e.FullPath);
            _watcher.Changed += (_, e) => Add(e.FullPath);
            _watcher.Renamed += (_, e) =>
            {
                Add(e.OldFullPath);
                Add(e.FullPath);
            };

            // Too many changes at once: report the folder itself, meaning "look at everything".
            _watcher.Error += (_, _) =>
            {
                lock (_lock)
                {
                    _overflowed = true;
                }

                Add(folder);
            };
            _watcher.EnableRaisingEvents = true;
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _disposed = true;
            }

            _watcher.Dispose();
            _timer.Dispose();
        }

        private void Add(string path)
        {
            // A folder (renamed, deleted or added) can hold photos, so it's reported too.
            var relevant = SupportedFormats.IsSupported(path) || string.IsNullOrEmpty(Path.GetExtension(path)) || _overflowed;
            if (!relevant)
            {
                return;
            }

            lock (_lock)
            {
                if (_disposed)
                {
                    return;
                }

                _pending.Add(path);
                _timer.Change(_quiet, Timeout.InfiniteTimeSpan);
            }
        }

        private void Flush()
        {
            List<string> paths;
            lock (_lock)
            {
                if (_disposed || _pending.Count == 0)
                {
                    return;
                }

                paths = [.. _pending];
                _pending.Clear();
                _overflowed = false;
            }

            _changed(paths);
        }
    }
}
