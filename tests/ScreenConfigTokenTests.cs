using LiveBackground;
using Xunit;

namespace LiveBackground.Tests
{
    public class ScreenConfigTokenTests
    {
        [Fact]
        public void Compute_is_empty_without_screens()
        {
            Assert.Equal("", ScreenConfigToken.Compute(null));
            Assert.Equal("", ScreenConfigToken.Compute(new AppConfig()));
        }

        [Fact]
        public void Compute_is_stable_for_equal_settings()
        {
            Assert.Equal(ScreenConfigToken.Compute(ConfigWithOneScreen()),
                ScreenConfigToken.Compute(ConfigWithOneScreen()));
        }

        [Fact]
        public void Compute_changes_when_a_screen_setting_changes()
        {
            AppConfig changed = ConfigWithOneScreen();
            changed.ExtraScreens[0].SingleClip = "another.mp4";
            Assert.NotEqual(ScreenConfigToken.Compute(ConfigWithOneScreen()),
                ScreenConfigToken.Compute(changed));
        }

        [Fact]
        public void Compute_changes_when_the_playlist_changes()
        {
            AppConfig changed = ConfigWithOneScreen();
            changed.ExtraScreens[0].Playlist.Add(new PlaylistEntry("extra.mp4", true));
            Assert.NotEqual(ScreenConfigToken.Compute(ConfigWithOneScreen()),
                ScreenConfigToken.Compute(changed));
        }

        [Fact]
        public void Compute_skips_null_screens()
        {
            AppConfig config = new AppConfig();
            config.ExtraScreens.Add(null);
            Assert.Equal("", ScreenConfigToken.Compute(config));
        }

        private static AppConfig ConfigWithOneScreen()
        {
            AppConfig config = new AppConfig();
            ScreenSettings screen = new ScreenSettings();
            screen.Device = "\\\\.\\DISPLAY2";
            screen.Playlist.Add(new PlaylistEntry("clip.mp4", true));
            config.ExtraScreens.Add(screen);
            return config;
        }
    }
}
