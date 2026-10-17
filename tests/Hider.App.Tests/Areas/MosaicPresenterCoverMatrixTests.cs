using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Immutable;
using Hider.Areas;
using Hider.Core;
using Hider.Core.Model;
using Hider.Core.Mosaic;
using SkiaSharp;
using static Hider.App.Tests.Areas.CoverKit;

namespace Hider.App.Tests.Areas
{
    /// <summary>
    /// The cover over every dimension that could leave a pixel see-through (NFR-05): display scales and area sizes (the last
    /// row and column included), every mosaic and generated method, every way the fill color can be written, the kind of
    /// exception, failures that come and go, the window closing, several areas at once and a real area window.
    /// </summary>
    public class MosaicPresenterCoverMatrixTests
    {
        /// <summary>How a draw fails.</summary>
        public enum Failure
        {
            /// <summary>The mosaic of a frame without pixels throws (the real path).</summary>
            BrokenFrame,
            /// <summary>The Skia canvas fails once: the cover is drawn through Skia.</summary>
            CanvasOnce,
            /// <summary>The Skia canvas always fails: Avalonia draws the cover.</summary>
            CanvasAlways,
            /// <summary>No Skia lease at all: Avalonia draws the cover.</summary>
            NoLease,
        }

        public static readonly double[] Scales = { 1.0, 1.1, 1.25, 1.3, 1.5, 1.75, 2.0, 2.25, 2.5, 3.0, 3.5 };

        /// <summary>Physical sizes: a pixel, lines, odd lengths that lose a column at 175% when truncated, a square.</summary>
        public static readonly (int W, int H)[] Sizes = { (1, 1), (1, 9), (9, 1), (7, 3), (61, 115), (64, 48), (100, 100) };

        public static TheoryData<double, int, int, Failure> ScaleSizeFailure()
        {
            var data = new TheoryData<double, int, int, Failure>();
            foreach (double scale in Scales)
            {
                foreach (var (w, h) in Sizes)
                {
                    foreach (Failure failure in Enum.GetValues<Failure>())
                    {
                        data.Add(scale, w, h, failure);
                    }
                }
            }
            return data;
        }

        /// <summary>A presenter of an area of <paramref name="_w"/> x <paramref name="_h"/> px set up to fail as asked.</summary>
        private static (MosaicPresenter Presenter, FakeLease? Lease) Failing(Failure _failure, PatternKind _pattern, int _w, int _h, string _fill = AreaDefaults.FillColor, double _scale = 1.0)
        {
            var area = NewArea(_pattern, _fill, _w, _h);
            var presenter = new MosaicPresenter { Model = area };
            FakeLease? lease = null;
            switch (_failure)
            {
                case Failure.BrokenFrame:
                    presenter.Frames = BrokenFrame();
                    break;
                case Failure.CanvasOnce:
                    presenter.Frames = GoodFrame(_w, _h);
                    lease = new FakeLease(1, SKColors.Red, null, _w, _h, _scale);
                    break;
                case Failure.CanvasAlways:
                    presenter.Frames = GoodFrame(_w, _h);
                    lease = new FakeLease(-1, SKColors.Red, null, _w, _h, _scale);
                    break;
                case Failure.NoLease:
                    presenter.Frames = GoodFrame(_w, _h);
                    presenter.LeaseSkia = _ => null;
                    break;
            }
            if (lease is not null)
            {
                presenter.LeaseSkia = _ => lease;
            }
            return (presenter, lease);
        }

        /// <summary>What the area shows: the target bitmap, or the fake lease's surface when the cover went through it.</summary>
        private static SKBitmap Shown(Failure _failure, MosaicPresenter _presenter, FakeLease? _lease, int _w, int _h, double _scale)
        {
            using var target = DrawPhysical(_presenter, _w, _h, _scale);
            return _failure == Failure.CanvasOnce ? _lease!.Pixels() : target.Copy();
        }

