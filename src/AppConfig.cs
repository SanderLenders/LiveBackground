using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;

namespace LiveBackground
{
    // One entry in the ordered playlist. A clip can stay in the folder but be
    // switched off, so the folder is the library and the playlist is the show.
    [DataContract]
    public class PlaylistEntry
    {
        [DataMember(Name = "name")] public string Name = "";
        [DataMember(Name = "enabled")] public bool Enabled = true;

        public PlaylistEntry() { }

        public PlaylistEntry(string name, bool enabled)
        {
            Name = name;
            Enabled = enabled;
        }
    }

    [DataContract]
    public class AppConfig
    {
        [DataMember(Name = "wallpaperMode")] public string WallpaperMode = "video";
        [DataMember(Name = "videoFolder")] public string VideoFolder = @"%USERPROFILE%\LiveBackground\Videos";
        [DataMember(Name = "imageFolder")] public string ImageFolder = @"%USERPROFILE%\LiveBackground\images\wallpapers";
        [DataMember(Name = "imageMode")] public string ImageMode = "single";
        // Fresh installs start on the bundled standard picture.
        [DataMember(Name = "imageSingle")] public string ImageSingle = "Galaxy Image.jpg";
        [DataMember(Name = "imagePlaylist")] public List<PlaylistEntry> ImagePlaylist = new List<PlaylistEntry>();
        [DataMember(Name = "imageIntervalSeconds")] public int ImageIntervalSeconds = 5;
        [DataMember(Name = "imageCrossfadeSeconds")] public double ImageCrossfadeSeconds = 2.0;
        [DataMember(Name = "imageFineCrossfadeSteps")] public bool ImageFineCrossfadeSteps = false;
        [DataMember(Name = "imageShuffle")] public bool ImageShuffle = false;
        [DataMember(Name = "crossfadeSeconds")] public double CrossfadeSeconds = 3.0;
        [DataMember(Name = "fineCrossfadeSteps")] public bool FineCrossfadeSteps = false;
        [DataMember(Name = "renderWidth")] public int RenderWidth = 1920;
        [DataMember(Name = "renderHeight")] public int RenderHeight = 1080;
        [DataMember(Name = "fps")] public int Fps = 30;
        [DataMember(Name = "crf")] public int Crf = 18;
        [DataMember(Name = "preset")] public string Preset = "veryfast";
        [DataMember(Name = "shuffle")] public bool Shuffle = false;
        [DataMember(Name = "playbackMode")] public string PlaybackMode = "single";
        // Fresh installs start on the bundled standard clip in One-clip mode.
        [DataMember(Name = "singleClip")] public string SingleClip = "Galaxy HD Clip.mp4";
        [DataMember(Name = "playlist")] public List<PlaylistEntry> Playlist = new List<PlaylistEntry>();
        [DataMember(Name = "autoRebuild")] public bool AutoRebuild = true;
        [DataMember(Name = "lockScreenMode")] public string LockScreenMode = "frame";
        [DataMember(Name = "lockScreenImage")] public string LockScreenImage = "";
        [DataMember(Name = "desktopWallpaper")] public bool DesktopWallpaper = true;
        [DataMember(Name = "desktopWallpaperMode")] public string DesktopWallpaperMode = "frame";
        [DataMember(Name = "desktopWallpaperImage")] public string DesktopWallpaperImage = "";
        [DataMember(Name = "mpvPath")] public string MpvPath = @"mpv\mpv.exe";
        [DataMember(Name = "ffmpegPath")] public string FfmpegPath = @"tools\ffmpeg\ffmpeg.exe";
        [DataMember(Name = "ffprobePath")] public string FfprobePath = @"tools\ffmpeg\ffprobe.exe";
        [DataMember(Name = "taskName")] public string TaskName = "LiveBackground (early startup)";
        [DataMember(Name = "extraMpvArgs")] public string ExtraMpvArgs = "";
        [DataMember(Name = "logLevel")] public string LogLevel = "info";
        // On by default: the video must not stop Windows from dimming or
        // locking the screen. Turning this off keeps the display awake.
        [DataMember(Name = "screensaverActive")] public bool ScreensaverActive = true;

