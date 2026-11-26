using System.Collections.Generic;
using System.IO;
using LiveBackground;
using Xunit;

namespace LiveBackground.Tests
{
    public class AppConfigTests
    {
        [Fact]
        public void Normalize_canonicalizes_the_mode_strings()
        {
            AppConfig c = new AppConfig();
            c.WallpaperMode = "IMAGES";
            c.ImageMode = "SLIDESHOW";
            c.PlaybackMode = "PLAYLIST";
            c.SecondScreenPlaybackMode = "garbage";
            c.DesktopWallpaperMode = "CUSTOM";
            c.LockScreenMode = "FOLLOWIMAGE";
            c.Normalize();

            Assert.Equal("images", c.WallpaperMode);
            Assert.Equal("slideshow", c.ImageMode);
            Assert.Equal("playlist", c.PlaybackMode);
            Assert.Equal("playlist", c.SecondScreenPlaybackMode);
            Assert.Equal("custom", c.DesktopWallpaperMode);
            Assert.Equal("frame", c.LockScreenMode);
        }

        [Fact]
        public void Normalize_falls_back_to_video_for_an_unknown_wallpaper_mode()
        {
            AppConfig c = new AppConfig();
            c.WallpaperMode = "something-else";
            c.Normalize();
            Assert.Equal("video", c.WallpaperMode);
        }

        [Fact]
        public void Normalize_remaps_the_removed_lock_screen_modes()
        {
            foreach (string removed in new[] { "", "previous", "followimage", "PREVIOUS" })
            {
                AppConfig c = new AppConfig();
                c.LockScreenMode = removed;
                c.Normalize();
                Assert.Equal("frame", c.LockScreenMode);
            }
        }

        [Fact]
        public void Normalize_clamps_the_low_ends()
        {
            AppConfig c = new AppConfig();
            c.ImageIntervalSeconds = 0;
            c.ImageCrossfadeSeconds = -1;
            c.CrossfadeSeconds = 0.1;
            c.Fps = 5;
            c.Crf = 5;
            c.Preset = "medium";
            c.Normalize();

            Assert.Equal(10, c.ImageIntervalSeconds);
            Assert.Equal(0.0, c.ImageCrossfadeSeconds);
            Assert.Equal(3.0, c.CrossfadeSeconds);
            Assert.Equal(30, c.Fps);
            Assert.Equal(18, c.Crf);
            Assert.Equal("veryfast", c.Preset);
        }

        [Fact]
        public void Normalize_clamps_the_high_ends()
        {
            AppConfig c = new AppConfig();
            c.ImageIntervalSeconds = 100000;
            c.ImageCrossfadeSeconds = 99;
            c.CrossfadeSeconds = 99;
            c.Fps = 1000;
            c.Crf = 99;
            c.Normalize();

            Assert.Equal(86400, c.ImageIntervalSeconds);
            Assert.Equal(5.0, c.ImageCrossfadeSeconds);
            Assert.Equal(10.0, c.CrossfadeSeconds);
            Assert.Equal(30, c.Fps);
            Assert.Equal(18, c.Crf);
        }

        [Fact]
        public void Normalize_treats_a_NaN_image_crossfade_as_zero()
        {
            AppConfig c = new AppConfig();
            c.ImageCrossfadeSeconds = double.NaN;
            c.Normalize();
            Assert.Equal(0.0, c.ImageCrossfadeSeconds);
        }

        [Fact]
        public void Normalize_keeps_a_valid_slow_preset()
        {
            AppConfig c = new AppConfig();
            c.Preset = "slow";
            c.Normalize();
            Assert.Equal("slow", c.Preset);
        }

        [Fact]
        public void Normalize_fills_empty_tool_and_task_paths()
        {
            AppConfig c = new AppConfig();
            c.TaskName = "";
            c.MpvPath = "";
            c.FfmpegPath = "";
            c.FfprobePath = "";
            c.Normalize();

            Assert.Equal("LiveBackground (early startup)", c.TaskName);
            Assert.Equal(@"mpv\mpv.exe", c.MpvPath);
            Assert.Equal(@"tools\ffmpeg\ffmpeg.exe", c.FfmpegPath);
            Assert.Equal(@"tools\ffmpeg\ffprobe.exe", c.FfprobePath);
        }

        [Fact]
        public void Normalize_forces_the_installer_task_name()
        {
            AppConfig c = new AppConfig();
            c.TaskName = "My custom task";
            c.Normalize();
            Assert.Equal("LiveBackground (early startup)", c.TaskName);
        }