        [AvaloniaTheory]
        [MemberData(nameof(ScaleSizeFailure))]
        public void GT80_EveryPhysicalPixel_IsCovered_AtEveryScaleAndSize(double _scale, int _w, int _h, Failure _failure)
        {
            // The DIP size truncates back to the same pixels (as the platform sizes the window): the cover must reach the last
            // row and column, or a line of the content shows.
            var (presenter, lease) = Failing(_failure, PatternKind.Mosaic, _w, _h, AreaDefaults.FillColor, _scale);
            try
            {
                using (presenter)
                {
                    using var shown = Shown(_failure, presenter, lease, _w, _h, _scale);
                    Assert.Equal(0, CountOther(shown, DefaultFill, _w, _h));
                }
            }
            finally
            {
                lease?.Free();
            }
        }

        [AvaloniaTheory]
        [InlineData(1.0)]
        [InlineData(1.25)]
        [InlineData(1.5)]
        [InlineData(1.75)]
        [InlineData(2.0)]
        public void GT80_GeneratedPattern_CoveredAtEveryScale_WhenSkiaFails(double _scale)
        {
            foreach (var failure in new[] { Failure.CanvasOnce, Failure.CanvasAlways, Failure.NoLease })
            {
                var (presenter, lease) = Failing(failure, PatternKind.Generated, 61, 115, AreaDefaults.FillColor, _scale);
                try
                {
                    using (presenter)
                    {
                        using var shown = Shown(failure, presenter, lease, 61, 115, _scale);
                        Assert.Equal(0, CountOther(shown, DefaultFill, 61, 115));
                    }
                }
                finally
                {
                    lease?.Free();
                }
            }
        }

        [AvaloniaTheory]
        [InlineData(2000, 3)]
        [InlineData(3, 2000)]
        [InlineData(1280, 720)]
        public void GT80_LargeAndExtremeAreas_AreCoveredCompletely(int _w, int _h)
        {
            foreach (Failure failure in Enum.GetValues<Failure>())
            {
                var (presenter, lease) = Failing(failure, PatternKind.Mosaic, _w, _h);
                try
                {
                    using (presenter)
                    {
                        using var shown = Shown(failure, presenter, lease, _w, _h, 1.0);
                        Assert.Equal(0, CountOther(shown, DefaultFill, _w, _h));
                    }
                }
                finally
                {
                    lease?.Free();
                }
            }
        }

        public static TheoryData<string> MosaicMethods()
        {
            var data = new TheoryData<string>();
            foreach (var method in MethodRegistry.Filter(MethodRegistry.OfKind(MethodKind.Mosaic), MethodRegistry.CurrentPlatform))
            {
                data.Add(method.Id);
            }
            return data;
        }

        public static TheoryData<string> GeneratedMethods()
        {
            var data = new TheoryData<string>();
            foreach (var method in MethodRegistry.Filter(MethodRegistry.OfKind(MethodKind.Generated), MethodRegistry.CurrentPlatform))
            {
                data.Add(method.Id);
            }
            return data;
        }

        [Fact]
        public void GT80_MethodLists_AreNotEmpty()
        {
            // The theories below run once per method: an empty list would test nothing.
            Assert.NotEmpty(MosaicMethods());
            Assert.NotEmpty(GeneratedMethods());
        }

        [AvaloniaTheory]
        [MemberData(nameof(MosaicMethods))]
        public void GT80_EveryMosaicMethod_ThatCannotDrawTheFrame_IsCovered(string _method)
        {
            var area = NewArea(PatternKind.Mosaic);
            area.Mosaic.Method = _method;
            using var presenter = new MosaicPresenter { Model = area, Frames = BrokenFrame() };
            using var bitmap = Draw(presenter);
            Assert.True(AllCovered(bitmap, DefaultFill));
            Assert.Equal(0, presenter.MosaicRenderCount);
        }

        [AvaloniaTheory]
        [MemberData(nameof(MosaicMethods))]
        public void GT80_EveryMosaicMethod_DrawsNormally_WhenItCan(string _method)
        {
            // The other side: a good frame is drawn as the mosaic, not covered.
            var area = NewArea(PatternKind.Mosaic);
            area.Mosaic.Method = _method;
            using var presenter = new MosaicPresenter { Model = area, Frames = GoodFrame() };
            using var bitmap = Draw(presenter);
            Assert.Equal(1, presenter.MosaicRenderCount);
            Assert.False(AllCovered(bitmap, DefaultFill));
        }

