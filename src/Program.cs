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
        private static volatile AppConfig _config;
        private static MpvPlayer _player;
        private static IntPtr _progman = IntPtr.Zero;
        private static string _mediaPath = "";

        // One name, used both to create the single-instance mutex and to check
        // whether the wallpaper is already running. Kept in one place so the
        // two can never drift apart.
        private const string MutexName = @"Local\LiveBackgroundApp";

        private static readonly object Sync = new object();
        private static bool _building;
        // Signature of the build the background worker is currently
        // running, so a settings apply can tell "this exact build is
        // already running" from "the running build is now outdated".
        private static string _buildingSignature;
        private static bool _dirty;
        private static DateTime _nextCheck = DateTime.MinValue;
        // The main video folder is watched, so its periodic signature scan is
        // only a safety net for a missed event. Screen folders are not watched,
        // so they get their own slower poll. Both start due (MinValue) so the
        // first pass validates like before.
        private static DateTime _nextSafetyScan = DateTime.MinValue;
        private static DateTime _nextScreenScan = DateTime.MinValue;
        // Kept open for the life of the process: the job kills every child
        // (mpv, ffmpeg) when its last handle closes, i.e. when we exit.
        private static IntPtr _jobHandle = IntPtr.Zero;
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
        // Guards _imageSurfaces and _screenImages. The watchdog thread builds
        // and iterates them while the settings UI thread can rebuild them the
        // moment the user saves, so both sides take this lock.
        private static readonly object ImageSync = new object();
        private static readonly List<ImageSurface> _imageSurfaces = new List<ImageSurface>();
        // Independent slideshow state for the first two extra screens. Other
        // extra surfaces simply follow the main image. The same surfaces are
        // reused for per-screen desktop fallback images in Video mode.
        private static readonly List<ScreenImageState> _screenImages = new List<ScreenImageState>();
        private static string _mainFramePath = "";
        private static IntPtr _imageProgman = IntPtr.Zero;
        private static string _lastMissingImageLog = "";
        private static readonly Random ImageRandom = new Random();
        private static bool _cleanupDone;
        private static DateTime _processStartUtc;

        // Display layout. _screens[0] is the primary screen and _screens[1] and
        // _screens[2] the optional extra ones; the list is refreshed when
        // Windows reports a change. The list is only ever replaced (never
        // mutated) after Screens.Enumerate returns it, so a reader that reads
        // the field once always sees a complete layout. volatile makes the
        // replacement visible to the watchdog and UI threads promptly.
        private static volatile List<ScreenInfo> _screens = new List<ScreenInfo>();
        private static string _screensSignature = "";
        private static DateTime _nextScreenCheck = DateTime.MinValue;
        private static bool _layoutChanged;
        private static bool _screenCountChanged;

        // One player per extra screen, at most two (the second and third
        // monitors). Each is remembered by monitor device name, so its settings
        // survive replugging and reordering. _screensDirty is set by a settings
        // save that touched any screen. The list is copy-on-write: the watchdog
        // builds a new list and swaps it in, so a UI thread iterating the old
        // one can never hit a "collection was modified" exception.
        private static volatile List<ScreenPlayer> _screenPlayers = new List<ScreenPlayer>();
        // volatile for the same reason as _screens and _screenPlayers: it is
        // written on the UI thread (a settings save) and read/cleared by the
        // watchdog, and it must not be observed before the _config swap that
        // goes with it.
        private static volatile bool _screensDirty;

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
            public string FramePath = "";
            public string FallbackImage = "";
            public bool FallbackVisible;
            public bool ForceBuild;
            public string LastMissingLog = "";
            public BuildResult Pending;
        }

        // One extra screen's independent slideshow. The surface itself stays
        // in _imageSurfaces[ScreenIndex]; this record owns only selection,
        // timing and the folder watcher for that screen.
        private sealed class ScreenImageState
        {
            public string Device = "";
            public int ScreenIndex = 1;
            public string CurrentImage = "";
            public string SurfaceImage = "";
            public DateTime NextImageAt = DateTime.MinValue;
            public bool Active;
            public bool ListDirty;
            public string LastMissingLog = "";
            public FileSystemWatcher Watcher;
            public string WatchedFolder = "";
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
            catch { /* best-effort; the stop flag below is the fallback */ }
            try { File.WriteAllText(Installer.StopFlag(_appRoot), "stop"); }
            catch { /* best-effort; the process is exiting anyway */ }
            Ui.Stop();
        }

        // Called by the settings window after it saves config.json. A fresh
        // config object is published in one atomic swap and every player is
        // re-pointed at it, then a rebuild is forced.
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
            // at once; comparing it before and after the swap catches them.
            string screensBefore = ScreensToken(_config);
            // Publish a brand-new config object in a single atomic reference
            // write instead of copying into the live one field by field. The
            // watchdog reads _config without a lock, so it must never observe a
            // half-applied update; with a swap it always sees either the old
            // config or the new one, never a mix of the two.
            AppConfig fresh = updated.Clone();
            _config = fresh;
            ReconfigurePlayers(fresh);
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
                foreach (ScreenPlayer screen in _screenPlayers)
                {
                    if (screen.Player != null) screen.Player.Stop();
                }
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
                UpdateExtraImageSlideshow();
                if (!string.IsNullOrEmpty(_currentImage)) ImageEnsureVisibleAll();
                Log.Info("settings applied; images mode is active");
                Ui.BuildProgressFinish(true, "Wallpaper updated");
                Ui.RefreshMenu();
                return;
            }

            _imageActive = false;
            _nextImageAt = DateTime.MinValue;
            ResetExtraImageStates();
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
            catch { /* best-effort: an unreadable folder forces a rebuild */ }
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
            _mainFramePath = frame;
            string wallpaper = DesktopHost.WallpaperFor(_appRoot, _config, frame);
            if (!string.IsNullOrEmpty(wallpaper)) DesktopHost.SetDesktopWallpaper(wallpaper);
            UpdateFallbackSurfaces();

            Ui.RefreshMenu();
        }

        // A settings apply swaps in a new config object, so every long-lived
        // holder of the old reference is re-pointed at the new one. The players
        // are the only such holders; the settings window works on its own clone.
        private static void ReconfigurePlayers(AppConfig config)
        {
            if (_player != null) _player.Reconfigure(config);
            foreach (ScreenPlayer screen in _screenPlayers)
            {
                if (screen.Player != null) screen.Player.Reconfigure(config);
            }
        }

        public static int Main(string[] args)
        {
            _processStartUtc = DateTime.UtcNow;
            try { _processStartUtc = Process.GetCurrentProcess().StartTime.ToUniversalTime(); } catch { /* best-effort: fall back to the time we started */ }

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
                catch { /* best-effort: the running instance may already be gone */ }
            }

            if (HasArg(args, "--stop"))
            {
                try { File.WriteAllText(Installer.QuitFlag(_appRoot), "quit"); }
                catch { /* best-effort: the running instance may already be gone */ }
                try { File.WriteAllText(Installer.StopFlag(_appRoot), "stop"); }
                catch { /* best-effort: the running instance may already be gone */ }
                Log.Info("stop requested");
                return 0;
            }

            // Scriptable equivalents of the tray menu. Each writes a flag that
            // the running instance picks up within half a second.
            if (HasArg(args, "--pause"))
            {
                try { File.WriteAllText(Installer.PauseFlag(_appRoot), "pause"); }
                catch { /* best-effort: the running instance may already be gone */ }
                Log.Info("pause requested");
                return 0;
            }

            if (HasArg(args, "--resume"))
            {
                try { File.WriteAllText(Installer.ResumeFlag(_appRoot), "resume"); }
                catch { /* best-effort: the running instance may already be gone */ }
                Log.Info("resume requested");
                return 0;
            }

            if (HasArg(args, "--rebuild"))
            {
                try { File.WriteAllText(Installer.RebuildFlag(_appRoot), "rebuild"); }
                catch { /* best-effort: the running instance may already be gone */ }
                Log.Info("rebuild requested");
                return 0;
            }

            bool createdNew;
            using (Mutex mutex = new Mutex(true, MutexName, out createdNew))
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
                    try { mutex.ReleaseMutex(); } catch { /* best-effort: the mutex is released on exit regardless */ }
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
                    using (Mutex existing = Mutex.OpenExisting(MutexName))
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
            // Own every process we start: a job that kills its children when
            // this process exits, even on a hard kill, so an mpv can never be
            // left painting over the desktop. Best-effort; if the OS refuses we
            // carry on and rely on the startup reap below.
            _jobHandle = Native.CreateKillOnCloseJob();
            if (_jobHandle != IntPtr.Zero) Log.Info("child-process job created (mpv is reaped even on a hard kill)");
            else Log.Warn("child-process job unavailable; orphans are only reaped at startup");
            // A previous run that was killed hard can leave its mpv behind.
            // Reap it before we start our own, so only one video is ever shown.
            int reaped = Installer.KillOrphanMpv(delegate(string message) { Log.Info(message); });
            if (reaped > 0) Log.Info("reaped " + reaped + " orphaned mpv process(es) at startup");
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
            // A config that could not be read silently resets everything the
            // user configured; say so once instead of letting them think the
            // app forgot their settings. Ui.Notify holds the message until the
            // tray thread is ready.
            if (!string.IsNullOrEmpty(ConfigStore.LoadWarning))
            {
                Log.Warn("config warning: " + ConfigStore.LoadWarning);
                Ui.Notify("LiveBackground", ConfigStore.LoadWarning);
            }
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
            // Bring the bundled pictures up to date when they changed in an
            // update, so an upgraded library gets the new set instead of
            // keeping the picture that shipped before it.
            try { Installer.SyncBundledImages(_appRoot, _config, InstallState.Load(_appRoot), Log.Info); }
            catch (Exception ex) { Log.Warn("could not sync the bundled pictures: " + ex.Message); }
            SeedPlaylistIfNeeded(folder);

            // Read the manifest once and use it for the frame, the loop and the
            // quick cache check. Reading it repeatedly is expensive at logon.
            MediaManifest manifest = MediaPipeline.LoadManifest(_appRoot);
            if (manifest != null) MediaPipeline.SeedCacheIndex(_appRoot, manifest.Signature);
            string frame = MediaPipeline.FramePath(_appRoot, manifest);
            _mainFramePath = frame;
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
                            UpdateImageSlideshow();
                            if (!string.IsNullOrEmpty(_currentImage)) ImageEnsureVisibleAll();
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
                        // Explorer took every wallpaper window with it: the main
                        // player, each screen's player and the fallback image
                        // layers. The mpv processes survive with no window, so
                        // the "player exited" watchdog never restarts them and
                        // the screen would stay blank. Rebuild them all inside
                        // the new desktop, and drop the fallback flags so a
                        // customized static image is shown again instead of
                        // being assumed still visible.
                        if (!_paused && !string.IsNullOrEmpty(_mediaPath)) _player.Start(_progman, _mediaPath);
                        foreach (ScreenPlayer screen in _screenPlayers)
                        {
                            if (screen.Player != null && !_paused &&
                                !string.IsNullOrEmpty(screen.MediaPath) && File.Exists(screen.MediaPath))
                            {
                                screen.Player.Start(_progman, screen.MediaPath);
                            }
                            screen.FallbackVisible = false;
                            screen.FallbackImage = "";
                        }
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
                            _mainFramePath = finished.FramePath;
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
                            screen.FramePath = finished2.FramePath;
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
                    bool screensChanged = _screensDirty;
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
                        else if (_watcher == null || DateTime.UtcNow >= _nextSafetyScan)
                        {
                            // The folder watcher is authoritative: any change
                            // sets _dirty and is handled above. This branch is
                            // only the safety net for a missed event (a network
                            // share, a watcher buffer overflow, or a folder
                            // swapped under the same path), so it walks the
                            // folder every ten minutes instead of every ten
                            // seconds. With no watcher at all it keeps the
                            // frequent check, since nothing else would notice.
                            _nextSafetyScan = DateTime.UtcNow.AddMinutes(10);
                            MediaManifest check;
                            if (!MediaPipeline.IsCacheValid(_appRoot, _config, out check)) buildMain = true;
                        }

                        // Each screen with its own loop that is missing or stale
                        // becomes a job. The settings are snapshotted here so a
                        // later save cannot make the encode write the wrong loop.
                        // Screen folders are not watched, so they are checked on
                        // their own slower poll; a settings change, or a screen
                        // that already wants a build, is handled right away.
                        bool scanScreens = screensChanged || DateTime.UtcNow >= _nextScreenScan;
                        if (scanScreens) _nextScreenScan = DateTime.UtcNow.AddSeconds(60);
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
                            else if (scanScreens)
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
                            catch { /* best-effort: a screen whose folder cannot be read keeps its loop */ }
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

                    // Show a customized static desktop image only on screens
                    // whose player is paused, stopped or still being built.
                    UpdateFallbackSurfaces();

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
                catch { /* best-effort: an unreadable folder skips the cache check */ }
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
            catch { /* best-effort: treat an unknown power state as online */ }
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
            // Build a fresh list and publish it in one write, the same way the
            // settings apply swaps in a whole config, so a reader never sees a
            // half-built playlist.
            List<PlaylistEntry> seeded = new List<PlaylistEntry>();
            foreach (string file in videos)
            {
                seeded.Add(new PlaylistEntry(Path.GetFileName(file), true));
            }
            _config.Playlist = seeded;
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

            bool apply = false;
            bool advance = false;
            if (_imageListDirty)
            {
                _imageListDirty = false;
                apply = true;
            }
            else if (!_imageActive)
            {
                apply = true;
            }
            else if (!string.Equals(_config.ImageMode, "single", StringComparison.OrdinalIgnoreCase) &&
                DateTime.UtcNow >= _nextImageAt)
            {
                apply = true;
                advance = true;
            }

            if (apply)
            {
                ApplyImage(advance);
                _imageActive = true;
            }
            UpdateExtraImageSlideshow();
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

            string chosen = ChooseImage(_config, images, _currentImage, advance);

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
                ImageShowMainAndMirrors(chosen, advance ? ImageCrossfadeSeconds() : 0, !advance);
                _surfaceImage = chosen;
            }

            _currentImage = chosen;
            _nextImageAt = DateTime.UtcNow.AddSeconds(IntervalSeconds());
        }

        // Shared by the main screen and every independent extra screen.
        private static string ChooseImage(AppConfig config, List<string> images,
            string currentImage, bool advance)
        {
            bool single = string.Equals(config.ImageMode, "single", StringComparison.OrdinalIgnoreCase);
            if (single || !advance)
            {
                string chosen = images[0];
                if (!single && !string.IsNullOrEmpty(currentImage))
                {
                    foreach (string file in images)
                    {
                        if (string.Equals(file, currentImage, StringComparison.OrdinalIgnoreCase))
                        {
                            return file;
                        }
                    }
                }
                return chosen;
            }
            if (images.Count == 1) return images[0];
            if (config.ImageShuffle)
            {
                string chosen = images[ImageRandom.Next(images.Count)];
                int guard = 0;
                while (string.Equals(chosen, currentImage, StringComparison.OrdinalIgnoreCase) && guard++ < 8)
                {
                    chosen = images[ImageRandom.Next(images.Count)];
                }
                return chosen;
            }

            int index = -1;
            for (int i = 0; i < images.Count; i++)
            {
                if (string.Equals(images[i], currentImage, StringComparison.OrdinalIgnoreCase))
                {
                    index = i;
                    break;
                }
            }
            return images[(index + 1) % images.Count];
        }

        // Advances each extra screen that has its own image settings. Mirrored
        // screens are painted by ApplyImage together with the primary screen.
        private static void UpdateExtraImageSlideshow()
        {
            // Save/apply runs on the settings UI thread while the watchdog is
            // also advancing pictures. Serialize the state transition so one
            // screen never gets two folder watchers at the same time.
            lock (ImageSync)
            {
                SyncScreenImageStates();
                foreach (ScreenImageState state in _screenImages)
                {
                    ScreenSettings settings = _config.ScreenFor(state.Device);
                    if (settings.ImagesSameAsMain)
                    {
                        StopScreenImageWatcher(state);
                        state.Active = false;
                        state.CurrentImage = "";
                        state.SurfaceImage = "";
                        continue;
                    }

                    AppConfig own = _config.ScreenConfig(settings);
                    if (state.ListDirty || !state.Active)
                    {
                        state.ListDirty = false;
                        ApplyScreenImage(state, own, false);
                    }
                    else if (!string.Equals(own.ImageMode, "single", StringComparison.OrdinalIgnoreCase) &&
                        DateTime.UtcNow >= state.NextImageAt)
                    {
                        ApplyScreenImage(state, own, true);
                    }
                }
            }
        }

        private static void ApplyScreenImage(ScreenImageState state, AppConfig config, bool advance)
        {
            string folder = Util.ResolvePath(_appRoot, config.ImageFolder);
            if (!string.Equals(folder, state.WatchedFolder, StringComparison.OrdinalIgnoreCase))
            {
                StartScreenImageWatcher(state, folder);
            }

            List<string> images = MediaPipeline.ResolveImages(folder, config);
            if (images == null || images.Count == 0)
            {
                string message = "no images for " + ScreenLabelForIndex(state.ScreenIndex) + " in " + folder;
                if (!string.Equals(message, state.LastMissingLog, StringComparison.Ordinal))
                {
                    Log.Warn(message);
                    state.LastMissingLog = message;
                }
                state.NextImageAt = DateTime.UtcNow.AddSeconds(config.ImageIntervalSeconds);
                state.Active = true;
                return;
            }
            state.LastMissingLog = "";

            string chosen = ChooseImage(config, images, state.CurrentImage, advance);
            if (!string.Equals(chosen, state.CurrentImage, StringComparison.OrdinalIgnoreCase))
            {
                Log.Info(ScreenLabelForIndex(state.ScreenIndex) + " image: " + Path.GetFileName(chosen));
            }
            if (!string.Equals(chosen, state.SurfaceImage, StringComparison.OrdinalIgnoreCase) || !advance)
            {
                if (state.ScreenIndex >= 0 && state.ScreenIndex < _imageSurfaces.Count)
                {
                    double fade = advance ? config.ImageCrossfadeSeconds : 0;
                    _imageSurfaces[state.ScreenIndex].Show(chosen, fade, !advance);
                }
                state.SurfaceImage = chosen;
            }
            state.CurrentImage = chosen;
            state.NextImageAt = DateTime.UtcNow.AddSeconds(Math.Max(1, config.ImageIntervalSeconds));
            state.Active = true;
        }

        // Keeps one slideshow record per supported extra screen. A changed
        // monitor layout rebuilds the surfaces and resets only the records
        // whose physical monitor changed.
        private static void SyncScreenImageStates()
        {
            lock (ImageSync) { SyncScreenImageStatesLocked(); }
        }

        private static void SyncScreenImageStatesLocked()
        {
            List<ScreenInfo> extras = new List<ScreenInfo>();
            bool duplicate = _screens != null && Screens.IsDuplicate(_screens);
            if (!duplicate && _screens != null)
            {
                foreach (ScreenInfo screen in _screens)
                {
                    if (screen.Primary) continue;
                    if (extras.Count >= 2) break;
                    extras.Add(screen);
                }
            }

            for (int i = _screenImages.Count - 1; i >= 0; i--)
            {
                ScreenImageState state = _screenImages[i];
                bool keep = false;
                foreach (ScreenInfo screen in extras)
                {
                    if (string.Equals(screen.Device, state.Device, StringComparison.OrdinalIgnoreCase))
                    {
                        keep = true;
                        state.ScreenIndex = screen.Index;
                        break;
                    }
                }
                if (!keep)
                {
                    StopScreenImageWatcher(state);
                    _screenImages.RemoveAt(i);
                }
            }

            foreach (ScreenInfo screen in extras)
            {
                bool found = false;
                foreach (ScreenImageState state in _screenImages)
                {
                    if (string.Equals(state.Device, screen.Device, StringComparison.OrdinalIgnoreCase))
                    {
                        state.ScreenIndex = screen.Index;
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    ScreenImageState state = new ScreenImageState();
                    state.Device = screen.Device ?? "";
                    state.ScreenIndex = screen.Index;
                    _screenImages.Add(state);
                }
            }
        }

        private static bool IsIndependentImageIndex(int index)
        {
            if (_config == null) return false;
            lock (ImageSync)
            {
                foreach (ScreenImageState state in _screenImages)
                {
                    if (state.ScreenIndex != index) continue;
                    return !_config.ScreenFor(state.Device).ImagesSameAsMain;
                }
                return false;
            }
        }

        private static string ScreenLabelForIndex(int index)
        {
            if (index == 1) return "second screen";
            if (index == 2) return "third screen";
            return "screen " + (index + 1);
        }

        private static void StartScreenImageWatcher(ScreenImageState state, string folder)
        {
            StopScreenImageWatcher(state);
            try
            {
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                FileSystemWatcher watcher = new FileSystemWatcher(folder);
                watcher.IncludeSubdirectories = false;
                watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size;
                FileSystemEventHandler changed = delegate(object sender, FileSystemEventArgs e)
                {
                    if (!MediaPipeline.IsImage(e.FullPath)) return;
                    state.ListDirty = true;
                    state.NextImageAt = DateTime.UtcNow;
                };
                RenamedEventHandler renamed = delegate(object sender, RenamedEventArgs e)
                {
                    if (!MediaPipeline.IsImage(e.FullPath) && !MediaPipeline.IsImage(e.OldFullPath)) return;
                    state.ListDirty = true;
                    state.NextImageAt = DateTime.UtcNow;
                };
                watcher.Created += changed;
                watcher.Changed += changed;
                watcher.Deleted += changed;
                watcher.Renamed += renamed;
                watcher.Error += delegate { state.ListDirty = true; };
                watcher.EnableRaisingEvents = true;
                state.Watcher = watcher;
                state.WatchedFolder = folder;
                Log.Info("watching " + ScreenLabelForIndex(state.ScreenIndex) + " images in " + folder);
            }
            catch (Exception ex)
            {
                Log.Warn("could not watch an extra image folder: " + ex.Message);
                state.WatchedFolder = folder;
            }
        }

        private static void StopScreenImageWatcher(ScreenImageState state)
        {
            if (state == null || state.Watcher == null) return;
            try
            {
                state.Watcher.EnableRaisingEvents = false;
                state.Watcher.Dispose();
            }
            catch { /* best-effort dispose on teardown */ }
            state.Watcher = null;
            state.WatchedFolder = "";
        }

        private static void StopExtraImageWatchers()
        {
            lock (ImageSync)
            {
                foreach (ScreenImageState state in _screenImages) StopScreenImageWatcher(state);
            }
        }

        private static void ResetExtraImageStates()
        {
            lock (ImageSync)
            {
                foreach (ScreenImageState state in _screenImages)
                {
                    StopScreenImageWatcher(state);
                    state.Active = false;
                    state.ListDirty = false;
                    state.CurrentImage = "";
                    state.SurfaceImage = "";
                }
            }
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
            lock (ImageSync)
            {
                if (_screens == null || _screens.Count == 0) _screens = Screens.Enumerate();
                int wanted = Math.Max(1, _screens.Count);
                while (_imageSurfaces.Count > wanted)
                {
                    ImageSurface extra = _imageSurfaces[_imageSurfaces.Count - 1];
                    _imageSurfaces.RemoveAt(_imageSurfaces.Count - 1);
                    try { extra.Shutdown(); } catch { /* best-effort shutdown of a removed surface */ }
                }
                for (int i = 0; i < wanted; i++)
                {
                    if (i >= _imageSurfaces.Count) _imageSurfaces.Add(new ImageSurface());
                    ImageSurface surface = _imageSurfaces[i];
                    Rectangle bounds = i < _screens.Count ? _screens[i].Bounds : _screens[0].Bounds;
                    if (!surface.IsStarted)
                    {
                        surface.Start(progman, bounds);
                        foreach (ScreenPlayer player in _screenPlayers) player.FallbackVisible = false;
                    }
                    else if (progman != IntPtr.Zero) surface.Attach(progman);
                }
                SyncScreenImageStates();
            }
        }

        private static void ShutdownImageSurfaces()
        {
            lock (ImageSync)
            {
                StopExtraImageWatchers();
                _screenImages.Clear();
                foreach (ImageSurface surface in _imageSurfaces)
                {
                    try { surface.Shutdown(); } catch { /* best-effort shutdown on teardown */ }
                }
                _imageSurfaces.Clear();
                foreach (ScreenPlayer player in _screenPlayers) player.FallbackVisible = false;
            }
        }

        private static void ImageAttachAll(IntPtr progman)
        {
            lock (ImageSync)
            {
                foreach (ImageSurface surface in _imageSurfaces)
                {
                    if (surface.IsStarted) surface.Attach(progman);
                }
            }
        }

        private static void ImageShowMainAndMirrors(string path, double fadeSeconds, bool immediate)
        {
            lock (ImageSync)
            {
                for (int i = 0; i < _imageSurfaces.Count; i++)
                {
                    if (IsIndependentImageIndex(i)) continue;
                    ImageSurface surface = _imageSurfaces[i];
                    if (surface.IsStarted) surface.Show(path, fadeSeconds, immediate);
                }
            }
        }

        // Video's static fallback remains the native Windows wallpaper on every
        // screen by default. Only an independent screen whose desktop fallback
        // was explicitly customized needs an app-owned image layer.
        private static void UpdateFallbackSurfaces()
        {
            if (IsImagesMode || _config == null || _screens == null) return;
            lock (ImageSync)
            {
                bool any = false;
                foreach (ScreenPlayer screen in _screenPlayers)
                {
                    if (NeedsFallbackSurface(screen))
                    {
                        any = true;
                        break;
                    }
                }
                if (!any)
                {
                    ImageHideAll();
                    foreach (ScreenPlayer screen in _screenPlayers)
                    {
                        screen.FallbackVisible = false;
                        screen.FallbackImage = "";
                    }
                    return;
                }
                if (_progman == IntPtr.Zero) return;
                StartImageSurfaces(_progman);

                // The primary and mirroring screens use the native wallpaper.
                for (int i = 0; i < _imageSurfaces.Count; i++)
                {
                    bool managed = false;
                    foreach (ScreenPlayer screen in _screenPlayers)
                    {
                        if (screen.ScreenIndex == i && NeedsFallbackSurface(screen))
                        {
                            managed = true;
                            break;
                        }
                    }
                    if (!managed && _imageSurfaces[i].IsStarted) _imageSurfaces[i].Hide();
                }

                foreach (ScreenPlayer screen in _screenPlayers)
                {
                    if (!NeedsFallbackSurface(screen))
                    {
                        HideFallbackSurface(screen);
                        continue;
                    }
                    bool playing = !_paused && screen.Player != null && screen.Player.IsRunning;
                    if (playing || screen.ScreenIndex < 0 || screen.ScreenIndex >= _imageSurfaces.Count)
                    {
                        HideFallbackSurface(screen);
                        continue;
                    }

                    AppConfig own = _config.ScreenConfig(_config.ScreenFor(screen.Device));
                    string path = DesktopHost.WallpaperFor(_appRoot, own, screen.FramePath);
                    if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    {
                        HideFallbackSurface(screen);
                        continue;
                    }
                    if (!screen.FallbackVisible ||
                        !string.Equals(path, screen.FallbackImage, StringComparison.OrdinalIgnoreCase))
                    {
                        _imageSurfaces[screen.ScreenIndex].Show(path, 0, true);
                        screen.FallbackImage = path;
                        screen.FallbackVisible = true;
                    }
                }
            }
        }

        private static bool NeedsFallbackSurface(ScreenPlayer screen)
        {
            if (screen == null || screen.Player == null || _config == null ||
                !_config.DesktopWallpaper) return false;
            ScreenSettings settings = _config.ScreenFor(screen.Device);
            return !settings.SameAsMain && !string.IsNullOrEmpty(settings.DesktopWallpaperMode);
        }

        private static void HideFallbackSurface(ScreenPlayer screen)
        {
            if (screen == null) return;
            lock (ImageSync)
            {
                if (screen.FallbackVisible && screen.ScreenIndex >= 0 && screen.ScreenIndex < _imageSurfaces.Count)
                {
                    _imageSurfaces[screen.ScreenIndex].Hide();
                }
                screen.FallbackVisible = false;
                screen.FallbackImage = "";
            }
        }

        private static void ImageEnsureVisibleAll()
        {
            lock (ImageSync)
            {
                foreach (ImageSurface surface in _imageSurfaces)
                {
                    if (surface.IsStarted) surface.EnsureVisible();
                }
            }
        }

        private static void ImageHideAll()
        {
            lock (ImageSync)
            {
                foreach (ImageSurface surface in _imageSurfaces)
                {
                    if (surface.IsStarted) surface.Hide();
                }
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

            // Copy-on-write: build the new list and publish it in one write at
            // the end, so a reader iterating the previous list always keeps a
            // stable snapshot.
            List<ScreenPlayer> next = new List<ScreenPlayer>(_screenPlayers);

            // Retire the players whose monitor is gone.
            for (int i = next.Count - 1; i >= 0; i--)
            {
                ScreenPlayer existing = next[i];
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
                    try { existing.Player.Stop(); } catch { /* best-effort stop of a retired player */ }
                }
                next.RemoveAt(i);
            }

            // Add a player for each new monitor and refresh the ones kept.
            int number = 2;
            foreach (ScreenInfo screen in extras)
            {
                ScreenPlayer player = null;
                foreach (ScreenPlayer existing in next)
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
                    next.Add(player);
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

            _screenPlayers = next;
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
                screen.FramePath = _mainFramePath;
                return;
            }

            AppConfig screenConfig = _config.ScreenConfig(settings);
            MediaManifest manifest;
            if (MediaPipeline.IsScreenCacheValid(_appRoot, screenConfig, screen.Device, out manifest, false))
            {
                screen.MediaPath = MediaPipeline.LoopPath(_appRoot, manifest);
                screen.FramePath = MediaPipeline.FramePath(_appRoot, manifest);
            }
            else
            {
                screen.MediaPath = "";
                screen.FramePath = "";
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
                  .Append(screen.SingleClip ?? "").Append('\u0001')
                  .Append(screen.CrossfadeSeconds.HasValue
                      ? screen.CrossfadeSeconds.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                      : "").Append('\u0001')
                  .Append(screen.FineCrossfadeSteps.HasValue
                      ? (screen.FineCrossfadeSteps.Value ? '1' : '0') : '-').Append('\u0001');
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
            catch { /* best-effort: an unknown process counts as not running */ }
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
                catch { /* best-effort dispose on teardown */ }
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
                catch { /* best-effort dispose on teardown */ }
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
