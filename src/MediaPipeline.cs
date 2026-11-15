using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;

namespace LiveBackground
{
    [DataContract]
    public class MediaManifest
    {
        [DataMember(Name = "version")] public int Version = 1;
        [DataMember(Name = "signature")] public string Signature;
        [DataMember(Name = "width")] public int Width;
        [DataMember(Name = "height")] public int Height;
        [DataMember(Name = "fps")] public int Fps;
        [DataMember(Name = "crossfade")] public double Crossfade;
        [DataMember(Name = "crf")] public int Crf;
        [DataMember(Name = "preset")] public string Preset;
        [DataMember(Name = "outputDuration")] public double OutputDuration;
        [DataMember(Name = "builtUtc")] public string BuiltUtc;
        [DataMember(Name = "loopFile")] public string LoopFile;
        [DataMember(Name = "frameFile")] public string FrameFile;
    }

    [DataContract]
    internal class ProbeStream
    {
        [DataMember(Name = "duration")] public string Duration;
    }

    [DataContract]
    internal class ProbeFormat
    {
        [DataMember(Name = "duration")] public string Duration;
    }

    [DataContract]
    internal class ProbeResult
    {
        [DataMember(Name = "streams")] public ProbeStream[] Streams;
        [DataMember(Name = "format")] public ProbeFormat Format;
    }

    public class BuildResult
    {
        public bool Success;
        public bool Cancelled;
        public string Message = "";
        public string LoopPath = "";
        public string FramePath = "";
        public double Duration;
    }

    public static class MediaPipeline
    {
        // There is deliberately no whitelist of video formats: anything that is
        // not obviously something else is handed to ffprobe, which decides by
        // content. That way an unusual container works without having to keep a
        // complete list of everything ffmpeg can read. Only files that are never
        // video are rejected up front, so a stray note, partial download or
        // shortcut in the folder cannot disturb the wallpaper.
        public static readonly string[] IgnoredExtensions =
        {
            // text, documents and data
            ".txt", ".md", ".nfo", ".rtf", ".pdf", ".doc", ".docx", ".odt",
            ".json", ".xml", ".ini", ".cfg", ".conf", ".log", ".db", ".db-wal",
            ".db-shm", ".lnk", ".url", ".website", ".html", ".htm",
            // archives, installers and disk images
            ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".iso", ".cab",
            ".msi", ".exe", ".dll", ".sys",
            // still images and raw photos
            ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff", ".heic", ".heif",
            ".ico", ".psd", ".dng", ".cr2", ".nef", ".arw", ".raw",
            // audio only (the wallpaper is silent)
            ".mp3", ".m4a", ".aac", ".flac", ".wav", ".wma", ".opus", ".aiff",
            ".aif", ".ape", ".alac", ".mid", ".midi",
            // subtitles
            ".srt", ".ass", ".ssa", ".vtt", ".sub", ".idx", ".sup",
            // partial or temporary downloads
            ".part", ".partial", ".crdownload", ".download", ".opdownload", ".tmp"
        };

        public static string CacheDir(string appRoot) { return Path.Combine(appRoot, "cache"); }

        public static string LoopPath(string appRoot)
        {
            return LoopPath(appRoot, LoadManifest(appRoot));
        }

        // Overload used during startup so the manifest is only read once.
        public static string LoopPath(string appRoot, MediaManifest manifest)
        {
            if (manifest != null && !string.IsNullOrEmpty(manifest.LoopFile))
            {
                string versioned = Path.Combine(CacheDir(appRoot), manifest.LoopFile);
                if (File.Exists(versioned)) return versioned;
            }
            return Path.Combine(CacheDir(appRoot), "loop.mp4");
        }

        public static string FramePath(string appRoot)
        {
            return FramePath(appRoot, LoadManifest(appRoot));
        }

        public static string FramePath(string appRoot, MediaManifest manifest)
        {
            if (manifest != null && !string.IsNullOrEmpty(manifest.FrameFile))
            {
                string versioned = Path.Combine(CacheDir(appRoot), manifest.FrameFile);
                if (File.Exists(versioned)) return versioned;
            }
            return Path.Combine(CacheDir(appRoot), "frame.jpg");
        }

        public static string ManifestPath(string appRoot) { return Path.Combine(CacheDir(appRoot), "manifest.json"); }

        // Each extra screen keeps its own "what is playing there" pointer, so
        // an independent screen loop never disturbs the main one. The file is
        // named after the monitor's device name.
        public static string ScreenManifestPath(string appRoot, string device)
        {
            return Path.Combine(ScreensDir(appRoot), ScreenKey(device) + ".json");
        }