        [AvaloniaTheory]
        [MemberData(nameof(MosaicMethods))]
        public void GT80_EveryMosaicMethod_CanvasFailure_IsCovered(string _method)
        {
            foreach (var failure in new[] { Failure.CanvasOnce, Failure.CanvasAlways, Failure.NoLease })
            {
                var (presenter, lease) = Failing(failure, PatternKind.Mosaic, W, H);
                presenter.Model!.Mosaic.Method = _method;
                try
                {
                    using (presenter)
                    {
                        using var shown = Shown(failure, presenter, lease, W, H, 1.0);
                        Assert.True(AllCovered(shown, DefaultFill), $"{failure}");
                    }
                }
                finally
                {
                    lease?.Free();
                }
            }
        }

        [AvaloniaTheory]
        [MemberData(nameof(GeneratedMethods))]
        public void GT80_EveryGeneratedMethod_CanvasFailure_IsCovered(string _method)
        {
            foreach (var failure in new[] { Failure.CanvasOnce, Failure.CanvasAlways, Failure.NoLease })
            {
                var (presenter, lease) = Failing(failure, PatternKind.Generated, W, H);
                presenter.Model!.Generated.Method = _method;
                try
                {
                    using (presenter)
                    {
                        using var shown = Shown(failure, presenter, lease, W, H, 1.0);
                        Assert.True(AllCovered(shown, DefaultFill), $"{failure}");
                    }
                }
                finally
                {
                    lease?.Free();
                }
            }
        }

        [AvaloniaTheory]
        [MemberData(nameof(MosaicMethods))]
        public void GT80_EveryMosaicMethod_UnderAGeneratedPattern_WhileTheFrameIsMissing_IsNotCovered(string _method)
        {
            // Before the first frame a mosaic area shows its generated pattern (it covers by itself): no cover there.
            var area = NewArea(PatternKind.Mosaic);
            area.Mosaic.Method = _method;
            using var presenter = new MosaicPresenter { Model = area };
            using var bitmap = Draw(presenter);
            Assert.Equal(1, presenter.GeneratedRenderCount);
            Assert.False(AllCovered(bitmap, DefaultFill));
            Assert.Equal(0, CountSeeThrough(bitmap));
        }

        [AvaloniaTheory]
        [InlineData(null, "#68768A")]          // no color: the default
        [InlineData("", "#68768A")]
        [InlineData("#12345", "#68768A")]      // too short
        [InlineData("#1234567", "#68768A")]    // too long
        [InlineData("123456", "#68768A")]      // no '#'
        [InlineData("#GGGGGG", "#68768A")]     // not hex
        [InlineData(" #123456", "#68768A")]    // spaces are not trimmed
        [InlineData("#123456 ", "#68768A")]
        [InlineData("＃123456", "#68768A")]    // a full-width '#'
        [InlineData("#１２３４５６", "#68768A")] // full-width digits are not ASCII hex
        [InlineData("#abcdef", "#ABCDEF")]
        [InlineData("#ABCDEF", "#ABCDEF")]
        [InlineData("#aBcDeF", "#ABCDEF")]
        [InlineData("#FF0000", "#FF0000")]
        [InlineData("#00FF00", "#00FF00")]
        [InlineData("#0000FF", "#0000FF")]
        [InlineData("#010203", "#010203")]
        [InlineData("#FEFDFC", "#FEFDFC")]
        [InlineData("#000000", "#000000")]
        [InlineData("#FFFFFF", "#FFFFFF")]
        public void GT81_CoverColor_IsTheFillColor_AsTheFillPatternReadsIt(string? _fill, string _expected)
        {
            foreach (Failure failure in Enum.GetValues<Failure>())
            {
                var (presenter, lease) = Failing(failure, PatternKind.Mosaic, W, H, _fill!);
                try
                {
                    using (presenter)
                    {
                        using var shown = Shown(failure, presenter, lease, W, H, 1.0);
                        Assert.True(AllCovered(shown, Parse(_expected)), $"{failure}");
                    }
                }
                finally
                {
                    lease?.Free();
                }
            }
            // The fill pattern shows the same color: the cover looks like the area set to fill.
            using var fill = new MosaicPresenter { Model = NewArea(PatternKind.Fill, _fill) };
            using var bitmap = Draw(fill);
            Assert.True(AllCovered(bitmap, Parse(_expected)));
        }

