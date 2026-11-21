using LiveBackground;
using Xunit;

namespace LiveBackground.Tests
{
    public class ScreensaverIdleTests
    {
        [Theory]
        [InlineData(90, 90)]
        [InlineData(1, 1)]
        public void NormalizeTimeout_keeps_positive_values(int configured, int expected)
        {
            Assert.Equal(expected, ScreensaverIdle.NormalizeTimeout(configured));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-5)]
        public void NormalizeTimeout_falls_back_for_unusable_values(int configured)
        {
            Assert.Equal(ScreensaverIdle.DefaultTimeoutSeconds,
                ScreensaverIdle.NormalizeTimeout(configured));
        }

        [Fact]
        public void IsDue_waits_until_idle_timeout_has_elapsed()
        {
            Assert.False(ScreensaverIdle.IsDue(false, true, 299999, 300));
            Assert.True(ScreensaverIdle.IsDue(false, true, 300000, 300));
            Assert.True(ScreensaverIdle.IsDue(false, true, 900000, 300));
        }

        [Theory]
        [InlineData(true, true, 999999)]
        [InlineData(false, false, 999999)]
        public void IsDue_stays_off_when_paused_or_not_running(bool paused, bool running, long idle)
        {
            Assert.False(ScreensaverIdle.IsDue(paused, running, idle, 300));
        }

        [Fact]
        public void IsDue_never_triggers_without_a_positive_timeout()
        {
            Assert.False(ScreensaverIdle.IsDue(false, true, 999999, 0));
            Assert.False(ScreensaverIdle.IsDue(false, true, 999999, -1));
        }
    }
}
