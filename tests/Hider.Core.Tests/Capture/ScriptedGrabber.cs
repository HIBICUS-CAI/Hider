using Hider.Core.Capture;
using Hider.Core.Model;

namespace Hider.Core.Tests.Capture
{
    /// <summary>A grabber whose answers are functions, for the paths <see cref="Fakes.FakeScreenGrabber"/> cannot take.</summary>
    internal sealed class ScriptedGrabber : IScreenGrabber
    {
        public Func<PhysicalRect, Task<bool>> Prepare { get; set; } = _ => Task.FromResult(true);
        public Func<long, PhysicalRect, CancellationToken, Task<ScreenFrame?>> Grab { get; set; } = (_, _, _) => Task.FromResult<ScreenFrame?>(null);
        public bool Ready { get; set; }
        public int Disposed { get; private set; }
        public List<string>? DisposeLog { get; set; }
        public string Name { get; set; } = "";

        public bool ReadyWithoutPrepare => Ready;

        public Task<bool> PrepareAsync(PhysicalRect _needed, CancellationToken _ct)
        {
            return Prepare(_needed);
        }

        public Task<ScreenFrame?> GrabAfterAsync(long _notBeforeTimestamp, PhysicalRect _needed, CancellationToken _ct)
        {
            return Grab(_notBeforeTimestamp, _needed, _ct);
        }

        public void Dispose()
        {
            Disposed++;
            DisposeLog?.Add(Name);
        }
    }

    /// <summary>Implements only the required members, so the interface's default members are what callers get.</summary>
    internal sealed class MinimalGrabber : IScreenGrabber
    {
        public Task<ScreenFrame?> GrabAfterAsync(long _notBeforeTimestamp, PhysicalRect _needed, CancellationToken _ct)
        {
            return Task.FromResult<ScreenFrame?>(null);
        }

        public void Dispose()
        {
        }
    }
}