        [Fact]
        public void InheritsMainFade_compares_value_and_fine_steps()
        {
            Assert.True(AppConfig.InheritsMainFade(3.0, false, 3.0, false));
            Assert.True(AppConfig.InheritsMainFade(2.5, true, 2.5, true));
            Assert.False(AppConfig.InheritsMainFade(3.5, false, 3.0, false));
            Assert.False(AppConfig.InheritsMainFade(3.0, true, 3.0, false));
            Assert.False(AppConfig.InheritsMainFade(double.NaN, false, 3.0, false));
        }

        [Fact]
        public void Normalize_cleans_the_playlist()
        {
            AppConfig c = new AppConfig();
            c.Playlist = new List<PlaylistEntry>
            {
                new PlaylistEntry("a.mp4", true),
                null,
                new PlaylistEntry("", true),
                new PlaylistEntry("   ", true),
                new PlaylistEntry("a.mp4", false),
                new PlaylistEntry("A.MP4", false),
                new PlaylistEntry("  b.mp4  ", true),
            };
            c.Normalize();

            Assert.Equal(2, c.Playlist.Count);
            Assert.Equal("a.mp4", c.Playlist[0].Name);
            Assert.True(c.Playlist[0].Enabled);
            Assert.Equal("b.mp4", c.Playlist[1].Name);
            Assert.True(c.Playlist[1].Enabled);
        }

        [Fact]
        public void Normalize_migrates_the_flat_second_screen_fields_once()
        {
            AppConfig c = new AppConfig();
            c.SecondScreenSameAsMain = false;
            c.SecondScreenFolder = @"D:\vids";
            c.SecondScreenPlaybackMode = "playlist";
            c.SecondScreenSingleClip = "x.mp4";
            c.SecondScreenPlaylist = new List<PlaylistEntry> { new PlaylistEntry("y.mp4", true) };
            c.Normalize();

            Assert.Single(c.ExtraScreens);
            ScreenSettings legacy = c.ExtraScreens[0];
            Assert.Equal("", legacy.Device);
            Assert.False(legacy.SameAsMain);
            Assert.Equal(@"D:\vids", legacy.Folder);
            Assert.Equal("playlist", legacy.PlaybackMode);
            Assert.Equal("x.mp4", legacy.SingleClip);
            Assert.Single(legacy.Playlist);
            Assert.Equal("y.mp4", legacy.Playlist[0].Name);
        }

        [Fact]
        public void Normalize_does_not_re_migrate_when_screens_already_exist()
        {
            AppConfig c = new AppConfig();
            c.ExtraScreens = new List<ScreenSettings>
            {
                new ScreenSettings { Device = @"\\.\DISPLAY2" },
            };
            c.Normalize();

            Assert.Single(c.ExtraScreens);
            Assert.Equal(@"\\.\DISPLAY2", c.ExtraScreens[0].Device);
        }

        [Fact]
        public void Normalize_drops_duplicate_device_screens_but_keeps_one_legacy_slot()
        {
            AppConfig c = new AppConfig();
            c.ExtraScreens = new List<ScreenSettings>
            {
                new ScreenSettings { Device = "" },
                new ScreenSettings { Device = "" },
                new ScreenSettings { Device = @"\\.\DISPLAY2" },
                new ScreenSettings { Device = @"\\.\display2" },
            };
            c.Normalize();

            Assert.Equal(2, c.ExtraScreens.Count);
            Assert.Equal("", c.ExtraScreens[0].Device);
            Assert.Equal(@"\\.\DISPLAY2", c.ExtraScreens[1].Device);
        }

        [Fact]
        public void ScreenFor_matches_a_device_case_insensitively()
        {
            AppConfig c = new AppConfig();
            c.ExtraScreens = new List<ScreenSettings>
            {
                new ScreenSettings { Device = @"\\.\DISPLAY2", SingleClip = "two.mp4" },
                new ScreenSettings { Device = @"\\.\DISPLAY3", SingleClip = "three.mp4" },
            };

            Assert.Equal("two.mp4", c.ScreenFor(@"\\.\display2").SingleClip);
            Assert.Equal("three.mp4", c.ScreenFor(@"\\.\DISPLAY3").SingleClip);
        }

        [Fact]
        public void ScreenFor_returns_a_fresh_fallback_for_an_unknown_device()
        {
            AppConfig c = new AppConfig();
            c.ExtraScreens = new List<ScreenSettings>
            {
                new ScreenSettings { Device = @"\\.\DISPLAY2" },
            };

            ScreenSettings fallback = c.ScreenFor(@"\\.\DISPLAY9");
            Assert.Equal(@"\\.\DISPLAY9", fallback.Device);
            Assert.True(fallback.SameAsMain);
        }