        public static string ScreensDir(string appRoot) { return Path.Combine(CacheDir(appRoot), "screens"); }

        // Turns "\\.\DISPLAY2" into a file name: only letters and digits are
        // kept, everything else becomes an underscore.
        public static string ScreenKey(string device)
        {
            if (string.IsNullOrEmpty(device)) return "screen";
            StringBuilder sb = new StringBuilder(device.Length);
            foreach (char c in device)
            {
                sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            }
            return sb.ToString();
        }

        public static string LoopsDir(string appRoot) { return Path.Combine(CacheDir(appRoot), "loops"); }

        public static string CacheIndexPath(string appRoot, string signature)
        {
            return Path.Combine(LoopsDir(appRoot), signature + ".json");
        }

        // A loop rendered for exactly this signature can be reused instead of
        // re-encoded. Switching back to a clip or to an earlier playlist then
        // takes as long as restarting mpv, not as long as the encode.
        public static MediaManifest LoadCachedManifest(string appRoot, string signature)
        {
            if (string.IsNullOrEmpty(signature)) return null;
            string path = CacheIndexPath(appRoot, signature);
            if (!File.Exists(path)) return null;
            try
            {
                MediaManifest manifest = JsonStore.DeserializeFile<MediaManifest>(path);
                if (manifest == null || string.IsNullOrEmpty(manifest.LoopFile)) return null;
                if (!File.Exists(Path.Combine(CacheDir(appRoot), manifest.LoopFile))) return null;
                return manifest;
            }
            catch (Exception ex)
            {
                Log.Warn("could not read cached loop: " + ex.Message);
                return null;
            }
        }

        public static bool HasCachedLoop(string appRoot, string signature)
        {
            return LoadCachedManifest(appRoot, signature) != null;
        }

        // The loop that is playing was built before the cache existed on older
        // versions. Remember it under its signature so switching away and back
        // is instant too.
        public static void SeedCacheIndex(string appRoot, string signature)
        {
            if (string.IsNullOrEmpty(signature)) return;
            try
            {
                if (File.Exists(CacheIndexPath(appRoot, signature))) return;
                MediaManifest manifest = LoadManifest(appRoot);
                if (manifest == null ||
                    !string.Equals(manifest.Signature, signature, StringComparison.OrdinalIgnoreCase) ||
                    !File.Exists(LoopPath(appRoot, manifest)))
                {
                    return;
                }
                Directory.CreateDirectory(LoopsDir(appRoot));
                SaveManifest(CacheIndexPath(appRoot, signature), manifest);
            }
            catch (Exception ex) { Log.Warn("could not seed the loop cache: " + ex.Message); }
        }

        // Keeps the cache bounded: the live loop plus the six newest reusable
        // entries. Open files are left alone; the next build tries again.
        private const int CacheKeep = 6;

        public static void CleanupCache(string appRoot)
        {
            try
            {
                string cache = CacheDir(appRoot);
                if (!Directory.Exists(cache)) return;
                string loops = LoopsDir(appRoot);
                MediaManifest live = LoadManifest(appRoot);
                List<MediaManifest> screenLive = LoadScreenManifests(appRoot);

                List<KeyValuePair<string, MediaManifest>> entries =
                    new List<KeyValuePair<string, MediaManifest>>();
                if (Directory.Exists(loops))
                {
                    foreach (string index in Directory.GetFiles(loops, "*.json"))
                    {
                        MediaManifest entry = null;
                        try { entry = JsonStore.DeserializeFile<MediaManifest>(index); }
                        catch { }
                        if (entry == null || string.IsNullOrEmpty(entry.LoopFile) ||
                            !File.Exists(Path.Combine(cache, entry.LoopFile)))
                        {
                            Util.SafeDelete(index);
                            continue;
                        }
                        entries.Add(new KeyValuePair<string, MediaManifest>(index, entry));
                    }
                }
                entries.Sort(delegate(KeyValuePair<string, MediaManifest> a, KeyValuePair<string, MediaManifest> b)
                {
                    return BuiltTime(b.Value).CompareTo(BuiltTime(a.Value));
                });

                HashSet<string> keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (live != null) AddKeep(keep, cache, live);
                foreach (MediaManifest screen in screenLive) AddKeep(keep, cache, screen);

                int kept = 0;
                foreach (KeyValuePair<string, MediaManifest> item in entries)
                {
                    bool isLive = live != null &&
                        string.Equals(item.Value.Signature, live.Signature, StringComparison.OrdinalIgnoreCase);
                    if (!isLive)
                    {
                        foreach (MediaManifest screen in screenLive)
                        {
                            if (screen != null && string.Equals(item.Value.Signature, screen.Signature,
                                StringComparison.OrdinalIgnoreCase))
                            {
                                isLive = true;
                                break;
                            }
                        }
                    }
                    if (kept < CacheKeep || isLive)
                    {
                        AddKeep(keep, cache, item.Value);
                        if (!isLive) kept++;
                    }
                    else
                    {
                        Util.SafeDelete(item.Key);
                    }
                }

                Sweep(cache, keep);
                if (Directory.Exists(loops)) Sweep(loops, keep);
            }
            catch (Exception ex) { Log.Warn("cache cleanup failed: " + ex.Message); }
        }

