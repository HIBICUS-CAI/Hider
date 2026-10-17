using Avalonia.Headless.XUnit;
using Hider.Areas;
using Hider.Core.Capture;
using Hider.Core.Model;
using Hider.Core.Persistence;
using Hider.Core.Tests.Fakes;

namespace Hider.App.Tests.Areas
{
    /// <summary>
    /// AreaController's captures (FR-50 .. FR-52): the request, taking the frame, failures and the fallback, and the Linux
    /// strip capture held, cancelled and taken again around popups and mode changes. Posts to the UI thread are queued as
    /// the dispatcher would run them.
    /// </summary>
    public class AreaControllerCaptureTests
    {
        private static readonly PhysicalRect areaRect = new(100, 200, 400, 240);

        /// <summary>A mosaic area whose creation capture has run, without timer captures.</summary>
        private static AreaController Mosaic(AreaHarness _f, PhysicalRect? _rect = null)
        {
            var area = _f.Add(_rect ?? areaRect, PatternKind.Mosaic);
            area.Model.Mosaic.Common.IntervalMs = 0;
            area.SettingsChanged();
            _f.RunPosts();
            return area;
        }

        private static List<string> Log(AreaController _area)
        {
            var log = new List<string>();
            _area.Transition += log.Add;
            return log;
        }

        private static void AssertFellBack(AreaHarness _f, AreaController _area, CaptureFailure _failure, List<string> _log)
        {
            Assert.True(_area.CaptureFailed);
            Assert.Equal(PatternKind.Generated, _area.Model.Pattern);
            Assert.Contains($"capture-failed:{_failure}", _log);
            Assert.Null(_area.Frames.Read((_front, _) => _front));
            Assert.True(_area.Window.Toolbar.FailedText.IsVisible);
            Assert.Equal(PatternKind.Generated, AreasFile.Load(_f.AreasPath, out _).Areas.Single().Pattern);
        }

        /// <summary>A strip capture (Linux) the test completes; posts queued.</summary>
        private static (AreaHarness F, ScriptedCapture Capture) Linux()
        {
            var capture = new ScriptedCapture { Traits = CaptureTraits.NeedsStripCooperation };
            var f = new AreaHarness(_capture: capture, _isWindows: false, _queuePosts: true);
            return (f, capture);
        }

        /// <summary>Starts a capture that waits for the test, and returns its token.</summary>
        private static CancellationToken StartWaiting(AreaHarness _f, ScriptedCapture _capture, AreaController _area)
        {
            _capture.Next = null;
            _area.Refresh();
            _f.RunPosts();
            Assert.True(_capture.IsWaiting);
            return _capture.Tokens[^1];
        }

        /// <summary>The capture's first strip turns rows of the window see-through (as StripCoordinator does).</summary>
        private static void StartStrip(AreaController _area, CancellationToken _token)
        {
            _ = _area.Window.SetTransparentRowsAsync(0, 8, _token);
        }

        [AvaloniaFact]
        public void Request_HasTheRectTheStripHeightAndTheOwnWindowFirst()
        {
            var capture = new FakeUnderlayCapture();
            using var f = new AreaHarness(_capture: capture, _firstHandle: 100);
            var a = Mosaic(f);
            var b = Mosaic(f, areaRect.Offset(500, 0));
            a.Model.Mosaic.Common.StripPx = 12;
            a.Refresh();
            var request = capture.Requests[^1];
            Assert.Equal(areaRect, request.Rect);
            Assert.Equal(12, request.StripPx);
            Assert.Equal(new nint[] { 100, 101 }, request.ExcludeWindows);
            b.Refresh();
            Assert.Equal(new nint[] { 101, 100 }, capture.Requests[^1].ExcludeWindows);
        }

        [AvaloniaFact]
        public void Request_WithoutNativeHandles_HasOnlyTheAreasOwnEntry()
        {
            var capture = new FakeUnderlayCapture();
            using var f = new AreaHarness(_capture: capture);
            var a = Mosaic(f);
            Mosaic(f, areaRect.Offset(500, 0));
            a.Refresh();
            Assert.Equal(new nint[] { 0 }, capture.Requests[^1].ExcludeWindows);
        }

