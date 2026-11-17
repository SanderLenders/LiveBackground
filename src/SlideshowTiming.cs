using System;

namespace LiveBackground
{
    // Window-free clamping for the two picture-slideshow timings, so the
    // settings window, the watchdog and the tests all agree on the same limits.
    // Both take the config explicitly instead of reading the running one, so
    // they can be exercised on their own.
    public static class SlideshowTiming
    {
        public static int ImageIntervalSeconds(AppConfig config)
        {
            int seconds = config == null ? 10 : config.ImageIntervalSeconds;
            if (seconds < 1) seconds = 10;
            if (seconds > 86400) seconds = 86400;
            return seconds;
        }

        public static double ImageCrossfadeSeconds(AppConfig config)
        {
            double seconds = config == null ? 2.0 : config.ImageCrossfadeSeconds;
            if (double.IsNaN(seconds) || seconds < 0) seconds = 0;
            if (seconds > 5.0) seconds = 5.0;
            return seconds;
        }
    }
}
