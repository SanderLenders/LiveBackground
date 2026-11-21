using System;

namespace LiveBackground
{
    // Pure decisions behind the app-managed idle screensaver actions, kept
    // free of P/Invoke and UI so they can be unit-tested. The worker owns the
    // stateful parts (hidden/minimized/lock-fired) and only feeds these answers.
    public static class ScreensaverIdle
    {
        // Fallback when config has no usable idle timeout.
        public const int DefaultTimeoutSeconds = 300;

        public static int NormalizeTimeout(int configuredSeconds)
        {
            return configuredSeconds > 0 ? configuredSeconds : DefaultTimeoutSeconds;
        }

        public static bool IsDue(bool paused, bool running, long idleMilliseconds, int timeoutSeconds)
        {
            if (paused || !running) return false;
            if (timeoutSeconds <= 0) return false;
            return idleMilliseconds >= (long)timeoutSeconds * 1000L;
        }
    }
}
