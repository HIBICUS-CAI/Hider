namespace Hider.Core.Model
{
    /// <summary>Default values from the spec §4.5 / FR-14 / FR-51 in one place.</summary>
    public static class AreaDefaults
    {
        public const string FillColor = "#68768A";
        public const double NewAreaWidthDip = 400;
        public const double NewAreaHeightDip = 240;
        public const double MinSizeDip = 24;
        public const string MosaicMethod = "pixelate";
        public const string GeneratedMethod = "block";
        public const double MosaicBlur = 0;
        public const int MosaicLevels = 256;
        public const int StripPx = 8;
        public const int Seed = 1234;
        public const int IntervalMsWindows = 100;
        public const int IntervalMsLinux = 300000;

        /// <summary>Mosaic refresh interval for a new area, decided by the OS it is created on (FR-51).</summary>
        public static int IntervalMs(bool _isWindows)
        {
            return _isWindows ? IntervalMsWindows : IntervalMsLinux;
        }

        public static int IntervalMsForCurrentOs => IntervalMs(OperatingSystem.IsWindows());
    }
}
