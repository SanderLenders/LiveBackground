using LiveBackground;
using Xunit;

namespace LiveBackground.Tests
{
    public class ScreensaverIdleTests
    {
        [Fact]
        public void ResolveTimeout_uses_the_Windows_value_when_it_is_enabled()
        {
            Assert.Equal(90, ScreensaverIdle.ResolveTimeout(true, 90));
        }

        [Theory]
        [InlineData(false, 90)]
        [InlineData(true, 0)]
        [InlineData(true, -5)]
        [InlineData(false, 0)]
        public void ResolveTimeout_falls_back_when_Windows_has_no_usable_value(
            bool active, int configured)
        {
            Assert.Equal(ScreensaverIdle.DefaultTimeoutSeconds,
                ScreensaverIdle.ResolveTimeout(active, configured));
        }

        [Fact]
        public void ShouldHide_waits_until_the_idle_timeout_has_elapsed()
        {
            Assert.False(ScreensaverIdle.ShouldHide(true, false, true, 299999, 300));
            Assert.True(ScreensaverIdle.ShouldHide(true, false, true, 300000, 300));
            Assert.True(ScreensaverIdle.ShouldHide(true, false, true, 900000, 300));
        }

        [Theory]
        [InlineData(false, false, true, 999999)]
        [InlineData(true, true, true, 999999)]
        [InlineData(true, false, false, 999999)]
        public void ShouldHide_stays_off_when_the_mode_or_the_player_is_not_active(
            bool videoBackground, bool paused, bool running, long idle)
        {
            Assert.False(ScreensaverIdle.ShouldHide(videoBackground, paused, running, idle, 300));
        }

        [Fact]
        public void ShouldHide_never_triggers_without_a_positive_timeout()
        {
            Assert.False(ScreensaverIdle.ShouldHide(true, false, true, 999999, 0));
            Assert.False(ScreensaverIdle.ShouldHide(true, false, true, 999999, -1));
        }
    }
}
