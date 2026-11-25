using System;

namespace LiveBackground
{
    // The startup decision is kept separate from the watchdog's full cache
    // validation. At logon we only look at the manifest's encoded settings;
    // hashing the whole video folder would delay the first wallpaper. A stale
    // loop is still used immediately and rebuilt in the background.
    public static class StartupCache
    {
        public enum Decision
        {
            Build,
            ReuseStale,
            ReuseValid
        }

        public static Decision Decide(bool loopExists, MediaManifest manifest, AppConfig config)
        {
            if (!loopExists || config == null) return Decision.Build;
            if (manifest == null) return Decision.ReuseStale;

            if (manifest.Width != config.RenderWidth || manifest.Height != config.RenderHeight ||
                manifest.Fps != config.Fps || manifest.Crf != config.Crf ||
                !string.Equals(manifest.Preset ?? "", config.Preset ?? "", StringComparison.OrdinalIgnoreCase) ||
                Math.Abs(manifest.Crossfade - config.CrossfadeSeconds) > 0.001)
            {
                return Decision.ReuseStale;
            }

            return Decision.ReuseValid;
        }
    }
}
