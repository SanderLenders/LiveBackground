using System;
using System.Text;

namespace LiveBackground
{
    // A fingerprint of every screen's settings, so a save that changed any of
    // them can be told apart from a save that did not. The exact bytes do not
    // matter, only that the same settings always produce the same token and a
    // changed setting produces a different one.
    public static class ScreenConfigToken
    {
        public static string Compute(AppConfig config)
        {
            if (config == null || config.ExtraScreens == null) return "";
            StringBuilder sb = new StringBuilder();
            foreach (ScreenSettings screen in config.ExtraScreens)
            {
                if (screen == null) continue;
                sb.Append(screen.Device ?? "").Append('\u0001')
                  .Append(screen.ModeOverride ? '1' : '0').Append('\u0001')
                  .Append(screen.SameAsMain ? '1' : '0').Append('\u0001')
                  .Append(screen.Folder ?? "").Append('\u0001')
                  .Append(screen.PlaybackMode ?? "").Append('\u0001')
                  .Append(screen.SingleClip ?? "").Append('\u0001')
                  .Append(screen.CrossfadeSeconds.HasValue
                      ? screen.CrossfadeSeconds.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                      : "").Append('\u0001')
                  .Append(screen.FineCrossfadeSteps.HasValue
                      ? (screen.FineCrossfadeSteps.Value ? '1' : '0') : '-').Append('\u0001');
                if (screen.Playlist != null)
                {
                    foreach (PlaylistEntry entry in screen.Playlist)
                    {
                        if (entry == null) continue;
                        sb.Append(entry.Name).Append(':').Append(entry.Enabled ? '1' : '0').Append(';');
                    }
                }
                sb.Append('\u0002');
            }
            return sb.ToString();
        }
    }
}
