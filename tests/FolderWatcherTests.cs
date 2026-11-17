using System;
using System.IO;
using LiveBackground;
using Xunit;

namespace LiveBackground.Tests
{
    public class FolderWatcherTests
    {
        [Fact]
        public void A_new_watcher_is_idle()
        {
            using (FolderWatcher watcher = NewWatcher())
            {
                Assert.Equal("", watcher.Folder);
                Assert.False(watcher.IsActive);
            }
        }

        [Fact]
        public void Start_creates_the_folder_and_activates()
        {
            using (TempFolder temp = new TempFolder())
            {
                string folder = Path.Combine(temp.Dir, "videos");
                using (FolderWatcher watcher = NewWatcher())
                {
                    Assert.True(watcher.Start(folder));
                    Assert.True(Directory.Exists(folder));
                    Assert.Equal(folder, watcher.Folder);
                    Assert.True(watcher.IsActive);
                }
            }
        }

        [Fact]
        public void Stop_forgets_the_folder()
        {
            using (TempFolder temp = new TempFolder())
            {
                using (FolderWatcher watcher = NewWatcher())
                {
                    watcher.Start(temp.Dir);
                    watcher.Stop();
                    Assert.False(watcher.IsActive);
                    Assert.Equal("", watcher.Folder);
                }
            }
        }

        [Fact]
        public void Start_moves_to_a_new_folder()
        {
            using (TempFolder temp = new TempFolder())
            {
                string first = Path.Combine(temp.Dir, "first");
                string second = Path.Combine(temp.Dir, "second");
                using (FolderWatcher watcher = NewWatcher())
                {
                    watcher.Start(first);
                    watcher.Start(second);
                    Assert.True(watcher.IsActive);
                    Assert.Equal(second, watcher.Folder);
                    Assert.True(Directory.Exists(second));
                }
            }
        }

        [Fact]
        public void Start_remembers_a_folder_it_cannot_watch()
        {
            using (TempFolder temp = new TempFolder())
            {
                // A file where the folder should be: the watcher cannot start,
                // but the folder is still remembered so the caller does not
                // retry it on every tick.
                string file = temp.Write("not-a-folder.txt", "x");
                using (FolderWatcher watcher = NewWatcher())
                {
                    Assert.False(watcher.Start(file));
                    Assert.False(watcher.IsActive);
                    Assert.Equal(file, watcher.Folder);
                }
            }
        }

        private static FolderWatcher NewWatcher()
        {
            return new FolderWatcher("test folder", delegate { return true; },
                delegate { }, delegate { });
        }
    }
}
