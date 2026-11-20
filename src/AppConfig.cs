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

    // One extra screen's settings, remembered by the monitor's device name
    // ("\\.\DISPLAY2") rather than by position. Video and Images have separate
    // mirror switches so a screen can, for example, mirror the video but have
    // its own slideshow. Everything mirrors the main screen by default.
    [DataContract]
    public class ScreenSettings
    {
        [DataMember(Name = "device")] public string Device = "";
        [DataMember(Name = "sameAsMain")] public bool SameAsMain = true;
        [DataMember(Name = "folder")] public string Folder = @"%USERPROFILE%\LiveBackground\Videos";
        [DataMember(Name = "playbackMode")] public string PlaybackMode = "single";
        [DataMember(Name = "singleClip")] public string SingleClip = "Galaxy HD Clip.mp4";
        [DataMember(Name = "playlist")] public List<PlaylistEntry> Playlist = new List<PlaylistEntry>();
        // Null means "not customized yet": an upgraded screen keeps inheriting
        // the main video fade until its own fade is saved in the settings.
        [DataMember(Name = "crossfadeSeconds")] public double? CrossfadeSeconds;
        [DataMember(Name = "fineCrossfadeSteps")] public bool? FineCrossfadeSteps;

        [DataMember(Name = "imagesSameAsMain")] public bool ImagesSameAsMain = true;
        [DataMember(Name = "imageFolder")] public string ImageFolder = @"%USERPROFILE%\LiveBackground\images\wallpapers";
        [DataMember(Name = "imageMode")] public string ImageMode = "single";
        [DataMember(Name = "imageSingle")] public string ImageSingle = "Galaxy Image.jpg";
        [DataMember(Name = "imagePlaylist")] public List<PlaylistEntry> ImagePlaylist = new List<PlaylistEntry>();
        // A screen that has not customized its slideshow uses the same
        // defaults as the main screen: 10 s per picture, 2 s crossfade.
        [DataMember(Name = "imageIntervalSeconds")] public int ImageIntervalSeconds = 10;
        [DataMember(Name = "imageCrossfadeSeconds")] public double ImageCrossfadeSeconds = 2.0;
        [DataMember(Name = "imageFineCrossfadeSteps")] public bool ImageFineCrossfadeSteps = false;
        [DataMember(Name = "imageShuffle")] public bool ImageShuffle = false;

        // Empty mode means the desktop fallback still follows the main screen.
        // Once customized, it is "frame" (this screen's loop) or "custom".
        [DataMember(Name = "desktopWallpaperMode")] public string DesktopWallpaperMode = "";
        [DataMember(Name = "desktopWallpaperImage")] public string DesktopWallpaperImage = "";

        [OnDeserializing]
        private void SetDefaults(StreamingContext context)
        {
            Device = "";
            SameAsMain = true;
            Folder = @"%USERPROFILE%\LiveBackground\Videos";
            PlaybackMode = "single";
            SingleClip = "Galaxy HD Clip.mp4";
            Playlist = new List<PlaylistEntry>();
            CrossfadeSeconds = null;
            FineCrossfadeSteps = null;
            ImagesSameAsMain = true;
            ImageFolder = @"%USERPROFILE%\LiveBackground\images\wallpapers";
            ImageMode = "single";
            ImageSingle = "Galaxy Image.jpg";
            ImagePlaylist = new List<PlaylistEntry>();
            ImageIntervalSeconds = 10;
            ImageCrossfadeSeconds = 2.0;
            ImageFineCrossfadeSteps = false;
            ImageShuffle = false;
            DesktopWallpaperMode = "";
            DesktopWallpaperImage = "";
        }

        public ScreenSettings Clone()
        {
            ScreenSettings copy = new ScreenSettings();
            copy.Device = Device;
            copy.SameAsMain = SameAsMain;
            copy.Folder = Folder;
            copy.PlaybackMode = PlaybackMode;
            copy.SingleClip = SingleClip;
            copy.Playlist = CopyEntries(Playlist);
            copy.CrossfadeSeconds = CrossfadeSeconds;
            copy.FineCrossfadeSteps = FineCrossfadeSteps;
            copy.ImagesSameAsMain = ImagesSameAsMain;
            copy.ImageFolder = ImageFolder;
            copy.ImageMode = ImageMode;
            copy.ImageSingle = ImageSingle;
            copy.ImagePlaylist = CopyEntries(ImagePlaylist);
            copy.ImageIntervalSeconds = ImageIntervalSeconds;
            copy.ImageCrossfadeSeconds = ImageCrossfadeSeconds;
            copy.ImageFineCrossfadeSteps = ImageFineCrossfadeSteps;
            copy.ImageShuffle = ImageShuffle;
            copy.DesktopWallpaperMode = DesktopWallpaperMode;
            copy.DesktopWallpaperImage = DesktopWallpaperImage;
            return copy;
        }

        private static List<PlaylistEntry> CopyEntries(List<PlaylistEntry> entries)
        {
            return PlaylistState.CopyEntries(entries);
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
        // Factory default: a new slideshow changes picture every 10 seconds,
        // with a 2 s crossfade between pictures.
        [DataMember(Name = "imageIntervalSeconds")] public int ImageIntervalSeconds = 10;
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
        // Screensaver action after the user has been idle for
        // ScreensaverHideSeconds: dim and lock the workstation.
        [DataMember(Name = "screensaverActive")] public bool ScreensaverActive = false;
        // Alternative screensaver action: keep the screen on and, after the
        // user has been idle for ScreensaverHideSeconds, hide the taskbar and
        // desktop icons so only the video is visible. The two choices are
        // mutually exclusive.
        [DataMember(Name = "screensaverVideo")] public bool ScreensaverVideo = true;
        // Idle delay before hiding taskbar/desktop icons when ScreensaverVideo
        // is enabled. Stored in seconds (preset values selected in settings).
        [DataMember(Name = "screensaverHideSeconds")] public int ScreensaverHideSeconds = 300;

        // The second screen can mirror the main clip or play its own loop.
        // "Same as main" keeps a single loop and lets each screen crop it;
        // turning it off renders and caches a separate loop for the second
        // screen. Only used when two displays are connected.
        [DataMember(Name = "secondScreenSameAsMain")] public bool SecondScreenSameAsMain = true;
        [DataMember(Name = "secondScreenFolder")] public string SecondScreenFolder = @"%USERPROFILE%\LiveBackground\Videos";
        [DataMember(Name = "secondScreenPlaybackMode")] public string SecondScreenPlaybackMode = "single";
        [DataMember(Name = "secondScreenSingleClip")] public string SecondScreenSingleClip = "Galaxy HD Clip.mp4";
        [DataMember(Name = "secondScreenPlaylist")] public List<PlaylistEntry> SecondScreenPlaylist = new List<PlaylistEntry>();

        // Per-screen settings for the extra monitors, keyed by device name.
        // The flat SecondScreen* fields above are the older single-screen
        // shape; Normalize migrates them into this list once.
        [DataMember(Name = "extraScreens")] public List<ScreenSettings> ExtraScreens = new List<ScreenSettings>();

        [OnDeserializing]
        private void SetDefaults(StreamingContext context)
        {
            WallpaperMode = "video";
            VideoFolder = @"%USERPROFILE%\LiveBackground\Videos";
            ImageFolder = @"%USERPROFILE%\LiveBackground\images\wallpapers";
            ImageMode = "single";
            ImageSingle = "Galaxy Image.jpg";
            ImagePlaylist = new List<PlaylistEntry>();
            ImageIntervalSeconds = 10;
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
            ScreensaverActive = false;
            ScreensaverVideo = true;
            ScreensaverHideSeconds = 300;
            SecondScreenSameAsMain = true;
            SecondScreenFolder = @"%USERPROFILE%\LiveBackground\Videos";
            SecondScreenPlaybackMode = "single";
            SecondScreenSingleClip = "Galaxy HD Clip.mp4";
            SecondScreenPlaylist = new List<PlaylistEntry>();
            ExtraScreens = new List<ScreenSettings>();
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
            ScreensaverVideo = other.ScreensaverVideo;
            ScreensaverHideSeconds = other.ScreensaverHideSeconds;
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
            ExtraScreens = new List<ScreenSettings>();
            if (other.ExtraScreens != null)
            {
                foreach (ScreenSettings screen in other.ExtraScreens)
                {
                    if (screen != null) ExtraScreens.Add(screen.Clone());
                }
            }
        }

        // A config that builds one screen's own loop: the same render settings
        // as the main loop, but that screen's folder, mode and clip selection.
        // Used only when "same as main" is off for the screen.
        public AppConfig ScreenConfig(ScreenSettings screen)
        {
            AppConfig own = Clone();
            if (screen != null)
            {
                own.VideoFolder = screen.Folder;
                own.PlaybackMode = screen.PlaybackMode;
                own.SingleClip = screen.SingleClip;
                own.Playlist = new List<PlaylistEntry>();
                if (screen.Playlist != null)
                {
                    foreach (PlaylistEntry entry in screen.Playlist)
                    {
                        if (entry != null) own.Playlist.Add(new PlaylistEntry(entry.Name, entry.Enabled));
                    }
                }
                if (screen.CrossfadeSeconds.HasValue) own.CrossfadeSeconds = screen.CrossfadeSeconds.Value;
                if (screen.FineCrossfadeSteps.HasValue) own.FineCrossfadeSteps = screen.FineCrossfadeSteps.Value;

                own.ImageFolder = screen.ImageFolder;
                own.ImageMode = screen.ImageMode;
                own.ImageSingle = screen.ImageSingle;
                own.ImagePlaylist = new List<PlaylistEntry>();
                if (screen.ImagePlaylist != null)
                {
                    foreach (PlaylistEntry entry in screen.ImagePlaylist)
                    {
                        if (entry != null) own.ImagePlaylist.Add(new PlaylistEntry(entry.Name, entry.Enabled));
                    }
                }
                own.ImageIntervalSeconds = screen.ImageIntervalSeconds;
                own.ImageCrossfadeSeconds = screen.ImageCrossfadeSeconds;
                own.ImageFineCrossfadeSteps = screen.ImageFineCrossfadeSteps;
                own.ImageShuffle = screen.ImageShuffle;

                if (!string.IsNullOrEmpty(screen.DesktopWallpaperMode))
                {
                    own.DesktopWallpaperMode = screen.DesktopWallpaperMode;
                    own.DesktopWallpaperImage = screen.DesktopWallpaperImage;
                }
            }
            return own;
        }

        // The settings for one physical screen, matched by device name.
        // A screen with no entry of its own falls back to a migrated,
        // device-less entry (an old second-screen setup); with no such entry
        // either, the screen mirrors the main loop.
        public ScreenSettings ScreenFor(string device)
        {
            if (ExtraScreens != null)
            {
                ScreenSettings legacy = null;
                foreach (ScreenSettings screen in ExtraScreens)
                {
                    if (screen == null) continue;
                    if (string.IsNullOrEmpty(screen.Device))
                    {
                        if (legacy == null) legacy = screen;
                        continue;
                    }
                    if (string.Equals(screen.Device, device, StringComparison.OrdinalIgnoreCase))
                    {
                        return screen;
                    }
                }
                if (legacy != null) return legacy;
            }
            ScreenSettings fallback = new ScreenSettings();
            fallback.Device = device ?? "";
            return fallback;
        }

        // The stored entry for a device, optionally created on the spot. The
        // settings window uses this so editing a screen always has somewhere
        // to write, without disturbing screens it does not show.
        public ScreenSettings ScreenByDevice(string device, bool create)
        {
            if (ExtraScreens == null) ExtraScreens = new List<ScreenSettings>();
            if (!string.IsNullOrEmpty(device))
            {
                foreach (ScreenSettings screen in ExtraScreens)
                {
                    if (screen != null && !string.IsNullOrEmpty(screen.Device) &&
                        string.Equals(screen.Device, device, StringComparison.OrdinalIgnoreCase))
                    {
                        return screen;
                    }
                }
            }
            if (!create) return null;
            ScreenSettings created = new ScreenSettings();
            created.Device = device ?? "";
            ExtraScreens.Add(created);
            return created;
        }

        // Binds a migrated, device-less entry to the first non-primary monitor,
        // so from then on the settings are remembered by device name. Called at
        // startup when the screen list is known. Returns true when it changed.
        public bool BindLegacyScreens(List<ScreenInfo> screens)
        {
            if (screens == null || ExtraScreens == null) return false;
            ScreenSettings legacy = null;
            foreach (ScreenSettings screen in ExtraScreens)
            {
                if (screen != null && string.IsNullOrEmpty(screen.Device))
                {
                    legacy = screen;
                    break;
                }
            }
            if (legacy == null) return false;

            foreach (ScreenInfo screen in screens)
            {
                if (screen.Primary) continue;
                bool taken = false;
                foreach (ScreenSettings existing in ExtraScreens)
                {
                    if (existing != null && !string.IsNullOrEmpty(existing.Device) &&
                        string.Equals(existing.Device, screen.Device, StringComparison.OrdinalIgnoreCase))
                    {
                        taken = true;
                        break;
                    }
                }
                if (taken) continue;
                legacy.Device = screen.Device ?? "";
                return true;
            }
            return false;
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
            if (ImageIntervalSeconds < 1) ImageIntervalSeconds = 10;
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

            // Migrate the flat second-screen fields from before per-screen
            // settings existed into one device-less entry. Startup binds that
            // entry to the first non-primary monitor.
            if (ExtraScreens == null) ExtraScreens = new List<ScreenSettings>();
            if (ExtraScreens.Count == 0)
            {
                ScreenSettings legacy = new ScreenSettings();
                legacy.Device = "";
                legacy.SameAsMain = SecondScreenSameAsMain;
                legacy.Folder = SecondScreenFolder;
                legacy.PlaybackMode = SecondScreenPlaybackMode;
                legacy.SingleClip = SecondScreenSingleClip;
                legacy.Playlist = new List<PlaylistEntry>();
                foreach (PlaylistEntry entry in SecondScreenPlaylist)
                {
                    if (entry != null) legacy.Playlist.Add(new PlaylistEntry(entry.Name, entry.Enabled));
                }
                ExtraScreens.Add(legacy);
            }
            NormalizeScreens();

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

        // Keeps the per-screen list tidy: one entry per device, sane defaults,
        // and the same playlist clean-up the main and image lists get.
        private void NormalizeScreens()
        {
            List<ScreenSettings> clean = new List<ScreenSettings>();
            HashSet<string> seenDevices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool legacySeen = false;
            foreach (ScreenSettings screen in ExtraScreens)
            {
                if (screen == null) continue;

                if (string.IsNullOrEmpty(screen.Folder))
                {
                    screen.Folder = @"%USERPROFILE%\LiveBackground\Videos";
                }
                if (!string.Equals(screen.PlaybackMode, "single", StringComparison.OrdinalIgnoreCase))
                {
                    screen.PlaybackMode = "playlist";
                }
                else
                {
                    screen.PlaybackMode = "single";
                }
                if (screen.SingleClip == null) screen.SingleClip = "";
                if (screen.Playlist == null) screen.Playlist = new List<PlaylistEntry>();

                List<PlaylistEntry> cleanPlaylist = new List<PlaylistEntry>();
                HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (PlaylistEntry entry in screen.Playlist)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.Name)) continue;
                    string name = entry.Name.Trim();
                    if (name.Length == 0 || !seen.Add(name)) continue;
                    cleanPlaylist.Add(new PlaylistEntry(name, entry.Enabled));
                }
                screen.Playlist = cleanPlaylist;

                if (screen.CrossfadeSeconds.HasValue &&
                    (double.IsNaN(screen.CrossfadeSeconds.Value) || screen.CrossfadeSeconds.Value < 0.5))
                {
                    screen.CrossfadeSeconds = 3.0;
                }
                if (screen.CrossfadeSeconds.HasValue && screen.CrossfadeSeconds.Value > 10.0)
                {
                    screen.CrossfadeSeconds = 10.0;
                }

                if (string.IsNullOrEmpty(screen.ImageFolder))
                {
                    screen.ImageFolder = @"%USERPROFILE%\LiveBackground\images\wallpapers";
                }
                screen.ImageMode = string.Equals(screen.ImageMode, "slideshow",
                    StringComparison.OrdinalIgnoreCase) ? "slideshow" : "single";
                if (screen.ImageSingle == null) screen.ImageSingle = "";
                if (screen.ImageIntervalSeconds < 1) screen.ImageIntervalSeconds = 10;
                if (screen.ImageIntervalSeconds > 86400) screen.ImageIntervalSeconds = 86400;
                if (double.IsNaN(screen.ImageCrossfadeSeconds) || screen.ImageCrossfadeSeconds < 0)
                {
                    screen.ImageCrossfadeSeconds = 0;
                }
                if (screen.ImageCrossfadeSeconds > 5.0) screen.ImageCrossfadeSeconds = 5.0;
                List<PlaylistEntry> cleanScreenImages = new List<PlaylistEntry>();
                HashSet<string> seenScreenImages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (screen.ImagePlaylist != null)
                {
                    foreach (PlaylistEntry entry in screen.ImagePlaylist)
                    {
                        if (entry == null || string.IsNullOrEmpty(entry.Name)) continue;
                        string name = entry.Name.Trim();
                        if (name.Length == 0 || !seenScreenImages.Add(name)) continue;
                        cleanScreenImages.Add(new PlaylistEntry(name, entry.Enabled));
                    }
                }
                screen.ImagePlaylist = cleanScreenImages;

                if (!string.Equals(screen.DesktopWallpaperMode, "frame", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(screen.DesktopWallpaperMode, "custom", StringComparison.OrdinalIgnoreCase))
                {
                    screen.DesktopWallpaperMode = "";
                }
                else if (string.Equals(screen.DesktopWallpaperMode, "custom", StringComparison.OrdinalIgnoreCase))
                {
                    screen.DesktopWallpaperMode = "custom";
                }
                else
                {
                    screen.DesktopWallpaperMode = "frame";
                }
                if (screen.DesktopWallpaperImage == null) screen.DesktopWallpaperImage = "";

                // Keep one device-less entry (the migration slot) and one
                // entry per device name.
                if (string.IsNullOrEmpty(screen.Device))
                {
                    if (legacySeen) continue;
                    legacySeen = true;
                }
                else if (!seenDevices.Add(screen.Device))
                {
                    continue;
                }
                clean.Add(screen);
            }
            ExtraScreens = clean;
        }
    }

    public static class ConfigStore
    {
        public static string PathFor(string appRoot)
        {
            return Path.Combine(appRoot, "config.json");
        }

        // Set when the last Load() had to fall back to defaults because the
        // config file could not be read. The GUI shows it once as a tray
        // balloon, so a non-technical user is told rather than silently losing
        // their settings. The CLI ignores it.
        public static string LoadWarning { get; private set; }

        public static AppConfig Load(string appRoot)
        {
            LoadWarning = null;
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
                catch (Exception copyEx)
                {
                    Log.Warn("could not back up the unreadable config: " + copyEx.Message);
                }
                LoadWarning = "Your settings could not be read, so the defaults were loaded. " +
                    "A copy of the old file was saved as config.json.bad.";
                AppConfig fresh = new AppConfig();
                Paths.ApplyDefaults(fresh);
                // Replace the unreadable file with valid defaults. Without this
                // every future start would fail the same way and warn again, and
                // the user's "fixed" settings could never be saved over a file
                // that always fails to parse. The .bad copy above keeps the old
                // contents for inspection.
                try { Save(appRoot, fresh); }
                catch (Exception saveEx)
                {
                    Log.Warn("could not replace the unreadable config: " + saveEx.Message);
                }
                return fresh;
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
