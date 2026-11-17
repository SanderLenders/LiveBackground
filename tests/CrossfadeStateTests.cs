using LiveBackground;
using Xunit;

namespace LiveBackground.Tests
{
    public class CrossfadeStateTests
    {
        [Fact]
        public void Step_is_half_a_second_when_fine_and_a_second_otherwise()
        {
            Assert.Equal(1, CrossfadeState.Step(true));
            Assert.Equal(2, CrossfadeState.Step(false));
        }

        [Fact]
        public void Snap_keeps_every_half_second_when_fine()
        {
            Assert.Equal(1, CrossfadeState.Snap(1, true));
            Assert.Equal(7, CrossfadeState.Snap(7, true));
        }

        [Fact]
        public void Snap_rounds_odd_half_seconds_up_when_coarse()
        {
            Assert.Equal(2, CrossfadeState.Snap(1, false));
            Assert.Equal(4, CrossfadeState.Snap(3, false));
            Assert.Equal(10, CrossfadeState.Snap(9, false));
        }

        [Fact]
        public void Snap_leaves_even_values_alone_when_coarse()
        {
            Assert.Equal(0, CrossfadeState.Snap(0, false));
            Assert.Equal(6, CrossfadeState.Snap(6, false));
        }

        [Fact]
        public void Snap_stays_inside_the_track()
        {
            Assert.Equal(0, CrossfadeState.Snap(-5, false));
            Assert.Equal(10, CrossfadeState.Snap(99, false));
            Assert.Equal(10, CrossfadeState.Snap(99, true));
        }

        [Fact]
        public void Text_words_off_under_a_twentieth_of_a_second()
        {
            Assert.Equal("Off (instant)", CrossfadeState.Text(0, "Off (instant)"));
            Assert.Equal("Off", CrossfadeState.Text(0, "Off"));
        }

        [Fact]
        public void Text_shows_one_decimal_place_in_invariant_culture()
        {
            Assert.Equal("0.5 s", CrossfadeState.Text(1, "Off"));
            Assert.Equal("2.0 s", CrossfadeState.Text(4, "Off"));
            Assert.Equal("5.0 s", CrossfadeState.Text(10, "Off"));
        }
    }
}
