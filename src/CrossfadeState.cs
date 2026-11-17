using System.Globalization;

namespace LiveBackground
{
    // The picture crossfade slider runs from 0 to 5 seconds, and the track bar
    // stores half-seconds, so its range is 0..10. With the "half-second fade
    // steps" option off the slider snaps to whole seconds; with it on it moves
    // in 0.5 s steps. The same arithmetic backs both the main Images pane and
    // the per-screen picture editors.
    public static class CrossfadeState
    {
        // The slider's top value, in half-seconds (5.0 s).
        public const int MaxHalfSeconds = 10;

        // The slider step in half-seconds: 1 (0.5 s) with fine steps on,
        // 2 (1 s) otherwise.
        public static int Step(bool fine)
        {
            return fine ? 1 : 2;
        }

        // Rounds a half-second slider value onto the current step and keeps it
        // inside the track.
        public static int Snap(int value, bool fine)
        {
            if (value < 0) value = 0;
            if (value > MaxHalfSeconds) value = MaxHalfSeconds;
            if (!fine && (value % 2) != 0)
            {
                value++;
                if (value > MaxHalfSeconds) value = MaxHalfSeconds;
            }
            return value;
        }

        // "Off" below a twentieth of a second, otherwise one decimal place.
        // The two callers word "Off" differently, so the caller supplies it.
        public static string Text(int halfSeconds, string offText)
        {
            double seconds = halfSeconds / 2.0;
            return seconds < 0.05
                ? offText
                : seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s";
        }
    }
}