        private static void AddKeep(HashSet<string> keep, string cache, MediaManifest manifest)
        {
            if (manifest == null) return;
            if (!string.IsNullOrEmpty(manifest.LoopFile))
            {
                string path = Path.Combine(cache, manifest.LoopFile);
                if (File.Exists(path)) keep.Add(Path.GetFullPath(path));
            }
            if (!string.IsNullOrEmpty(manifest.FrameFile))
            {
                string path = Path.Combine(cache, manifest.FrameFile);
                if (File.Exists(path)) keep.Add(Path.GetFullPath(path));
            }
        }

        private static void Sweep(string dir, HashSet<string> keep)
        {
            foreach (string file in Directory.GetFiles(dir, "loop-*.mp4"))
            {
                if (!keep.Contains(Path.GetFullPath(file))) Util.SafeDelete(file);
            }
            foreach (string file in Directory.GetFiles(dir, "frame-*.jpg"))
            {
                if (!keep.Contains(Path.GetFullPath(file))) Util.SafeDelete(file);
            }
            foreach (string file in Directory.GetFiles(dir, "*.tmp.mp4")) Util.SafeDelete(file);
            foreach (string file in Directory.GetFiles(dir, "*.json.tmp")) Util.SafeDelete(file);
        }

        private static DateTime BuiltTime(MediaManifest manifest)
        {
            DateTime when;
            if (manifest != null && !string.IsNullOrEmpty(manifest.BuiltUtc) &&
                DateTime.TryParse(manifest.BuiltUtc, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out when))
            {
                return when;
            }
            return DateTime.MinValue;
        }

        // A file is a candidate when it is not on the ignore list. ffprobe makes
        // the real decision later; a file without a video stream is skipped.
        public static bool IsVideo(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext.Length == 0) return false;
            foreach (string candidate in IgnoredExtensions)
            {
                if (candidate == ext) return false;
            }
            return true;
        }

        public static List<string> ScanVideos(string folder)
        {
            List<string> videos = new List<string>();
            try
            {
                if (!Directory.Exists(folder)) return videos;
                foreach (string file in Directory.GetFiles(folder))
                {
                    if (IsVideo(file)) videos.Add(file);
                }
            }
            catch (Exception ex) { Log.Error("could not scan video folder", ex); }

            videos.Sort(delegate(string a, string b)
            {
                return new NaturalComparer().Compare(Path.GetFileName(a), Path.GetFileName(b));
            });
            return videos;
        }

        private static readonly string[] ImageExtensions =
        {
            ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp"
        };