        /// <summary>An exception type of no library: a failure nobody anticipated.</summary>
        private sealed class UnexpectedException(string _message) : Exception(_message);

        public static TheoryData<string> ExceptionKinds() => new()
        {
            nameof(InvalidOperationException),
            nameof(ObjectDisposedException),
            nameof(ArgumentException),
            nameof(ArgumentOutOfRangeException),
            nameof(NotSupportedException),
            nameof(FormatException),
            nameof(InvalidCastException),
            nameof(IOException),
            nameof(TimeoutException),
            nameof(AggregateException),
            nameof(OperationCanceledException),
            nameof(UnexpectedException),
        };

        private static Exception Make(string _kind) => _kind switch
        {
            nameof(InvalidOperationException) => new InvalidOperationException("x"),
            nameof(ObjectDisposedException) => new ObjectDisposedException("canvas"),
            nameof(ArgumentException) => new ArgumentException("x", nameof(_kind)),
            nameof(ArgumentOutOfRangeException) => new ArgumentOutOfRangeException(nameof(_kind)),
            nameof(NotSupportedException) => new NotSupportedException("x"),
            nameof(FormatException) => new FormatException("x"),
            nameof(InvalidCastException) => new InvalidCastException("x"),
            nameof(IOException) => new IOException("x"),
            nameof(TimeoutException) => new TimeoutException("x"),
            nameof(AggregateException) => new AggregateException(new InvalidOperationException("inner")),
            nameof(OperationCanceledException) => new OperationCanceledException("x"),
            _ => new UnexpectedException("x"),
        };

        [AvaloniaTheory]
        [MemberData(nameof(ExceptionKinds))]
        public void GT80_AnyExceptionFromTheCanvas_IsCaught_AndCovered(string _kind)
        {
            foreach (int failures in new[] { 1, -1 })
            {
                var lease = new FakeLease(failures, SKColors.Red, () => Make(_kind));
                try
                {
                    using var presenter = new MosaicPresenter { Model = NewArea(PatternKind.Mosaic), Frames = GoodFrame(), LeaseSkia = _ => lease };
                    var ex = Record.Exception(() =>
                    {
                        using var target = Draw(presenter);
                        using var shown = failures == 1 ? lease.Pixels() : target.Copy();
                        Assert.True(AllCovered(shown, DefaultFill));
                    });
                    Assert.Null(ex);
                    Assert.Equal(1, lease.Disposals);
                }
                finally
                {
                    lease.Free();
                }
            }
        }

        [AvaloniaFact]
        public void GT82_RepeatedFailures_CoverEveryTime_AndMakeNoImages()
        {
            using var presenter = new MosaicPresenter { Model = NewArea(PatternKind.Mosaic), Frames = BrokenFrame() };
            for (int i = 0; i < 10; i++)
            {
                using var bitmap = Draw(presenter);
                Assert.True(AllCovered(bitmap, DefaultFill), $"draw {i}");
            }
            Assert.Equal(0, presenter.MosaicRenderCount);
            Assert.Equal(0, presenter.GeneratedRenderCount);
        }

