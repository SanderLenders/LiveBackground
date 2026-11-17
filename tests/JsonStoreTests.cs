using System;
using System.IO;
using System.Text;
using LiveBackground;
using Xunit;

namespace LiveBackground.Tests
{
    public class JsonStoreTests
    {
        [Fact]
        public void Round_trips_a_value_through_a_file()
        {
            using (TempFolder temp = new TempFolder())
            {
                string path = Path.Combine(temp.Dir, "entry.json");
                JsonStore.SerializeFile(path, new PlaylistEntry("clip.mp4", true));
                PlaylistEntry loaded = JsonStore.DeserializeFile<PlaylistEntry>(path);
                Assert.Equal("clip.mp4", loaded.Name);
                Assert.True(loaded.Enabled);
            }
        }

        [Fact]
        public void Creates_the_directory_when_it_is_missing()
        {
            using (TempFolder temp = new TempFolder())
            {
                string path = Path.Combine(temp.Dir, "a", "b", "entry.json");
                JsonStore.SerializeFile(path, new PlaylistEntry("clip.mp4", false));
                Assert.True(File.Exists(path));
            }
        }

        [Fact]
        public void Overwrites_an_existing_file()
        {
            using (TempFolder temp = new TempFolder())
            {
                string path = Path.Combine(temp.Dir, "entry.json");
                JsonStore.SerializeFile(path, new PlaylistEntry("first.mp4", true));
                JsonStore.SerializeFile(path, new PlaylistEntry("second.mp4", false));
                PlaylistEntry loaded = JsonStore.DeserializeFile<PlaylistEntry>(path);
                Assert.Equal("second.mp4", loaded.Name);
                Assert.False(loaded.Enabled);
            }
        }

        [Fact]
        public void Leaves_no_temporary_files_behind()
        {
            using (TempFolder temp = new TempFolder())
            {
                JsonStore.SerializeFile(Path.Combine(temp.Dir, "entry.json"),
                    new PlaylistEntry("clip.mp4", true));
                Assert.Empty(Directory.GetFiles(temp.Dir, "*.tmp"));
            }
        }

        [Fact]
        public void DeserializeFile_skips_a_utf8_bom()
        {
            using (TempFolder temp = new TempFolder())
            {
                string path = Path.Combine(temp.Dir, "bom.json");
                byte[] json = Encoding.UTF8.GetBytes("{\"name\":\"clip.mp4\",\"enabled\":true}");
                byte[] bytes = new byte[json.Length + 3];
                bytes[0] = 0xEF;
                bytes[1] = 0xBB;
                bytes[2] = 0xBF;
                Array.Copy(json, 0, bytes, 3, json.Length);
                File.WriteAllBytes(path, bytes);

                PlaylistEntry loaded = JsonStore.DeserializeFile<PlaylistEntry>(path);
                Assert.Equal("clip.mp4", loaded.Name);
                Assert.True(loaded.Enabled);
            }
        }

        [Fact]
        public void Deserialize_reads_a_json_string()
        {
            PlaylistEntry loaded = JsonStore.Deserialize<PlaylistEntry>(
                "{\"name\":\"a.mp4\",\"enabled\":false}");
            Assert.Equal("a.mp4", loaded.Name);
            Assert.False(loaded.Enabled);
        }
    }
}
