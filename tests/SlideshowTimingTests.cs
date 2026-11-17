using LiveBackground;
using Xunit;

namespace LiveBackground.Tests
{
    public class SlideshowTimingTests
    {
        [Fact]
        public void ImageIntervalSeconds_defaults_when_config_is_missing()
        {
            Assert.Equal(10, SlideshowTiming.ImageIntervalSeconds(null));
        }

        [Fact]
        public void ImageIntervalSeconds_uses_the_configured_value()
        {
            AppConfig config = new AppConfig();
            config.ImageIntervalSeconds = 30;
            Assert.Equal(30, SlideshowTiming.ImageIntervalSeconds(config));
        }

        [Theory]
        [InlineData(0, 10)]        // below the minimum falls back to the default
        [InlineData(-5, 10)]
        [InlineData(1, 1)]         // the minimum is kept
        [InlineData(86400, 86400)] // the maximum is kept
        [InlineData(100000, 86400)]// above the maximum is clamped
        public void ImageIntervalSeconds_clamps_to_the_allowed_range(int configured, int expected)
        {
            AppConfig config = new AppConfig();
            config.ImageIntervalSeconds = configured;
            Assert.Equal(expected, SlideshowTiming.ImageIntervalSeconds(config));
        }

        [Fact]
        public void ImageCrossfadeSeconds_defaults_when_config_is_missing()
        {
            Assert.Equal(2.0, SlideshowTiming.ImageCrossfadeSeconds(null));
        }

        [Theory]
        [InlineData(-1.0, 0.0)]
        [InlineData(0.0, 0.0)]
        [InlineData(2.5, 2.5)]
        [InlineData(5.0, 5.0)]
        [InlineData(7.0, 5.0)]
        public void ImageCrossfadeSeconds_clamps_to_the_allowed_range(double configured, double expected)
        {
            AppConfig config = new AppConfig();
            config.ImageCrossfadeSeconds = configured;
            Assert.Equal(expected, SlideshowTiming.ImageCrossfadeSeconds(config));
        }

        [Fact]
        public void ImageCrossfadeSeconds_treats_NaN_as_zero()
        {
            AppConfig config = new AppConfig();
            config.ImageCrossfadeSeconds = double.NaN;
            Assert.Equal(0.0, SlideshowTiming.ImageCrossfadeSeconds(config));
        }
    }
}