        [AvaloniaFact]
        public void GT82_GoodThenBrokenThenGood_ShowsMosaicCoverMosaic_NeverAStaleImage()
        {
            var frames = GoodFrame();
            using var presenter = new MosaicPresenter { Model = NewArea(PatternKind.Mosaic), Frames = frames };
            using (var first = Draw(presenter))
            {
                Assert.False(AllCovered(first, DefaultFill));
            }
            Assert.Equal(1, presenter.MosaicRenderCount);

            Break(frames); // a newer front frame that cannot be drawn: the old mosaic is of content that has moved on
            using (var second = Draw(presenter))
            {
                Assert.True(AllCovered(second, DefaultFill));
            }
            Assert.Equal(1, presenter.MosaicRenderCount);

            Publish(frames);
            using (var third = Draw(presenter))
            {
                Assert.False(AllCovered(third, DefaultFill));
                Assert.Equal(0, CountSeeThrough(third));
            }
            Assert.Equal(2, presenter.MosaicRenderCount);
        }

        [AvaloniaFact]
        public void GT82_CachedMosaic_ThenSkiaGoesAway_IsCovered_ThenTheCacheIsUsedAgain()
        {
            using var presenter = new MosaicPresenter { Model = NewArea(PatternKind.Mosaic), Frames = GoodFrame() };
            var real = presenter.LeaseSkia;
            using (var first = Draw(presenter))
            {
                Assert.False(AllCovered(first, DefaultFill));
            }
            presenter.LeaseSkia = _ => null;
            using (var covered = Draw(presenter))
            {
                Assert.True(AllCovered(covered, DefaultFill));
            }
            presenter.LeaseSkia = real;
            using (var again = Draw(presenter))
            {
                Assert.False(AllCovered(again, DefaultFill));
            }
            Assert.Equal(1, presenter.MosaicRenderCount); // the same frame: the cached mosaic, not a new one
        }

        [AvaloniaFact]
        public void GT82_SwitchingPatterns_AfterAFailure_DrawsEachPatternNormally()
        {
            var area = NewArea(PatternKind.Mosaic, "#123456");
            using var presenter = new MosaicPresenter { Model = area, Frames = BrokenFrame() };
            using (var covered = Draw(presenter))
            {
                Assert.True(AllCovered(covered, Parse("#123456")));
            }
            area.Pattern = PatternKind.Generated;
            using (var generated = Draw(presenter))
            {
                Assert.False(AllCovered(generated, Parse("#123456")));
                Assert.Equal(0, CountSeeThrough(generated));
            }
            area.Pattern = PatternKind.Fill;
            using (var fill = Draw(presenter))
            {
                Assert.True(AllCovered(fill, Parse("#123456")));
            }
        }

        [AvaloniaTheory]
        [InlineData(Failure.NoLease)]
        [InlineData(Failure.CanvasAlways)]
        [InlineData(Failure.CanvasOnce)]
        [InlineData(Failure.BrokenFrame)]
        public void GT83_Disposed_AndFailing_IsStillCovered(Failure _failure)
        {
            var (presenter, lease) = Failing(_failure, PatternKind.Mosaic, W, H);
            try
            {
                presenter.Dispose();
                using var shown = Shown(_failure, presenter, lease, W, H, 1.0);
                Assert.True(AllCovered(shown, DefaultFill));
                Assert.Equal(0, presenter.MosaicRenderCount);
                Assert.Equal(0, presenter.GeneratedRenderCount);
            }
            finally
            {
                lease?.Free();
            }
        }

        [AvaloniaFact]
        public void GT83_Disposed_WithAGoodLease_CoversThroughSkia()
        {
            var lease = new FakeLease(0, SKColors.Red);
            try
            {
                var presenter = new MosaicPresenter { Model = NewArea(PatternKind.Generated), LeaseSkia = _ => lease };
                presenter.Dispose();
                Draw(presenter).Dispose();
                using var pixels = lease.Pixels();
                Assert.True(AllCovered(pixels, DefaultFill));
                Assert.Equal(1, lease.CanvasReads);
                Assert.Equal(1, lease.Disposals);
            }
            finally
            {
                lease.Free();
            }
        }

        [AvaloniaFact]
        public void GT83_Disposed_FillPattern_IsTheFillAsBefore()
        {
            var presenter = new MosaicPresenter { Model = NewArea(PatternKind.Fill, "#C0FFEE") };
            presenter.Dispose();
            using var bitmap = Draw(presenter);
            Assert.True(AllCovered(bitmap, Parse("#C0FFEE")));
        }

