using System.Text.RegularExpressions;

namespace Hider.Core.Tests
{
    /// <summary>Log on standard error. Changes Console.Error and Log.Enabled, so it runs alone.</summary>
    [Collection(ProcessWideState.Name)]
    public class LogTests
    {
        private static string WithStandardError(bool _enabled, Action _action)
        {
            var previousError = Console.Error;
            bool previousEnabled = Log.Enabled;
            using var error = new StringWriter();
            try
            {
                Console.SetError(error);
                Log.Enabled = _enabled;
                _action();
            }
            finally
            {
                Log.Enabled = previousEnabled;
                Console.SetError(previousError);
            }
            return error.ToString();
        }

        [Fact]
        public void CT107_Enabled_IsOffByDefault_AndSettable()
        {
            bool previous = Log.Enabled;
            try
            {
                Assert.False(previous); // only --verbose turns it on (Program)
                Log.Enabled = true;
                Assert.True(Log.Enabled);
                Log.Enabled = false;
                Assert.False(Log.Enabled);
            }
            finally
            {
                Log.Enabled = previous;
            }
        }

        [Fact]
        public void CT107_Verbose_Disabled_WritesNothing()
        {
            Assert.Equal("", WithStandardError(false, () => Log.Verbose("hidden")));
        }

        [Fact]
        public void CT107_Verbose_Enabled_WritesATimeStampedLineToStandardError()
        {
            string text = WithStandardError(true, () =>
            {
                Log.Verbose("first message");
                Log.Verbose("second");
            });
            string[] lines = text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(2, lines.Length);
            Assert.Matches(new Regex(@"^\d{2}:\d{2}:\d{2}\.\d{3} first message$"), lines[0]);
            Assert.Matches(new Regex(@"^\d{2}:\d{2}:\d{2}\.\d{3} second$"), lines[1]);
        }

        [Fact]
        public void CT108_Verbose_Captured_GoesToTheCaptureOnly_WithoutTimeStamp()
        {
            using var capture = new StringWriter();
            string text = WithStandardError(true, () =>
            {
                using (Log.CaptureTo(capture))
                {
                    Log.Verbose("captured");
                }
            });
            Assert.Equal("", text);
            Assert.Equal("captured" + Environment.NewLine, capture.ToString());
        }

        [Fact]
        public void CT108_Verbose_Captured_EvenWhenDisabled()
        {
            using var capture = new StringWriter();
            WithStandardError(false, () =>
            {
                using (Log.CaptureTo(capture))
                {
                    Log.Verbose("still captured");
                }
            });
            Assert.Equal("still captured" + Environment.NewLine, capture.ToString());
        }

        [Fact]
        public void CT108_CaptureTo_Nested_RestoresTheOuterOne()
        {
            using var outer = new StringWriter();
            using var inner = new StringWriter();
            string text = WithStandardError(true, () =>
            {
                using (Log.CaptureTo(outer))
                {
                    Log.Verbose("a");
                    using (Log.CaptureTo(inner))
                    {
                        Log.Verbose("b");
                    }
                    Log.Verbose("c");
                }
                Log.Verbose("d");
            });
            Assert.Equal("a" + Environment.NewLine + "c" + Environment.NewLine, outer.ToString());
            Assert.Equal("b" + Environment.NewLine, inner.ToString());
            Assert.EndsWith(" d" + Environment.NewLine, text, StringComparison.Ordinal); // after the last dispose: standard error
        }

        [Fact]
        public async Task CT108_CaptureTo_FlowsIntoWorkItStarts()
        {
            using var capture = new StringWriter();
            using (Log.CaptureTo(capture))
            {
                await Task.Run(() => Log.Verbose("from the pool"), TestContext.Current.CancellationToken);
            }
            Assert.Equal("from the pool" + Environment.NewLine, capture.ToString());
        }
    }
}
