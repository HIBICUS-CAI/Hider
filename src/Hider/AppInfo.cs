using System.Reflection;

namespace Hider
{
    internal static class AppInfo
    {
        /// <summary>Informational version without the source revision suffix ("2.0.0", not "2.0.0+abc").</summary>
        public static string Version
        {
            get
            {
                string? version = typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                return version is null ? "unknown" : version.Split('+')[0];
            }
        }
    }
}
