using System;
using System.Globalization;
using System.Text;

namespace LiveBackground
{
    // A fingerprint of every screen's settings, so a save that changed any of
    // them can be told apart from a save that did not. The exact bytes do not
    // matter, only that the same settings always produce the same token and a
    // changed setting produces a different one. Every field that affects a
    // screen's video loop, picture rotation or desktop fallback belongs here;
    // omitting one leaves that change invisible to the runtime handoff.
    public static class ScreenConfigToken
    {
        private static void Add(StringBuilder sb, string value)
        {
            sb.Append(value ?? "").Append('\u0001');
        }

        private static void Add(StringBuilder sb, bool value)
        {
            sb.Append(value ? '1' : '0').Append('\u0001');
        }

        private static void Add(StringBuilder sb, int value)
        {
            sb.Append(value.ToString(CultureInfo.InvariantCulture)).Append('\u0001');
        }

        private static void Add(StringBuilder sb, double value)
        {
            sb.Append(value.ToString("R", CultureInfo.InvariantCulture)).Append('\u0001');
        }

        private static void AddNullable(StringBuilder sb, int? value)
        {
            sb.Append(value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "-").Append('\u0001');
        }

        private static void AddNullable(StringBuilder sb, double? value)
        {
            sb.Append(value.HasValue ? value.Value.ToString("R", CultureInfo.InvariantCulture) : "-").Append('\u0001');
        }

        private static void AddNullable(StringBuilder sb, bool? value)
        {
            sb.Append(value.HasValue ? (value.Value ? '1' : '0') : '-').Append('\u0001');
        }

        private static void AddPlaylist(StringBuilder sb, System.Collections.Generic.List<PlaylistEntry> playlist)
        {
            if (playlist == null)
            {
                sb.Append("none").Append('\u0002');
                return;
            }
            foreach (PlaylistEntry entry in playlist)
            {
                if (entry == null) continue;
                Add(sb, entry.Name ?? "");
                Add(sb, entry.Enabled);
            }
            sb.Append('\u0002');
        }

        public static string Compute(AppConfig config)
        {
            if (config == null || config.ExtraScreens == null) return "";
            StringBuilder sb = new StringBuilder();
            foreach (ScreenSettings screen in config.ExtraScreens)
            {
                if (screen == null) continue;

                Add(sb, screen.Device ?? "");
                Add(sb, screen.ModeOverride);
                Add(sb, screen.SameAsMain);

                Add(sb, screen.Folder ?? "");
                Add(sb, screen.PlaybackMode ?? "");
                Add(sb, screen.SingleClip ?? "");
                AddPlaylist(sb, screen.Playlist);
                AddNullable(sb, screen.CrossfadeSeconds);
                AddNullable(sb, screen.FineCrossfadeSteps);
                AddNullable(sb, screen.RenderWidth);
                AddNullable(sb, screen.RenderHeight);
                AddNullable(sb, screen.Fps);
                AddNullable(sb, screen.Crf);

                Add(sb, screen.ImagesSameAsMain);
                Add(sb, screen.ImageFolder ?? "");
                Add(sb, screen.ImageMode ?? "");
                Add(sb, screen.ImageSingle ?? "");
                AddPlaylist(sb, screen.ImagePlaylist);
                Add(sb, screen.ImageIntervalSeconds);
                Add(sb, screen.ImageCrossfadeSeconds);
                Add(sb, screen.ImageFineCrossfadeSteps);
                Add(sb, screen.ImageShuffle);

                Add(sb, screen.DesktopWallpaperMode ?? "");
                Add(sb, screen.DesktopWallpaperImage ?? "");
            }
            return sb.ToString();
        }
    }
}
