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
        public void Compute_changes_when_the_mode_override_changes()
        {
            AppConfig changed = ConfigWithOneScreen();
            changed.ExtraScreens[0].ModeOverride = true;
            Assert.NotEqual(ScreenConfigToken.Compute(ConfigWithOneScreen()),
                ScreenConfigToken.Compute(changed));
        }

        [Fact]
        public void Compute_changes_when_only_the_render_settings_change()
        {
            AppConfig changed = ConfigWithOneScreen();
            changed.ExtraScreens[0].RenderWidth = 2560;
            changed.ExtraScreens[0].RenderHeight = 1440;
            Assert.NotEqual(ScreenConfigToken.Compute(ConfigWithOneScreen()),
                ScreenConfigToken.Compute(changed));

            AppConfig quality = ConfigWithOneScreen();
            quality.ExtraScreens[0].Crf = 23;
            Assert.NotEqual(ScreenConfigToken.Compute(ConfigWithOneScreen()),
                ScreenConfigToken.Compute(quality));
        }

        [Theory]
        [InlineData("imagesSameAsMain")]
        [InlineData("imageFolder")]
        [InlineData("imageMode")]
        [InlineData("imageSingle")]
        [InlineData("imagePlaylist")]
        [InlineData("imageIntervalSeconds")]
        [InlineData("imageCrossfadeSeconds")]
        [InlineData("imageFineCrossfadeSteps")]
        [InlineData("imageShuffle")]
        [InlineData("desktopWallpaperMode")]
        [InlineData("desktopWallpaperImage")]
        public void Compute_changes_when_a_picture_or_fallback_setting_changes(string field)
        {
            AppConfig changed = ConfigWithOneScreen();
            ScreenSettings screen = changed.ExtraScreens[0];
            switch (field)
            {
                case "imagesSameAsMain": screen.ImagesSameAsMain = false; break;
                case "imageFolder": screen.ImageFolder = @"D:\pics"; break;
                case "imageMode": screen.ImageMode = "slideshow"; break;
                case "imageSingle": screen.ImageSingle = "other.jpg"; break;
                case "imagePlaylist": screen.ImagePlaylist.Add(new PlaylistEntry("other.jpg", true)); break;
                case "imageIntervalSeconds": screen.ImageIntervalSeconds = 25; break;
                case "imageCrossfadeSeconds": screen.ImageCrossfadeSeconds = 3.5; break;
                case "imageFineCrossfadeSteps": screen.ImageFineCrossfadeSteps = true; break;
                case "imageShuffle": screen.ImageShuffle = true; break;
                case "desktopWallpaperMode": screen.DesktopWallpaperMode = "custom"; break;
                case "desktopWallpaperImage": screen.DesktopWallpaperImage = @"D:\wallpaper.jpg"; break;
            }

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
