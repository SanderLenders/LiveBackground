using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;

namespace LiveBackground
{
    public static class Program
    {
        private static string _appRoot;
        private static AppConfig _config;
        private static MpvPlayer _player;
        private static IntPtr _progman = IntPtr.Zero;
        private static string _mediaPath = "";

        private static readonly object Sync = new object();
        private static bool _building;
        // Signature of the build the background worker is currently
        // running, so a settings apply can tell "this exact build is
        // already running" from "the running build is now outdated".
        private static string _buildingSignature;
        private static bool _dirty;
        private static DateTime _nextCheck = DateTime.MinValue;
        private static BuildResult _pendingBuild;
        private static DateTime _lastWatcherEvent = DateTime.MinValue;
        private static string _lastMissingLog = "";
        private static volatile bool _paused;
        private static volatile bool _resumeFromPause;
        private static volatile bool _forceBuild;
        private static volatile int _buildGeneration;
        private static volatile bool _prefetching;
        private static volatile bool _prefetchCancel;
        private static volatile bool _restartPlayer;
        private static bool _prefetchDone;
        private static DateTime _prefetchAfter = DateTime.MaxValue;
        private static FileSystemWatcher _watcher;
        private static string _watchedFolder = "";
        private static FileSystemWatcher _imageWatcher;
        private static string _watchedImageFolder = "";
        private static bool _imageListDirty;
        private static bool _imageActive;
        private static DateTime _nextImageAt = DateTime.MinValue;
        private static string _currentImage = "";
        private static string _surfaceImage = "";
        private static ImageSurface _imageSurface;
        private static IntPtr _imageProgman = IntPtr.Zero;
        private static string _lastMissingImageLog = "";
        private static readonly Random ImageRandom = new Random();
        private static bool _cleanupDone;
        private static DateTime _processStartUtc;

        public static string AppRoot { get { return _appRoot; } }
        public static AppConfig Config { get { return _config; } }
        public static bool IsPaused { get { return _paused; } }

        public static bool IsImagesMode
        {
            get
            {
                return _config != null &&
                    string.Equals(_config.WallpaperMode, "images", StringComparison.OrdinalIgnoreCase);
            }
        }

        public static void SetPaused(bool paused)
        {
            _paused = paused;
            if (paused)
            {
                // A later resume is expected to bring the video back, which is
                // not the same as recovering a crashed player.
                _resumeFromPause = true;
            }
            else
            {
                _nextCheck = DateTime.UtcNow;
                if (IsImagesMode) _nextImageAt = DateTime.UtcNow;
            }
            Log.Info(paused ? "paused by the user" : "resumed by the user");
            Ui.RefreshMenu();
        }

        private static bool HasTickedEntry(List<PlaylistEntry> playlist)
        {
            if (playlist == null) return false;
            foreach (PlaylistEntry entry in playlist)
            {
                if (entry != null && entry.Enabled) return true;
            }
            return false;
        }

        public static void RequestRebuild()
        {
            _forceBuild = true;
            _nextCheck = DateTime.UtcNow;
            Log.Info("rebuild requested from the tray");

            bool building;
            lock (Sync) { building = _building; }
            if (!building)
            {
                Ui.BuildProgressStart("Rebuilding the wallpaper video");
                Ui.BuildProgressUpdate(0, "Preparing...");
            }
        }

        // Used by the Images tab and the tray: move to the next still picture
        // without waiting for the interval. One-image mode simply reapplies it.
        public static void RequestNextImage()
        {
            _imageListDirty = false;
            _nextImageAt = DateTime.UtcNow;
            _imageActive = true;
            Log.Info("next image requested");
        }

        public static void RequestExit()
        {
            Log.Info("exit requested from the tray");
            try { File.WriteAllText(Installer.QuitFlag(_appRoot), "quit"); }
            catch { }
            try { File.WriteAllText(Installer.StopFlag(_appRoot), "stop"); }
            catch { }
            Ui.Stop();
        }