        [AvaloniaFact]
        public void Frame_IsCopiedRowByRow_WithoutTheSourcesRowPadding()
        {
            const int w = 30, h = 20, stride = w * 4 + 12;
            var source = new byte[stride * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < stride; x++)
                {
                    source[y * stride + x] = x < w * 4 ? (byte)(y * 7 + x) : (byte)0xEE;
                }
            }
            var capture = new ScriptedCapture
            {
                Next = (_, _) => Task.FromResult(new CaptureResult(new FrameBuffer { Width = w, Height = h, Stride = stride, Bgra = source }, CaptureFailure.None)),
            };
            using var f = new AreaHarness(_capture: capture);
            var area = Mosaic(f);
            long version = area.Frames.Version;
            area.Refresh();
            Assert.Equal(version + 1, area.Frames.Version);
            area.Frames.Read((_front, _) =>
            {
                Assert.NotNull(_front);
                Assert.Equal((w, h, w * 4), (_front.Width, _front.Height, _front.Stride));
                var bytes = _front.Bgra.Span;
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w * 4; x++)
                    {
                        Assert.Equal((byte)(y * 7 + x), bytes[y * w * 4 + x]);
                    }
                }
                return 0;
            });
            Assert.False(area.CaptureFailed);
        }

        [AvaloniaFact]
        public void StartingTheCaptureThrows_FallsBackAsUnavailable()
        {
            using var f = new AreaHarness(_runCapture: _ => throw new InvalidOperationException("no thread"));
            var area = f.Manager.AddNew();
            Assert.True(area.CaptureFailed);
            Assert.Equal(PatternKind.Generated, area.Model.Pattern);
        }

        [AvaloniaFact]
        public void StartingTheCaptureThrows_LaterRequest_FallsBackWithTheFailureNamed()
        {
            bool fail = false;
            using var f = new AreaHarness(_runCapture: _c => fail ? throw new InvalidOperationException("no thread") : _c());
            var area = Mosaic(f);
            var log = Log(area);
            fail = true;
            area.Refresh();
            AssertFellBack(f, area, CaptureFailure.Unavailable, log);
        }

        [AvaloniaFact]
        public void CaptureFaults_FallsBackAsUnavailable()
        {
            var capture = new ScriptedCapture();
            using var f = new AreaHarness(_capture: capture);
            var area = Mosaic(f);
            var log = Log(area);
            capture.Next = (_, _) => Task.FromException<CaptureResult>(new IOException("gone"));
            area.Refresh();
            AssertFellBack(f, area, CaptureFailure.Unavailable, log);
        }

        [AvaloniaFact]
        public void CaptureCancelledOnItsOwn_FallsBackAsTimeout()
        {
            var capture = new ScriptedCapture();
            using var f = new AreaHarness(_capture: capture);
            var area = Mosaic(f);
            var log = Log(area);
            capture.Next = (_, _) => Task.FromCanceled<CaptureResult>(new CancellationToken(true));
            area.Refresh();
            AssertFellBack(f, area, CaptureFailure.Timeout, log);
        }

        [AvaloniaTheory]
        [InlineData(CaptureFailure.Black)]
        [InlineData(CaptureFailure.Denied)]
        [InlineData(CaptureFailure.Unavailable)]
        [InlineData(CaptureFailure.Timeout)]
        public void ReportedFailure_FallsBackWithItsName(CaptureFailure _failure)
        {
            var capture = new ScriptedCapture();
            using var f = new AreaHarness(_capture: capture);
            var area = Mosaic(f);
            var log = Log(area);
            capture.Next = (_, _) => Task.FromResult(CaptureResult.Failed(_failure));
            area.Refresh();
            AssertFellBack(f, area, _failure, log);
        }

        [AvaloniaFact]
        public void SuccessWithoutAFrame_IsNoFrame_AndFallsBack()
        {
            var capture = new ScriptedCapture();
            using var f = new AreaHarness(_capture: capture);
            var area = Mosaic(f);
            var log = Log(area);
            capture.Next = (_, _) => Task.FromResult(new CaptureResult(null, CaptureFailure.None));
            area.Refresh();
            AssertFellBack(f, area, CaptureFailure.None, log);
        }

        [AvaloniaFact]
        public void FrameThatCannotBeCopied_FallsBackAsUnavailable()
        {
            // A frame whose buffer is shorter than its size says: copying it fails on the capture thread.
            var capture = new ScriptedCapture();
            using var f = new AreaHarness(_capture: capture);
            var area = Mosaic(f);
            var log = Log(area);
            capture.Next = (_, _) => Task.FromResult(new CaptureResult(new FrameBuffer { Width = 50, Height = 50, Stride = 200, Bgra = new byte[16] }, CaptureFailure.None));
            area.Refresh();
            AssertFellBack(f, area, CaptureFailure.Unavailable, log);
        }

        [AvaloniaFact]
        public void FailureAfterTheUserChoseAnotherPattern_KeepsTheChoice()
        {
            var capture = new ScriptedCapture();
            using var f = new AreaHarness(_capture: capture);
            var area = Mosaic(f);
            capture.Next = null;
            area.Refresh();
            Assert.True(capture.IsWaiting);
            area.SetPattern(PatternKind.Fill);
            var log = Log(area);
            capture.Complete(CaptureResult.Failed(CaptureFailure.Denied));
            Assert.Equal(PatternKind.Fill, area.Model.Pattern);
            Assert.False(area.CaptureFailed);
            Assert.Empty(log);
            // The run ended: choosing mosaic again captures.
            capture.Next = ScriptedCapture.Immediate;
            int calls = capture.Calls;
            area.SetPattern(PatternKind.Mosaic);
            Assert.Equal(calls + 1, capture.Calls);
            Assert.False(area.CaptureFailed);
        }

        [AvaloniaFact]
        public void RequestsWhileACaptureRuns_AreMergedIntoOneAfterIt()
        {
            var capture = new ScriptedCapture();
            using var f = new AreaHarness(_capture: capture);
            var area = Mosaic(f);
            capture.Next = null;
            area.Refresh();
            int calls = capture.Calls;
            area.Refresh();
            area.Refresh();
            Assert.Equal(calls, capture.Calls);
            capture.Next = ScriptedCapture.Immediate;
            capture.Complete();
            Assert.Equal(calls + 1, capture.Calls);
        }

        [AvaloniaFact]
        public void PatternChangedBeforeThePostedStartRuns_CapturesNothing_AndEndsTheRun()
        {
            var capture = new FakeUnderlayCapture();
            using var f = new AreaHarness(_capture: capture, _queuePosts: true);
            var area = Mosaic(f);
            int calls = capture.Calls;
            area.Refresh();
            area.Model.Pattern = PatternKind.Fill;
            f.RunPosts();
            Assert.Equal(calls, capture.Calls);
            area.Model.Pattern = PatternKind.Mosaic;
            area.Refresh();
            f.RunPosts();
            Assert.Equal(calls + 1, capture.Calls);
        }

        [AvaloniaFact]
        public void AreaDeletedBeforeThePostedStartRuns_CapturesNothing()
        {
            var capture = new FakeUnderlayCapture();
            using var f = new AreaHarness(_capture: capture, _queuePosts: true);
            var area = Mosaic(f);
            int calls = capture.Calls;
            area.Refresh();
            area.Delete();
            f.RunPosts();
            Assert.Equal(calls, capture.Calls);
        }

        [AvaloniaFact]
        public void AreaDeletedBeforeThePostedFinishRuns_DoesNotFallBack()
        {
            var capture = new FakeUnderlayCapture();
            using var f = new AreaHarness(_capture: capture, _queuePosts: true);
            var area = Mosaic(f);
            var log = Log(area);
            capture.Failure = CaptureFailure.Denied;
            area.Refresh();
            Assert.Equal(1, f.PendingPosts);
            f.RunOnePost(); // the start: the capture fails at once and posts its finish
            Assert.Equal(1, f.PendingPosts);
            area.Delete();
            int saves = f.Manager.SaveCount;
            f.RunPosts();
            Assert.DoesNotContain(log, _l => _l.StartsWith("capture-failed", StringComparison.Ordinal));
            Assert.Equal(PatternKind.Mosaic, area.Model.Pattern);
            Assert.False(area.CaptureFailed);
            Assert.Equal(saves, f.Manager.SaveCount);
        }

        [AvaloniaFact]
        public void DeletingTheAreaDuringACapture_CancelsIt_WithoutFallingBack()
        {
            var capture = new ScriptedCapture();
            using var f = new AreaHarness(_capture: capture);
            var area = Mosaic(f);
            capture.Next = null;
            area.Refresh();
            var token = capture.Tokens[^1];
            var log = Log(area);
            area.Delete();
            Assert.True(token.IsCancellationRequested);
            Assert.Equal("delete", Assert.Single(log));
            Assert.False(area.CaptureFailed);
            Assert.Equal(PatternKind.Mosaic, area.Model.Pattern);
        }

        [AvaloniaFact]
        public void PopupOnWindows_DoesNotHoldOrCancelTheCapture()
        {
            var capture = new ScriptedCapture();
            using var f = new AreaHarness(_capture: capture, _queuePosts: true);
            var area = Mosaic(f);
            var token = StartWaiting(f, capture, area);
            StartStrip(area, token);
            using (f.Manager.HoldCapturesForDialog())
            {
                Assert.False(token.IsCancellationRequested);
            }
            area.ExitEdit(); // a mode change does not retake a capture that needs no strips
            Assert.False(token.IsCancellationRequested);
            capture.Complete();
            f.RunPosts();
            Assert.False(area.CaptureFailed);
        }

        [AvaloniaFact]
        public void PopupWithoutARunningCapture_HoldsNothing()
        {
            var (f, capture) = Linux();
            using (f)
            {
                var area = Mosaic(f);
                int calls = capture.Calls;
                using (f.Manager.HoldCapturesForDialog())
                {
                }
                f.RunPosts();
                Assert.Equal(calls, capture.Calls);
                Assert.False(area.CaptureFailed);
            }
        }

        [AvaloniaFact]
        public void PopupDuringStrips_CancelsWithoutFallingBack_AndRetakesWhenClosed()
        {
            var (f, capture) = Linux();
            using (f)
            {
                var area = Mosaic(f);
                var log = Log(area);
                var token = StartWaiting(f, capture, area);
                StartStrip(area, token);
                int calls = capture.Calls;
                var hold = f.Manager.HoldCapturesForDialog();
                Assert.True(token.IsCancellationRequested);
                f.RunPosts();
                Assert.Empty(log);
                Assert.Equal(PatternKind.Mosaic, area.Model.Pattern);
                capture.Next = ScriptedCapture.Immediate;
                area.Refresh(); // held while the popup is open
                f.RunPosts();
                Assert.Equal(calls, capture.Calls);
                hold.Dispose();
                f.RunPosts();
                Assert.Equal(calls + 1, capture.Calls);
                Assert.False(area.CaptureFailed);
            }
        }

        [AvaloniaFact]
        public void PopupClosedWhileTheCancelledCaptureIsFinishing_RetakesWhenItHasFinished()
        {
            var (f, capture) = Linux();
            using (f)
            {
                var area = Mosaic(f);
                var token = StartWaiting(f, capture, area);
                StartStrip(area, token);
                int calls = capture.Calls;
                var hold = f.Manager.HoldCapturesForDialog();
                Assert.True(token.IsCancellationRequested); // its finish is posted, not run yet
                capture.Next = ScriptedCapture.Immediate;
                hold.Dispose(); // the capture has not finished: nothing can start yet
                Assert.Equal(calls, capture.Calls);
                f.RunPosts();
                Assert.Equal(calls + 1, capture.Calls);
                Assert.False(area.CaptureFailed);
            }
        }

        [AvaloniaFact]
        public void CaptureFinishedJustBeforeThePopupsCancel_ShowsItsFrame_AndTakesNothingAgain()
        {
            // The frame was good: the cancel came after the capture had finished, before its finish ran on the UI thread.
            var (f, capture) = Linux();
            using (f)
            {
                var area = Mosaic(f);
                var token = StartWaiting(f, capture, area);
                StartStrip(area, token);
                long version = area.Frames.Version;
                capture.Complete(); // the finish is posted
                Assert.Equal(version + 1, area.Frames.Version);
                int calls = capture.Calls;
                var hold = f.Manager.HoldCapturesForDialog();
                f.RunPosts();
                hold.Dispose();
                f.RunPosts();
                Assert.Equal(calls, capture.Calls);
                Assert.False(area.CaptureFailed);
                Assert.NotNull(area.Frames.Read((_front, _) => _front));
            }
        }

        [AvaloniaFact]
        public void PopupClosedBeforeTheFirstStrip_LetsTheCaptureGoOn()
        {
            var (f, capture) = Linux();
            using (f)
            {
                var area = Mosaic(f);
                var token = StartWaiting(f, capture, area);
                int calls = capture.Calls;
                using (f.Manager.HoldCapturesForDialog())
                {
                    Assert.False(token.IsCancellationRequested); // a permission dialog may be open: not cancelled yet
                }
                StartStrip(area, token);
                Assert.False(token.IsCancellationRequested); // the held cancel was dropped
                capture.Complete();
                f.RunPosts();
                Assert.Equal(calls, capture.Calls);
                Assert.False(area.CaptureFailed);
            }
        }

        [AvaloniaFact]
        public void TwoPopupsBeforeTheFirstStrip_CancelOnceItStarts_AndRetakeOnce()
        {
            var (f, capture) = Linux();
            using (f)
            {
                var area = Mosaic(f);
                var token = StartWaiting(f, capture, area);
                int calls = capture.Calls;
                var first = f.Manager.HoldCapturesForDialog();
                var second = f.Manager.HoldCapturesForDialog();
                Assert.False(token.IsCancellationRequested);
                StartStrip(area, token);
                Assert.True(token.IsCancellationRequested);
                f.RunPosts();
                capture.Next = ScriptedCapture.Immediate;
                first.Dispose();
                f.RunPosts();
                Assert.Equal(calls, capture.Calls); // one is still open
                second.Dispose();
                f.RunPosts();
                Assert.Equal(calls + 1, capture.Calls);
                Assert.False(area.CaptureFailed);
            }
        }

        [AvaloniaFact]
        public void CaptureEndingBeforeAnyStrip_WhileAPopupIsOpen_DropsTheHeldCancel()
        {
            // A capture that never reached its strips (a refusal, a portal screenshot) did not see the popup.
            var (f, capture) = Linux();
            using (f)
            {
                var area = Mosaic(f);
                var token = StartWaiting(f, capture, area);
                int calls = capture.Calls;
                var hold = f.Manager.HoldCapturesForDialog();
                capture.Complete();
                f.RunPosts();
                Assert.False(token.IsCancellationRequested);
                hold.Dispose();
                f.RunPosts();
                Assert.Equal(calls, capture.Calls);
                Assert.False(area.CaptureFailed);
            }
        }

        [AvaloniaFact]
        public void ModeChangeDuringStrips_WithAPopupOpen_RetakesOnceThePopupCloses()
        {
            var (f, capture) = Linux();
            using (f)
            {
                var area = Mosaic(f);
                var token = StartWaiting(f, capture, area);
                StartStrip(area, token);
                int calls = capture.Calls;
                area.ExitEdit();
                Assert.True(token.IsCancellationRequested);
                var hold = f.Manager.HoldCapturesForDialog(); // the capture is cancelled already: nothing more to cancel
                capture.Next = ScriptedCapture.Immediate;
                f.RunPosts(); // the retake is held for the popup
                Assert.Equal(calls, capture.Calls);
                hold.Dispose();
                f.RunPosts();
                Assert.Equal(calls + 1, capture.Calls);
                Assert.False(area.CaptureFailed);
                Assert.Equal(PatternKind.Mosaic, area.Model.Pattern);
            }
        }

        [AvaloniaFact]
        public void ModeChangeAfterAPopupCancelled_RetakesOnlyOnce()
        {
            var (f, capture) = Linux();
            using (f)
            {
                var area = Mosaic(f);
                var token = StartWaiting(f, capture, area);
                StartStrip(area, token);
                int calls = capture.Calls;
                var hold = f.Manager.HoldCapturesForDialog();
                Assert.True(token.IsCancellationRequested);
                area.ExitEdit(); // already cancelled: no second retake is set up
                capture.Next = ScriptedCapture.Immediate;
                f.RunPosts();
                hold.Dispose();
                f.RunPosts();
                Assert.Equal(calls + 1, capture.Calls);
                area.EnterEdit();
                f.RunPosts();
                Assert.Equal(calls + 1, capture.Calls);
            }
        }

        [AvaloniaFact]
        public void ModeChangeAfterTheStripCaptureFinished_TakesNothingAgain()
        {
            var (f, capture) = Linux();
            using (f)
            {
                var area = Mosaic(f);
                var token = StartWaiting(f, capture, area);
                StartStrip(area, token);
                capture.Complete();
                f.RunPosts();
                int calls = capture.Calls;
                area.ExitEdit();
                area.EnterEdit();
                f.RunPosts();
                Assert.Equal(calls, capture.Calls);
            }
        }

        [AvaloniaFact]
        public void StripWithoutARunningCaptureOfThisArea_ChangesNothing()
        {
            var (f, capture) = Linux();
            using (f)
            {
                var area = Mosaic(f);
                using var other = new CancellationTokenSource();
                StartStrip(area, other.Token); // another area's strip while this one is idle
                area.ExitEdit();
                f.RunPosts();
                int calls = capture.Calls;
                var token = StartWaiting(f, capture, area);
                area.EnterEdit(); // this capture has not started its own strips
                Assert.False(token.IsCancellationRequested);
                capture.Complete();
                f.RunPosts();
                Assert.Equal(calls + 1, capture.Calls);
            }
        }
    }
}
