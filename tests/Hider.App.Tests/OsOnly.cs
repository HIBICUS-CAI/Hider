namespace Hider.App.Tests
{
    /// <summary>
    /// Skip reasons of the tests that can only run on one OS (P-11): they are skipped elsewhere with the reason and always run
    /// by the CI job of their OS. The Linux tests that need real services have theirs in LinuxTestEnvironment.
    /// </summary>
    public static class OsOnly
    {
        public const string Win32Calls = "needs Windows (user32 / gdi32 / shcore calls); always run on Windows CI";
        public const string EnumWindows = "needs Windows (EnumWindows and DWM); always run on Windows CI";
        public const string PrintWindow = "needs Windows (PrintWindow); always run on Windows CI";
        public const string WindowCapture = "needs Windows (window enumeration and PrintWindow); always run on Windows CI";
        public const string MonitorDpi = "needs Windows (MonitorFromRect / GetDpiForMonitor); always run on Windows CI";
        public const string Cursor = "needs Windows (GetCursorPos); always run on Windows CI";
        public const string TrayLeftClick = "needs Windows (the left-click popup at GetCursorPos); always run on Windows CI";
        public const string MessageBox = "needs Windows (the start-up error message box of a child process); always run on Windows CI";
        public const string NotWindowsCursor = "not on Windows (there the pointer position comes from GetCursorPos); always run on Linux CI";
        public const string NotWindowsTrayLeftClick = "not on Windows (there a left click opens the popup); always run on Linux CI";
        public const string Sigterm = "needs Linux (SIGTERM; Windows registers none); always run on Linux CI";
        public const string ShippedHelper = "needs Linux (a libhidercap.so next to the app); always run on Linux CI";
        public const string LockErrorExit = "needs Linux (on Windows the process waits for its message box: see the Windows test); always run on Linux CI";
        public const string NoDisplay = "needs Linux (a start without a display; on Windows the resident app would start); always run on Linux CI";
    }
}