        [Fact]
        public void ScreenFor_uses_the_device_less_entry_as_a_fallback()
        {
            AppConfig c = new AppConfig();
            c.ExtraScreens = new List<ScreenSettings>
            {
                new ScreenSettings { Device = "", SingleClip = "legacy.mp4" },
            };

            Assert.Equal("legacy.mp4", c.ScreenFor(@"\\.\DISPLAY5").SingleClip);
        }

        [Fact]
        public void ScreenForSeeded_starts_a_new_screen_from_the_main_settings()
        {
            AppConfig c = new AppConfig();
            c.VideoFolder = @"D:\vids";
            c.PlaybackMode = "playlist";
            c.SingleClip = "main.mp4";
            c.Playlist = new List<PlaylistEntry> { new PlaylistEntry("main.mp4", true) };
            c.ImageFolder = @"D:\pics";
            c.ImageMode = "slideshow";
            c.ImageSingle = "main.jpg";
            c.ImagePlaylist = new List<PlaylistEntry> { new PlaylistEntry("main.jpg", true) };
            c.ImageIntervalSeconds = 42;
            c.ImageCrossfadeSeconds = 1.5;
            c.ImageShuffle = true;

            ScreenSettings seeded = c.ScreenForSeeded(@"\\.\DISPLAY7");

            Assert.Equal(@"\\.\DISPLAY7", seeded.Device);
            Assert.Equal(@"D:\vids", seeded.Folder);
            Assert.Equal("playlist", seeded.PlaybackMode);
            Assert.Equal("main.mp4", seeded.SingleClip);
            Assert.Single(seeded.Playlist);
            Assert.Equal(@"D:\pics", seeded.ImageFolder);
            Assert.Equal("slideshow", seeded.ImageMode);
            Assert.Equal("main.jpg", seeded.ImageSingle);
            Assert.Single(seeded.ImagePlaylist);
            Assert.Equal(42, seeded.ImageIntervalSeconds);
            Assert.Equal(1.5, seeded.ImageCrossfadeSeconds);
            Assert.True(seeded.ImageShuffle);

            seeded.Playlist[0].Enabled = false;
            Assert.True(c.Playlist[0].Enabled);
        }

        [Fact]
        public void ScreenForSeeded_keeps_a_saved_device_entry()
        {
            AppConfig c = new AppConfig();
            c.VideoFolder = @"D:\main";
            c.ExtraScreens = new List<ScreenSettings>
            {
                new ScreenSettings { Device = @"\\.\DISPLAY2", Folder = @"D:\own", SingleClip = "own.mp4" },
            };

            ScreenSettings saved = c.ScreenForSeeded(@"\\.\display2");
            Assert.Equal(@"D:\own", saved.Folder);
            Assert.Equal("own.mp4", saved.SingleClip);
        }

        [Fact]
        public void ScreenForSeeded_keeps_a_legacy_device_less_entry()
        {
            AppConfig c = new AppConfig();
            c.VideoFolder = @"D:\main";
            c.ExtraScreens = new List<ScreenSettings>
            {
                new ScreenSettings { Device = "", Folder = @"D:\legacy", SingleClip = "legacy.mp4" },
            };

            ScreenSettings legacy = c.ScreenForSeeded(@"\\.\DISPLAY9");
            Assert.Equal(@"D:\legacy", legacy.Folder);
            Assert.Equal("legacy.mp4", legacy.SingleClip);
        }

        [Fact]
        public void ScreenByDevice_creates_on_demand_and_then_finds_it()
        {
            AppConfig c = new AppConfig();
            c.ExtraScreens = new List<ScreenSettings>();

            Assert.Null(c.ScreenByDevice(@"\\.\DISPLAY2", false));

            ScreenSettings made = c.ScreenByDevice(@"\\.\DISPLAY2", true);
            Assert.NotNull(made);
            Assert.Equal(@"\\.\DISPLAY2", made.Device);
            Assert.Same(made, c.ScreenByDevice(@"\\.\display2", false));
            Assert.Single(c.ExtraScreens);
        }

        [Fact]
        public void BindLegacyScreens_binds_the_legacy_entry_to_the_first_extra_monitor()
        {
            AppConfig c = new AppConfig();
            c.ExtraScreens = new List<ScreenSettings> { new ScreenSettings { Device = "" } };
            List<ScreenInfo> screens = new List<ScreenInfo>
            {
                new ScreenInfo { Device = @"\\.\DISPLAY1", Primary = true },
                new ScreenInfo { Device = @"\\.\DISPLAY2", Primary = false },
            };

            Assert.True(c.BindLegacyScreens(screens));
            Assert.Equal(@"\\.\DISPLAY2", c.ExtraScreens[0].Device);
        }

