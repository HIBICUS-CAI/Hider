using Hider.Areas;

namespace Hider.App.Tests.Areas
{
    /// <summary>The strip side of a Linux capture, as StripCoordinator drives an area's window on the UI thread.</summary>
    internal static class StripKit
    {
        /// <summary>
        /// The first strip of a capture: the rows stay see-through while the frame is awaited (until the capture is
        /// cancelled, or until <paramref name="_captured"/> has completed: StripCoordinator gives the rows back once its grab
        /// is done), then the window is restored, also after a cancel. Any other exception is left in the task.
        /// </summary>
        public static async Task FirstStripAsync(AreaController _area, CancellationToken _ct, Task? _captured = null)
        {
            try
            {
                await _area.Window.SetTransparentRowsAsync(0, 8, _ct);
                await (_captured ?? Task.Delay(Timeout.Infinite, _ct)).WaitAsync(_ct);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                await _area.Window.RestoreRowsAsync();
            }
        }

        /// <summary>The whole window is shown again: no transparent rows, no clip.</summary>
        public static void AssertRestored(AreaController _area)
        {
            Assert.Null(_area.Window.TransparentRows);
            Assert.Null(_area.Window.Root.Clip);
        }

        /// <summary>
        /// A strip whose capture was cancelled or has completed has ended without an error and restored the window (frames
        /// are rendered: a strip still waiting for its rows to be shown gets there).
        /// </summary>
        public static void AssertEnded(Task _strip, AreaController _area)
        {
            UiJobs.Complete(_strip, _renderFrames: true);
            Assert.True(_strip.IsCompletedSuccessfully, _strip.Exception?.ToString());
            AssertRestored(_area);
        }
    }
}
