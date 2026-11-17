using System.Collections.Generic;
using System.Drawing;
using LiveBackground;
using Xunit;

namespace LiveBackground.Tests
{
    public class ScreenTextTests
    {
        [Fact]
        public void LabelForIndex_names_the_extra_screens()
        {
            Assert.Equal("second screen", ScreenText.LabelForIndex(1));
            Assert.Equal("third screen", ScreenText.LabelForIndex(2));
            Assert.Equal("screen 1", ScreenText.LabelForIndex(0));
            Assert.Equal("screen 4", ScreenText.LabelForIndex(3));
        }

        [Fact]
        public void LogName_sanitises_the_device()
        {
            Assert.Equal("mpv-.log", ScreenText.LogName(null));
            Assert.Equal("mpv-.log", ScreenText.LogName(""));
            Assert.Equal("mpv-DISPLAY1.log", ScreenText.LogName("DISPLAY1"));
            // Backslashes and the dot are not letters or digits, so each becomes
            // an underscore.
            Assert.Equal("mpv-____DISPLAY1.log", ScreenText.LogName("\\\\.\\DISPLAY1"));
        }

        [Fact]
        public void DescribeAll_is_none_without_screens()
        {
            Assert.Equal("none", ScreenText.DescribeAll(null, false));
            Assert.Equal("none", ScreenText.DescribeAll(new List<ScreenInfo>(), false));
        }

        [Fact]
        public void DescribeAll_joins_the_screens_with_commas()
        {
            List<ScreenInfo> screens = new List<ScreenInfo>
            {
                Screen("\\\\.\\DISPLAY1", 0, 1920, true),
                Screen("\\\\.\\DISPLAY2", 1920, 3840, false)
            };
            Assert.Equal(
                "\\\\.\\DISPLAY1 1920x1080 at 0,0 (primary), \\\\.\\DISPLAY2 3840x1080 at 1920,0",
                ScreenText.DescribeAll(screens, false));
        }

        [Fact]
        public void DescribeAll_notes_windows_duplication()
        {
            List<ScreenInfo> screens = new List<ScreenInfo> { Screen("\\\\.\\DISPLAY1", 0, 1920, true) };
            Assert.EndsWith(" (Windows is duplicating)", ScreenText.DescribeAll(screens, true));
            Assert.DoesNotContain("duplicating", ScreenText.DescribeAll(screens, false));
        }

        [Fact]
        public void DescribeAll_notes_when_more_than_three_screens()
        {
            List<ScreenInfo> screens = new List<ScreenInfo>
            {
                Screen("A", 0, 100, true),
                Screen("B", 100, 100, false),
                Screen("C", 200, 100, false),
                Screen("D", 300, 100, false)
            };
            string text = ScreenText.DescribeAll(screens, false);
            Assert.EndsWith(" (only the first three are used)", text);
            // Every screen is still described; only the note changes.
            Assert.Contains("D 100x1080", text);
        }

        private static ScreenInfo Screen(string device, int x, int width, bool primary)
        {
            ScreenInfo screen = new ScreenInfo();
            screen.Device = device;
            screen.Bounds = new Rectangle(x, 0, width, 1080);
            screen.Primary = primary;
            return screen;
        }
    }
}
