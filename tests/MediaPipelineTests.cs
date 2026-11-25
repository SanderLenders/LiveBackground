using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiveBackground;
using Xunit;

namespace LiveBackground.Tests
{
    public class MediaPipelineTests
    {
        [Theory]
        [InlineData(null, "screen")]
        [InlineData("", "screen")]
        [InlineData(@"\\.\DISPLAY2", "____DISPLAY2")]
        [InlineData(@"\\.\DISPLAY1", "____DISPLAY1")]
        public void ScreenKey_keeps_only_letters_and_digits(string device, string expected)
        {
            Assert.Equal(expected, MediaPipeline.ScreenKey(device));
        }

        [Theory]
        [InlineData("movie.mp4", true)]
        [InlineData("movie.MKV", true)]
        [InlineData("clip.avi", true)]
        [InlineData("notes.txt", false)]
        [InlineData("cover.jpg", false)]
        [InlineData("archive.zip", false)]
        [InlineData("noextension", false)]
        public void IsVideo_uses_the_ignored_extension_list(string name, bool expected)
        {
            Assert.Equal(expected, MediaPipeline.IsVideo(name));
        }

        [Fact]
        public void ResolveClips_single_mode_returns_the_chosen_clip()
        {
            using (TempFolder temp = new TempFolder())
            {
                temp.Write("clip1.mp4", "1");
                temp.Write("clip2.mp4", "2");

                AppConfig config = new AppConfig();
                config.PlaybackMode = "single";
                config.SingleClip = "clip2.mp4";

                List<string> clips = MediaPipeline.ResolveClips(temp.Dir, config);
                Assert.Single(clips);
                Assert.Equal("clip2.mp4", Path.GetFileName(clips[0]));
            }
        }

        [Fact]
        public void ResolveClips_single_mode_falls_back_to_the_first_clip()
        {
            using (TempFolder temp = new TempFolder())
            {
                temp.Write("clip1.mp4", "1");
                temp.Write("clip2.mp4", "2");

                AppConfig config = new AppConfig();
                config.PlaybackMode = "single";
                config.SingleClip = "missing.mp4";

                List<string> clips = MediaPipeline.ResolveClips(temp.Dir, config);
                Assert.Single(clips);
                Assert.Equal("clip1.mp4", Path.GetFileName(clips[0]));
            }
        }

        [Fact]
        public void ResolveClips_playlist_mode_keeps_enabled_clips_in_order()
        {
            using (TempFolder temp = new TempFolder())
            {
                temp.Write("clip1.mp4", "1");
                temp.Write("clip2.mp4", "2");
                temp.Write("clip3.mp4", "3");

                AppConfig config = new AppConfig();
                config.PlaybackMode = "playlist";
                config.Playlist = new List<PlaylistEntry>
                {
                    new PlaylistEntry("clip3.mp4", true),
                    new PlaylistEntry("clip1.mp4", true),
                    new PlaylistEntry("clip2.mp4", false),   // switched off
                    new PlaylistEntry("missing.mp4", true),  // not in the folder
                };

                List<string> clips = MediaPipeline.ResolveClips(temp.Dir, config);
                Assert.Equal(new[] { "clip3.mp4", "clip1.mp4" }, clips.Select(Path.GetFileName).ToArray());
            }
        }

        [Fact]
        public void ResolveClips_playlist_mode_with_an_empty_list_returns_everything()
        {
            using (TempFolder temp = new TempFolder())
            {
                temp.Write("clip1.mp4", "1");
                temp.Write("clip2.mp4", "2");
                temp.Write("clip10.mp4", "10");

                AppConfig config = new AppConfig();
                config.PlaybackMode = "playlist";
                config.Playlist = new List<PlaylistEntry>();

                List<string> clips = MediaPipeline.ResolveClips(temp.Dir, config);
                Assert.Equal(new[] { "clip1.mp4", "clip2.mp4", "clip10.mp4" },
                    clips.Select(Path.GetFileName).ToArray());
            }
        }

        [Fact]
        public void ResolveClips_returns_nothing_for_a_missing_folder()
        {
            AppConfig config = new AppConfig();
            Assert.Empty(MediaPipeline.ResolveClips(MissingFolder(), config));
        }

        [Fact]
        public void Signature_is_stable_and_looks_like_a_sha256()
        {
            using (TempFolder temp = new TempFolder())
            {
                temp.Write("clip1.mp4", "content");
                AppConfig config = new AppConfig();

                string first = MediaPipeline.Signature(temp.Dir, config);
                string second = MediaPipeline.Signature(temp.Dir, config);

                Assert.Equal(first, second);
                Assert.Equal(64, first.Length);
                Assert.Matches("^[0-9a-f]{64}$", first);
            }
        }

        [Fact]
        public void Signature_changes_with_the_render_settings()
        {
            using (TempFolder temp = new TempFolder())
            {
                temp.Write("clip1.mp4", "content");
                AppConfig config = new AppConfig();
                string baseline = MediaPipeline.Signature(temp.Dir, config);

                config.Fps = 60;
                Assert.NotEqual(baseline, MediaPipeline.Signature(temp.Dir, config));
                config.Fps = 30;

                config.RenderWidth = 1280;
                Assert.NotEqual(baseline, MediaPipeline.Signature(temp.Dir, config));
                config.RenderWidth = 1920;

                config.Crf = 20;
                Assert.NotEqual(baseline, MediaPipeline.Signature(temp.Dir, config));
                config.Crf = 18;

                config.Preset = "slow";
                Assert.NotEqual(baseline, MediaPipeline.Signature(temp.Dir, config));
                config.Preset = "veryfast";

                config.CrossfadeSeconds = 5.0;
                Assert.NotEqual(baseline, MediaPipeline.Signature(temp.Dir, config));
                config.CrossfadeSeconds = 3.0;

                config.PlaybackMode = "playlist";
                Assert.NotEqual(baseline, MediaPipeline.Signature(temp.Dir, config));
                config.PlaybackMode = "single";

                Assert.Equal(baseline, MediaPipeline.Signature(temp.Dir, config));
            }
        }

