using Hider.Core.Capture;

namespace Hider.Areas
{
    /// <summary>
    /// The area windows as the strip capture's <see cref="IStripHost"/>, and the area list it reads. Created before the
    /// <see cref="AreaManager"/> (the capture is one of its services) and attached to it afterwards. Calls arrive on the
    /// capture thread and are posted to the UI thread.
    /// </summary>
    public sealed class AreaStripHost : IStripHost
    {
        private readonly Action<Action> uiPost;
        private AreaManager? manager;

        public AreaStripHost(Action<Action> _uiPost)
        {
            uiPost = _uiPost;
        }

        public void Attach(AreaManager _manager)
        {
            manager = _manager;
        }

        /// <summary>The areas at the start of the latest capture (safe to read on any thread).</summary>
        public IReadOnlyList<StripArea> Areas => manager?.StripAreas ?? Array.Empty<StripArea>();

        public Task SetTransparentRowsAsync(string _areaId, int _y0, int _y1, CancellationToken _ct)
        {
            return OnUi(_areaId, w => w.SetTransparentRowsAsync(_y0, _y1, _ct));
        }

        public Task RestoreAsync(string _areaId)
        {
            return OnUi(_areaId, w => w.RestoreRowsAsync());
        }

        private Task OnUi(string _areaId, Func<AreaWindow, Task> _action)
        {
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            uiPost(async () =>
            {
                try
                {
                    // An area deleted meanwhile has nothing left to hide or restore.
                    var area = manager?.Areas.FirstOrDefault(a => a.Model.Id == _areaId);
                    if (area is not null)
                    {
                        await _action(area.Window);
                    }
                    done.TrySetResult();
                }
                catch (OperationCanceledException)
                {
                    done.TrySetCanceled();
                }
                catch (Exception e)
                {
                    done.TrySetException(e);
                }
            });
            return done.Task;
        }
    }
}