        [AvaloniaFact]
        public void GT80_OneFailingArea_DoesNotAffectAnother()
        {
            using var failing = new MosaicPresenter { Model = NewArea(PatternKind.Mosaic, "#102030"), Frames = BrokenFrame() };
            using var good = new MosaicPresenter { Model = NewArea(PatternKind.Mosaic, "#102030"), Frames = GoodFrame() };
            using var reference = new MosaicPresenter { Model = NewArea(PatternKind.Mosaic, "#102030"), Frames = GoodFrame() };
            using var a = Draw(failing);
            using var b = Draw(good);
            using var c = Draw(reference);
            Assert.True(AllCovered(a, Parse("#102030")));
            Assert.Equal(0, Hider.Core.Tests.Imaging.Golden.MaxDiff(c, b));
            Assert.Equal(1, good.MosaicRenderCount);
        }

        [AvaloniaTheory]
        [InlineData(PatternKind.Mosaic, 0.0)]
        [InlineData(PatternKind.Mosaic, 0.1)]
        [InlineData(PatternKind.Mosaic, 0.25)]
        [InlineData(PatternKind.Mosaic, 0.75)]
        [InlineData(PatternKind.Mosaic, 0.99)]
        [InlineData(PatternKind.Generated, 0.1)]
        [InlineData(PatternKind.Generated, 0.5)]
        [InlineData(PatternKind.Generated, 1.0)]
        public void GT81_Cover_IsOpaque_ForEveryPatternAndOpacity(PatternKind _pattern, double _opacity)
        {
            foreach (var failure in new[] { Failure.CanvasOnce, Failure.CanvasAlways, Failure.NoLease })
            {
                var (presenter, lease) = Failing(failure, _pattern, W, H);
                presenter.Model!.Opacity = _opacity;
                try
                {
                    using (presenter)
                    {
                        using var shown = Shown(failure, presenter, lease, W, H, 1.0);
                        Assert.True(AllCovered(shown, DefaultFill), $"{failure}");
                    }
                }
                finally
                {
                    lease?.Free();
                }
            }
        }

        [AvaloniaFact]
        public void GT80_ModelOfAnotherSize_TheCoverFollowsTheBounds()
        {
            // The model's rectangle (physical px) and the laid-out bounds can differ for a moment (a resize in flight): the
            // cover fills what is on screen, the bounds.
            var area = NewArea(PatternKind.Mosaic, _w: 10, _h: 10);
            using var presenter = new MosaicPresenter { Model = area, Frames = BrokenFrame() };
            using var bitmap = Draw(presenter);
            Assert.True(AllCovered(bitmap, DefaultFill));
        }

        [AvaloniaFact]
        public void GT82_Logs_NamePath_AndNothingOnAGoodDraw()
        {
            var lease = new FakeLease(-1, SKColors.Red);
            try
            {
                using var log = new StringWriter();
                using (Log.CaptureTo(log))
                {
                    using var good = new MosaicPresenter { Model = NewArea(PatternKind.Mosaic), Frames = GoodFrame() };
                    Draw(good).Dispose();
                    Assert.DoesNotContain("failed", log.ToString(), StringComparison.Ordinal);

                    using var none = new MosaicPresenter { Model = NewArea(PatternKind.Mosaic), Frames = GoodFrame(), LeaseSkia = _ => null };
                    Draw(none).Dispose();
                    Assert.DoesNotContain("failed", log.ToString(), StringComparison.Ordinal); // no lease is not an error

                    using var failing = new MosaicPresenter { Model = NewArea(PatternKind.Mosaic), Frames = GoodFrame(), LeaseSkia = _ => lease };
                    Draw(failing).Dispose();
                }
                string[] lines = log.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(_l => _l.TrimEnd('\r')).ToArray();
                Assert.Equal(1, lines.Count(_l => _l == "pattern draw failed: canvas lost; the area is covered with its fill color"));
                Assert.Equal(1, lines.Count(_l => _l == "cover through Skia failed: canvas lost"));
            }
            finally
            {
                lease.Free();
            }
        }

