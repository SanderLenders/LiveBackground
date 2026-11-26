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
        public void Slider_minimums_match_the_available_steps()
        {
            Assert.Equal(0.5, FadeSteps.VideoFineMinimum);
            Assert.Equal(1.0, FadeSteps.VideoCoarseMinimum);
            Assert.Equal(0.0, FadeSteps.ImageMinimum);
        }

        [Fact]
        public void Snap_rounds_to_whole_second_steps_from_the_slider_minimum()
        {
            Assert.Equal(2.0, FadeSteps.Snap(2.4, 0.0, 5.0, false));
            Assert.Equal(3.0, FadeSteps.Snap(2.5, 0.0, 5.0, false));
            // Coarse video steps start at 1.0, so the default stays on the grid.
            Assert.Equal(3.0, FadeSteps.Snap(3.0, FadeSteps.VideoCoarseMinimum, 10.0, false));
            Assert.Equal(1.0, FadeSteps.Snap(0.7, FadeSteps.VideoCoarseMinimum, 10.0, false));
            Assert.Equal(4.0, FadeSteps.Snap(3.5, FadeSteps.VideoCoarseMinimum, 10.0, false));
        }

        [Fact]
        public void Snap_keeps_half_second_values_when_fine_is_checked()
        {
            Assert.Equal(2.5, FadeSteps.Snap(2.5, FadeSteps.VideoFineMinimum, 5.0, true));
            Assert.Equal(9.5, FadeSteps.Snap(9.5, FadeSteps.VideoFineMinimum, 10.0, true));
            Assert.Equal(0.5, FadeSteps.Snap(0.7, FadeSteps.VideoFineMinimum, 10.0, true));
        }

        [Fact]
        public void Snap_uses_zero_as_the_image_slider_minimum()
        {
            Assert.Equal(2.5, FadeSteps.Snap(2.5, FadeSteps.ImageMinimum, 5.0, true));
            Assert.Equal(3.0, FadeSteps.Snap(2.7, FadeSteps.ImageMinimum, 5.0, false));
            Assert.Equal(0.0, FadeSteps.Snap(-1, FadeSteps.ImageMinimum, 5.0, true));
        }

        [Fact]
        public void Snap_clamps_to_the_slider_range()
        {
            Assert.Equal(0.0, FadeSteps.Snap(-2, 0.0, 5.0, true));
            Assert.Equal(5.0, FadeSteps.Snap(99, 0.0, 5.0, true));
        }
    }
}