        // The second screen can mirror the main clip or play its own loop.
        // "Same as main" keeps a single loop and lets each screen crop it;
        // turning it off renders and caches a separate loop for the second
        // screen. Only used when two displays are connected.
        [DataMember(Name = "secondScreenSameAsMain")] public bool SecondScreenSameAsMain = true;
        [DataMember(Name = "secondScreenFolder")] public string SecondScreenFolder = @"%USERPROFILE%\LiveBackground\Videos";
        [DataMember(Name = "secondScreenPlaybackMode")] public string SecondScreenPlaybackMode = "single";
        [DataMember(Name = "secondScreenSingleClip")] public string SecondScreenSingleClip = "Galaxy HD Clip.mp4";
        [DataMember(Name = "secondScreenPlaylist")] public List<PlaylistEntry> SecondScreenPlaylist = new List<PlaylistEntry>();

        [OnDeserializing]
        private void SetDefaults(StreamingContext context)
        {
            WallpaperMode = "video";
            VideoFolder = @"%USERPROFILE%\LiveBackground\Videos";
            ImageFolder = @"%USERPROFILE%\LiveBackground\images\wallpapers";
            ImageMode = "single";
            ImageSingle = "Galaxy Image.jpg";
            ImagePlaylist = new List<PlaylistEntry>();
            ImageIntervalSeconds = 5;
            ImageCrossfadeSeconds = 2.0;
            ImageFineCrossfadeSteps = false;
            ImageShuffle = false;
            CrossfadeSeconds = 3.0;
            FineCrossfadeSteps = false;
            RenderWidth = 1920;
            RenderHeight = 1080;
            Fps = 30;
            Crf = 18;
            Preset = "veryfast";
            Shuffle = false;
            PlaybackMode = "single";
            SingleClip = "Galaxy HD Clip.mp4";
            Playlist = new List<PlaylistEntry>();
            AutoRebuild = true;
            LockScreenMode = "frame";
            LockScreenImage = "";
            DesktopWallpaper = true;
            DesktopWallpaperMode = "frame";
            DesktopWallpaperImage = "";
            MpvPath = @"mpv\mpv.exe";
            FfmpegPath = @"tools\ffmpeg\ffmpeg.exe";
            FfprobePath = @"tools\ffmpeg\ffprobe.exe";
            TaskName = "LiveBackground (early startup)";
            ExtraMpvArgs = "";
            LogLevel = "info";
            ScreensaverActive = true;
            SecondScreenSameAsMain = true;
            SecondScreenFolder = @"%USERPROFILE%\LiveBackground\Videos";
            SecondScreenPlaybackMode = "single";
            SecondScreenSingleClip = "Galaxy HD Clip.mp4";
            SecondScreenPlaylist = new List<PlaylistEntry>();
        }

        public AppConfig Clone()
        {
            AppConfig copy = new AppConfig();
            copy.CopyFrom(this);
            return copy;
        }

        // Copies every persisted field. Used by the settings window so a save
        // never drops settings the window does not show.
        public void CopyFrom(AppConfig other)
        {
            if (other == null) return;
            WallpaperMode = other.WallpaperMode;
            VideoFolder = other.VideoFolder;
            ImageFolder = other.ImageFolder;
            ImageMode = other.ImageMode;
            ImageSingle = other.ImageSingle;
            ImagePlaylist = new List<PlaylistEntry>();
            if (other.ImagePlaylist != null)
            {
                foreach (PlaylistEntry entry in other.ImagePlaylist)
                {
                    if (entry != null) ImagePlaylist.Add(new PlaylistEntry(entry.Name, entry.Enabled));
                }
            }
            ImageIntervalSeconds = other.ImageIntervalSeconds;
            ImageCrossfadeSeconds = other.ImageCrossfadeSeconds;
            ImageFineCrossfadeSteps = other.ImageFineCrossfadeSteps;
            ImageShuffle = other.ImageShuffle;
            CrossfadeSeconds = other.CrossfadeSeconds;
            FineCrossfadeSteps = other.FineCrossfadeSteps;
            RenderWidth = other.RenderWidth;
            RenderHeight = other.RenderHeight;
            Fps = other.Fps;
            Crf = other.Crf;
            Preset = other.Preset;
            Shuffle = other.Shuffle;
            PlaybackMode = other.PlaybackMode;
            SingleClip = other.SingleClip;
            Playlist = new List<PlaylistEntry>();
            if (other.Playlist != null)
            {
                foreach (PlaylistEntry entry in other.Playlist)
                {
                    if (entry != null) Playlist.Add(new PlaylistEntry(entry.Name, entry.Enabled));
                }
            }
            AutoRebuild = other.AutoRebuild;
            LockScreenMode = other.LockScreenMode;
            LockScreenImage = other.LockScreenImage;
            DesktopWallpaper = other.DesktopWallpaper;
            DesktopWallpaperMode = other.DesktopWallpaperMode;
            DesktopWallpaperImage = other.DesktopWallpaperImage;
            MpvPath = other.MpvPath;
            FfmpegPath = other.FfmpegPath;
            FfprobePath = other.FfprobePath;
            TaskName = other.TaskName;
            ExtraMpvArgs = other.ExtraMpvArgs;
            LogLevel = other.LogLevel;
            ScreensaverActive = other.ScreensaverActive;
            SecondScreenSameAsMain = other.SecondScreenSameAsMain;
            SecondScreenFolder = other.SecondScreenFolder;
            SecondScreenPlaybackMode = other.SecondScreenPlaybackMode;
            SecondScreenSingleClip = other.SecondScreenSingleClip;
            SecondScreenPlaylist = new List<PlaylistEntry>();
            if (other.SecondScreenPlaylist != null)
            {
                foreach (PlaylistEntry entry in other.SecondScreenPlaylist)
                {
                    if (entry != null) SecondScreenPlaylist.Add(new PlaylistEntry(entry.Name, entry.Enabled));
                }
            }
        }

