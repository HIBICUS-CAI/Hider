using Hider.Areas;

namespace Hider.App.Tests.Areas
{
    /// <summary>The strip side of a Linux capture, as StripCoordinator drives an area's window on the UI thread.</summary>
    internal static class StripKit
    {
        /// <summary>
        /// The first strip of a capture: the rows stay see-through while the frame is awaited (until the capture is
        /// cancelled), then the window is restored, also after a cancel. Any other exception is left in the task.
        /// </summary>
        public static async Task FirstStripAsync(AreaController _area, CancellationToken _ct)
        {
            try
            {
                await _area.Window.SetTransparentRowsAsync(0, 8, _ct);
                await Task.Delay(Timeout.Infinite, _ct);
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

        /// <summary>A strip whose capture was cancelled has ended without an error and restored the window.</summary>
        public static void AssertEnded(Task _strip, AreaController _area)
        {
            UiJobs.Complete(_strip);
            Assert.True(_strip.IsCompletedSuccessfully, _strip.Exception?.ToString());
            AssertRestored(_area);
        }
    }
}
