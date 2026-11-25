using LiveBackground;
using Xunit;

namespace LiveBackground.Tests
{
    public class StartupCacheTests
    {
        [Fact]
        public void Builds_when_there_is_no_loop()
        {
            Assert.Equal(StartupCache.Decision.Build, StartupCache.Decide(false, ValidManifest(), new AppConfig()));
        }

        [Fact]
        public void Reuses_a_loop_stale_when_the_manifest_is_missing()
        {
            Assert.Equal(StartupCache.Decision.ReuseStale, StartupCache.Decide(true, null, new AppConfig()));
        }

        [Fact]
        public void Reuses_a_valid_loop_without_a_rebuild()
        {
            Assert.Equal(StartupCache.Decision.ReuseValid,
                StartupCache.Decide(true, ValidManifest(), new AppConfig()));
        }

        [Theory]
        [InlineData("width")]
        [InlineData("height")]
        [InlineData("fps")]
        [InlineData("crf")]
        [InlineData("preset")]
        [InlineData("crossfade")]
        public void Reuses_but_rebuilds_when_an_encoded_setting_changed(string field)
        {
            AppConfig config = new AppConfig();
            MediaManifest manifest = ValidManifest();
            switch (field)
            {
                case "width": manifest.Width = config.RenderWidth + 1; break;
                case "height": manifest.Height = config.RenderHeight + 1; break;
                case "fps": manifest.Fps = config.Fps + 1; break;
                case "crf": manifest.Crf = config.Crf + 1; break;
                case "preset": manifest.Preset = "slow"; break;
                case "crossfade": manifest.Crossfade = config.CrossfadeSeconds + 1; break;
            }

            Assert.Equal(StartupCache.Decision.ReuseStale,
                StartupCache.Decide(true, manifest, config));
        }

        private static MediaManifest ValidManifest()
        {
            AppConfig config = new AppConfig();
            return new MediaManifest
            {
                Width = config.RenderWidth,
                Height = config.RenderHeight,
                Fps = config.Fps,
                Crf = config.Crf,
                Preset = config.Preset,
                Crossfade = config.CrossfadeSeconds
            };
        }
    }
}