        [Fact]
        public void BindLegacyScreens_does_nothing_without_a_legacy_entry()
        {
            AppConfig c = new AppConfig();
            c.ExtraScreens = new List<ScreenSettings>
            {
                new ScreenSettings { Device = @"\\.\DISPLAY2" },
            };
            List<ScreenInfo> screens = new List<ScreenInfo>
            {
                new ScreenInfo { Device = @"\\.\DISPLAY2", Primary = false },
            };

            Assert.False(c.BindLegacyScreens(screens));
        }

        [Fact]
        public void BindLegacyScreens_leaves_the_legacy_entry_alone_when_the_monitor_is_taken()
        {
            AppConfig c = new AppConfig();
            c.ExtraScreens = new List<ScreenSettings>
            {
                new ScreenSettings { Device = "" },
                new ScreenSettings { Device = @"\\.\DISPLAY2" },
            };
            List<ScreenInfo> screens = new List<ScreenInfo>
            {
                new ScreenInfo { Device = @"\\.\DISPLAY2", Primary = false },
            };

            Assert.False(c.BindLegacyScreens(screens));
            Assert.Equal("", c.ExtraScreens[0].Device);
        }

        [Fact]
        public void Clone_deep_copies_the_nested_lists()
        {
            AppConfig original = new AppConfig();
            original.Playlist.Add(new PlaylistEntry("a.mp4", true));
            original.ExtraScreens.Add(new ScreenSettings { Device = @"\\.\DISPLAY2" });

            AppConfig copy = original.Clone();
            copy.Playlist[0].Name = "changed";
            copy.ExtraScreens[0].Device = "changed";

            Assert.Equal("a.mp4", original.Playlist[0].Name);
            Assert.Equal(@"\\.\DISPLAY2", original.ExtraScreens[0].Device);
        }

        [Fact]
        public void ScreenConfig_applies_only_explicit_render_overrides()
        {
            AppConfig c = new AppConfig();
            c.ExtraScreens.Add(new ScreenSettings
            {
                Device = @"\\.\DISPLAY2",
                RenderWidth = 2560,
                RenderHeight = 1440,
                Fps = 60,
                Crf = 23
            });
            c.ExtraScreens.Add(new ScreenSettings { Device = @"\\.\DISPLAY3" });

            AppConfig own = c.ScreenConfig(c.ExtraScreens[0]);
            Assert.Equal(2560, own.RenderWidth);
            Assert.Equal(1440, own.RenderHeight);
            Assert.Equal(60, own.Fps);
            Assert.Equal(23, own.Crf);

            AppConfig inherited = c.ScreenConfig(c.ExtraScreens[1]);
            Assert.Equal(c.RenderWidth, inherited.RenderWidth);
            Assert.Equal(c.RenderHeight, inherited.RenderHeight);
            Assert.Equal(c.Fps, inherited.Fps);
            Assert.Equal(c.Crf, inherited.Crf);
        }

        [Fact]
        public void Normalize_discards_broken_screen_render_overrides()
        {
            AppConfig c = new AppConfig();
            c.ExtraScreens.Add(new ScreenSettings { Device = "half", RenderWidth = 1280 });
            c.ExtraScreens.Add(new ScreenSettings
            {
                Device = "tiny",
                RenderWidth = 320,
                RenderHeight = 240,
                Fps = 5,
                Crf = 5
            });
            c.ExtraScreens.Add(new ScreenSettings
            {
                Device = "good",
                RenderWidth = 3440,
                RenderHeight = 1440,
                Fps = 50,
                Crf = 20
            });
            c.Normalize();

            Assert.Null(c.ExtraScreens[0].RenderWidth);
            Assert.Null(c.ExtraScreens[0].RenderHeight);
            Assert.Null(c.ExtraScreens[1].RenderWidth);
            Assert.Null(c.ExtraScreens[1].RenderHeight);
            Assert.Null(c.ExtraScreens[0].Fps);
            Assert.Null(c.ExtraScreens[0].Crf);
            Assert.Equal(3440, c.ExtraScreens[2].RenderWidth);
            Assert.Equal(1440, c.ExtraScreens[2].RenderHeight);
            Assert.Equal(50, c.ExtraScreens[2].Fps);
            Assert.Equal(20, c.ExtraScreens[2].Crf);
        }

