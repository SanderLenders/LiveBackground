using LiveBackground;
using Xunit;

namespace LiveBackground.Tests
{
    public class CommandLineTests
    {
        [Fact]
        public void HasArg_is_false_for_no_arguments()
        {
            Assert.False(CommandLine.HasArg(null, "--stop"));
            Assert.False(CommandLine.HasArg(new string[0], "--stop"));
        }

        [Fact]
        public void HasArg_finds_a_switch_in_any_position()
        {
            Assert.True(CommandLine.HasArg(new[] { "--stop" }, "--stop"));
            Assert.True(CommandLine.HasArg(new[] { "first", "--stop", "last" }, "--stop"));
        }

        [Fact]
        public void HasArg_ignores_case()
        {
            Assert.True(CommandLine.HasArg(new[] { "/REMOVE" }, "/remove"));
            Assert.True(CommandLine.HasArg(new[] { "--Stop" }, "--stop"));
        }

        [Fact]
        public void HasArg_is_false_when_the_switch_is_absent()
        {
            Assert.False(CommandLine.HasArg(new[] { "--settings", "--pause" }, "--stop"));
            Assert.False(CommandLine.HasArg(new[] { "--stopwatch" }, "--stop"));
        }
    }
}
