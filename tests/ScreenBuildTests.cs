using LiveBackground;
using Xunit;

namespace LiveBackground.Tests
{
    public class ScreenBuildTests
    {
        [Fact]
        public void Own_video_without_a_pointer_needs_an_immediate_check()
        {
            Assert.True(ScreenBuild.NeedsImmediateCheck(false, false, false, ""));
            Assert.True(ScreenBuild.NeedsImmediateCheck(false, false, false, null));
        }

        [Fact]
        public void Own_video_with_a_cached_pointer_does_not_need_a_check()
        {
            Assert.False(ScreenBuild.NeedsImmediateCheck(false, false, false, @"C:\cache\loop.mp4"));
        }

        [Fact]
        public void Mirroring_or_images_never_needs_a_video_build()
        {
            Assert.False(ScreenBuild.NeedsImmediateCheck(false, true, false, ""));
            Assert.False(ScreenBuild.NeedsImmediateCheck(true, false, false, ""));
            Assert.False(ScreenBuild.NeedsImmediateCheck(false, false, true, ""));
        }
    }
}