        // A config that builds the second screen's own loop: the same render
        // settings as the main loop, but the second screen's folder, mode and
        // clip selection. Used only when "same as main" is off.
        public AppConfig SecondScreenConfig()
        {
            AppConfig second = Clone();
            second.VideoFolder = SecondScreenFolder;
            second.PlaybackMode = SecondScreenPlaybackMode;
            second.SingleClip = SecondScreenSingleClip;
            second.Playlist = new List<PlaylistEntry>();
            if (SecondScreenPlaylist != null)
            {
                foreach (PlaylistEntry entry in SecondScreenPlaylist)
                {
                    if (entry != null) second.Playlist.Add(new PlaylistEntry(entry.Name, entry.Enabled));
                }
            }
            return second;
        }

        public void Normalize()
        {
            if (!string.Equals(WallpaperMode, "images", StringComparison.OrdinalIgnoreCase))
            {
                WallpaperMode = "video";
            }
            else
            {
                WallpaperMode = "images";
            }
            if (string.IsNullOrEmpty(ImageFolder))
            {
                ImageFolder = @"%USERPROFILE%\LiveBackground\images\wallpapers";
            }
            if (!string.Equals(ImageMode, "slideshow", StringComparison.OrdinalIgnoreCase))
            {
                ImageMode = "single";
            }
            else
            {
                ImageMode = "slideshow";
            }
            if (ImageSingle == null) ImageSingle = "";
            if (ImagePlaylist == null) ImagePlaylist = new List<PlaylistEntry>();
            if (ImageIntervalSeconds < 1) ImageIntervalSeconds = 5;
            if (ImageIntervalSeconds > 86400) ImageIntervalSeconds = 86400;
            // 0 is allowed and means "no crossfade". The desktop image layer
            // only blends during a transition; between pictures it is static.
            if (double.IsNaN(ImageCrossfadeSeconds) || ImageCrossfadeSeconds < 0) ImageCrossfadeSeconds = 0;
            if (ImageCrossfadeSeconds > 5.0) ImageCrossfadeSeconds = 5.0;
            if (CrossfadeSeconds < 0.5) CrossfadeSeconds = 3.0;
            if (CrossfadeSeconds > 10.0) CrossfadeSeconds = 10.0;
            if (Fps < 10 || Fps > 60) Fps = 30;
            if (Crf < 10 || Crf > 30) Crf = 18;
            // "veryfast" is roughly twice as fast as "medium" at the same CRF
            // and produces no larger file, so older configs are migrated too.
            if (string.IsNullOrEmpty(Preset) || Preset == "medium") Preset = "veryfast";
            if (!string.Equals(PlaybackMode, "single", StringComparison.OrdinalIgnoreCase))
            {
                PlaybackMode = "playlist";
            }
            else
            {
                PlaybackMode = "single";
            }
            if (SingleClip == null) SingleClip = "";
            if (Playlist == null) Playlist = new List<PlaylistEntry>();
            // "previous" and the old "followimage" option were removed;
            // configs saved by older versions move to the first loop frame.
            if (string.IsNullOrEmpty(LockScreenMode) ||
                string.Equals(LockScreenMode, "previous", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(LockScreenMode, "followimage", StringComparison.OrdinalIgnoreCase))
            {
                LockScreenMode = "frame";
            }
            if (!string.Equals(DesktopWallpaperMode, "custom", StringComparison.OrdinalIgnoreCase))
            {
                DesktopWallpaperMode = "frame";
            }
            else
            {
                DesktopWallpaperMode = "custom";
            }
            if (string.IsNullOrEmpty(DesktopWallpaperImage)) DesktopWallpaperImage = "";
            if (string.IsNullOrEmpty(TaskName)) TaskName = "LiveBackground (early startup)";
            if (string.IsNullOrEmpty(MpvPath)) MpvPath = @"mpv\mpv.exe";
            if (string.IsNullOrEmpty(FfmpegPath)) FfmpegPath = @"tools\ffmpeg\ffmpeg.exe";
            if (string.IsNullOrEmpty(FfprobePath)) FfprobePath = @"tools\ffmpeg\ffprobe.exe";
            if (string.IsNullOrEmpty(VideoFolder)) VideoFolder = @"%USERPROFILE%\LiveBackground\Videos";

            if (string.IsNullOrEmpty(SecondScreenFolder)) SecondScreenFolder = @"%USERPROFILE%\LiveBackground\Videos";
            if (!string.Equals(SecondScreenPlaybackMode, "single", StringComparison.OrdinalIgnoreCase))
            {
                SecondScreenPlaybackMode = "playlist";
            }
            else
            {
                SecondScreenPlaybackMode = "single";
            }
            if (SecondScreenSingleClip == null) SecondScreenSingleClip = "";
            if (SecondScreenPlaylist == null) SecondScreenPlaylist = new List<PlaylistEntry>();

            // Drop empty and duplicate names, keeping the first occurrence in
            // the user's order. A clip that is missing from the folder is kept
            // here so it comes back if the file is restored.
            List<PlaylistEntry> clean = new List<PlaylistEntry>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (PlaylistEntry entry in Playlist)
            {
                if (entry == null || string.IsNullOrEmpty(entry.Name)) continue;
                string name = entry.Name.Trim();
                if (name.Length == 0 || !seen.Add(name)) continue;
                clean.Add(new PlaylistEntry(name, entry.Enabled));
            }
            Playlist = clean;

            List<PlaylistEntry> cleanImages = new List<PlaylistEntry>();
            HashSet<string> seenImages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (PlaylistEntry entry in ImagePlaylist)
            {
                if (entry == null || string.IsNullOrEmpty(entry.Name)) continue;
                string name = entry.Name.Trim();
                if (name.Length == 0 || !seenImages.Add(name)) continue;
                cleanImages.Add(new PlaylistEntry(name, entry.Enabled));
            }
            ImagePlaylist = cleanImages;

            List<PlaylistEntry> cleanSecond = new List<PlaylistEntry>();
            HashSet<string> seenSecond = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (PlaylistEntry entry in SecondScreenPlaylist)
            {
                if (entry == null || string.IsNullOrEmpty(entry.Name)) continue;
                string name = entry.Name.Trim();
                if (name.Length == 0 || !seenSecond.Add(name)) continue;
                cleanSecond.Add(new PlaylistEntry(name, entry.Enabled));
            }
            SecondScreenPlaylist = cleanSecond;
        }
    }

    public static class ConfigStore
    {
        public static string PathFor(string appRoot)
        {
            return Path.Combine(appRoot, "config.json");
        }

        public static AppConfig Load(string appRoot)
        {
            string path = PathFor(appRoot);
            if (!File.Exists(path))
            {
                AppConfig fresh = new AppConfig();
                Paths.ApplyDefaults(fresh);
                return fresh;
            }
            try
            {
                AppConfig config = JsonStore.DeserializeFile<AppConfig>(path);
                if (config == null) return new AppConfig();
                config.Normalize();
                Paths.ApplyDefaults(config);
                return config;
            }
            catch (Exception ex)
            {
                Log.Error("config load failed, using defaults", ex);
                try { File.Copy(path, path + ".bad", true); }
                catch { }
                return new AppConfig();
            }
        }

        public static void Save(string appRoot, AppConfig config)
        {
            if (config == null) config = new AppConfig();
            config.Normalize();
            JsonStore.SerializeFile(PathFor(appRoot), config);
        }

        public static AppConfig LoadOrCreate(string appRoot)
        {
            AppConfig config = Load(appRoot);
            if (!File.Exists(PathFor(appRoot))) Save(appRoot, config);
            return config;
        }
    }
}
