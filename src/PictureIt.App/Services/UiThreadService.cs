using Microsoft.UI.Dispatching;
using PictureIt.Presentation.Services;

namespace PictureIt.App.Services;

/// <summary>Posts work to the UI thread's dispatcher queue. Created on the UI thread.</summary>
public sealed class UiThreadService : IUiThread
{
    private readonly DispatcherQueue _queue = DispatcherQueue.GetForCurrentThread()
        ?? throw new InvalidOperationException("UiThreadService must be created on the UI thread.");

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (_queue.HasThreadAccess)
        {
            action();
        }
        else
        {
            _queue.TryEnqueue(() => action());
        }
    }
}
