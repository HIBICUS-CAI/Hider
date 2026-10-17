using Avalonia.Controls;
using Hider.Areas;
using Hider.Core.Capture;
using Hider.Core.Model;
using Hider.Core.Tests.Fakes;
using Hider.Platform;

namespace Hider.App.Tests.Areas
{
    /// <summary>
    /// Like <see cref="AreaFixture"/>, with every service open to the test: posts to the UI thread can be queued (run with
    /// <see cref="RunPosts"/>) instead of running inline, captures can be started through a test function, and hosts carry
    /// native handles.
    /// </summary>
    internal sealed class AreaHarness : IDisposable
    {
        private readonly string dir = Path.Combine(Path.GetTempPath(), "hider-gui-" + Guid.NewGuid().ToString("N"));
        private readonly Queue<Action> posts = new();
        private readonly bool queuePosts;
        private nint nextHandle;

        public AreaHarness(double _scale = 1.0, FakeScreenService? _screens = null, IUnderlayCapture? _capture = null,
            bool _isWindows = true, bool _queuePosts = false, Func<Func<Task<CaptureResult>>, Task<CaptureResult>>? _runCapture = null,
            nint _firstHandle = 0)
        {
            Directory.CreateDirectory(dir);
            queuePosts = _queuePosts;
            nextHandle = _firstHandle;
            Screens = _screens ?? new FakeScreenService(FakeScreenService.OneScreen(_scale));
            Capture = _capture ?? new FakeUnderlayCapture();
            Services = new AreaServices(Screens, _w =>
            {
                var host = new HandleHost(_w, _scale, nextHandle);
                if (nextHandle != 0)
                {
                    nextHandle++;
                }
                Hosts.Add(host);
                return host;
            }, AreasPath, Time, Post, _isWindows, Capture, _runCapture ?? (_c => _c()));
            Manager = new AreaManager(Services);
        }

        public AreaServices Services { get; }
        public IUnderlayCapture Capture { get; }
        public string Dir => dir;
        public string AreasPath => Path.Combine(dir, "areas.json");
        public FakeTimeProvider Time { get; } = new();
        public FakeScreenService Screens { get; }
        public List<HandleHost> Hosts { get; } = new();
        public AreaManager Manager { get; }

        /// <summary>Posts waiting to run (queued mode only).</summary>
        public int PendingPosts => posts.Count;

        private void Post(Action _action)
        {
            if (queuePosts)
            {
                posts.Enqueue(_action);
            }
            else
            {
                _action();
            }
        }

        /// <summary>Runs queued posts, including the ones they post, until none is left.</summary>
        public void RunPosts()
        {
            while (posts.TryDequeue(out var action))
            {
                action();
            }
        }

        /// <summary>Runs the oldest queued post only.</summary>
        public void RunOnePost()
        {
            posts.Dequeue()();
        }

        /// <summary>A new area (edit mode, FR-14) moved to <paramref name="_rect"/>.</summary>
        public AreaController Add(PhysicalRect _rect, PatternKind _pattern = PatternKind.Fill)
        {
            var area = Manager.AddNew();
            area.Model.Rect = _rect;
            area.Model.Pattern = _pattern;
            area.Layout();
            return area;
        }

        public void Dispose()
        {
            Manager.Dispose();
            Directory.Delete(dir, true);
        }
    }

    /// <summary><see cref="FakeAreaWindowHost"/> with a native handle (the fake one always reports 0).</summary>
    internal sealed class HandleHost : IAreaWindowHost
    {
        private readonly FakeAreaWindowHost inner;

        public HandleHost(Window _window, double _scale, nint _handle)
        {
            inner = new FakeAreaWindowHost(_window, _scale);
            Handle = _handle;
            inner.DisplayChanged += () => DisplayChanged?.Invoke();
        }

        public nint Handle { get; }
        public double Scale => inner.Scale;
        public FakeAreaWindowHost Inner => inner;
        public List<PhysicalRect> Placements => inner.Placements;
        public PhysicalRect Last => inner.Last;
        public bool Disposed => inner.Disposed;

        public Func<PhysicalRect, double>? ScaleAt
        {
            get => inner.ScaleAt;
            set => inner.ScaleAt = value;
        }

        /// <summary>Every rectangle the controller asked a scale for, in order.</summary>
        public List<PhysicalRect> ScaleQueries { get; } = new();

        public double ScaleFor(PhysicalRect _rect)
        {
            ScaleQueries.Add(_rect);
            return inner.ScaleFor(_rect);
        }

        public void Place(PhysicalRect _rect)
        {
            inner.Place(_rect);
        }

        public void SetTopmost(bool _on)
        {
            inner.SetTopmost(_on);
        }

        public void RaiseDisplayChanged()
        {
            inner.RaiseDisplayChanged();
        }

        public event Action? DisplayChanged;

        public void Dispose()
        {
            inner.Dispose();
        }
    }

    /// <summary>A capture whose result the test decides: a completed result, a fault, a cancel, or one that waits.</summary>
    internal sealed class ScriptedCapture : IUnderlayCapture
    {
        private TaskCompletionSource<CaptureResult>? waiting;

        public CaptureTraits Traits { get; set; } = CaptureTraits.Live | CaptureTraits.ExposureFree;
        public int Calls { get; private set; }
        public List<CaptureRequest> Requests { get; } = new();
        public List<CancellationToken> Tokens { get; } = new();

        /// <summary>
        /// Makes the result of the next calls (by default a frame of the requested size at once); null waits until
        /// <see cref="Complete"/> or the token is cancelled.
        /// </summary>
        public Func<CaptureRequest, CancellationToken, Task<CaptureResult>>? Next { get; set; } = Immediate;

        /// <summary>A frame of the requested size, at once.</summary>
        public static Task<CaptureResult> Immediate(CaptureRequest _request, CancellationToken _ct)
        {
            return Task.FromResult(Frame(_request.Rect.Width, _request.Rect.Height));
        }

        /// <summary>Called on every capture with its token before the result is made.</summary>
        public Action<CancellationToken>? OnCall { get; set; }

        public bool IsWaiting => waiting is not null && !waiting.Task.IsCompleted;

        public Task<CaptureResult> CaptureAsync(CaptureRequest _request, CancellationToken _ct)
        {
            Calls++;
            Requests.Add(_request);
            Tokens.Add(_ct);
            OnCall?.Invoke(_ct);
            if (Next is { } next)
            {
                return next(_request, _ct);
            }
            var tcs = new TaskCompletionSource<CaptureResult>();
            waiting = tcs;
            _ct.Register(() => tcs.TrySetCanceled(_ct));
            return tcs.Task;
        }

        /// <summary>Completes the waiting capture with a frame of its size (or with <paramref name="_result"/>).</summary>
        public void Complete(CaptureResult? _result = null)
        {
            var request = Requests[^1];
            waiting!.TrySetResult(_result ?? Frame(request.Rect.Width, request.Rect.Height));
        }

        public static CaptureResult Frame(int _width, int _height)
        {
            var frame = new FrameBuffer { Width = _width, Height = _height, Stride = _width * 4, Bgra = SyntheticImage.CreatePixels(_width, _height) };
            return new CaptureResult(frame, CaptureFailure.None);
        }

        public void Dispose()
        {
        }
    }
}