        // Called by the settings window after it saves config.json. The live
        // config object is mutated in place so the player keeps a valid
        // reference, then a rebuild is forced.
        public static void ApplyConfig(AppConfig updated)
        {
            if (updated == null || _config == null) return;
            bool wasImages = IsImagesMode;
            // The screen-power option is an mpv argument, so a changed value
            // needs a player restart rather than a new loop.
            bool screenChanged = _config.ScreensaverActive != updated.ScreensaverActive;
            bool lockChanged = !string.Equals(_config.LockScreenMode, updated.LockScreenMode,
                StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(_config.LockScreenImage ?? "", updated.LockScreenImage ?? "",
                StringComparison.OrdinalIgnoreCase);
            _config.CopyFrom(updated);
            if (lockChanged) StartLockScreenWorker();

            if (IsImagesMode)
            {
                // Images mode is deliberately independent of mpv and ffmpeg:
                // stop the video, cancel any loop work and show the selected
                // picture (or the first slideshow picture) right away.
                _buildGeneration++;
                CancelPrefetch();
                _prefetchDone = false;
                _prefetchAfter = DateTime.UtcNow.AddSeconds(30);
                _nextCheck = DateTime.UtcNow;
                if (_player != null) _player.Stop();
                _imageActive = false;
                _nextImageAt = DateTime.UtcNow;
                string imageFolder = Util.ResolvePath(_appRoot, _config.ImageFolder);
                if (!string.Equals(imageFolder, _watchedImageFolder, StringComparison.OrdinalIgnoreCase))
                {
                    StartImageWatcher(imageFolder);
                }
                _imageProgman = Native.FindWindow("Progman", null);
                StartImageSurface(_imageProgman);
                ApplyImage(false);
                _imageActive = true;
                if (_imageSurface != null && !string.IsNullOrEmpty(_currentImage))
                {
                    _imageSurface.EnsureVisible();
                }
                Log.Info("settings applied; images mode is active");
                Ui.BuildProgressFinish(true, "Wallpaper updated");
                Ui.RefreshMenu();
                return;
            }

            _imageActive = false;
            _nextImageAt = DateTime.MinValue;
            if (_imageSurface != null)
            {
                _imageSurface.Hide();
                _surfaceImage = "";
            }
            // Switching back from Images mode has to start the cached loop
            // again; the player was stopped when images mode took over.
            if (screenChanged || wasImages) _restartPlayer = true;
            // A build for exactly this configuration may already be running
            // ("Rebuild now" in the settings window followed by "Save and
            // apply"); cancelling it would throw away its encode, so let it
            // finish instead of restarting from zero.
            bool sameBuildRunning = false;
            try
            {
                string signatureNow = MediaPipeline.Signature(
                    Util.ResolvePath(_appRoot, updated.VideoFolder), updated);
                lock (Sync)
                {
                    sameBuildRunning = _building && signatureNow != null &&
                        string.Equals(signatureNow, _buildingSignature, StringComparison.Ordinal);
                }
            }
            catch { }
            if (!sameBuildRunning) _buildGeneration++;
            _nextCheck = DateTime.UtcNow;
            _prefetchDone = false;
            _prefetchAfter = DateTime.UtcNow.AddSeconds(30);
            CancelPrefetch();
            Log.Info(wasImages
                ? "settings applied; switching back to the cached video loop"
                : "settings applied; checking the loop");

            // Only show the progress card when something actually has to be
            // rendered. Switching to a cached clip is instant, and a save that
            // only changes the lock screen should not flash a card at all.
            bool needsBuild = false;
            string folder = Util.ResolvePath(_appRoot, _config.VideoFolder);
            if (MediaPipeline.ResolveClips(folder, _config).Count > 0)
            {
                string signature = MediaPipeline.Signature(folder, _config);
                if (!MediaPipeline.HasCachedLoop(_appRoot, signature))
                {
                    MediaManifest manifest = MediaPipeline.LoadManifest(_appRoot);
                    bool valid = manifest != null && manifest.Signature == signature &&
                        File.Exists(MediaPipeline.LoopPath(_appRoot, manifest));
                    needsBuild = !valid;
                }
            }

            if (needsBuild)
            {
                Ui.BuildProgressStart("Applying new settings");
                Ui.BuildProgressUpdate(0, "Preparing the new loop...");
            }
            else
            {
                // This save may have cancelled a build that was running; make
                // sure its card does not linger.
                Ui.BuildProgressFinish(true, "Wallpaper updated");
            }

            // Apply a changed desktop wallpaper right away, so the user does
            // not have to wait for the rebuild to finish.
            string frame = MediaPipeline.FramePath(_appRoot);
            string wallpaper = DesktopHost.WallpaperFor(_appRoot, _config, frame);
            if (!string.IsNullOrEmpty(wallpaper)) DesktopHost.SetDesktopWallpaper(wallpaper);

            Ui.RefreshMenu();
        }

        public static int Main(string[] args)
        {
            _processStartUtc = DateTime.UtcNow;
            try { _processStartUtc = Process.GetCurrentProcess().StartTime.ToUniversalTime(); } catch { }

            string programRoot = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            Paths.Init(programRoot);
            _appRoot = Paths.DataRoot;
            Log.Init(_appRoot);

            // Leave a readable trace if anything escapes the normal paths.
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
            {
                Log.Error("unhandled exception", e.ExceptionObject as Exception);
            };

            // The logon task also schedules this lightweight check. It only
            // starts the wallpaper when the tray app is missing, so a crash is
            // recovered without ever fighting the single-instance mutex.
            if (HasArg(args, "--watchdog"))
            {
                return RunWatchdog();
            }

            // "LiveBackground.exe --settings" opens the dialog. If another
            // instance is running, the flag file tells it to show the dialog.
            bool settingsRequested = HasArg(args, "--settings");
            if (settingsRequested)
            {
                try { File.WriteAllText(Installer.SettingsFlag(_appRoot), "open"); }
                catch { }
            }

            if (HasArg(args, "--stop"))
            {
                try { File.WriteAllText(Installer.QuitFlag(_appRoot), "quit"); }
                catch { }
                try { File.WriteAllText(Installer.StopFlag(_appRoot), "stop"); }
                catch { }
                Log.Info("stop requested");
                return 0;
            }

            // Scriptable equivalents of the tray menu. Each writes a flag that
            // the running instance picks up within half a second.
            if (HasArg(args, "--pause"))
            {
                try { File.WriteAllText(Installer.PauseFlag(_appRoot), "pause"); }
                catch { }
                Log.Info("pause requested");
                return 0;
            }

            if (HasArg(args, "--resume"))
            {
                try { File.WriteAllText(Installer.ResumeFlag(_appRoot), "resume"); }
                catch { }
                Log.Info("resume requested");
                return 0;
            }

            if (HasArg(args, "--rebuild"))
            {
                try { File.WriteAllText(Installer.RebuildFlag(_appRoot), "rebuild"); }
                catch { }
                Log.Info("rebuild requested");
                return 0;
            }

            bool createdNew;
            using (Mutex mutex = new Mutex(true, @"Local\LiveBackgroundApp", out createdNew))
            {
                if (!createdNew)
                {
                    // "--settings" is the expected way to open the dialog on a
                    // running wallpaper, so it is not a second instance.
                    if (settingsRequested) Log.Info("settings requested for the running instance");
                    else Log.Info("another wallpaper instance is already running, exiting");
                    return 0;
                }
                try { return Run(); }
                catch (Exception ex)
                {
                    Log.Error("fatal error", ex);
                    return 1;
                }
                finally
                {
                    if (_player != null) _player.Stop();
                    if (_imageSurface != null) _imageSurface.Shutdown();
                    Ui.Stop();
                    try { mutex.ReleaseMutex(); } catch { }
                }
            }
        }

        // Runs from the scheduled watchdog task. Does nothing when the app is
        // healthy or when the user exited from the tray on purpose.
        private static int RunWatchdog()
        {
            try
            {
                if (File.Exists(Installer.QuitFlag(_appRoot)))
                {
                    Log.Info("watchdog: wallpaper was stopped by the user, not restarting");
                    return 0;
                }

                try
                {
                    using (Mutex existing = Mutex.OpenExisting(@"Local\LiveBackgroundApp"))
                    {
                        // The wallpaper is already running.
                        return 0;
                    }
                }
                catch (WaitHandleCannotBeOpenedException)
                {
                    // Not running: start it below.
                }

                string exe = Installer.AppExe(_appRoot);
                if (!File.Exists(exe))
                {
                    Log.Warn("watchdog: " + exe + " not found");
                    return 1;
                }
                ProcessStartInfo info = new ProcessStartInfo(exe, "--startup");
                info.UseShellExecute = true;
                info.WorkingDirectory = Paths.ProgramRoot;
                Process.Start(info);
                Log.Info("watchdog: wallpaper was missing, started it again");
                return 0;
            }
            catch (Exception ex)
            {
                Log.Error("watchdog failed", ex);
                return 1;
            }
        }

        private static int Run()
        {
            try { Native.SetProcessDPIAware(); } catch { }

            Log.Info("=== LiveBackground starting ===");
            // Prepare loops for the other clips only after the wallpaper is up,
            // so the prefetch never competes with logon.
            _prefetchAfter = DateTime.UtcNow.AddSeconds(60);
            Util.SafeDelete(Installer.StopFlag(_appRoot));
            Util.SafeDelete(Installer.PauseFlag(_appRoot));
            Util.SafeDelete(Installer.ResumeFlag(_appRoot));
            Util.SafeDelete(Installer.RebuildFlag(_appRoot));
            // A normal start (logon task, tray restart or watchdog) cancels the
            // "the user closed it on purpose" marker.
            Util.SafeDelete(Installer.QuitFlag(_appRoot));

            bool firstRun = !File.Exists(ConfigStore.PathFor(_appRoot));
            _config = ConfigStore.LoadOrCreate(_appRoot);
            bool imagesMode = IsImagesMode;
            string folder = Util.ResolvePath(_appRoot, _config.VideoFolder);
            string imageFolder = Util.ResolvePath(_appRoot, _config.ImageFolder);
            Log.Info("video folder: " + folder);
            Log.Info("image folder: " + imageFolder);
            try { Directory.CreateDirectory(imageFolder); }
            catch (Exception ex) { Log.Warn("could not create the image folder: " + ex.Message); }
            if (firstRun)
            {
                try { Installer.FirstRun(_appRoot, _config, Log.Info); }
                catch (Exception ex) { Log.Warn("first-run setup failed: " + ex.Message); }
            }
            SeedPlaylistIfNeeded(folder);

            // Read the manifest once and use it for the frame, the loop and the
            // quick cache check. Reading it repeatedly is expensive at logon.
            MediaManifest manifest = MediaPipeline.LoadManifest(_appRoot);
            if (manifest != null) MediaPipeline.SeedCacheIndex(_appRoot, manifest.Signature);
            string frame = MediaPipeline.FramePath(_appRoot, manifest);
            string wallpaper = imagesMode ? "" : DesktopHost.WallpaperFor(_appRoot, _config, frame);

            string loop = MediaPipeline.LoopPath(_appRoot, manifest);
            bool loopExists = File.Exists(loop);
            if (loopExists && manifest != null &&
                manifest.Width == _config.RenderWidth &&
                manifest.Height == _config.RenderHeight &&
                manifest.Fps == _config.Fps)
            {
                _mediaPath = loop;
                Log.Info("using cached loop");

                // The folder signature check is deliberately deferred. mpv can
                // start now; the watchdog validates the cache a few seconds
                // later and rebuilds in the background if anything changed.
                _nextCheck = DateTime.UtcNow.AddSeconds(10);
            }
            else if (loopExists)
            {
                _mediaPath = loop;
                _dirty = true;
                _nextCheck = DateTime.UtcNow.AddSeconds(10);
                Log.Info("using existing loop while a rebuild is prepared");
            }
            else
            {
                _dirty = true;
                Log.Info("no loop yet, building in the background");
            }

            _player = new MpvPlayer(_appRoot, _config);

            // The lock screen and tray are not needed for the first wallpaper
            // frame. Starting them later keeps them from competing with the
            // first cold disk read.
            StartLockScreenWorker();

            if (imagesMode)
            {
                // No mpv, no WorkerW layer and no loop work at all. The still
                // image goes up immediately and the rotation timer takes over.
                _imageProgman = DesktopHost.WaitForProgman(30000);
                if (_imageProgman == IntPtr.Zero)
                {
                    Log.Error("Progman not found for the image layer; the wallpaper fallback is used");
                }
                StartImageSurface(_imageProgman);
                StartWatcher(folder);
                StartImageWatcher(imageFolder);
                ApplyImage(false);
                _imageActive = true;
                Log.Info("images mode: showing " +
                    (string.IsNullOrEmpty(_currentImage) ? "no picture" : Path.GetFileName(_currentImage)));
                Ui.Start(_appRoot);
                return WatchLoop();
            }

            _progman = DesktopHost.WaitForProgman(30000);
            if (_progman == IntPtr.Zero)
            {
                Log.Error("Progman not found, the desktop is not ready");
            }
            else
            {
                Log.Info("Progman = 0x" + _progman.ToString("X"));
                if (!string.IsNullOrEmpty(_mediaPath))
                {
                    _player.Start(_progman, _mediaPath);
                }

                // Create the WorkerW wallpaper layer while mpv is still loading.
                StartWallpaperLayerWarmup(_progman);
            }

            // The static frame hides the wait for mpv, but it does not need to
            // block the startup path.
            if (!string.IsNullOrEmpty(wallpaper))
            {
                string path = wallpaper;
                ThreadPool.QueueUserWorkItem(delegate { DesktopHost.SetDesktopWallpaper(path); });
            }

            StartWatcher(folder);
            StartImageWatcher(imageFolder);
            Ui.Start(_appRoot);
            return WatchLoop();
        }

        private static int WatchLoop()
        {
            bool firstPlacementLogged = false;
            bool startDeltaLogged = false;

            while (true)
            {
                if (File.Exists(Installer.StopFlag(_appRoot)))
                {
                    Log.Info("stop flag detected, exiting");
                    Util.SafeDelete(Installer.StopFlag(_appRoot));
                    if (_player != null) _player.Stop();
                    return 0;
                }

                if (File.Exists(Installer.SettingsFlag(_appRoot)))
                {
                    // Only clear the request once the tray thread is ready.
                    if (Ui.ShowSettings()) Util.SafeDelete(Installer.SettingsFlag(_appRoot));
                }

                if (File.Exists(Installer.PauseFlag(_appRoot)))
                {
                    Util.SafeDelete(Installer.PauseFlag(_appRoot));
                    SetPaused(true);
                }

                if (File.Exists(Installer.ResumeFlag(_appRoot)))
                {
                    Util.SafeDelete(Installer.ResumeFlag(_appRoot));
                    SetPaused(false);
                }

                if (File.Exists(Installer.RebuildFlag(_appRoot)))
                {
                    Util.SafeDelete(Installer.RebuildFlag(_appRoot));
                    RequestRebuild();
                }

                // Pausing keeps the process (and the tray icon) alive, but
                // stops mpv so the static desktop wallpaper shows through.
                if (_paused && _player != null && _player.IsRunning)
                {
                    _player.Stop();
                    _resumeFromPause = true;
                }

                // Images mode never starts mpv or touches the video cache. The
                // same watchdog loop just advances the wallpaper on its timer.
                if (IsImagesMode)
                {
                    try
                    {
                        IntPtr current = Native.FindWindow("Progman", null);
                        if (current != IntPtr.Zero && current != _imageProgman)
                        {
                            // The first sighting just adopts the handle; the
                            // surface itself logs its attach. A later change
                            // means Explorer was restarted.
                            if (_imageProgman != IntPtr.Zero)
                            {
                                Log.Info("Explorer restarted, reattaching the image layer");
                            }
                            _imageProgman = current;
                            if (_imageSurface != null) _imageSurface.Attach(current);
                        }

                        if (_paused)
                        {
                            // Pausing must reveal the real Windows wallpaper,
                            // which always holds the last picture.
                            if (_imageSurface != null) _imageSurface.Hide();
                        }
                        else
                        {
                            if (_imageSurface != null && !string.IsNullOrEmpty(_currentImage))
                            {
                                _imageSurface.EnsureVisible();
                            }
                            UpdateImageSlideshow();
                        }
                    }
                    catch (Exception ex) { Log.Error("image rotation error", ex); }
                    Thread.Sleep(300);
                    continue;
                }

                try
                {
                    IntPtr current = Native.FindWindow("Progman", null);
                    if (current == IntPtr.Zero)
                    {
                        Thread.Sleep(100);
                        continue;
                    }
                    if (_progman != IntPtr.Zero && current != _progman)
                    {
                        Log.Info("Explorer restarted, restarting the video");
                        _progman = current;
                        if (!_paused && !string.IsNullOrEmpty(_mediaPath)) _player.Start(_progman, _mediaPath);
                        firstPlacementLogged = false;
                    }
                    else if (_progman == IntPtr.Zero)
                    {
                        _progman = current;
                    }

                    // Pick up a finished background build.
                    BuildResult finished = null;
                    lock (Sync)
                    {
                        if (_pendingBuild != null)
                        {
                            finished = _pendingBuild;
                            _pendingBuild = null;
                        }
                    }
                    if (finished != null)
                    {
                        if (finished.Cancelled)
                        {
                            Log.Info("build replaced: " + finished.Message);
                            Ui.BuildProgressUpdate(0, "Restarting with the new settings...");
                        }
                        else if (finished.Success)
                        {
                            Log.Info("rebuild finished: " + finished.Message);
                            _mediaPath = finished.LoopPath;
                            if (_player.IsRunning) _player.Stop();
                            if (!_paused) _player.Start(_progman, _mediaPath);
                            string wallpaper = DesktopHost.WallpaperFor(_appRoot, _config, finished.FramePath);
                            if (!string.IsNullOrEmpty(wallpaper)) DesktopHost.SetDesktopWallpaper(wallpaper);
                            // The loop's first frame just changed with the
                            // rebuild, so the lock screen has to be updated
                            // when it uses that frame.
                            StartLockScreenWorker();
                            firstPlacementLogged = false;
                            Ui.BuildProgressFinish(true, "Wallpaper updated");
                        }
                        else
                        {
                            Log.Warn("rebuild failed: " + finished.Message);
                            Ui.BuildProgressFinish(false, "Build failed - see logs\\app.log");
                            _nextCheck = DateTime.UtcNow.AddSeconds(60);
                        }
                    }

                    // The screen-power option is passed to mpv at start, so the
                    // running player is replaced when it changes.
                    if (_restartPlayer)
                    {
                        _restartPlayer = false;
                        if (!_paused && _player != null &&
                            _progman != IntPtr.Zero && !string.IsNullOrEmpty(_mediaPath) &&
                            File.Exists(_mediaPath))
                        {
                            Log.Info("restarting the video");
                            _player.Start(_progman, _mediaPath);
                            firstPlacementLogged = false;
                        }
                    }

                    // Follow the video folder if it was changed in the settings.
                    string activeFolder = Util.ResolvePath(_appRoot, _config.VideoFolder);
                    if (!string.Equals(activeFolder, _watchedFolder, StringComparison.OrdinalIgnoreCase))
                    {
                        StartWatcher(activeFolder);
                        _forceBuild = true;
                        _prefetchDone = false;
                        _prefetchAfter = DateTime.UtcNow.AddSeconds(30);
                    }

                    // Rebuild when the folder changed, the user asked for it, or
                    // the cache is stale.
                    if (!_building && (_forceBuild || DateTime.UtcNow >= _nextCheck))
                    {
                        _nextCheck = DateTime.UtcNow.AddSeconds(10);
                        bool needsBuild = false;
                        bool force = false;
                        if (_forceBuild)
                        {
                            _forceBuild = false;
                            needsBuild = true;
                            force = true;
                        }
                        else if (_dirty)
                        {
                            if ((DateTime.UtcNow - _lastWatcherEvent).TotalSeconds >= 3)
                            {
                                _dirty = false;
                                // A file that is not in the playlist changes
                                // nothing, so check the signature before
                                // throwing away the current loop.
                                MediaManifest check;
                                if (!MediaPipeline.IsCacheValid(_appRoot, _config, out check)) needsBuild = true;
                            }
                        }
                        else
                        {
                            MediaManifest check;
                            if (!MediaPipeline.IsCacheValid(_appRoot, _config, out check)) needsBuild = true;
                        }

                        if (needsBuild)
                        {
                            string folder = Util.ResolvePath(_appRoot, _config.VideoFolder);
                            if (MediaPipeline.ResolveClips(folder, _config).Count == 0)
                            {
                                // "Nothing to build" has three distinct causes;
                                // say which one so the log is actionable.
                                bool hasEntries = _config.Playlist != null && _config.Playlist.Count > 0;
                                bool singleMode = string.Equals(_config.PlaybackMode, "single",
                                    StringComparison.OrdinalIgnoreCase);
                                bool hasSelection = singleMode
                                    ? !string.IsNullOrEmpty(_config.SingleClip)
                                    : HasTickedEntry(_config.Playlist);
                                string message;
                                string cardText;
                                if (hasSelection)
                                {
                                    message = (singleMode ? "the selected clip is missing from "
                                        : "the selected clips are missing from ") + folder;
                                    cardText = "Nothing to build - the selected clip is missing from the video folder";
                                }
                                else if (hasEntries)
                                {
                                    message = "no clips are ticked in the playlist";
                                    cardText = "Nothing to build - no clips are ticked in the playlist";
                                }
                                else
                                {
                                    message = "no videos in " + folder;
                                    cardText = "Nothing to build - no videos in the folder";
                                }
                                if (_lastMissingLog != message)
                                {
                                    Log.Warn(message);
                                    _lastMissingLog = message;
                                }
                                // A rebuild the user asked for opened the
                                // progress card; with nothing to build that
                                // card would sit on "Preparing..." forever.
                                // Finish it with the reason instead.
                                if (force)
                                {
                                    Ui.BuildProgressFinish(false, cardText);
                                }
                                _nextCheck = DateTime.UtcNow.AddSeconds(30);
                            }
                            else
                            {
                                _lastMissingLog = "";
                                CancelPrefetch();
                                StartBuildWorker(force);
                            }
                        }
                    }

                    if (_player.IsRunning)
                    {
                        if (_player.Window == IntPtr.Zero || !Native.IsWindow(_player.Window))
                        {
                            _player.Window = DesktopHost.FindMpvWindow(_progman);
                        }
                        if (_player.Window != IntPtr.Zero)
                        {
                            DesktopHost.EnsureWallpaperLayer(_progman);
                            bool placed = DesktopHost.EnsurePlacement(_progman, _player.Window);
                            if (placed && !firstPlacementLogged)
                            {
                                string message = "video window ready after " +
                                    (int)(DateTime.UtcNow - _player.StartedUtc).TotalMilliseconds + " ms";
                                if (!startDeltaLogged)
                                {
                                    message += " (" +
                                        (int)(DateTime.UtcNow - _processStartUtc).TotalMilliseconds +
                                        " ms after process start)";
                                    startDeltaLogged = true;
                                }
                                Log.Info(message);
                                firstPlacementLogged = true;
                            }

                            // Old loop/frame files are not urgent. Clean them up
                            // once the video is visible and no build is running.
                            if (!_cleanupDone)
                            {
                                bool clean = false;
                                lock (Sync)
                                {
                                    if (!_building)
                                    {
                                        _cleanupDone = true;
                                        clean = true;
                                    }
                                }
                                if (clean) MediaPipeline.CleanupCache(_appRoot);
                            }
                        }
                    }
                    else if (!_building && !_paused && !string.IsNullOrEmpty(_mediaPath))
                    {
                        // mpv exited or was killed: bring it back.
                        if (_player.StartedRecently(5))
                        {
                            Thread.Sleep(1000);
                        }
                        else
                        {
                            // A player stopped by the pause command is not a
                            // crash; resuming is the expected path.
                            if (_resumeFromPause)
                            {
                                _resumeFromPause = false;
                                Log.Info("resuming the wallpaper video");
                            }
                            else
                            {
                                Log.Warn("mpv is not running, restarting");
                            }
                            _player.Start(_progman, _mediaPath);
                            firstPlacementLogged = false;
                        }
                    }

                    // Prepare loops for the other clips once the desktop is up
                    // and things are calm, so the first switch is instant too.
                    if (!_prefetchDone && !_prefetching && !_building && !_paused &&
                        _player.IsRunning && firstPlacementLogged &&
                        DateTime.UtcNow >= _prefetchAfter)
                    {
                        StartPrefetchWorker();
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("watchdog error", ex);
                }

                // Poll quickly until the first frame is on the desktop, then
                // relax to the normal watchdog interval.
                Thread.Sleep(firstPlacementLogged ? 400 : 25);
            }
        }

        private static void StartBuildWorker(bool force)
        {
            int generation = _buildGeneration;
            string buildSignature = null;
            try
            {
                buildSignature = MediaPipeline.Signature(
                    Util.ResolvePath(_appRoot, _config.VideoFolder), _config);
            }
            catch { }
            lock (Sync) { _building = true; _buildingSignature = buildSignature; }
            Log.Info("starting background build");

            // A cached loop is swapped in within a fraction of a second. Do not
            // flash the progress card for work that is already finished.
            bool cached = false;
            if (!force)
            {
                try
                {
                    string folder = Util.ResolvePath(_appRoot, _config.VideoFolder);
                    cached = MediaPipeline.HasCachedLoop(_appRoot, MediaPipeline.Signature(folder, _config));
                }
                catch { }
            }

            if (!cached)
            {
                Ui.BuildProgressStart("Building wallpaper video");
                Ui.BuildProgressUpdate(0, "Reading the clips...");
            }

            Thread thread = new Thread(delegate()
            {
                BuildResult result;
                try
                {
                    result = MediaPipeline.Build(_appRoot, _config,
                        delegate(string message) { Log.Info(message); },
                        delegate(double fraction)
                        {
                            Ui.BuildProgressUpdate(fraction, "Encoding the loop...");
                        },
                        delegate { return generation != _buildGeneration; },
                        force, false);
                }
                catch (Exception ex)
                {
                    result = new BuildResult();
                    result.Message = ex.GetType().Name + ": " + ex.Message;
                    Log.Error("background build crashed", ex);
                }
                lock (Sync)
                {
                    _pendingBuild = result;
                    _building = false;
                }
            });
            thread.IsBackground = true;
            thread.Name = "livebackground-build";
            thread.Start();
        }

        // Builds the loops for the clips that are not on screen, one at a time,
        // so switching to them later is instant. It is deliberately deferred
        // and runs at below-normal priority; a real build cancels it.
        private static void StartPrefetchWorker()
        {
            if (!string.Equals(_config.PlaybackMode, "single", StringComparison.OrdinalIgnoreCase))
            {
                _prefetchDone = true;
                return;
            }

            string folder = Util.ResolvePath(_appRoot, _config.VideoFolder);
            List<string> all = MediaPipeline.ScanVideos(folder);
            if (all.Count <= 1)
            {
                _prefetchDone = true;
                return;
            }

            // Preparing loops on battery is a waste of power; try again later.
            bool onBattery = false;
            try
            {
                onBattery = System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus ==
                    System.Windows.Forms.PowerLineStatus.Offline;
            }
            catch { }
            if (onBattery)
            {
                _prefetchDone = false;
                _prefetchAfter = DateTime.UtcNow.AddMinutes(5);
                return;
            }

            _prefetchDone = true;
            _prefetching = true;
            _prefetchCancel = false;
            int generation = _buildGeneration;
            AppConfig baseConfig = _config.Clone();
            Log.Info("prefetching loops for " + all.Count + " clip(s)");

            Thread thread = new Thread(delegate()
            {
                try
                {
                    MediaManifest live = MediaPipeline.LoadManifest(_appRoot);
                    foreach (string file in all)
                    {
                        if (_prefetchCancel || _paused || generation != _buildGeneration) break;

                        string name = Path.GetFileName(file);
                        AppConfig single = baseConfig.Clone();
                        single.PlaybackMode = "single";
                        single.SingleClip = name;
                        string signature = MediaPipeline.Signature(folder, single);
                        // The clip on screen is already rendered; there is
                        // nothing to prepare for it.
                        if (live != null && string.Equals(live.Signature, signature,
                            StringComparison.OrdinalIgnoreCase)) continue;
                        if (MediaPipeline.HasCachedLoop(_appRoot, signature)) continue;

                        Log.Info("prefetch: " + name);
                        BuildResult result = MediaPipeline.Build(_appRoot, single,
                            delegate(string message) { Log.Info("prefetch: " + message); }, null,
                            delegate { return _prefetchCancel || generation != _buildGeneration; },
                            false, true);
                        Log.Info("prefetch " + name + ": " +
                            (result.Cancelled ? "cancelled" : result.Message));
                    }
                    if (!_prefetchCancel && generation == _buildGeneration)
                    {
                        MediaPipeline.CleanupCache(_appRoot);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn("prefetch failed: " + ex.Message);
                }
                finally
                {
                    _prefetching = false;
                    _prefetchCancel = false;
                    if (generation != _buildGeneration)
                    {
                        _prefetchDone = false;
                        _prefetchAfter = DateTime.UtcNow.AddSeconds(30);
                    }
                }
            });
            thread.IsBackground = true;
            thread.Name = "livebackground-prefetch";
            thread.Start();
        }

        private static void CancelPrefetch()
        {
            if (_prefetching)
            {
                Log.Info("prefetch cancelled: a build was requested");
                _prefetchCancel = true;
            }
        }

        // First run after install, or an old config without a playlist: remember
        // the clips that exist now, so clips added later start switched off.
        private static void SeedPlaylistIfNeeded(string folder)
        {
            if (_config.Playlist != null && _config.Playlist.Count > 0) return;
            List<string> videos = MediaPipeline.ScanVideos(folder);
            if (videos.Count == 0) return;
            foreach (string file in videos)
            {
                _config.Playlist.Add(new PlaylistEntry(Path.GetFileName(file), true));
            }
            try
            {
                ConfigStore.Save(_appRoot, _config);
                Log.Info("playlist seeded with " + videos.Count + " clip(s)");
            }
            catch (Exception ex)
            {
                Log.Warn("could not save the seeded playlist: " + ex.Message);
            }
        }

        private static void StartWallpaperLayerWarmup(IntPtr progman)
        {
            Thread thread = new Thread(delegate()
            {
                try { DesktopHost.EnsureWallpaperLayer(progman); }
                catch (Exception ex) { Log.Warn("wallpaper layer warmup failed: " + ex.Message); }
            });
            thread.IsBackground = true;
            thread.Name = "livebackground-layer";
            thread.Start();
        }

        private static void StartLockScreenWorker()
        {
            Thread thread = new Thread(delegate()
            {
                // Give mpv the disk and CPU first; the lock screen is only
                // needed when the user locks the PC again.
                Thread.Sleep(4000);
                try
                {
                    InstallState state = InstallState.Load(_appRoot);
                    Installer.ApplyLockScreen(_appRoot, _config, state, Log.Info);
                }
                catch (Exception ex) { Log.Warn("lock-screen update failed: " + ex.Message); }
            });
            thread.IsBackground = true;
            thread.Name = "livebackground-lockscreen";
            thread.Start();
        }

        // ---------------------------------------------------------------
        // Image wallpaper rotation
        // ---------------------------------------------------------------

        private static void UpdateImageSlideshow()
        {
            if (_paused) return;

            if (_imageListDirty)
            {
                _imageListDirty = false;
                ApplyImage(false);
                _imageActive = true;
                return;
            }

            if (!_imageActive)
            {
                ApplyImage(false);
                _imageActive = true;
                return;
            }

            if (string.Equals(_config.ImageMode, "single", StringComparison.OrdinalIgnoreCase)) return;
            if (DateTime.UtcNow < _nextImageAt) return;
            ApplyImage(true);
        }

        // Puts one picture on the desktop. advance=false keeps the current
        // picture when it is still in the list (used at startup and after a
        // settings change); advance=true moves to the next one. In one-image
        // mode the chosen picture is always used.
        private static void ApplyImage(bool advance)
        {
            string folder = Util.ResolvePath(_appRoot, _config.ImageFolder);
            List<string> images = MediaPipeline.ResolveImages(folder, _config);
            if (images == null || images.Count == 0)
            {
                string message = "no images in " + folder;
                if (!string.Equals(message, _lastMissingImageLog, StringComparison.Ordinal))
                {
                    Log.Warn(message);
                    _lastMissingImageLog = message;
                }
                _nextImageAt = DateTime.UtcNow.AddSeconds(IntervalSeconds());
                return;
            }
            _lastMissingImageLog = "";

            bool single = string.Equals(_config.ImageMode, "single", StringComparison.OrdinalIgnoreCase);
            string chosen;
            if (single || !advance)
            {
                chosen = images[0];
                if (!single && !string.IsNullOrEmpty(_currentImage))
                {
                    foreach (string file in images)
                    {
                        if (string.Equals(file, _currentImage, StringComparison.OrdinalIgnoreCase))
                        {
                            chosen = file;
                            break;
                        }
                    }
                }
            }
            else if (images.Count == 1)
            {
                chosen = images[0];
            }
            else if (_config.ImageShuffle)
            {
                chosen = images[ImageRandom.Next(images.Count)];
                int guard = 0;
                while (string.Equals(chosen, _currentImage, StringComparison.OrdinalIgnoreCase) &&
                    guard++ < 8)
                {
                    chosen = images[ImageRandom.Next(images.Count)];
                }
            }
            else
            {
                int index = -1;
                for (int i = 0; i < images.Count; i++)
                {
                    if (string.Equals(images[i], _currentImage, StringComparison.OrdinalIgnoreCase))
                    {
                        index = i;
                        break;
                    }
                }
                chosen = images[(index + 1) % images.Count];
            }

            bool changed = !string.Equals(chosen, _currentImage, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(_currentImage);
            if (changed)
            {
                Log.Info("desktop image: " + Path.GetFileName(chosen));
            }

            // Always write the picture as the real Windows wallpaper first.
            // If the app ever crashes, the desktop still shows the last image.
            DesktopHost.SetDesktopWallpaper(chosen);

            bool surfaceHasImage = string.Equals(chosen, _surfaceImage, StringComparison.OrdinalIgnoreCase);
            if (_imageSurface != null && _imageSurface.IsStarted && (!surfaceHasImage || !advance))
            {
                // The layer fades only for a real slideshow step. Startup and
                // settings changes swap instantly.
                _imageSurface.Show(chosen, advance ? ImageCrossfadeSeconds() : 0, !advance);
                _surfaceImage = chosen;
            }

            _currentImage = chosen;
            _nextImageAt = DateTime.UtcNow.AddSeconds(IntervalSeconds());
        }

        private static double ImageCrossfadeSeconds()
        {
            double seconds = _config == null ? 2.0 : _config.ImageCrossfadeSeconds;
            if (double.IsNaN(seconds) || seconds < 0) seconds = 0;
            if (seconds > 5.0) seconds = 5.0;
            return seconds;
        }

        private static void StartImageSurface(IntPtr progman)
        {
            if (_imageSurface == null) _imageSurface = new ImageSurface();
            if (!_imageSurface.IsStarted)
            {
                _imageSurface.Start(progman);
            }
            else if (progman != IntPtr.Zero)
            {
                _imageSurface.Attach(progman);
            }
        }

        private static int IntervalSeconds()
        {
            int seconds = _config == null ? 10 : _config.ImageIntervalSeconds;
            if (seconds < 1) seconds = 10;
            if (seconds > 86400) seconds = 86400;
            return seconds;
        }

        private static void StartImageWatcher(string folder)
        {
            StopImageWatcher();
            try
            {
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                FileSystemWatcher watcher = new FileSystemWatcher(folder);
                watcher.IncludeSubdirectories = false;
                watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size;
                watcher.EnableRaisingEvents = true;

                FileSystemEventHandler changed = delegate(object sender, FileSystemEventArgs e)
                {
                    if (!MediaPipeline.IsImage(e.FullPath)) return;
                    _imageListDirty = true;
                    _nextImageAt = DateTime.UtcNow;
                    Log.Info("image folder changed: " + e.ChangeType + " " + Path.GetFileName(e.FullPath));
                };
                RenamedEventHandler renamed = delegate(object sender, RenamedEventArgs e)
                {
                    if (!MediaPipeline.IsImage(e.FullPath) && !MediaPipeline.IsImage(e.OldFullPath)) return;
                    _imageListDirty = true;
                    _nextImageAt = DateTime.UtcNow;
                    Log.Info("image folder renamed: " + Path.GetFileName(e.OldFullPath) + " -> " + Path.GetFileName(e.FullPath));
                };

                watcher.Created += changed;
                watcher.Changed += changed;
                watcher.Deleted += changed;
                watcher.Renamed += renamed;
                watcher.Error += delegate(object sender, ErrorEventArgs e)
                {
                    _imageListDirty = true;
                    _nextImageAt = DateTime.UtcNow;
                    Log.Warn("image folder watcher error: " + e.GetException().Message);
                };
                Log.Info("watching images in " + folder);
                _imageWatcher = watcher;
                _watchedImageFolder = folder;
            }
            catch (Exception ex)
            {
                Log.Warn("could not watch the image folder: " + ex.Message);
                _watchedImageFolder = folder;
            }
        }

        private static void StopImageWatcher()
        {
            if (_imageWatcher != null)
            {
                try
                {
                    _imageWatcher.EnableRaisingEvents = false;
                    _imageWatcher.Dispose();
                }
                catch { }
                _imageWatcher = null;
            }
            _watchedImageFolder = "";
        }

        private static void StartWatcher(string folder)
        {
            StopWatcher();
            try
            {
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                FileSystemWatcher watcher = new FileSystemWatcher(folder);
                watcher.IncludeSubdirectories = false;
                watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size;
                watcher.EnableRaisingEvents = true;

                FileSystemEventHandler changed = delegate(object sender, FileSystemEventArgs e)
                {
                    if (!MediaPipeline.IsVideo(e.FullPath)) return;
                    _dirty = true;
                    _lastWatcherEvent = DateTime.UtcNow;
                    _prefetchDone = false;
                    _prefetchAfter = DateTime.UtcNow.AddSeconds(30);
                    Log.Info("video folder changed: " + e.ChangeType + " " + Path.GetFileName(e.FullPath));
                };
                RenamedEventHandler renamed = delegate(object sender, RenamedEventArgs e)
                {
                    if (!MediaPipeline.IsVideo(e.FullPath) && !MediaPipeline.IsVideo(e.OldFullPath)) return;
                    _dirty = true;
                    _lastWatcherEvent = DateTime.UtcNow;
                    _prefetchDone = false;
                    _prefetchAfter = DateTime.UtcNow.AddSeconds(30);
                    Log.Info("video folder renamed: " + Path.GetFileName(e.OldFullPath) + " -> " + Path.GetFileName(e.FullPath));
                };

                watcher.Created += changed;
                watcher.Changed += changed;
                watcher.Deleted += changed;
                watcher.Renamed += renamed;
                watcher.Error += delegate(object sender, ErrorEventArgs e)
                {
                    _dirty = true;
                    _lastWatcherEvent = DateTime.UtcNow;
                    Log.Warn("video folder watcher error: " + e.GetException().Message);
                };
                Log.Info("watching " + folder);
                _watcher = watcher;
                _watchedFolder = folder;
            }
            catch (Exception ex)
            {
                Log.Warn("could not watch the video folder: " + ex.Message);
                _watchedFolder = folder;
            }
        }

        private static void StopWatcher()
        {
            if (_watcher != null)
            {
                try
                {
                    _watcher.EnableRaisingEvents = false;
                    _watcher.Dispose();
                }
                catch { }
                _watcher = null;
            }
            _watchedFolder = "";
        }

        private static bool HasArg(string[] args, string value)
        {
            if (args == null) return false;
            foreach (string arg in args)
            {
                if (string.Equals(arg, value, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }
}
