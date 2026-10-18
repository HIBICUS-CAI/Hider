using System.Reflection;

namespace Hider
{
    internal static class AppInfo
    {
        /// <summary>
        /// How this process runs: "aot" (native AOT, the default build) or "jit" (the regular build, dotnet run, the
        /// tests), the words the build names use (scripts' VARIANTS, CI's matrix.variant). --selftest prints it first,
        /// so CI can check that each published build is the one it claims to be. Native AOT has no JIT and counts no
        /// compiled methods; a JIT run has compiled Main at least. Not RuntimeFeature.IsDynamicCodeSupported:
        /// PublishAot turns that off in a JIT run of the same project too.
        /// </summary>
        public static string Runtime => System.Runtime.JitInfo.GetCompiledMethodCount() > 0 ? "jit" : "aot";

        /// <summary>
        /// Informational version without the source revision suffix ("2.0.0", not "2.0.0+abc"); null when the assembly has
        /// none. Read once: it cannot change while running.
        /// </summary>
        public static string? Version { get; } = Read();

        private static string? Read()
        {
            string? version = typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            return version?.Split('+')[0];
        }
    }
}
