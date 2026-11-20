using System;

namespace LiveBackground
{
    // The decisions behind hiding the taskbar and desktop icons after idle
    // (so only the wallpaper is visible), kept free of P/Invoke and the
    // registry so they can be unit-tested. The actual hiding lives in
    // ShellChrome; the worker only feeds it these answers.
    public static class ScreensaverIdle
    {
        // Windows can have the screen saver switched off with no usable timeout
        // stored. The option should still work in that case, so it falls back
        // to a sane, documented interval instead of doing nothing.
        public const int DefaultTimeoutSeconds = 300;

        public static int ResolveTimeout(bool screenSaverActive, int configuredSeconds)
        {
            if (!screenSaverActive || configuredSeconds <= 0) return DefaultTimeoutSeconds;
            return configuredSeconds;
        }

        public static bool ShouldHide(bool videoBackground, bool paused, bool running,
            long idleMilliseconds, int timeoutSeconds)
        {
            if (!videoBackground || paused || !running) return false;
            if (timeoutSeconds <= 0) return false;
            return idleMilliseconds >= (long)timeoutSeconds * 1000L;
        }
    }
}