        private static MosaicPresenter.DrawOp OpFor(MosaicPresenter _presenter, string _fill = AreaDefaults.FillColor)
        {
            var generated = MethodRegistry.Find(AreaDefaults.GeneratedMethod)!;
            var (r, g, b) = ColorText.Parse(_fill);
            return new MosaicPresenter.DrawOp(_presenter, new Rect(0, 0, W, H), new SKSizeI(W, H), new SKPointI(0, 0), generated,
                ParamValues.ForGenerated(generated, NewArea(PatternKind.Generated).Generated), null, null, 256,
                new ImmutableSolidColorBrush(Avalonia.Media.Color.FromRgb(r, g, b)));
        }

        [AvaloniaFact]
        public void GT83_TryDraw_NoLease_IsFalse()
        {
            using var presenter = new MosaicPresenter { Model = NewArea(PatternKind.Generated) };
            Assert.False(OpFor(presenter).TryDraw(null));
            Assert.Equal(0, presenter.GeneratedRenderCount);
        }

        [AvaloniaFact]
        public void GT83_TryDraw_GoodLease_IsTrue_DrawsThePattern_AndDisposesTheLease()
        {
            var lease = new FakeLease(0, SKColors.Red);
            try
            {
                using var presenter = new MosaicPresenter { Model = NewArea(PatternKind.Generated) };
                Assert.True(OpFor(presenter).TryDraw(lease));
                Assert.Equal(1, presenter.GeneratedRenderCount);
                Assert.Equal(1, lease.Disposals);
                using var pixels = lease.Pixels();
                Assert.False(AllCovered(pixels, DefaultFill));
                Assert.NotEqual(0, CountOther(pixels, SKColors.Red, W, H)); // drawn over the red
            }
            finally
            {
                lease.Free();
            }
        }

        [AvaloniaFact]
        public void GT83_TryDraw_CanvasFailsOnce_IsTrue_WithTheCover()
        {
            var lease = new FakeLease(1, SKColors.Red);
            try
            {
                using var presenter = new MosaicPresenter { Model = NewArea(PatternKind.Generated) };
                Assert.True(OpFor(presenter, "#AA5500").TryDraw(lease));
                using var pixels = lease.Pixels();
                Assert.True(AllCovered(pixels, Parse("#AA5500")));
                Assert.Equal(2, lease.CanvasReads);
                Assert.Equal(1, lease.Disposals);
            }
            finally
            {
                lease.Free();
            }
        }

        [AvaloniaFact]
        public void GT83_TryDraw_CanvasAlwaysFails_IsFalse_AndTheLeaseIsStillDisposed()
        {
            var lease = new FakeLease(-1, SKColors.Red);
            try
            {
                using var presenter = new MosaicPresenter { Model = NewArea(PatternKind.Generated) };
                Assert.False(OpFor(presenter).TryDraw(lease));
                Assert.Equal(2, lease.CanvasReads);
                Assert.Equal(1, lease.Disposals);
                using var pixels = lease.Pixels();
                Assert.True(AllCovered(pixels, SKColors.Red)); // nothing reached the canvas
            }
            finally
            {
                lease.Free();
            }
        }

        [AvaloniaFact]
        public void GT83_DrawOp_KeepsTheCoverBrush_AndHitTestsItsBounds()
        {
            using var presenter = new MosaicPresenter { Model = NewArea(PatternKind.Generated) };
            var op = OpFor(presenter, "#0F1E2D");
            Assert.Equal(Avalonia.Media.Color.FromRgb(0x0F, 0x1E, 0x2D), op.CoverBrush.Color);
            Assert.True(op.HitTest(new Point(0, 0)));
            Assert.True(op.HitTest(new Point(W - 0.5, H - 0.5)));
            Assert.False(op.HitTest(new Point(W + 1, 0)));
            Assert.False(op.Equals(op)); // never "the same as last frame": always redrawn
            op.Dispose(); // nothing to free
        }