        [Fact]
        public void Config_survives_a_save_and_load_round_trip()
        {
            using (TempFolder temp = new TempFolder())
            {
                AppConfig original = new AppConfig();
                original.WallpaperMode = "images";
                original.ImageMode = "slideshow";
                original.ImageIntervalSeconds = 25;
                original.RenderWidth = 2560;
                original.RenderHeight = 1440;
                original.SingleClip = "My Clip.mp4";
                original.Playlist = new List<PlaylistEntry>
                {
                    new PlaylistEntry("a.mp4", true),
                    new PlaylistEntry("b.mp4", false),
                };
                original.ExtraScreens = new List<ScreenSettings>
                {
                    new ScreenSettings
                    {
                        Device = @"\\.\DISPLAY2",
                        Folder = @"D:\vids",
                        CrossfadeSeconds = 4.5,
                        RenderWidth = 2560,
                        RenderHeight = 1440,
                        Fps = 60,
                        Crf = 23
                    },
                };

                JsonStore.SerializeFile(ConfigStore.PathFor(temp.Dir), original);
                AppConfig loaded = ConfigStore.Load(temp.Dir);

                Assert.Equal("images", loaded.WallpaperMode);
                Assert.Equal("slideshow", loaded.ImageMode);
                Assert.Equal(25, loaded.ImageIntervalSeconds);
                Assert.Equal(2560, loaded.RenderWidth);
                Assert.Equal(1440, loaded.RenderHeight);
                Assert.Equal("My Clip.mp4", loaded.SingleClip);
                Assert.Equal(2, loaded.Playlist.Count);
                Assert.True(loaded.Playlist[0].Enabled);
                Assert.False(loaded.Playlist[1].Enabled);
                Assert.Single(loaded.ExtraScreens);
                Assert.Equal(@"\\.\DISPLAY2", loaded.ExtraScreens[0].Device);
                Assert.Equal(@"D:\vids", loaded.ExtraScreens[0].Folder);
                Assert.Equal((double?)4.5, loaded.ExtraScreens[0].CrossfadeSeconds);
                Assert.Equal((int?)2560, loaded.ExtraScreens[0].RenderWidth);
                Assert.Equal((int?)1440, loaded.ExtraScreens[0].RenderHeight);
                Assert.Equal((int?)60, loaded.ExtraScreens[0].Fps);
                Assert.Equal((int?)23, loaded.ExtraScreens[0].Crf);
            }
        }

        [Fact]
        public void Load_returns_defaults_when_there_is_no_config_file()
        {
            using (TempFolder temp = new TempFolder())
            {
                AppConfig loaded = ConfigStore.Load(temp.Dir);
                Assert.NotNull(loaded);
                Assert.Equal("video", loaded.WallpaperMode);
                Assert.Equal("single", loaded.PlaybackMode);
            }
        }

        [Fact]
        public void Load_treats_a_literal_null_config_as_fresh_defaults()
        {
            using (TempFolder temp = new TempFolder())
            {
                temp.Write("config.json", "null");

                AppConfig loaded = ConfigStore.Load(temp.Dir);

                Assert.NotNull(loaded);
                Assert.Equal("video", loaded.WallpaperMode);
                Assert.Equal(3.0, loaded.CrossfadeSeconds);
                // Normalize() builds the migrated screen slot for a fresh config.
                Assert.Single(loaded.ExtraScreens);
                Assert.Null(ConfigStore.LoadWarning);
                Assert.False(File.Exists(Path.Combine(temp.Dir, "config.json.bad")));
            }
        }

        [Fact]
        public void ScreenUsesImages_flips_only_the_overridden_device()
        {
            AppConfig c = new AppConfig();
            c.ExtraScreens = new List<ScreenSettings>
            {
                new ScreenSettings { Device = @"\\.\DISPLAY2", ModeOverride = true },
                new ScreenSettings { Device = @"\\.\DISPLAY3" },
            };

            Assert.True(c.ScreenUsesImages(@"\\.\DISPLAY2"));
            Assert.False(c.ScreenUsesImages(@"\\.\DISPLAY3"));

            c.WallpaperMode = "images";
            Assert.False(c.ScreenUsesImages(@"\\.\DISPLAY2"));
            Assert.True(c.ScreenUsesImages(@"\\.\DISPLAY3"));
        }

        [Fact]
        public void ModeOverride_survives_a_clone()
        {
            AppConfig c = new AppConfig();
            c.ExtraScreens.Add(new ScreenSettings { Device = @"\\.\DISPLAY2", ModeOverride = true });
            Assert.True(c.Clone().ExtraScreens[0].ModeOverride);
        }
    }
}
