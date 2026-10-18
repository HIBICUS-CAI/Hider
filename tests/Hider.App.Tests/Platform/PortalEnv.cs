using System.Runtime.Versioning;

namespace Hider.App.Tests.Platform
{
    /// <summary>A private bus with the scriptable portal on it, for one test.</summary>
    [SupportedOSPlatform("linux")]
    public sealed class PortalEnv : IDisposable
    {
        private PortalEnv(PrivateBus _bus, FakeDesktopPortal? _portal)
        {
            Bus = _bus;
            Portal = _portal!;
        }

        public PrivateBus Bus { get; }

        /// <summary>The fake portal (null when started without one).</summary>
        public FakeDesktopPortal Portal { get; }

        public string Address => Bus.Address;

        /// <param name="_withPortal">False: a bus where nobody owns org.freedesktop.portal.Desktop.</param>
        public static async Task<PortalEnv> StartAsync(bool _withPortal = true)
        {
            var bus = PrivateBus.Start();
            try
            {
                var portal = _withPortal ? await FakeDesktopPortal.StartAsync(bus.Address) : null;
                return new PortalEnv(bus, portal);
            }
            catch
            {
                bus.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            Portal?.Dispose();
            Bus.Dispose();
        }
    }
}
