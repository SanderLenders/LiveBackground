using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
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
        // True while the running background worker also builds the main loop.
        // Its result is what finishes the progress card, so a screen result
        // must not finish the same card a second time.
        private static bool _buildCardForMain;
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
        private static readonly List<ImageSurface> _imageSurfaces = new List<ImageSurface>();
        private static IntPtr _imageProgman = IntPtr.Zero;
        private static string _lastMissingImageLog = "";
        private static readonly Random ImageRandom = new Random();
        private static bool _cleanupDone;
        private static DateTime _processStartUtc;

        // Display layout. _screens[0] is the primary screen and _screens[1] and
        // _screens[2] the optional extra ones; the list is refreshed when
        // Windows reports a change.
        private static List<ScreenInfo> _screens = new List<ScreenInfo>();
        private static string _screensSignature = "";
        private static DateTime _nextScreenCheck = DateTime.MinValue;
        private static bool _layoutChanged;
        private static bool _screenCountChanged;

        // One player per extra screen, at most two (the second and third
        // monitors). Each is remembered by monitor device name, so its settings
        // survive replugging and reordering. _screensDirty is set by a settings
        // save that touched any screen.
        private static readonly List<ScreenPlayer> _screenPlayers = new List<ScreenPlayer>();
        private static bool _screensDirty;

        // One extra screen's player and the state the watchdog needs: which
        // monitor it belongs to, which loop it should show, and whether its
        // settings changed since the last pass.
        private sealed class ScreenPlayer
        {
            public MpvPlayer Player;
            public string Device = "";
            public int Number = 2;          // 2 = "second screen", 3 = "third screen"
            public int ScreenIndex = 1;     // index into _screens
            public string MediaPath = "";
            public bool ForceBuild;
            public string LastMissingLog = "";
            public BuildResult Pending;
        }

        // One extra screen's pending encode: a snapshot of its settings, its
        // own manifest and the signature used to decide whether it is cached.
        private sealed class ScreenJob
        {
            public ScreenPlayer Screen;
            public AppConfig Config;
            public bool Force;
            public string Signature;
            public string ManifestPath;
            public string LogPrefix;
        }

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
            // Any change to a screen's own settings (mirroring, folder, mode or
            // clip) is noticed here, so the players are re-pointed and any
            // needed loop rebuilt in the watchdog. The token covers all screens
            // at once; comparing it before and after CopyFrom catches them.
            string screensBefore = ScreensToken(_config);
            _config.CopyFrom(updated);
            if (!string.Equals(screensBefore, ScreensToken(_config), StringComparison.Ordinal))
            {
                _screensDirty = true;
            }
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
                StartImageSurfaces(_imageProgman);
                ApplyImage(false);
                _imageActive = true;
                if (!string.IsNullOrEmpty(_currentImage)) ImageEnsureVisibleAll();
                Log.Info("settings applied; images mode is active");
                Ui.BuildProgressFinish(true, "Wallpaper updated");
                Ui.RefreshMenu();
                return;
            }

            _imageActive = false;
            _nextImageAt = DateTime.MinValue;
            ImageHideAll();
            _surfaceImage = "";
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
                    foreach (ScreenPlayer screen in _screenPlayers)
                    {
                        if (screen.Player != null) screen.Player.Stop();
                    }
                    ShutdownImageSurfaces();
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
            DesktopHost.EnablePerMonitorDpi();

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
            _screens = Screens.Enumerate();
            _screensSignature = Screens.Signature(_screens);
            Log.Info("displays: " + DescribeScreens(_screens));
            // An old config's single second-screen block becomes the settings
            // for the first extra monitor, remembered by its device name from
            // then on.
            if (_config.BindLegacyScreens(_screens))
            {
                try { ConfigStore.Save(_appRoot, _config); }
                catch (Exception ex) { Log.Warn("could not save the bound screens: " + ex.Message); }
            }
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

            // Each extra display gets its own player. A screen mirrors the main
            // loop by default; an independent loop is built on demand. Duplicate
            // mode drives every target from one desktop monitor, so it never
            // gets extra players.
            RebuildScreenPlayers();

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
                StartImageSurfaces(_imageProgman);
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
                foreach (ScreenPlayer screen in _screenPlayers)
                {
                    if (screen.Player != null && !string.IsNullOrEmpty(screen.MediaPath))
                    {
                        screen.Player.Start(_progman, screen.MediaPath);
                    }
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
                    foreach (ScreenPlayer screen in _screenPlayers)
                    {
                        if (screen.Player != null) screen.Player.Stop();
                    }
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

                RefreshScreensIfNeeded();

                // Pausing keeps the process (and the tray icon) alive, but
                // stops mpv so the static desktop wallpaper shows through.
                if (_paused)
                {
                    if (_player != null && _player.IsRunning)
                    {
                        _player.Stop();
                        _resumeFromPause = true;
                    }
                    foreach (ScreenPlayer screen in _screenPlayers)
                    {
                        if (screen.Player != null && screen.Player.IsRunning) screen.Player.Stop();
                    }
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
                            ImageAttachAll(current);
                        }

                        // A monitor was added, removed or resized: rebuild the
                        // layers so each screen gets the right size again.
                        if (_layoutChanged)
                        {
                            _layoutChanged = false;
                            ShutdownImageSurfaces();
                            StartImageSurfaces(_imageProgman);
                            _surfaceImage = "";
                            if (!string.IsNullOrEmpty(_currentImage)) ApplyImage(false);
                        }

                        if (_paused)
                        {
                            // Pausing must reveal the real Windows wallpaper,
                            // which always holds the last picture.
                            ImageHideAll();
                        }
                        else
                        {
                            if (!string.IsNullOrEmpty(_currentImage)) ImageEnsureVisibleAll();
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
                            // Mirroring screens follow the main loop; restart
                            // them so they play the new file too. A screen with
                            // its own loop is left to its own build.
                            foreach (ScreenPlayer screen in _screenPlayers)
                            {
                                if (screen.Player == null || _paused) continue;
                                if (!_config.ScreenFor(screen.Device).SameAsMain) continue;
                                screen.MediaPath = _mediaPath;
                                if (screen.Player.IsRunning) screen.Player.Stop();
                                screen.Player.Start(_progman, screen.MediaPath);
                            }
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

                    // Pick up finished per-screen builds. Each screen reports
                    // through its own slot, so one screen's result never
                    // disturbs another's.
                    foreach (ScreenPlayer screen in _screenPlayers)
                    {
                        BuildResult finished2 = null;
                        lock (Sync)
                        {
                            if (screen.Pending != null)
                            {
                                finished2 = screen.Pending;
                                screen.Pending = null;
                            }
                        }
                        if (finished2 == null) continue;

                        if (finished2.Cancelled)
                        {
                            Log.Info(ScreenLabel(screen) + " build replaced: " + finished2.Message);
                        }
                        else if (finished2.Success)
                        {
                            Log.Info(ScreenLabel(screen) + " rebuild finished: " + finished2.Message);
                            screen.MediaPath = finished2.LoopPath;
                            if (screen.Player != null && !_paused)
                            {
                                if (screen.Player.IsRunning) screen.Player.Stop();
                                screen.Player.Start(_progman, screen.MediaPath);
                            }
                            // When only an extra screen needed a loop, this
                            // result owns the progress card; the main result
                            // is what finishes it otherwise.
                            if (!_buildCardForMain) Ui.BuildProgressFinish(true, "Wallpaper updated");
                        }
                        else
                        {
                            Log.Warn(ScreenLabel(screen) + " rebuild failed: " + finished2.Message);
                            _nextCheck = DateTime.UtcNow.AddSeconds(60);
                            if (!_buildCardForMain) Ui.BuildProgressFinish(false, "Build failed - see logs\\app.log");
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

                    // A monitor was added, removed or moved. Players are always
                    // re-placed onto their screen below; the player list is
                    // rebuilt so it matches the connected monitors again.
                    if (_layoutChanged)
                    {
                        _layoutChanged = false;
                        firstPlacementLogged = false;
                        int playersBefore = _screenPlayers.Count;
                        RebuildScreenPlayers();
                        if (_screenPlayers.Count != playersBefore) _screenCountChanged = true;
                        if (_screenCountChanged && !_paused && _progman != IntPtr.Zero)
                        {
                            if (!string.IsNullOrEmpty(_mediaPath) && File.Exists(_mediaPath))
                                _player.Start(_progman, _mediaPath);
                            foreach (ScreenPlayer screen in _screenPlayers)
                            {
                                if (screen.Player != null && !string.IsNullOrEmpty(screen.MediaPath) &&
                                    File.Exists(screen.MediaPath))
                                {
                                    screen.Player.Start(_progman, screen.MediaPath);
                                }
                            }
                        }
                        _screenCountChanged = false;
                    }

                    // A settings save may have changed one or more screens.
                    // Re-point each player at the loop its settings now ask for
                    // and restart it; a needed rebuild is picked up below.
                    if (_screensDirty)
                    {
                        _screensDirty = false;
                        RebuildScreenPlayers();
                        foreach (ScreenPlayer screen in _screenPlayers)
                        {
                            RefreshScreenMediaPath(screen);
                            if (screen.Player != null && !_paused && _progman != IntPtr.Zero &&
                                !string.IsNullOrEmpty(screen.MediaPath) && File.Exists(screen.MediaPath))
                            {
                                if (screen.Player.IsRunning) screen.Player.Stop();
                                screen.Player.Start(_progman, screen.MediaPath);
                            }
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
                    // the cache is stale. Every extra screen is checked on the
                    // same pass, but only while it plays its own loop; when it
                    // mirrors the main screen there is nothing to render.
                    if (!_building && (_forceBuild || AnyScreenBuildForced() || DateTime.UtcNow >= _nextCheck))
                    {
                        _nextCheck = DateTime.UtcNow.AddSeconds(10);
                        bool buildMain = false;
                        bool forceMain = false;

                        if (_forceBuild)
                        {
                            _forceBuild = false;
                            buildMain = true;
                            forceMain = true;
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
                                if (!MediaPipeline.IsCacheValid(_appRoot, _config, out check)) buildMain = true;
                            }
                        }
                        else
                        {
                            MediaManifest check;
                            if (!MediaPipeline.IsCacheValid(_appRoot, _config, out check)) buildMain = true;
                        }

                        // Each screen with its own loop that is missing or stale
                        // becomes a job. The settings are snapshotted here so a
                        // later save cannot make the encode write the wrong loop.
                        List<ScreenJob> screenJobs = new List<ScreenJob>();
                        foreach (ScreenPlayer screen in _screenPlayers)
                        {
                            if (screen.Player == null) continue;
                            ScreenSettings settings = _config.ScreenFor(screen.Device);
                            if (settings.SameAsMain)
                            {
                                screen.ForceBuild = false;
                                continue;
                            }
                            bool buildScreen = false;
                            bool forceScreen = false;
                            if (screen.ForceBuild)
                            {
                                screen.ForceBuild = false;
                                buildScreen = true;
                                forceScreen = true;
                            }
                            else
                            {
                                AppConfig screenConfig = _config.ScreenConfig(settings);
                                MediaManifest check;
                                if (!MediaPipeline.IsScreenCacheValid(_appRoot, screenConfig, screen.Device,
                                    out check, true))
                                {
                                    buildScreen = true;
                                }
                            }
                            if (!buildScreen) continue;

                            // "Nothing to build" has three distinct causes; say
                            // which one so the log is actionable.
                            AppConfig own = _config.ScreenConfig(settings);
                            string folder = Util.ResolvePath(_appRoot, own.VideoFolder);
                            if (MediaPipeline.ResolveClips(folder, own).Count == 0)
                            {
                                string message;
                                string cardText;
                                DescribeMissing(folder, own, out message, out cardText);
                                if (!string.Equals(message, screen.LastMissingLog, StringComparison.Ordinal))
                                {
                                    Log.Warn(ScreenLabel(screen) + ": " + message);
                                    screen.LastMissingLog = message;
                                }
                                _nextCheck = DateTime.UtcNow.AddSeconds(30);
                                continue;
                            }
                            screen.LastMissingLog = "";

                            ScreenJob job = new ScreenJob();
                            job.Screen = screen;
                            job.Config = own;
                            job.Force = forceScreen;
                            job.ManifestPath = MediaPipeline.ScreenManifestPath(_appRoot, screen.Device);
                            job.LogPrefix = ScreenLabel(screen) + ": ";
                            try
                            {
                                job.Signature = MediaPipeline.Signature(
                                    Util.ResolvePath(_appRoot, own.VideoFolder), own);
                            }
                            catch { }
                            screenJobs.Add(job);
                        }

                        if (buildMain)
                        {
                            string folder = Util.ResolvePath(_appRoot, _config.VideoFolder);
                            if (MediaPipeline.ResolveClips(folder, _config).Count == 0)
                            {
                                string message;
                                string cardText;
                                DescribeMissing(folder, _config, out message, out cardText);
                                if (_lastMissingLog != message)
                                {
                                    Log.Warn(message);
                                    _lastMissingLog = message;
                                }
                                // A rebuild the user asked for opened the
                                // progress card; with nothing to build that
                                // card would sit on "Preparing..." forever.
                                // Finish it with the reason instead.
                                if (forceMain) Ui.BuildProgressFinish(false, cardText);
                                _nextCheck = DateTime.UtcNow.AddSeconds(30);
                                buildMain = false;
                            }
                            else
                            {
                                _lastMissingLog = "";
                            }
                        }

                        if (buildMain || screenJobs.Count > 0)
                        {
                            if (buildMain) CancelPrefetch();
                            StartBuildWorker(buildMain, forceMain, screenJobs);
                        }
                    }

                    if (_player.IsRunning)
                    {
                        bool placed = PlacePlayerOnScreen(_player, 0);
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
                        if (_player.Window != IntPtr.Zero && !_cleanupDone)
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

                    // Each extra screen has its own player and watchdog: place
                    // it on its monitor and bring it back if it exited.
                    foreach (ScreenPlayer screen in _screenPlayers)
                    {
                        if (screen.Player == null) continue;
                        if (screen.Player.IsRunning)
                        {
                            PlacePlayerOnScreen(screen.Player, screen.ScreenIndex);
                        }
                        else if (!_building && !_paused && _progman != IntPtr.Zero &&
                            !string.IsNullOrEmpty(screen.MediaPath) && File.Exists(screen.MediaPath))
                        {
                            if (!screen.Player.StartedRecently(5))
                            {
                                Log.Warn("the " + ScreenLabel(screen) + " player is not running, restarting");
                                screen.Player.Start(_progman, screen.MediaPath);
                            }
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

        // Builds the main loop and/or the extra screens' own loops in the
        // background. The main loop is built first so the primary screen never
        // waits for the others; every result is reported back through the
        // screen's own pending slot.
        private static void StartBuildWorker(bool buildMain, bool forceMain, List<ScreenJob> screens)
        {
            int generation = _buildGeneration;
            string mainSignature = null;
            if (buildMain)
            {
                try
                {
                    mainSignature = MediaPipeline.Signature(
                        Util.ResolvePath(_appRoot, _config.VideoFolder), _config);
                }
                catch { }
            }
            if (screens == null) screens = new List<ScreenJob>();

            lock (Sync)
            {
                _building = true;
                _buildingSignature = mainSignature;
                _buildCardForMain = buildMain;
            }
            Log.Info(buildMain && screens.Count > 0 ? "starting background build (main and extra screens)"
                : screens.Count > 0 ? "starting background build (extra screens)"
                : "starting background build");

            // A cached loop is swapped in within a fraction of a second. Do not
            // flash the progress card for work that is already finished.
            bool cached = true;
            try
            {
                if (buildMain && (forceMain || mainSignature == null ||
                    !MediaPipeline.HasCachedLoop(_appRoot, mainSignature))) cached = false;
                foreach (ScreenJob job in screens)
                {
                    if (job.Force || job.Signature == null ||
                        !MediaPipeline.HasCachedLoop(_appRoot, job.Signature)) cached = false;
                }
            }
            catch { cached = false; }

            if (!cached)
            {
                Ui.BuildProgressStart("Building wallpaper video");
                Ui.BuildProgressUpdate(0, "Reading the clips...");
            }

            Thread thread = new Thread(delegate()
            {
                if (buildMain)
                {
                    BuildResult result = RunBuild(_config, generation, forceMain, null, "");
                    lock (Sync) { _pendingBuild = result; }
                }

                // A generation change means the settings moved on; the main
                // build already reported that, so do not start a stale encode.
                foreach (ScreenJob job in screens)
                {
                    if (generation != _buildGeneration) break;
                    BuildResult result = RunBuild(job.Config, generation, job.Force,
                        job.ManifestPath, job.LogPrefix);
                    lock (Sync) { job.Screen.Pending = result; }
                }

                lock (Sync) { _building = false; }
            });
            thread.IsBackground = true;
            thread.Name = "livebackground-build";
            thread.Start();
        }

        // One encode, with the log/progress/cancel wiring shared by every loop.
        private static BuildResult RunBuild(AppConfig config, int generation, bool force,
            string liveManifestPath, string logPrefix)
        {
            string who = string.IsNullOrEmpty(logPrefix) ? "" : logPrefix.TrimEnd(' ', ':');
            try
            {
                return MediaPipeline.Build(_appRoot, config,
                    delegate(string message) { Log.Info(logPrefix + message); },
                    delegate(double fraction)
                    {
                        Ui.BuildProgressUpdate(fraction, string.IsNullOrEmpty(who)
                            ? "Encoding the loop..." : "Encoding the " + who + " loop...");
                    },
                    delegate { return generation != _buildGeneration; },
                    force, false, liveManifestPath);
            }
            catch (Exception ex)
            {
                BuildResult result = new BuildResult();
                result.Message = ex.GetType().Name + ": " + ex.Message;
                Log.Error(string.IsNullOrEmpty(who) ? "background build crashed" : who + " build crashed", ex);
                return result;
            }
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
            if (!surfaceHasImage || !advance)
            {
                // The layers fade only for a real slideshow step. Startup and
                // settings changes swap instantly.
                ImageShowAll(chosen, advance ? ImageCrossfadeSeconds() : 0, !advance);
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

        // ---------------------------------------------------------------
        // Image layers, one per screen
        // ---------------------------------------------------------------

        // Creates or re-sizes one still-image layer per connected screen, so a
        // slideshow picture covers every monitor.
        private static void StartImageSurfaces(IntPtr progman)
        {
            if (_screens == null || _screens.Count == 0) _screens = Screens.Enumerate();
            int wanted = Math.Max(1, _screens.Count);
            while (_imageSurfaces.Count > wanted)
            {
                ImageSurface extra = _imageSurfaces[_imageSurfaces.Count - 1];
                _imageSurfaces.RemoveAt(_imageSurfaces.Count - 1);
                try { extra.Shutdown(); } catch { }
            }
            for (int i = 0; i < wanted; i++)
            {
                if (i >= _imageSurfaces.Count) _imageSurfaces.Add(new ImageSurface());
                ImageSurface surface = _imageSurfaces[i];
                Rectangle bounds = i < _screens.Count ? _screens[i].Bounds : _screens[0].Bounds;
                if (!surface.IsStarted) surface.Start(progman, bounds);
                else if (progman != IntPtr.Zero) surface.Attach(progman);
            }
        }

        private static void ShutdownImageSurfaces()
        {
            foreach (ImageSurface surface in _imageSurfaces)
            {
                try { surface.Shutdown(); } catch { }
            }
            _imageSurfaces.Clear();
        }

        private static void ImageAttachAll(IntPtr progman)
        {
            foreach (ImageSurface surface in _imageSurfaces)
            {
                if (surface.IsStarted) surface.Attach(progman);
            }
        }

        private static void ImageShowAll(string path, double fadeSeconds, bool immediate)
        {
            foreach (ImageSurface surface in _imageSurfaces)
            {
                if (surface.IsStarted) surface.Show(path, fadeSeconds, immediate);
            }
        }

        private static void ImageEnsureVisibleAll()
        {
            foreach (ImageSurface surface in _imageSurfaces)
            {
                if (surface.IsStarted) surface.EnsureVisible();
            }
        }

        private static void ImageHideAll()
        {
            foreach (ImageSurface surface in _imageSurfaces)
            {
                if (surface.IsStarted) surface.Hide();
            }
        }

        // ---------------------------------------------------------------
        // Displays and the extra screens
        // ---------------------------------------------------------------

        private static string DescribeScreens(List<ScreenInfo> screens)
        {
            if (screens == null || screens.Count == 0) return "none";
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < screens.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(screens[i].Describe());
            }
            if (screens.Count > 3) sb.Append(" (only the first three are used)");
            if (Screens.IsDuplicate(screens)) sb.Append(" (Windows is duplicating)");
            return sb.ToString();
        }

        // Re-enumerates the displays when Windows reports a change. A changed
        // layout moves the players; a changed screen count rebuilds them.
        private static void RefreshScreensIfNeeded()
        {
            if (DateTime.UtcNow < _nextScreenCheck) return;
            _nextScreenCheck = DateTime.UtcNow.AddSeconds(2);
            List<ScreenInfo> now = Screens.Enumerate();
            string signature = Screens.Signature(now);
            if (string.Equals(signature, _screensSignature, StringComparison.Ordinal))
            {
                return;
            }

            int before = _screens == null ? 0 : _screens.Count;
            _screens = now;
            _screensSignature = signature;
            _layoutChanged = true;
            if (now.Count != before) _screenCountChanged = true;
            Log.Info("display layout changed: " + DescribeScreens(now));
        }

        // Creates, keeps or retires one player per extra screen (at most two),
        // so the player list always matches the connected monitors. Players are
        // matched by device name, so a re-ordered monitor keeps its settings.
        private static void RebuildScreenPlayers()
        {
            bool duplicate = _screens != null && Screens.IsDuplicate(_screens);
            List<ScreenInfo> extras = new List<ScreenInfo>();
            if (!duplicate && _screens != null)
            {
                foreach (ScreenInfo screen in _screens)
                {
                    if (screen.Primary) continue;
                    if (extras.Count >= 2) break;
                    extras.Add(screen);
                }
            }

            // Retire the players whose monitor is gone.
            for (int i = _screenPlayers.Count - 1; i >= 0; i--)
            {
                ScreenPlayer existing = _screenPlayers[i];
                bool keep = false;
                foreach (ScreenInfo screen in extras)
                {
                    if (string.Equals(screen.Device, existing.Device, StringComparison.OrdinalIgnoreCase))
                    {
                        keep = true;
                        break;
                    }
                }
                if (keep) continue;
                if (existing.Player != null)
                {
                    try { existing.Player.Stop(); } catch { }
                }
                _screenPlayers.RemoveAt(i);
            }

            // Add a player for each new monitor and refresh the ones kept.
            int number = 2;
            foreach (ScreenInfo screen in extras)
            {
                ScreenPlayer player = null;
                foreach (ScreenPlayer existing in _screenPlayers)
                {
                    if (string.Equals(existing.Device, screen.Device, StringComparison.OrdinalIgnoreCase))
                    {
                        player = existing;
                        break;
                    }
                }
                if (player == null)
                {
                    player = new ScreenPlayer();
                    player.Device = screen.Device ?? "";
                    player.Player = new MpvPlayer(_appRoot, _config, ScreenLogName(player.Device));
                    _screenPlayers.Add(player);
                }
                player.Number = number;
                player.ScreenIndex = screen.Index;
                RefreshScreenMediaPath(player);
                if (string.IsNullOrEmpty(player.MediaPath) && !_config.ScreenFor(player.Device).SameAsMain)
                {
                    player.ForceBuild = true;
                    Log.Info("no loop yet for the " + ScreenLabel(player) + ", building in the background");
                }
                number++;
            }
        }

        // Points one screen's player at the right loop: the main one when it
        // mirrors, otherwise the cached loop for that screen if there is one.
        private static void RefreshScreenMediaPath(ScreenPlayer screen)
        {
            if (screen == null || _config == null) return;
            ScreenSettings settings = _config.ScreenFor(screen.Device);
            if (settings.SameAsMain)
            {
                screen.MediaPath = _mediaPath;
                return;
            }

            AppConfig screenConfig = _config.ScreenConfig(settings);
            MediaManifest manifest;
            if (MediaPipeline.IsScreenCacheValid(_appRoot, screenConfig, screen.Device, out manifest, false))
            {
                screen.MediaPath = MediaPipeline.LoopPath(_appRoot, manifest);
            }
            else
            {
                screen.MediaPath = "";
            }
        }

        // True while any screen still has a rebuild waiting to start.
        private static bool AnyScreenBuildForced()
        {
            foreach (ScreenPlayer screen in _screenPlayers)
            {
                if (screen.ForceBuild) return true;
            }
            return false;
        }

        // "second screen" / "third screen" for the log and the progress card,
        // matching the names the settings window uses.
        private static string ScreenLabel(ScreenPlayer screen)
        {
            if (screen == null) return "screen";
            if (screen.Number == 2) return "second screen";
            if (screen.Number == 3) return "third screen";
            return "screen " + screen.Number;
        }

        // A stable log file name per monitor: "mpv-<device>.log" with anything
        // that is not a letter or digit turned into an underscore.
        private static string ScreenLogName(string device)
        {
            StringBuilder sb = new StringBuilder("mpv-");
            if (!string.IsNullOrEmpty(device))
            {
                foreach (char c in device)
                {
                    sb.Append(char.IsLetterOrDigit(c) ? c : '_');
                }
            }
            sb.Append(".log");
            return sb.ToString();
        }

        // A fingerprint of every screen's settings, so a save that changed any
        // of them can be told apart from a save that did not.
        private static string ScreensToken(AppConfig config)
        {
            if (config == null || config.ExtraScreens == null) return "";
            StringBuilder sb = new StringBuilder();
            foreach (ScreenSettings screen in config.ExtraScreens)
            {
                if (screen == null) continue;
                sb.Append(screen.Device ?? "").Append('\u0001')
                  .Append(screen.SameAsMain ? '1' : '0').Append('\u0001')
                  .Append(screen.Folder ?? "").Append('\u0001')
                  .Append(screen.PlaybackMode ?? "").Append('\u0001')
                  .Append(screen.SingleClip ?? "").Append('\u0001');
                if (screen.Playlist != null)
                {
                    foreach (PlaylistEntry entry in screen.Playlist)
                    {
                        if (entry == null) continue;
                        sb.Append(entry.Name).Append(':').Append(entry.Enabled ? '1' : '0').Append(';');
                    }
                }
                sb.Append('\u0002');
            }
            return sb.ToString();
        }

        private static uint PlayerPid(MpvPlayer player)
        {
            if (player == null) return 0;
            try
            {
                Process process = player.Process;
                if (process != null && !process.HasExited) return (uint)process.Id;
            }
            catch { }
            return 0;
        }

        // Finds the player's window, sizes it to the screen and keeps it below
        // the desktop icons. Returns true when the window is really placed.
        private static bool PlacePlayerOnScreen(MpvPlayer player, int screenIndex)
        {
            if (player == null || _screens == null || _screens.Count == 0) return false;
            if (screenIndex < 0) screenIndex = 0;
            if (screenIndex >= _screens.Count) screenIndex = _screens.Count - 1;

            if (player.Window == IntPtr.Zero || !Native.IsWindow(player.Window))
            {
                player.Window = DesktopHost.FindMpvWindow(_progman, PlayerPid(player));
            }
            if (player.Window == IntPtr.Zero) return false;

            DesktopHost.EnsureWallpaperLayer(_progman);
            DesktopHost.PlaceOnScreen(_progman, player.Window, _screens[screenIndex].Bounds);
            return true;
        }

        // Describes why a loop has nothing to build, for the log and the card.
        private static void DescribeMissing(string folder, AppConfig config,
            out string message, out string cardText)
        {
            bool hasEntries = config.Playlist != null && config.Playlist.Count > 0;
            bool singleMode = string.Equals(config.PlaybackMode, "single",
                StringComparison.OrdinalIgnoreCase);
            bool hasSelection = singleMode
                ? !string.IsNullOrEmpty(config.SingleClip)
                : HasTickedEntry(config.Playlist);
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