        [Fact]
        public void IsScreenCacheValid_rejects_a_stale_render_quality()
        {
            using (TempFolder temp = new TempFolder())
            {
                string device = @"\\.\DISPLAY2";
                string loopName = "screen-loop.mp4";
                Directory.CreateDirectory(MediaPipeline.LoopsDir(temp.Dir));
                temp.Write(Path.Combine("cache", "loops", loopName), "loop");
                AppConfig config = new AppConfig();
                MediaManifest manifest = new MediaManifest
                {
                    Signature = MediaPipeline.Signature(temp.Dir, config),
                    Width = config.RenderWidth,
                    Height = config.RenderHeight,
                    Fps = config.Fps,
                    Crossfade = config.CrossfadeSeconds,
                    Crf = config.Crf,
                    Preset = config.Preset,
                    LoopFile = Path.Combine("loops", loopName),
                    FrameFile = ""
                };
                JsonStore.SerializeFile(MediaPipeline.ScreenManifestPath(temp.Dir, device), manifest);

                MediaManifest check;
                Assert.True(MediaPipeline.IsScreenCacheValid(temp.Dir, config, device, out check, false));

                config.Crf = 23;
                Assert.False(MediaPipeline.IsScreenCacheValid(temp.Dir, config, device, out check, false));
                config.Crf = 18;

                config.Preset = "slow";
                Assert.False(MediaPipeline.IsScreenCacheValid(temp.Dir, config, device, out check, false));
            }
        }

        [Fact]
        public void IsScreenCacheValid_rejects_a_missing_loop_file()
        {
            using (TempFolder temp = new TempFolder())
            {
                string device = @"\\.\DISPLAY2";
                AppConfig config = new AppConfig();
                MediaManifest manifest = new MediaManifest
                {
                    Signature = MediaPipeline.Signature(temp.Dir, config),
                    Width = config.RenderWidth,
                    Height = config.RenderHeight,
                    Fps = config.Fps,
                    Crossfade = config.CrossfadeSeconds,
                    Crf = config.Crf,
                    Preset = config.Preset,
                    LoopFile = Path.Combine("loops", "gone.mp4"),
                    FrameFile = ""
                };
                JsonStore.SerializeFile(MediaPipeline.ScreenManifestPath(temp.Dir, device), manifest);

                MediaManifest check;
                Assert.False(MediaPipeline.IsScreenCacheValid(temp.Dir, config, device, out check, false));
            }
        }

        [Fact]
        public void Build_without_force_adopts_a_cached_loop_for_the_screen_pointer()
        {
            using (TempFolder temp = new TempFolder())
            {
                string device = @"\\.\DISPLAY2";
                string loopName = "cached.mp4";
                string indexName = Path.Combine("cache", "loops");
                Directory.CreateDirectory(Path.Combine(temp.Dir, indexName));
                temp.Write(Path.Combine(indexName, loopName), "loop");
                Directory.CreateDirectory(Path.Combine(temp.Dir, "videos"));
                temp.Write(Path.Combine("videos", "clip1.mp4"), "one");

                AppConfig config = new AppConfig();
                config.VideoFolder = "videos";
                string signature = MediaPipeline.Signature(Path.Combine(temp.Dir, "videos"), config);
                MediaManifest cached = new MediaManifest
                {
                    Signature = signature,
                    LoopFile = Path.Combine("loops", loopName),
                    FrameFile = "",
                    OutputDuration = 12.5
                };
                JsonStore.SerializeFile(MediaPipeline.CacheIndexPath(temp.Dir, signature), cached);

                string pointer = MediaPipeline.ScreenManifestPath(temp.Dir, device);
                BuildResult result = MediaPipeline.Build(temp.Dir, config,
                    message => { }, progress => { }, null, false, false, pointer);

                Assert.True(result.Success, result.Message);
                Assert.Contains("cached loop ready", result.Message);
                MediaManifest adopted = JsonStore.DeserializeFile<MediaManifest>(pointer);
                Assert.NotNull(adopted);
                Assert.Equal(cached.LoopFile, adopted.LoopFile);
            }
        }

        [Fact]
        public void Signature_changes_when_a_clip_changes()
        {
            using (TempFolder temp = new TempFolder())
            {
                temp.Write("clip1.mp4", "one");
                AppConfig config = new AppConfig();
                string before = MediaPipeline.Signature(temp.Dir, config);

                temp.Write("clip1.mp4", "two-and-longer");

                Assert.NotEqual(before, MediaPipeline.Signature(temp.Dir, config));
            }
        }

        [Fact]
        public void Signature_does_not_throw_for_a_missing_folder()
        {
            AppConfig config = new AppConfig();
            string signature = MediaPipeline.Signature(MissingFolder(), config);
            Assert.Equal(64, signature.Length);
        }

        private static string MissingFolder()
        {
            return Path.Combine(Path.GetTempPath(), "lb-missing-" + Guid.NewGuid().ToString("N"));
        }
    }
}
