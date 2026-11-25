using LiveBackground;
using Xunit;

namespace LiveBackground.Tests
{
    public class FadeStepsTests
    {
        [Fact]
        public void Step_uses_half_seconds_only_when_fine_is_checked()
        {
            Assert.Equal(1.0, FadeSteps.Step(false));
            Assert.Equal(0.5, FadeSteps.Step(true));
        }

        [Fact]
        public void Snap_rounds_to_whole_second_steps_from_the_slider_minimum()
        {
            Assert.Equal(2.0, FadeSteps.Snap(2.4, 0.0, 5.0, false));
            Assert.Equal(3.0, FadeSteps.Snap(2.5, 0.0, 5.0, false));
            Assert.Equal(0.5, FadeSteps.Snap(0.7, 0.5, 10.0, false));
        }

        [Fact]
        public void Snap_keeps_half_second_values_when_fine_is_checked()
        {
            Assert.Equal(2.5, FadeSteps.Snap(2.5, 0.0, 5.0, true));
            Assert.Equal(9.5, FadeSteps.Snap(9.5, 0.5, 10.0, true));
        }

        [Fact]
        public void Snap_clamps_to_the_slider_range()
        {
            Assert.Equal(0.0, FadeSteps.Snap(-2, 0.0, 5.0, true));
            Assert.Equal(5.0, FadeSteps.Snap(99, 0.0, 5.0, true));
        }
    }
}
