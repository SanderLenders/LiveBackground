using System.IO;
using LiveBackground;
using Xunit;

namespace LiveBackground.Tests
{
    public class InstallerPathsTests
    {
        [Fact]
        public void IsInside_accepts_only_descendants()
        {
            string root = Path.Combine(Path.GetPathRoot(@"C:\"), "LiveBackground");
            Assert.True(InstallerPaths.IsInside(root, Path.Combine(root, "Videos")));
            Assert.True(InstallerPaths.IsInside(root, Path.Combine(root, "images", "wallpapers")));
            Assert.False(InstallerPaths.IsInside(root, root));
        }

        [Fact]
        public void IsInside_rejects_outside_paths_even_with_a_shared_prefix()
        {
            string root = Path.Combine(Path.GetPathRoot(@"C:\"), "LiveBackground");
            Assert.False(InstallerPaths.IsInside(root, Path.Combine(Path.GetPathRoot(@"C:\"), "LiveBackground-old", "Videos")));
            Assert.False(InstallerPaths.IsInside(root, Path.Combine(Path.GetPathRoot(@"C:\"), "Videos")));
        }

        [Fact]
        public void IsInside_is_case_insensitive_and_handles_missing_values()
        {
            string root = Path.Combine(Path.GetPathRoot(@"C:\"), "LiveBackground");
            Assert.True(InstallerPaths.IsInside(root.ToLowerInvariant(), Path.Combine(root, "VIDEOS")));
            Assert.False(InstallerPaths.IsInside("", Path.Combine(root, "Videos")));
            Assert.False(InstallerPaths.IsInside(root, ""));
        }
    }
}
