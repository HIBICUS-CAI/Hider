using System.Runtime.Versioning;
using Hider.Platform.Windows;

namespace Hider.App.Tests.Platform.Windows
{
    /// <summary>
    /// Which streams --version / --selftest send to the parent's console (Win32Console.Choose): the decision is plain logic
    /// over the redirection state and the Win32 calls, which are stand-ins here, so it runs on every OS.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class Win32ConsoleTests
    {
        /// <summary>Counts the Win32 steps and hands out a writer that records what reaches "the console".</summary>
        private sealed class Steps
        {
            public int Attaches { get; private set; }

            public int Opens { get; private set; }

            public bool AttachResult { get; set; } = true;

            public Exception? OpenFailure { get; set; }

            public StringWriter Console { get; } = new();

            public bool Attach()
            {
                Attaches++;
                return AttachResult;
            }

            public StringWriter Open()
            {
                Opens++;
                if (OpenFailure is { } e)
                {
                    throw e;
                }
                return Console;
            }
        }

        [Fact]
        public void GT114_BothRedirected_KeepsBoth_WithoutAttaching()
        {
            // A pipe or file for both (CI, `Hider --version | more`): the console is not even attached.
            var steps = new Steps();

            var chosen = Win32Console.Choose(true, true, steps.Attach, steps.Open);

            Assert.Equal((null, null), chosen);
            Assert.Equal(0, steps.Attaches);
            Assert.Equal(0, steps.Opens);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        public void GT114_NoParentConsole_KeepsBoth_WithoutOpening(bool _outputRedirected, bool _errorRedirected)
        {
            // Started from Explorer or a shortcut: nothing to attach to.
            var steps = new Steps { AttachResult = false };

            var chosen = Win32Console.Choose(_outputRedirected, _errorRedirected, steps.Attach, steps.Open);

            Assert.Equal((null, null), chosen);
            Assert.Equal(1, steps.Attaches);
            Assert.Equal(0, steps.Opens);
        }

        [Fact]
        public void GT114_NothingRedirected_BothGoToTheConsole_AfterAnEmptyLine()
        {
            var steps = new Steps();

            var (output, error) = Win32Console.Choose(false, false, steps.Attach, steps.Open);

            Assert.Same(steps.Console, output);
            Assert.Same(steps.Console, error);
            Assert.Equal(Environment.NewLine, steps.Console.ToString()); // ends the line of the shell's prompt
            Assert.Equal(1, steps.Attaches);
            Assert.Equal(1, steps.Opens);
        }

        [Fact]
        public void GT114_OutputRedirected_OnlyErrorGoesToTheConsole()
        {
            var steps = new Steps();

            var (output, error) = Win32Console.Choose(true, false, steps.Attach, steps.Open);

            Assert.Null(output);
            Assert.Same(steps.Console, error);
            Assert.Equal(Environment.NewLine, steps.Console.ToString());
        }

        [Fact]
        public void GT114_ErrorRedirected_OnlyOutputGoesToTheConsole()
        {
            var steps = new Steps();

            var (output, error) = Win32Console.Choose(false, true, steps.Attach, steps.Open);

            Assert.Same(steps.Console, output);
            Assert.Null(error);
            Assert.Equal(Environment.NewLine, steps.Console.ToString());
        }

        public static TheoryData<Exception> OpenFailures => new()
        {
            new IOException("no console"),
            new UnauthorizedAccessException("denied"),
        };

        [Theory]
        [MemberData(nameof(OpenFailures))]
        public void GT114_ConsoleCannotBeOpened_KeepsBoth(Exception _failure)
        {
            // Then only the exit code tells the result.
            var steps = new Steps { OpenFailure = _failure };

            var chosen = Win32Console.Choose(false, false, steps.Attach, steps.Open);

            Assert.Equal((null, null), chosen);
            Assert.Equal(1, steps.Opens);
        }

        [Fact]
        public void GT114_OtherFailures_AreNotSwallowed()
        {
            var steps = new Steps { OpenFailure = new InvalidOperationException("bug") };

            var error = Assert.Throws<InvalidOperationException>(() => Win32Console.Choose(false, false, steps.Attach, steps.Open));

            Assert.Equal("bug", error.Message);
        }

        [Fact]
        public void GT114_WritingTheFirstLineFails_KeepsBoth()
        {
            var steps = new Steps();
            var broken = new BrokenWriter();

            var chosen = Win32Console.Choose(false, false, steps.Attach, () => broken);

            Assert.Equal((null, null), chosen);
        }

        private sealed class BrokenWriter : StringWriter
        {
            public override void WriteLine()
            {
                throw new IOException("the console went away");
            }
        }
    }
}
