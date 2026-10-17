using System.Reflection;

namespace Hider
{
    internal static class AppInfo
    {
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
