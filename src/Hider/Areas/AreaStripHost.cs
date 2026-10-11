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
        private readonly Func<Func<Task>, Task> uiInvoke;
        private AreaManager? manager;

        /// <param name="_uiInvoke">Runs async work on the UI thread and completes with it (Dispatcher.UIThread.InvokeAsync).</param>
        public AreaStripHost(Func<Func<Task>, Task> _uiInvoke)
        {
            uiInvoke = _uiInvoke;
        }

        public void Attach(AreaManager _manager)
        {
            manager = _manager;
        }

        /// <summary>The areas at the start of the latest capture (safe to read on any thread).</summary>
        public IReadOnlyList<StripArea> Areas => manager?.StripAreas ?? Array.Empty<StripArea>();

        /// <summary>The virtual screen in physical px at the start of the latest capture (safe to read on any thread).</summary>
        public Hider.Core.Model.PhysicalRect Screen => manager?.StripScreen ?? default;

        /// <summary>The monitors at the start of the latest capture (safe to read on any thread).</summary>
        public IReadOnlyList<Hider.Platform.ScreenInfo> Screens => manager?.StripScreens ?? Array.Empty<Hider.Platform.ScreenInfo>();

        public Task SetTransparentRowsAsync(string _areaId, int _y0, int _y1, CancellationToken _ct)
        {
            return OnUi(_areaId, w => w.SetTransparentRowsAsync(_y0, _y1, _ct), _ct);
        }

        public Task RestoreAsync(string _areaId)
        {
            return OnUi(_areaId, w => w.RestoreRowsAsync(), CancellationToken.None);
        }

        /// <summary>
        /// Runs <paramref name="_action"/> on the area's window on the UI thread. The capture thread stops waiting when
        /// <paramref name="_ct"/> is cancelled; if the dispatcher drops the job (shutting down) the task is cancelled too.
        /// </summary>
        private Task OnUi(string _areaId, Func<AreaWindow, Task> _action, CancellationToken _ct)
        {
            var work = uiInvoke(async () =>
            {
                // An area deleted meanwhile has nothing left to hide or restore.
                var area = manager?.Areas.FirstOrDefault(a => a.Model.Id == _areaId);
                if (area is not null)
                {
                    await _action(area.Window);
                }
            });
            return work.WaitAsync(_ct);
        }
    }
}
