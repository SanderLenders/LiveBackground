using System.IO;
using LiveBackground;
using Xunit;

namespace LiveBackground.Tests
{
    // Paths holds process-wide state (ProgramRoot / DataRoot), so every test
    // calls Init for the copy it is checking. No other test class touches
    // Paths, so the shared state cannot leak across tests.
    public class PathsTests
    {
        [Fact]
        public void A_portable_copy_keeps_everything_together()
        {
            using (TempFolder temp = new TempFolder())
            {
                File.WriteAllText(Path.Combine(temp.Dir, "portable.txt"), "portable");
                Paths.Init(temp.Dir);

                Assert.True(Paths.IsPortable);
                Assert.Equal(Path.GetFullPath(temp.Dir), Paths.ProgramRoot);
                Assert.Equal(Path.GetFullPath(temp.Dir), Paths.DataRoot);
            }
        }

        [Fact]
        public void A_non_portable_copy_outside_program_files_uses_its_own_folder()
        {
            using (TempFolder temp = new TempFolder())
            {
                Paths.Init(temp.Dir);

                Assert.False(Paths.IsPortable);
                Assert.Equal(Path.GetFullPath(temp.Dir), Paths.ProgramRoot);
                Assert.Equal(Path.GetFullPath(temp.Dir), Paths.DataRoot);
            }
        }

        [Fact]
        public void ApplyDefaults_redirects_the_default_folders_in_a_portable_copy()
        {
            using (TempFolder temp = new TempFolder())
            {
                File.WriteAllText(Path.Combine(temp.Dir, "portable.txt"), "portable");
                Paths.Init(temp.Dir);

                AppConfig config = new AppConfig();
                config.ExtraScreens.Add(new ScreenSettings());
                Paths.ApplyDefaults(config);

                Assert.Equal(Path.Combine(Paths.ProgramRoot, "videos"), config.VideoFolder);
                Assert.Equal(Path.Combine(Paths.ProgramRoot, "videos"), config.SecondScreenFolder);
                Assert.Equal(Path.Combine(Paths.ProgramRoot, "images", "wallpapers"), config.ImageFolder);
                Assert.Equal(Path.Combine(Paths.ProgramRoot, "videos"), config.ExtraScreens[0].Folder);
                Assert.Equal(Path.Combine(Paths.ProgramRoot, "images", "wallpapers"),
                    config.ExtraScreens[0].ImageFolder);
            }
        }

        [Fact]
        public void ApplyDefaults_leaves_a_chosen_folder_alone()
        {
            using (TempFolder temp = new TempFolder())
            {
                File.WriteAllText(Path.Combine(temp.Dir, "portable.txt"), "portable");
                Paths.Init(temp.Dir);

                AppConfig config = new AppConfig();
                config.VideoFolder = @"D:\My Videos";
                Paths.ApplyDefaults(config);

                Assert.Equal(@"D:\My Videos", config.VideoFolder);
            }
        }

        [Fact]
        public void ApplyDefaults_does_nothing_for_a_non_portable_copy()
        {
            using (TempFolder temp = new TempFolder())
            {
                Paths.Init(temp.Dir); // no portable.txt

                AppConfig config = new AppConfig();
                string before = config.VideoFolder;
                Paths.ApplyDefaults(config);

                Assert.Equal(before, config.VideoFolder);
            }
        }
    }
}
