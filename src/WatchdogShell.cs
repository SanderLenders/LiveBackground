namespace LiveBackground
{
    // Pure decision behind the watchdog's shell restore. The watchdog may only
    // show the taskbar when it is about to restart a missing wallpaper process.
    // Showing it while the app is healthy would unveil the video screensaver,
    // whose worker re-hides the shell on its next tick.
    public static class WatchdogShell
    {
        public static bool ShouldRestoreShell(bool quitFlag, bool appRunning)
        {
            if (quitFlag) return false;
            return !appRunning;
        }
    }
}
