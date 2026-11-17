using System.Collections.Generic;
using LiveBackground;
using Xunit;

namespace LiveBackground.Tests
{
    public class NaturalComparerTests
    {
        [Theory]
        [InlineData("file2", "file10", -1)]
        [InlineData("file10", "file2", 1)]
        [InlineData("file2", "file2", 0)]
        [InlineData("File2", "file2", 0)]      // case-insensitive
        [InlineData("img007", "img7", 0)]      // leading zeros ignored
        [InlineData("a", "a1", -1)]            // shorter sorts first
        [InlineData("a1", "a", 1)]
        [InlineData("clip", "clip", 0)]
        public void Compare_orders_naturally(string x, string y, int expectedSign)
        {
            int cmp = NaturalComparer.Instance.Compare(x, y);
            Assert.Equal(expectedSign, System.Math.Sign(cmp));
        }

        [Fact]
        public void Compare_treats_null_as_smallest()
        {
            Assert.Equal(0, NaturalComparer.Instance.Compare(null, null));
            Assert.True(NaturalComparer.Instance.Compare(null, "x") < 0);
            Assert.True(NaturalComparer.Instance.Compare("x", null) > 0);
        }

        [Fact]
        public void Sort_puts_clip2_before_clip10()
        {
            List<string> names = new List<string> { "clip10.mp4", "clip2.mp4", "clip1.mp4" };
            names.Sort(NaturalComparer.Instance.Compare);
            Assert.Equal(new[] { "clip1.mp4", "clip2.mp4", "clip10.mp4" }, names);
        }
    }
}
