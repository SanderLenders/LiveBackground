using LiveBackground;
using Xunit;

namespace LiveBackground.Tests
{
    public class WatchdogShellTests
    {
        [Fact]
        public void ShouldRestoreShell_ignores_a_user_quit_flag()
        {
            Assert.False(WatchdogShell.ShouldRestoreShell(true, false));
            Assert.False(WatchdogShell.ShouldRestoreShell(true, true));
        }

        [Fact]
        public void ShouldRestoreShell_ignores_a_healthy_running_app()
        {
            Assert.False(WatchdogShell.ShouldRestoreShell(false, true));
        }

        [Fact]
        public void ShouldRestoreShell_restores_only_before_a_restart()
        {
            Assert.True(WatchdogShell.ShouldRestoreShell(false, false));
        }
    }
}