        public static TheoryData<double, PatternKind, Failure> WindowCases()
        {
            var data = new TheoryData<double, PatternKind, Failure>();
            foreach (double scale in new[] { 1.0, 1.25, 1.5, 1.75, 2.0 })
            {
                foreach (var pattern in new[] { PatternKind.Mosaic, PatternKind.Generated })
                {
                    foreach (var failure in new[] { Failure.CanvasAlways, Failure.NoLease })
                    {
                        data.Add(scale, pattern, failure);
                    }
                }
            }
            return data;
        }

        private static SKBitmap Frame(AreaController _area)
        {
            using var frame = _area.Window.CaptureRenderedFrame()!;
            using var buffer = frame.Lock();
            var colorType = buffer.Format == Avalonia.Platform.PixelFormat.Rgba8888 ? SKColorType.Rgba8888 : SKColorType.Bgra8888;
            var info = new SKImageInfo(buffer.Size.Width, buffer.Size.Height, colorType, SKAlphaType.Premul);
            var bytes = new byte[buffer.RowBytes * info.Height];
            System.Runtime.InteropServices.Marshal.Copy(buffer.Address, bytes, 0, bytes.Length);
            using var image = SKImage.FromPixelCopy(info, bytes, buffer.RowBytes);
            return Hider.Core.Tests.Imaging.Render.ToBitmap(image);
        }

        [AvaloniaTheory]
        [MemberData(nameof(WindowCases))]
        public void GT80_AreaWindow_WhosePatternFails_ShowsOnlyTheFillColor(double _scale, PatternKind _pattern, Failure _failure)
        {
            // A real area window (out of edit mode: nothing but the pattern): every rendered pixel is the fill color.
            using var f = new AreaHarness(_scale);
            var area = f.Add(new PhysicalRect(100, 100, 61, 115), _pattern);
            area.ExitEdit();
            area.Model.Fill.Color = "#3C5A78";
            var lease = _failure == Failure.CanvasAlways ? new FakeLease(-1, SKColors.Red) : null;
            try
            {
                area.Window.PatternView.LeaseSkia = _ => lease;
                area.Window.PatternView.InvalidateVisual();
                using var frame = Frame(area);
                Assert.True(AllCovered(frame, Parse("#3C5A78")), $"{frame.Width}x{frame.Height}");
            }
            finally
            {
                lease?.Free();
            }
        }

        [AvaloniaTheory]
        [InlineData(1.0)]
        [InlineData(1.5)]
        [InlineData(1.75)]
        public void GT80_AreaWindow_InEditMode_WhosePatternFails_HasNoSeeThroughPixel(double _scale)
        {
            // In edit mode the window also holds the toolbar and margins outside the area (transparent by design); the frame
            // and handles are drawn over the pattern. Inside the area, under all of that, the cover still hides everything.
            using var f = new AreaHarness(_scale);
            var area = f.Add(new PhysicalRect(100, 100, 400, 300), PatternKind.Mosaic);
            area.EnterEdit();
            var view = area.Window.PatternView;
            view.LeaseSkia = _ => null;
            view.InvalidateVisual();
            using var frame = Frame(area);
            // The rendered frame is in the headless window's own scaling (the area's scale only sizes it in DIP).
            double render = area.Window.RenderScaling;
            var origin = view.TranslatePoint(new Point(0, 0), area.Window)!.Value;
            int x0 = (int)Math.Ceiling(origin.X * render), y0 = (int)Math.Ceiling(origin.Y * render);
            int x1 = (int)((origin.X + view.Bounds.Width) * render), y1 = (int)((origin.Y + view.Bounds.Height) * render);
            Assert.True(x1 > x0 && y1 > y0 && x1 <= frame.Width && y1 <= frame.Height, $"pattern at {x0},{y0}..{x1},{y1} in {frame.Width}x{frame.Height}");
            int seeThrough = 0;
            for (int y = y0; y < y1; y++)
            {
                for (int x = x0; x < x1; x++)
                {
                    if (frame.GetPixel(x, y).Alpha != 255)
                    {
                        seeThrough++;
                    }
                }
            }
            Assert.Equal(0, seeThrough);
        }
    }
}
