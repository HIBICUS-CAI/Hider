using System.Runtime.CompilerServices;

namespace Hider.App.Tests.Platform
{
    /// <summary>A test on a private dbus-daemon (skipped with the reason only where it cannot run, see <see cref="LinuxTestEnvironment"/>).</summary>
    public sealed class DbusFactAttribute : FactAttribute
    {
        public DbusFactAttribute([CallerFilePath] string? _sourceFilePath = null, [CallerLineNumber] int _sourceLineNumber = -1)
            : base(_sourceFilePath, _sourceLineNumber)
        {
            Skip = LinuxTestEnvironment.DbusReason;
            SkipType = typeof(LinuxTestEnvironment);
            SkipUnless = nameof(LinuxTestEnvironment.HasDbus);
        }
    }

    /// <inheritdoc cref="DbusFactAttribute"/>
    public sealed class DbusTheoryAttribute : TheoryAttribute
    {
        public DbusTheoryAttribute([CallerFilePath] string? _sourceFilePath = null, [CallerLineNumber] int _sourceLineNumber = -1)
            : base(_sourceFilePath, _sourceLineNumber)
        {
            Skip = LinuxTestEnvironment.DbusReason;
            SkipType = typeof(LinuxTestEnvironment);
            SkipUnless = nameof(LinuxTestEnvironment.HasDbus);
        }
    }

    /// <summary>A test of the real libhidercap.so (no PipeWire daemon).</summary>
    public sealed class HidercapFactAttribute : FactAttribute
    {
        public HidercapFactAttribute([CallerFilePath] string? _sourceFilePath = null, [CallerLineNumber] int _sourceLineNumber = -1)
            : base(_sourceFilePath, _sourceLineNumber)
        {
            Skip = LinuxTestEnvironment.HidercapReason;
            SkipType = typeof(LinuxTestEnvironment);
            SkipUnless = nameof(LinuxTestEnvironment.HasHidercap);
        }
    }

    /// <summary>A test on a private PipeWire with a GStreamer video source, through the real libhidercap.so.</summary>
    public sealed class PipeWireFactAttribute : FactAttribute
    {
        public PipeWireFactAttribute([CallerFilePath] string? _sourceFilePath = null, [CallerLineNumber] int _sourceLineNumber = -1)
            : base(_sourceFilePath, _sourceLineNumber)
        {
            Skip = LinuxTestEnvironment.PipeWireReason;
            SkipType = typeof(LinuxTestEnvironment);
            SkipUnless = nameof(LinuxTestEnvironment.HasPipeWire);
        }
    }

    /// <inheritdoc cref="PipeWireFactAttribute"/>
    public sealed class PipeWireTheoryAttribute : TheoryAttribute
    {
        public PipeWireTheoryAttribute([CallerFilePath] string? _sourceFilePath = null, [CallerLineNumber] int _sourceLineNumber = -1)
            : base(_sourceFilePath, _sourceLineNumber)
        {
            Skip = LinuxTestEnvironment.PipeWireReason;
            SkipType = typeof(LinuxTestEnvironment);
            SkipUnless = nameof(LinuxTestEnvironment.HasPipeWire);
        }
    }
}
