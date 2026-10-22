using CommunityToolkit.Mvvm.Messaging;
using PictureIt.Core.Model;
using PictureIt.Core.Search;

namespace PictureIt.Presentation.Services;

/// <summary>
/// Keeps the catalogue up to date while the app runs: re-reads photos the app writes, follows renames and moves,
/// and watches catalogue folders for changes made outside the app. Updates run one at a time in the background, and
/// <see cref="CatalogChangedMessage"/> is sent (on the UI thread) after each.
/// </summary>
public sealed class CatalogSync : IRecipient<FilesChangedMessage>, IRecipient<FolderChangedMessage>, IDisposable
{
    private readonly IPhotoCatalog _catalog;
    private readonly IFolderWatcherFactory _watchers;
    private readonly IUiThread _ui;
    private readonly IMessenger _messenger;
    private readonly SemaphoreSlim _queue = new(1, 1);
    private readonly List<IDisposable> _watching = [];

    public CatalogSync(IPhotoCatalog catalog, IFolderWatcherFactory watchers, IUiThread ui, IMessenger messenger)
    {
        ArgumentNullException.ThrowIfNull(messenger);
        _catalog = catalog;
        _watchers = watchers;
        _ui = ui;
        _messenger = messenger;
        messenger.Register<FilesChangedMessage>(this);
        messenger.Register<FolderChangedMessage>(this);
    }

    /// <summary>The last background update that failed, if any, for display.</summary>
    public string? LastError { get; private set; }

    /// <summary>Completes when the updates started so far have finished (for tests and shutdown).</summary>
    public Task Idle => WaitForIdleAsync();

    public void Receive(FilesChangedMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        _ = RunAsync(() => _catalog.RefreshFilesAsync(message.FilePaths));
    }

    public void Receive(FolderChangedMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Moves.Count > 0)
        {
            _ = RunAsync(() => _catalog.ApplyMovesAsync(message.Moves));
        }
    }

    /// <summary>Watches every catalogue folder (again), e.g. after folders are added or removed.</summary>
    public async Task WatchFoldersAsync()
    {
        var folders = await _catalog.GetFoldersAsync();
        lock (_watching)
        {
            _watching.ForEach(w => w.Dispose());
            _watching.Clear();
            foreach (var folder in folders.Where(f => Directory.Exists(f.Path)))
            {
                _watching.Add(_watchers.Watch(folder.Path, includeSubfolders: true, paths => OnFolderChanged(folder.Path, paths)));
            }
        }
    }

    public void Dispose()
    {
        lock (_watching)
        {
            _watching.ForEach(w => w.Dispose());
            _watching.Clear();
        }

        _messenger.UnregisterAll(this);
        _queue.Dispose();
    }

    private void OnFolderChanged(string root, IReadOnlyList<string> paths)
    {
        // A folder was added, renamed or removed (or too much happened at once): rescan; it only reads what changed.
        _ = paths.Any(p => !SupportedFormats.IsSupported(p))
            ? RunAsync(() => _catalog.ScanAsync(root))
            : RunAsync(() => _catalog.RefreshFilesAsync(paths));
    }

    private async Task WaitForIdleAsync()
    {
        await _queue.WaitAsync().ConfigureAwait(false);
        _queue.Release();
    }

    private async Task RunAsync(Func<Task> update)
    {
        await _queue.WaitAsync().ConfigureAwait(false);
        try
        {
            await update().ConfigureAwait(false);
            LastError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Data.Common.DbException)
        {
            // The next scan catches up; a background refresh must never take the app down.
            LastError = ex.Message;
        }
        finally
        {
            _queue.Release();
        }

        _ui.Post(() => _messenger.Send(new CatalogChangedMessage()));
    }
}
