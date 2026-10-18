namespace Hider.App.Tests.Platform
{
    /// <summary>
    /// Conditions of the tests that run real Linux services (a private dbus-daemon, a private PipeWire, libhidercap). They
    /// are skipped, with the reason, where those cannot exist (another OS), and on a Linux developer machine that lacks a
    /// tool. On a CI runner (GITHUB_ACTIONS=true) under Linux they always run, so a missing package fails the job instead
    /// of passing it with nothing tested.
    /// </summary>
    public static class LinuxTestEnvironment
    {
        public const string DbusReason = "needs Linux and dbus-daemon (a private session bus); always run on Linux CI";
        public const string HidercapReason = "needs Linux and libhidercap.so next to the tests (make -C native/hidercap before the build); always run on Linux CI";
        public const string PipeWireReason = "needs Linux, libhidercap.so, pipewire, wireplumber, pw-cli and gst-launch-1.0 with pipewiresink; always run on Linux CI";

        public static bool InCi => Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true";

        public static string HidercapPath => Path.Combine(AppContext.BaseDirectory, "libhidercap.so");

        /// <summary>dbus-daemon can be started (Linux CI: assumed, so a missing one fails).</summary>
        public static bool HasDbus => OperatingSystem.IsLinux() && (InCi || OnPath("dbus-daemon"));

        /// <summary>libhidercap.so was copied next to the tests (Linux CI: assumed).</summary>
        public static bool HasHidercap => OperatingSystem.IsLinux() && (InCi || File.Exists(HidercapPath));

        /// <summary>A private PipeWire with a GStreamer video source can be run (Linux CI: assumed).</summary>
        public static bool HasPipeWire => OperatingSystem.IsLinux()
            && (InCi || (File.Exists(HidercapPath) && OnPath("pipewire") && OnPath("wireplumber") && OnPath("pw-cli") && OnPath("gst-launch-1.0")));

        public static bool OnPath(string _tool)
        {
            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            return path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).Any(_dir => File.Exists(Path.Combine(_dir, _tool)));
        }
    }
}