        // Wallpapers are still images, not the ignored image list used for
        // videos. Only formats Windows can paint on the desktop are included.
        public static bool IsImage(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext.Length == 0) return false;
            foreach (string candidate in ImageExtensions)
            {
                if (candidate == ext) return true;
            }
            return false;
        }

        public static List<string> ScanImages(string folder)
        {
            List<string> images = new List<string>();
            try
            {
                if (!Directory.Exists(folder)) return images;
                foreach (string file in Directory.GetFiles(folder))
                {
                    if (IsImage(file)) images.Add(file);
                }
            }
            catch (Exception ex) { Log.Error("could not scan image folder", ex); }

            images.Sort(delegate(string a, string b)
            {
                return new NaturalComparer().Compare(Path.GetFileName(a), Path.GetFileName(b));
            });
            return images;
        }

        // The images that will actually be shown, in order. In one-image mode
        // this is just the chosen picture. In slideshow mode the saved order and
        // ticks win, and any picture that is not in the saved list is appended
        // ticked so a newly added wallpaper joins the show immediately.
        public static List<string> ResolveImages(string folder, AppConfig config)
        {
            List<string> all = ScanImages(folder);
            if (all.Count == 0) return all;
            if (config == null) return all;

            if (string.Equals(config.ImageMode, "single", StringComparison.OrdinalIgnoreCase))
            {
                string wanted = config.ImageSingle;
                if (!string.IsNullOrEmpty(wanted))
                {
                    foreach (string file in all)
                    {
                        if (string.Equals(Path.GetFileName(file), wanted, StringComparison.OrdinalIgnoreCase))
                        {
                            return new List<string> { file };
                        }
                    }
                }
                return new List<string> { all[0] };
            }

            if (config.ImagePlaylist == null || config.ImagePlaylist.Count == 0) return all;

            Dictionary<string, string> byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in all) byName[Path.GetFileName(file)] = file;

            List<string> ordered = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (PlaylistEntry entry in config.ImagePlaylist)
            {
                if (entry == null || string.IsNullOrEmpty(entry.Name)) continue;
                string name = entry.Name.Trim();
                if (!seen.Add(name)) continue;
                if (!entry.Enabled) continue;
                string file;
                if (byName.TryGetValue(name, out file)) ordered.Add(file);
            }
            foreach (string file in all)
            {
                string name = Path.GetFileName(file);
                if (seen.Add(name)) ordered.Add(file);
            }
            return ordered;
        }

        // The clips that will actually be rendered, in play order. Playlist
        // mode keeps only the ticked clips and respects the saved order; single
        // mode returns the chosen clip (or the first one when it is missing).
        // An empty playlist means "everything in the folder", which keeps old
        // configs and fresh installs working before the first settings save.
        public static List<string> ResolveClips(string folder, AppConfig config)
        {
            List<string> all = ScanVideos(folder);
            if (all.Count == 0) return all;
            if (config == null) return all;

            if (string.Equals(config.PlaybackMode, "single", StringComparison.OrdinalIgnoreCase))
            {
                string wanted = config.SingleClip;
                if (!string.IsNullOrEmpty(wanted))
                {
                    foreach (string file in all)
                    {
                        if (string.Equals(Path.GetFileName(file), wanted, StringComparison.OrdinalIgnoreCase))
                        {
                            return new List<string> { file };
                        }
                    }
                }
                return new List<string> { all[0] };
            }

            if (config.Playlist == null || config.Playlist.Count == 0) return all;

            Dictionary<string, string> byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in all) byName[Path.GetFileName(file)] = file;

            List<string> ordered = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (PlaylistEntry entry in config.Playlist)
            {
                if (entry == null || !entry.Enabled || string.IsNullOrEmpty(entry.Name)) continue;
                string name = entry.Name.Trim();
                if (!seen.Add(name)) continue;
                string file;
                if (byName.TryGetValue(name, out file)) ordered.Add(file);
            }
            return ordered;
        }

        public static string Signature(string folder, AppConfig config)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("v2|").Append(config.RenderWidth).Append('x').Append(config.RenderHeight)
              .Append('|').Append(config.Fps)
              .Append('|').Append(config.CrossfadeSeconds.ToString("0.###", CultureInfo.InvariantCulture))
              .Append('|').Append(config.Crf)
              .Append('|').Append(config.Preset)
              .Append('|').Append(config.PlaybackMode)
              .Append('|');
            if (string.Equals(config.PlaybackMode, "single", StringComparison.OrdinalIgnoreCase))
            {
                sb.Append(config.SingleClip).Append('|');
            }
            foreach (string file in ResolveClips(folder, config))
            {
                FileInfo info = new FileInfo(file);
                sb.Append(Path.GetFileName(file)).Append(':')
                  .Append(info.Length).Append(':')
                  .Append(info.LastWriteTimeUtc.Ticks).Append(';');
            }
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                StringBuilder hex = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) hex.Append(b.ToString("x2"));
                return hex.ToString();
            }
        }

        public static MediaManifest LoadManifest(string appRoot)
        {
            string path = ManifestPath(appRoot);
            if (!File.Exists(path)) return null;
            try { return JsonStore.DeserializeFile<MediaManifest>(path); }
            catch (Exception ex) { Log.Warn("could not read manifest: " + ex.Message); return null; }
        }

        public static MediaManifest LoadScreenManifest(string appRoot, string device)
        {
            string path = ScreenManifestPath(appRoot, device);
            if (!File.Exists(path)) return null;
            try { return JsonStore.DeserializeFile<MediaManifest>(path); }
            catch (Exception ex) { Log.Warn("could not read a screen manifest: " + ex.Message); return null; }
        }

        // Every per-screen "now playing" pointer, for the cache clean-up.
        public static List<MediaManifest> LoadScreenManifests(string appRoot)
        {
            List<MediaManifest> list = new List<MediaManifest>();
            try
            {
                string dir = ScreensDir(appRoot);
                if (!Directory.Exists(dir)) return list;
                foreach (string file in Directory.GetFiles(dir, "*.json"))
                {
                    try
                    {
                        MediaManifest manifest = JsonStore.DeserializeFile<MediaManifest>(file);
                        if (manifest != null) list.Add(manifest);
                    }
                    catch { }
                }
            }
            catch (Exception ex) { Log.Warn("could not read the screen manifests: " + ex.Message); }
            return list;
        }

        // Same checks as IsCacheValid, but against one screen's own manifest
        // and clip selection.
        public static bool IsScreenCacheValid(string appRoot, AppConfig screenConfig, string device,
            out MediaManifest manifest, bool checkSignature)
        {
            manifest = LoadScreenManifest(appRoot, device);
            if (manifest == null) return false;
            if (!File.Exists(LoopPath(appRoot, manifest))) return false;
            if (manifest.Width != screenConfig.RenderWidth || manifest.Height != screenConfig.RenderHeight) return false;
            if (manifest.Fps != screenConfig.Fps) return false;
            if (!checkSignature) return true;
            string folder = Util.ResolvePath(appRoot, screenConfig.VideoFolder);
            return manifest.Signature == Signature(folder, screenConfig);
        }

        public static bool IsCacheValid(string appRoot, AppConfig config, out MediaManifest manifest)
        {
            return IsCacheValid(appRoot, config, out manifest, true);
        }

        // checkSignature = false lets the startup path use a loop immediately and
        // defer the folder scan/hash to the watchdog thread.
        public static bool IsCacheValid(string appRoot, AppConfig config, out MediaManifest manifest, bool checkSignature)
        {
            manifest = LoadManifest(appRoot);
            if (manifest == null) return false;
            if (!File.Exists(LoopPath(appRoot, manifest))) return false;
            if (manifest.Width != config.RenderWidth || manifest.Height != config.RenderHeight) return false;
            if (manifest.Fps != config.Fps) return false;
            if (!checkSignature) return true;
            string folder = Util.ResolvePath(appRoot, config.VideoFolder);
            return manifest.Signature == Signature(folder, config);
        }

        public static BuildResult Build(string appRoot, AppConfig config, Action<string> log, Action<double> progress)
        {
            return Build(appRoot, config, log, progress, null, false, false);
        }

        // The cancel callback lets the caller abandon a build whose settings
        // were replaced while it was running, instead of making the user wait
        // for a loop that will be thrown away.
        public static BuildResult Build(string appRoot, AppConfig config, Action<string> log, Action<double> progress,
            Func<bool> cancelled)
        {
            return Build(appRoot, config, log, progress, cancelled, false, false);
        }

        // force     re-render even when a cached loop exists ("Rebuild now")
        // cacheOnly prepare a reusable loop without replacing the one that is
        //           playing (background prefetch for the other clips)
        public static BuildResult Build(string appRoot, AppConfig config, Action<string> log, Action<double> progress,
            Func<bool> cancelled, bool force, bool cacheOnly)
        {
            return Build(appRoot, config, log, progress, cancelled, force, cacheOnly, null);
        }

        // liveManifestPath selects which "now playing" pointer a finished build
        // writes: the main loop by default, or a screen's own file.
        public static BuildResult Build(string appRoot, AppConfig config, Action<string> log, Action<double> progress,
            Func<bool> cancelled, bool force, bool cacheOnly, string liveManifestPath)
        {
            BuildResult result = new BuildResult();
            string folder = Util.ResolvePath(appRoot, config.VideoFolder);
            List<string> videos = ResolveClips(folder, config);
            if (videos.Count == 0)
            {
                if (config.Playlist != null && config.Playlist.Count > 0 &&
                    !string.Equals(config.PlaybackMode, "single", StringComparison.OrdinalIgnoreCase))
                {
                    result.Message = "No clips are selected in the playlist";
                }
                else
                {
                    result.Message = "No videos found in " + folder;
                }
                return result;
            }

            string cache = CacheDir(appRoot);
            string loops = LoopsDir(appRoot);
            string signature = Signature(folder, config);

            // A cached loop is already the finished product: skip probing and
            // encoding entirely. This is what makes switching back instant.
            if (!force)
            {
                MediaManifest cachedManifest = LoadCachedManifest(appRoot, signature);
                if (cachedManifest != null)
                {
                    string cachedLoop = Path.Combine(cache, cachedManifest.LoopFile);
                    string cachedFrame = string.IsNullOrEmpty(cachedManifest.FrameFile)
                        ? "" : Path.Combine(cache, cachedManifest.FrameFile);
                    if (!string.IsNullOrEmpty(cachedFrame) && !File.Exists(cachedFrame))
                    {
                        EnsureFrame(Paths.Tool(config.FfmpegPath), loops, cachedLoop, cachedFrame);
                    }
                    if (!cacheOnly)
                    {
                        SaveManifest(liveManifestPath ?? ManifestPath(appRoot), cachedManifest);
                        CleanupCache(appRoot);
                    }
                    result.Success = true;
                    result.LoopPath = cachedLoop;
                    result.FramePath = !string.IsNullOrEmpty(cachedFrame) && File.Exists(cachedFrame) ? cachedFrame : "";
                    result.Duration = cachedManifest.OutputDuration;
                    result.Message = "cached loop ready: " +
                        cachedManifest.OutputDuration.ToString("0.0", CultureInfo.InvariantCulture) + " s";
                    Log.Info(result.Message);
                    return result;
                }
            }

            string ffmpeg = Paths.Tool(config.FfmpegPath);
            string ffprobe = Paths.Tool(config.FfprobePath);
            if (!File.Exists(ffmpeg)) { result.Message = "ffmpeg not found: " + ffmpeg; return result; }
            if (!File.Exists(ffprobe)) { result.Message = "ffprobe not found: " + ffprobe; return result; }

            List<double> durations = new List<double>();
            List<string> usable = new List<string>();
            foreach (string video in videos)
            {
                if (cancelled != null && cancelled())
                {
                    result.Cancelled = true;
                    result.Message = "superseded by newer settings";
                    return result;
                }

                double duration;
                string error;
                if (!ProbeDuration(ffprobe, video, out duration, out error))
                {
                    // One odd file must never stop the wallpaper. Log it and
                    // carry on with the clips that do work.
                    if (log != null)
                    {
                        log("Skipping " + Path.GetFileName(video) + ": " + error);
                    }
                    continue;
                }
                if (duration < 1.0)
                {
                    if (log != null)
                    {
                        log("Skipping " + Path.GetFileName(video) + ": too short (" +
                            duration.ToString("0.0", CultureInfo.InvariantCulture) + " s)");
                    }
                    continue;
                }
                usable.Add(video);
                durations.Add(duration);
                if (log != null)
                {
                    log(string.Format("video: {0} ({1:0.0} s)",
                        Path.GetFileName(video), duration));
                }
            }

            if (usable.Count == 0)
            {
                result.Message = "No playable videos in " + folder +
                    ". Files that cannot be read are skipped; check logs\\app.log.";
                return result;
            }
            videos = usable;
            int count = videos.Count;
            double[] d = new double[count];
            for (int i = 0; i < count; i++)
            {
                double next = durations[(i + 1) % count];
                double value = config.CrossfadeSeconds;
                if (value > durations[i] / 3.0) value = durations[i] / 3.0;
                if (value > next / 3.0) value = next / 3.0;
                if (value < 0.3) value = 0.3;
                d[i] = Math.Round(value, 3);
            }

            double total = 0;
            for (int i = 0; i < count; i++) total += durations[i] - d[i];
            result.Duration = total;

            if (log != null)
            {
                log(string.Format("building loop: {0} video(s), {1:0.0} s, {2}x{3} @ {4} fps",
                    count, total, config.RenderWidth, config.RenderHeight, config.Fps));
            }

            string filter = BuildFilter(videos, durations, d, config);
            Directory.CreateDirectory(cache);
            Directory.CreateDirectory(loops);

            // Versioned output names mean a rebuild never has to overwrite the
            // file mpv is currently playing.
            string stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
            string shortSignature = signature.Substring(0, 12);
            string baseName = "loop-" + shortSignature + "-" + stamp;
            string temp = Path.Combine(loops, baseName + ".tmp.mp4");
            string loop = Path.Combine(loops, baseName + ".mp4");
            string frame = Path.Combine(loops, "frame-" + shortSignature + "-" + stamp + ".jpg");
            Util.SafeDelete(temp);

            string args = string.Format(
                "-hide_banner -y -nostats -progress pipe:1 {0} -filter_complex {1} -map [out] -an -c:v libx264 -preset {2} -crf {3} " +
                "-pix_fmt yuv420p -movflags +faststart -r {4} {5}",
                BuildInputs(videos),
                Util.Quote(filter),
                config.Preset,
                config.Crf,
                config.Fps,
                Util.Quote(temp));

            Log.Info("ffmpeg " + args);
            ProcessResult render = ProcessRunner.RunWithLineCallback(ffmpeg, args, loops, 6 * 60 * 60 * 1000,
                delegate(string line)
                {
                    if (progress != null && total > 0)
                    {
                        double seconds;
                        if (TryParseFfmpegTime(line, out seconds))
                        {
                            double fraction = seconds / total;
                            if (fraction < 0) fraction = 0;
                            if (fraction > 1) fraction = 1;
                            progress(fraction);
                        }
                    }
                }, cancelled,
                cacheOnly ? ProcessPriorityClass.BelowNormal : ProcessPriorityClass.Normal);

            if (render.Cancelled)
            {
                result.Cancelled = true;
                result.Message = "superseded by newer settings";
                Log.Info("build stopped: " + result.Message);
                Util.SafeDelete(temp);
                return result;
            }

            if (render.ExitCode != 0 || !File.Exists(temp) || new FileInfo(temp).Length < 1024)
            {
                result.Message = "ffmpeg failed (exit " + render.ExitCode + "): " + LastLines(render.StandardError, 8);
                Log.Error(result.Message);
                Util.SafeDelete(temp);
                return result;
            }

            try
            {
                File.Move(temp, loop);
            }
            catch (Exception ex)
            {
                result.Message = "could not save the loop: " + ex.Message;
                Log.Error(result.Message);
                Util.SafeDelete(temp);
                return result;
            }

            // Desktop wallpaper: frame 0 of the finished loop, so login and video
            // hand-off match exactly.
            bool haveFrame = EnsureFrame(ffmpeg, loops, loop, frame);
            if (!haveFrame) frame = "";

            MediaManifest manifest = new MediaManifest();
            manifest.Signature = signature;
            manifest.Width = config.RenderWidth;
            manifest.Height = config.RenderHeight;
            manifest.Fps = config.Fps;
            manifest.Crossfade = config.CrossfadeSeconds;
            manifest.Crf = config.Crf;
            manifest.Preset = config.Preset;
            manifest.OutputDuration = total;
            manifest.BuiltUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            manifest.LoopFile = Path.Combine("loops", Path.GetFileName(loop));
            manifest.FrameFile = haveFrame ? Path.Combine("loops", Path.GetFileName(frame)) : "";

            // Remember this result for instant reuse. cacheOnly builds only
            // fill the cache; they must not change what is on screen.
            SaveManifest(CacheIndexPath(appRoot, signature), manifest);
            if (!cacheOnly)
            {
                SaveManifest(liveManifestPath ?? ManifestPath(appRoot), manifest);
                CleanupCache(appRoot);
            }

            result.Success = true;
            result.LoopPath = loop;
            result.FramePath = frame;
            result.Message = string.Format("loop ready: {0:0.0} s, {1}",
                total, Path.GetFileName(loop));
            Log.Info(result.Message);
            return result;
        }

        // Extracts the first frame of the loop into the wallpaper image. Used by
        // both fresh builds and cache hits whose frame was cleaned up.
        private static bool EnsureFrame(string ffmpeg, string workingDirectory, string loop, string frame)
        {
            if (File.Exists(frame)) return true;
            if (!File.Exists(ffmpeg)) return false;
            string args = string.Format("-hide_banner -y -i {0} -frames:v 1 -q:v 2 {1}",
                Util.Quote(loop), Util.Quote(frame));
            ProcessResult result = ProcessRunner.Run(ffmpeg, args, workingDirectory, 120000);
            if (result.ExitCode != 0 || !File.Exists(frame))
            {
                Log.Warn("could not extract wallpaper frame: " + LastLines(result.StandardError, 4));
                return false;
            }
            return true;
        }

        private static void SaveManifest(string path, MediaManifest manifest)
        {
            string temp = path + ".tmp";
            JsonStore.SerializeFile(temp, manifest);
            Util.SafeDelete(path);
            File.Move(temp, path);
        }

        private static string BuildInputs(List<string> videos)
        {
            StringBuilder sb = new StringBuilder();
            foreach (string video in videos)
            {
                sb.Append("-i ").Append(Util.Quote(video)).Append(' ');
            }
            return sb.ToString();
        }

        private static string BuildFilter(List<string> videos, List<double> durations, double[] d, AppConfig config)
        {
            int n = videos.Count;
            StringBuilder sb = new StringBuilder();
            string chain = string.Format("fps={0},scale={1}:{2}:force_original_aspect_ratio=increase,crop={1}:{2},setsar=1,format=yuv420p",
                config.Fps, config.RenderWidth, config.RenderHeight);

            for (int i = 0; i < n; i++)
            {
                double dIn = d[(i - 1 + n) % n];
                double dOut = d[i];
                double length = durations[i];
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "[{0}:v]{1},split=3[h{0}][t{0}][m{0}];", i, chain);
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "[h{0}]trim=start=0:end={1:0.###},setpts=PTS-STARTPTS,settb=1/{2}[head{0}];",
                    i, dIn, config.Fps);
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "[t{0}]trim=start={1:0.###}:end={2:0.###},setpts=PTS-STARTPTS,settb=1/{3}[tail{0}];",
                    i, length - dOut, length, config.Fps);
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "[m{0}]trim=start={1:0.###}:end={2:0.###},setpts=PTS-STARTPTS,settb=1/{3}[mid{0}];",
                    i, dIn, length - dOut, config.Fps);
            }

            for (int i = 0; i < n; i++)
            {
                int next = (i + 1) % n;
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "[tail{0}][head{1}]xfade=transition=fade:duration={2:0.###}:offset=0[blend{0}];",
                    i, next, d[i]);
            }

            StringBuilder order = new StringBuilder();
            for (int i = 0; i < n; i++)
            {
                order.AppendFormat("[mid{0}][blend{0}]", i);
            }
            order.AppendFormat("concat=n={0}:v=1:a=0[out]", n * 2);
            sb.Append(order);
            return sb.ToString();
        }

        public static bool ProbeDuration(string ffprobe, string video, out double duration, out string error)
        {
            duration = 0;
            error = "";
            string args = string.Format(
                "-v error -select_streams v:0 -show_entries stream=duration " +
                "-show_entries format=duration -of json {0}", Util.Quote(video));
            ProcessResult result = ProcessRunner.Run(ffprobe, args, null, 60000);
            if (result.ExitCode != 0)
            {
                error = LastLines(result.StandardError, 3);
                return false;
            }
            try
            {
                ProbeResult probe = JsonStore.Deserialize<ProbeResult>(result.StandardOutput);
                // An audio file, a document or a broken download has no video
                // stream. Say so instead of letting ffmpeg fail later with a
                // much less helpful message.
                if (probe.Streams == null || probe.Streams.Length == 0)
                {
                    error = "no video stream";
                    return false;
                }
                if (probe.Format != null && !string.IsNullOrEmpty(probe.Format.Duration))
                {
                    double.TryParse(probe.Format.Duration, NumberStyles.Float, CultureInfo.InvariantCulture, out duration);
                }
                if (duration <= 0 && probe.Streams != null)
                {
                    foreach (ProbeStream stream in probe.Streams)
                    {
                        if (!string.IsNullOrEmpty(stream.Duration))
                        {
                            double.TryParse(stream.Duration, NumberStyles.Float, CultureInfo.InvariantCulture, out duration);
                            if (duration > 0) break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            if (duration <= 0)
            {
                error = "no duration in ffprobe output";
                return false;
            }
            return true;
        }

        private static bool TryParseFfmpegTime(string line, out double seconds)
        {
            seconds = 0;
            int index = line.IndexOf("time=", StringComparison.OrdinalIgnoreCase);
            if (index < 0) return false;
            string value = line.Substring(index + 5).Trim();
            int space = value.IndexOf(' ');
            if (space > 0) value = value.Substring(0, space);
            string[] parts = value.Split(':');
            if (parts.Length != 3) return false;
            double h, m, s;
            if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out h)) return false;
            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out m)) return false;
            if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out s)) return false;
            seconds = h * 3600 + m * 60 + s;
            return true;
        }

        private static string LastLines(string text, int count)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string[] lines = text.Replace("\r", "").Split('\n');
            int start = lines.Length - count;
            if (start < 0) start = 0;
            StringBuilder sb = new StringBuilder();
            for (int i = start; i < lines.Length; i++)
            {
                if (lines[i].Trim().Length == 0) continue;
                if (sb.Length > 0) sb.Append(" | ");
                sb.Append(lines[i].Trim());
            }
            return sb.ToString();
        }
    }
}
