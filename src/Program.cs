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
    public static partial class Program
    {
        private static string _appRoot;
        private static volatile AppConfig _config;
        private static MpvPlayer _player;
        // PID of an mpv the native bootstrapper started and this process adopted.
        // The startup orphan scan must not reap it. Written by the handoff
        // waiter, read from the scan thread, so it is volatile.
        private static volatile int _adoptedEarlyMpvPid;
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
        private static volatile bool _shuttingDown;
        private static volatile bool _resumeFromPause;
        private static volatile bool _forceBuild;
        private static volatile int _buildGeneration;
        private static volatile bool _prefetching;
        private static volatile bool _prefetchCancel;
        private static volatile bool _restartPlayer;
        private static bool _prefetchDone;
        private static DateTime _prefetchAfter = DateTime.MaxValue;
        // The video folder and the image folder are each watched by a
        // FolderWatcher. The two instances live for the life of the process and
        // report their changes back into the app's state through these
        // handlers, so the callbacks read exactly as the old hand-written
        // watchers did.
        private static readonly FolderWatcher _videoWatcher = new FolderWatcher(
            "video folder", MediaPipeline.IsVideo, OnVideoFolderChanged, OnVideoFolderError);
        private static readonly FolderWatcher _imageWatcher = new FolderWatcher(
            "image folder", MediaPipeline.IsImage, OnImageFolderChanged, OnImageFolderChanged);
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
        // Independent slideshow state for supported extra screens. The same
        // surfaces are reused for per-screen desktop fallback images in Video
        // mode.
        private static readonly List<ScreenImageState> _screenImages = new List<ScreenImageState>();
        private static string _mainFramePath = "";
        private static IntPtr _imageProgman = IntPtr.Zero;
        private static string _lastMissingImageLog = "";
        private static readonly Random ImageRandom = new Random();
        private static bool _cleanupDone;
        private static DateTime _processStartUtc;
        private static Stopwatch _startupSw;
        private static int _firstFrameProbeStarted;

        private static long StartupElapsedMs()
        {
            if (_startupSw != null) return _startupSw.ElapsedMilliseconds;
            return (long)(DateTime.UtcNow - _processStartUtc).TotalMilliseconds;
        }

        // A settings save applies on its own thread, because the work (folder
        // signature scans and waiting for the image layers to come up) can
        // block for seconds and the settings window must not freeze. Only the
        // newest save is kept: one that arrives while an apply is running
        // replaces the pending one, so no queue builds up.
        private static readonly object ApplySync = new object();
        private static AppConfig _applyQueued;
        private static bool _applyWorkerRunning;

        // A wallpaper video that will not play, or a loop that will not build,
        // must not be retried forever. These count consecutive rapid failures
        // and stop with a single tray notification once the limit is reached.
        private const int MaxPlayerRestarts = 5;
        private const int MaxBuildFailures = 3;
        private static int _playerRestarts;
        private static DateTime _playerRestartWindow = DateTime.MinValue;
        private static bool _playerRestartStopped;
        private static int _buildFailures;
        private static bool _buildFailureStopped;

        // Display layout. _screens[0] is the primary screen and _screens[1..3]
        // are optional extra ones (up to 4 screens total); the list is refreshed
        // when Windows reports a change. The list is only ever replaced (never
        // mutated) after Screens.Enumerate returns it, so a reader that reads
        // the field once always sees a complete layout. volatile makes the
        // replacement visible to the watchdog and UI threads promptly.
        private static volatile List<ScreenInfo> _screens = new List<ScreenInfo>();
        private static string _screensSignature = "";
        private static DateTime _nextScreenCheck = DateTime.MinValue;
        private static bool _layoutChanged;
        private static bool _screenCountChanged;

        // One player per extra screen, at most three (the second, third and
        // fourth monitors). Each is remembered by monitor device name, so its
        // settings survive replugging and reordering. _screensDirty is set by a
        // settings save that touched any screen. The list is copy-on-write: the
        // watchdog builds a new list and swaps it in, so a UI thread iterating
        // the old one can never hit a "collection was modified" exception.
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
            // Consecutive failed builds for this screen, so a folder that
            // cannot be rendered is not retried forever.
            public int Failures;
            public bool FailureNotified;
            // Consecutive rapid player restarts for this screen, so a loop it
            // cannot play is not retried forever.
            public int Restarts;
            public DateTime RestartWindow = DateTime.MinValue;
            public bool RestartStopped;
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
            try
            {
                string state = Installer.PauseStateFile(_appRoot);
                if (paused) File.WriteAllText(state, "paused");
                else Util.SafeDelete(state);
            }
            catch { /* best-effort: tray state file */ }
            if (paused)
            {
                // A later resume is expected to bring the video back, which is
                // not the same as recovering a crashed player.
                _resumeFromPause = true;
            }
            else
            {
                _nextCheck = DateTime.UtcNow;
                if (IsImagesMode)
                {
                    lock (ImageSync) { _nextImageAt = DateTime.UtcNow; }
                }
                // A resume is a deliberate fresh start for the video.
                ResetRestartBackoff();
            }
            Log.Info(paused ? "paused by the user" : "resumed by the user");
            Ui.RefreshMenu();
        }

        public static void RequestRebuild()
        {
            _forceBuild = true;
            _nextCheck = DateTime.UtcNow;
            // An explicit rebuild is a fresh attempt; let a later failure
            // notify again instead of staying silent from the previous run.
            ResetBuildFailureBackoff();
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
            // Called from the tray and the settings window, so it must take the
            // same lock the watchdog uses; otherwise it races a slideshow tick
            // (and the shared System.Random behind ImageShuffle).
            lock (ImageSync)
            {
                _imageListDirty = false;
                _nextImageAt = DateTime.UtcNow;
                _imageActive = true;
            }
            Log.Info("next image requested");
        }

        public static void RequestExit()
        {
            Log.Info("exit requested from the tray");
            _shuttingDown = true;
            try { File.WriteAllText(Installer.QuitFlag(_appRoot), "quit"); }
            catch { /* best-effort; the stop flag below is the fallback */ }
            try { File.WriteAllText(Installer.StopFlag(_appRoot), "stop"); }
            catch { /* best-effort; the process is exiting anyway */ }
            Ui.Stop();
        }

        // Called by the settings window after it saves config.json. A fresh
        // config object is published in one atomic swap and every player is
        // re-pointed at it, then a rebuild is forced.
        //
        // The work can block for seconds (folder signature scans, waiting for
        // the image layers), so it runs on its own thread and the settings
        // window can close right away. A save that arrives while an apply is
        // running replaces the pending one, so the newest settings always win.
        public static void ApplyConfig(AppConfig updated)
        {
            if (updated == null || _config == null) return;
            lock (ApplySync)
            {
                _applyQueued = updated.Clone();
                if (_applyWorkerRunning) return;
                _applyWorkerRunning = true;
            }
            Thread worker = new Thread(ApplyWorker);
            worker.IsBackground = true;
            worker.Name = "livebackground-apply";
            worker.Start();
        }

        private static void ApplyWorker()
        {
            while (true)
            {
                AppConfig next;
                lock (ApplySync)
                {
                    next = _applyQueued;
                    _applyQueued = null;
                    if (next == null)
                    {
                        _applyWorkerRunning = false;
                        return;
                    }
                }
                try { ApplyConfigCore(next); }
                catch (Exception ex) { Log.Error("applying settings failed", ex); }
            }
        }

        private static void ApplyConfigCore(AppConfig updated)
        {
            if (updated == null || _config == null) return;
            // New settings are a fresh start: retry a video or a build that
            // had been given up on earlier.
            ResetRestartBackoff();
            ResetBuildFailureBackoff();
            bool wasImages = IsImagesMode;
            // Idle/screensaver actions are app-managed (mpv always uses
            // --stop-screensaver=yes), so toggling them does not require
            // restarting the video player.
            bool lockChanged = !string.Equals(_config.LockScreenMode, updated.LockScreenMode,
                StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(_config.LockScreenImage ?? "", updated.LockScreenImage ?? "",
                StringComparison.OrdinalIgnoreCase);
            // Any change to a screen's own settings (mirroring, folder, mode or
            // clip) is noticed here, so the players are re-pointed and any
            // needed loop rebuilt in the watchdog. The token covers all screens
            // at once; comparing it before and after the swap catches them.
            string screensBefore = ScreenConfigToken.Compute(_config);
            // Publish a brand-new config object in a single atomic reference
            // write instead of copying into the live one field by field. The
            // watchdog reads _config without a lock, so it must never observe a
            // half-applied update; with a swap it always sees either the old
            // config or the new one, never a mix of the two.
            AppConfig fresh = updated.Clone();
            _config = fresh;
            ReconfigurePlayers(fresh);
            if (!string.Equals(screensBefore, ScreenConfigToken.Compute(_config), StringComparison.Ordinal))
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
                // This runs on the apply thread while the watchdog advances the
                // slideshow. Take ImageSync so the whole transition - the flags,
                // the chosen picture and the fade - is atomic with respect to a
                // watchdog tick; otherwise both threads could each pick a picture
                // and clobber each other (and the shared Random).
                //
                // The folder watcher is (re)started outside the lock: replacing
                // it disposes the old one, and that must not happen while a
                // callback that is waiting on ImageSync could still be in flight.
                string imageFolder = Util.ResolvePath(_appRoot, _config.ImageFolder);
                if (!string.Equals(imageFolder, WatchedImageFolder, StringComparison.OrdinalIgnoreCase))
                {
                    StartImageWatcher(imageFolder);
                }
                lock (ImageSync)
                {
                    _imageActive = false;
                    _nextImageAt = DateTime.UtcNow;
                    _imageProgman = Native.FindWindow("Progman", null);
                    StartImageSurfaces(_imageProgman);
                    ApplyImage(false);
                    _imageActive = true;
                }
                UpdateExtraImageSlideshow();
                if (!string.IsNullOrEmpty(_currentImage)) ImageEnsureVisibleAll();
                Log.Info("settings applied; images mode is active");
                Ui.BuildProgressFinish(true, "Wallpaper updated");
                Ui.RefreshMenu();
                return;
            }

            lock (ImageSync)
            {
                _imageActive = false;
                _nextImageAt = DateTime.MinValue;
                ResetExtraImageStates();
                ImageHideAll();
                _surfaceImage = "";
            }
            // Switching back from Images mode has to start the cached loop
            // again; the player was stopped when images mode took over.
            if (wasImages) _restartPlayer = true;
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
            // Wake the watchdog's loop check too, exactly like a folder change
            // does. Without this a settings save only re-checks the extra
            // screens and the main loop keeps playing the old clip. This is a
            // non-forced check, so a cached clip swaps in instantly and only an
            // uncached one is encoded.
            _dirty = true;
            _lastWatcherEvent = DateTime.MinValue;
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

        // A settings change, a resume or a loop that has played for a while
        // clears the restart backoff so a later failure gets a fresh set of
        // attempts.
        private static void ResetRestartBackoff()
        {
            _playerRestarts = 0;
            _playerRestartWindow = DateTime.MinValue;
            _playerRestartStopped = false;
        }

        private static void ResetBuildFailureBackoff()
        {
            _buildFailures = 0;
            _buildFailureStopped = false;
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
            _startupSw = Stopwatch.StartNew();
            Util.UseEnglishUiCulture();
            _processStartUtc = DateTime.UtcNow;
            try { _processStartUtc = Process.GetCurrentProcess().StartTime.ToUniversalTime(); } catch { /* best-effort: fall back to the time we started */ }

            string programRoot = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            Paths.Init(programRoot);
            _appRoot = Paths.DataRoot;
            try
            {
                string profileDir = Path.Combine(_appRoot, "cache", "profiles");
                Directory.CreateDirectory(profileDir);
                System.Runtime.ProfileOptimization.SetProfileRoot(profileDir);
                System.Runtime.ProfileOptimization.StartProfile("startup.profile");
            }
            catch { /* best-effort: profile-guided JIT optimization */ }
            Log.Init(_appRoot);
            ulong tickCount64 = Native.GetTickCount64();
            DateTime approxBootUtc = DateTime.UtcNow.AddMilliseconds(-(double)tickCount64);
            Log.Info("startup: Main entered " + _startupSw.ElapsedMilliseconds + " ms after process timer start; " +
                "process start " + _processStartUtc.ToString("o") + "; tickCount64 " + tickCount64 +
                " ms; approx boot " + approxBootUtc.ToString("o"));
            // Leave a readable trace if anything escapes the normal paths.
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
            {
                Log.Error("unhandled exception", e.ExceptionObject as Exception);
            };

            // The logon task also schedules this lightweight check. It only
            // starts the wallpaper when the tray app is missing, so a crash is
            // recovered without ever fighting the single-instance mutex.
            if (CommandLine.HasArg(args, "--watchdog"))
            {
                return RunWatchdog();
            }

            // "LiveBackground.exe --settings" opens the settings window. If another
            // instance is running, the flag file tells it to open settings.
            bool settingsRequested = CommandLine.HasArg(args, "--settings");
            if (settingsRequested)
            {
                try { File.WriteAllText(Installer.SettingsFlag(_appRoot), "open"); }
                catch { /* best-effort: the running instance may already be gone */ }
            }

            if (CommandLine.HasArg(args, "--stop"))
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
            if (CommandLine.HasArg(args, "--pause"))
            {
                try { File.WriteAllText(Installer.PauseFlag(_appRoot), "pause"); }
                catch { /* best-effort: the running instance may already be gone */ }
                Log.Info("pause requested");
                return 0;
            }

            if (CommandLine.HasArg(args, "--resume"))
            {
                try { File.WriteAllText(Installer.ResumeFlag(_appRoot), "resume"); }
                catch { /* best-effort: the running instance may already be gone */ }
                Log.Info("resume requested");
                return 0;
            }

            if (CommandLine.HasArg(args, "--rebuild"))
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
                if (settingsRequested)
                {
                    try
                    {
                        string settingsExe = Path.Combine(Paths.ProgramRoot, "LiveBackgroundSettings.exe");
                        if (File.Exists(settingsExe))
                        {
                            Process.Start(new ProcessStartInfo(settingsExe) { UseShellExecute = false, WorkingDirectory = Paths.ProgramRoot });
                            Log.Info("settings window launched");
                        }
                        else Log.Warn("settings exe not found: " + settingsExe);
                    }
                    catch (Exception ex) { Log.Warn("could not launch settings: " + ex.Message); }
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
                    _shuttingDown = true;
                    // A graceful exit must never leave the shell hidden by the
                    // video screensaver. A hard kill is covered at startup and
                    // by the watchdog, both of which call Show() as well.
                    ShellChrome.Show();
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

        private static void StartFirstFrameProbe(MpvPlayer player)
        {
            if (player == null) return;
            if (Interlocked.CompareExchange(ref _firstFrameProbeStarted, 1, 0) != 0) return;
            string logPath = player.LogPath;
            DateTime startedUtc = player.StartedUtc;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    Stopwatch wait = Stopwatch.StartNew();
                    long lastLength = -1;
                    while (wait.ElapsedMilliseconds < 15000)
                    {
                        try
                        {
                            if (File.Exists(logPath))
                            {
                                using (FileStream fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                                {
                                    if (fs.Length != lastLength)
                                    {
                                        lastLength = fs.Length;
                                        fs.Position = 0;
                                        using (StreamReader reader = new StreamReader(fs, Encoding.UTF8, true))
                                        {
                                            string text = reader.ReadToEnd();
                                            if (text.IndexOf("first video frame", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                                text.IndexOf("shown", StringComparison.OrdinalIgnoreCase) >= 0)
                                            {
                                                int msAfterMpv = (int)(DateTime.UtcNow - startedUtc).TotalMilliseconds;
                                                Log.Info("startup: mpv first video frame shown " + msAfterMpv +
                                                    " ms after mpv start (" + StartupElapsedMs() + " ms after process timer start)");
                                                return;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        catch { /* best-effort: probe must never affect playback */ }
                        Thread.Sleep(200);
                    }
                }
                catch { /* best-effort */ }
            });
        }

        // Runs from the scheduled watchdog task. Does nothing when the app is
        // healthy or when the user exited from the tray on purpose.
        private static int RunWatchdog()
        {
            try
            {
                // Belt and braces: if a hard kill happened while the video
                // screensaver had the shell hidden, restore it before doing
                // anything else. Show() is a no-op when it is already visible.
                ShellChrome.Show();
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

        private static string StartupHintPath()
        {
            return Path.Combine(_appRoot, "startup-hint.txt");
        }

        private static void SaveStartupHint(bool imagesMode, string mediaPath)
        {
            try
            {
                string path = StartupHintPath();
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                string content = (imagesMode ? "images" : "video") + "\n" + (mediaPath ?? "") + "\n";
                File.WriteAllText(path, content, Encoding.UTF8);
            }
            catch { /* best-effort: hint is an optimization */ }
        }

        private static bool TryReadStartupHint(out bool imagesMode, out string mediaPath)
        {
            imagesMode = false;
            mediaPath = "";
            try
            {
                string path = StartupHintPath();
                if (!File.Exists(path)) return false;
                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                if (lines.Length < 2) return false;
                string mode = (lines[0] ?? "").Trim().ToLowerInvariant();
                string media = (lines[1] ?? "").Trim();
                if (mode != "video" && mode != "images") return false;
                imagesMode = mode == "images";
                if (imagesMode)
                {
                    mediaPath = media;
                    return true;
                }
                if (string.IsNullOrEmpty(media) || !File.Exists(media)) return false;
                mediaPath = media;
                return true;
            }
            catch { /* best-effort: fall back to normal startup */ }
            imagesMode = false;
            mediaPath = "";
            return false;
        }

        // The native bootstrapper writes "pending" before it launches us, then
        // replaces it with the mpv PID once the desktop exists and mpv is up.
        // This lets us skip our own managed early start and adopt that process
        // instead, without ever starting two videos.
        private static string EarlyMpvHandoffPath()
        {
            return Path.Combine(_appRoot, "cache", "early-mpv.txt");
        }

        private static void DeleteEarlyMpvHandoff()
        {
            Util.SafeDelete(EarlyMpvHandoffPath());
        }

        private static bool TryReadEarlyMpvHandoff(out string mediaPath, out int pid)
        {
            mediaPath = "";
            pid = 0;
            try
            {
                string path = EarlyMpvHandoffPath();
                if (!File.Exists(path)) return false;
                if ((DateTime.UtcNow - File.GetLastWriteTimeUtc(path)).TotalMinutes > 5) return false;
                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                if (lines.Length < 2) return false;
                string first = (lines[0] ?? "").Trim();
                mediaPath = (lines[1] ?? "").Trim();
                if (string.IsNullOrEmpty(mediaPath)) return false;
                if (string.Equals(first, "pending", StringComparison.OrdinalIgnoreCase)) return true;
                if (!int.TryParse(first, out pid) || pid <= 0) return false;
                return true;
            }
            catch { /* best-effort: handoff is an optimization */ }
            mediaPath = "";
            pid = 0;
            return false;
        }

        private static void WaitForBootstrapperMpv(string expectedMedia, ManualResetEvent done)
        {
            try
            {
                Stopwatch wait = Stopwatch.StartNew();
                while (wait.ElapsedMilliseconds < 15000)
                {
                    string media;
                    int pid;
                    if (TryReadEarlyMpvHandoff(out media, out pid) && pid > 0 &&
                        string.Equals(media, expectedMedia, StringComparison.OrdinalIgnoreCase))
                    {
                        // Exclude it from the orphan scan even if validation is
                        // still in progress: the scan may run concurrently.
                        _adoptedEarlyMpvPid = pid;
                        if (_player != null && _player.Adopt(pid, expectedMedia))
                        {
                            if (_jobHandle != IntPtr.Zero && Native.AssignPidToJob(_jobHandle, pid))
                                Log.Info("startup: bootstrapper mpv added to child-process job");
                            else Log.Warn("startup: could not add bootstrapper mpv to child-process job");
                            Log.Info("startup: adopted bootstrapper mpv pid " + pid + " (" + StartupElapsedMs() +
                                " ms after process timer start)");
                            return;
                        }
                        _adoptedEarlyMpvPid = 0;
                        Log.Warn("startup: bootstrapper mpv pid " + pid + " failed validation");
                        return;
                    }
                    Thread.Sleep(50);
                }
                Log.Warn("startup: bootstrapper mpv handoff timed out");
            }
            catch (Exception ex) { Log.Warn("startup: bootstrapper mpv handoff failed: " + ex.Message); }
            finally { try { done.Set(); } catch { } }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "earlyStartDone/orphanScanDone are disposed on normal return paths; a fatal Run exception already tears down the process.")]
        private static int Run()
        {
            DesktopHost.EnablePerMonitorDpi();
            // Recover from a previous run that was killed while "Screensaver
            // Video Background" had the shell hidden. Show() is a no-op when
            // the taskbar and icons are already visible.
            long shellChromeMs = _startupSw != null ? _startupSw.ElapsedMilliseconds : -1;
            ShellChrome.Show();
            long shellChromeDoneMs = _startupSw != null ? _startupSw.ElapsedMilliseconds : -1;
            RecoverScreensaverCursor();

            Log.Info("=== LiveBackground starting === (" + StartupElapsedMs() + " ms after process timer start)");
            Log.Info("startup: ShellChrome.Show " + (shellChromeDoneMs - shellChromeMs) + " ms (started " + shellChromeMs + " ms)");
            // Win the logon race: the early startup work below (config, displays,
            // process reap) is CPU-bound, so run it at High and outrank the other
            // logon apps. Dropped back to Normal once the wallpaper is up.
            SetStartupPriority(true);
            Log.Info("startup priority raised to High for the logon race");
            // Own every process we start: a job that kills its children when
            // this process exits, even on a hard kill, so an mpv can never be
            // left painting over the desktop. Best-effort; if the OS refuses we
            // carry on and rely on the startup reap below.
            _jobHandle = Native.CreateKillOnCloseJob();
            if (_jobHandle != IntPtr.Zero) Log.Info("child-process job created (mpv is reaped even on a hard kill)");
            else Log.Warn("child-process job unavailable; orphans are only reaped at startup");
            Stopwatch startupStep = Stopwatch.StartNew();
            // Reap orphaned mpv from a hard-killed previous run in the
            // background. Any path that starts our own mpv waits for this scan
            // so we never show two videos and never kill our own early start.
            ManualResetEvent orphanScanDone = new ManualResetEvent(false);
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    Stopwatch s = Stopwatch.StartNew();
                    int reaped = Installer.KillOrphanMpv(delegate(string message) { Log.Info(message); },
                        delegate(int pid) { return pid == _adoptedEarlyMpvPid; });
                    if (reaped > 0) Log.Info("reaped " + reaped + " orphaned mpv process(es) at startup");
                    Log.Info("startup: orphan mpv scan " + s.ElapsedMilliseconds + " ms");
                }
                catch (Exception ex) { Log.Warn("startup: orphan mpv scan failed: " + ex.Message); }
                finally { try { orphanScanDone.Set(); } catch { } }
            });
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

            // Early-start: prefer an mpv the native bootstrapper already
            // launched, and only start one here when the app was launched
            // directly. The bootstrapper writes a "pending" handoff before it
            // launches us, so we never start a second mpv while it is still
            // waiting for the desktop. Reconciled below once the real config is
            // known; any mismatch falls back to the normal path.
            bool earlyHintImages = false;
            string earlyHintMedia = "";
            ManualResetEvent earlyStartDone = null;
            bool earlyVideoStarted = TryReadStartupHint(out earlyHintImages, out earlyHintMedia) && !earlyHintImages;
            string handoffMedia;
            int handoffPid;
            bool bootstrapperMpv = TryReadEarlyMpvHandoff(out handoffMedia, out handoffPid) &&
                earlyVideoStarted &&
                string.Equals(handoffMedia, earlyHintMedia, StringComparison.OrdinalIgnoreCase);
            if (earlyVideoStarted)
            {
                Log.Info("startup: early hint video " + earlyHintMedia);
                _mediaPath = earlyHintMedia;
                if (_player == null) _player = new MpvPlayer(_appRoot, new AppConfig());
                earlyStartDone = new ManualResetEvent(false);
                ManualResetEvent done = earlyStartDone;
                if (bootstrapperMpv)
                {
                    Log.Info("startup: waiting for bootstrapper mpv handoff");
                    ThreadPool.QueueUserWorkItem(delegate { WaitForBootstrapperMpv(earlyHintMedia, done); });
                }
                else
                {
                    DeleteEarlyMpvHandoff();
                    string hintMedia = earlyHintMedia;
                    ThreadPool.QueueUserWorkItem(delegate
                    {
                        try
                        {
                            IntPtr progman = DesktopHost.WaitForProgman(30000);
                            if (progman == IntPtr.Zero)
                            {
                                Log.Warn("startup: early Progman not found");
                                return;
                            }
                            lock (Sync) { if (_progman == IntPtr.Zero) _progman = progman; }
                            try { orphanScanDone.WaitOne(30000); } catch { /* best-effort */ }
                            if (_player != null && _player.Start(progman, hintMedia))
                            {
                                Log.Info("startup: early mpv started (" + StartupElapsedMs() + " ms after process timer start)");
                            }
                        }
                        catch (Exception ex) { Log.Warn("startup: early mpv start failed: " + ex.Message); }
                        finally { try { done.Set(); } catch { } }
                    });
                }
            }
            else DeleteEarlyMpvHandoff();

            startupStep.Restart();
            bool firstRun = !File.Exists(ConfigStore.PathFor(_appRoot));
            _config = ConfigStore.LoadOrCreate(_appRoot);
            Log.Info("startup: config load " + startupStep.ElapsedMilliseconds + " ms");
            // A config that could not be read silently resets everything the
            // user configured; say so once instead of letting them think the
            // app forgot their settings. Ui.Notify holds the message until the
            // tray thread is ready.
            if (!string.IsNullOrEmpty(ConfigStore.LoadWarning))
            {
                Log.Warn("config warning: " + ConfigStore.LoadWarning);
                Ui.Notify("LiveBackground", ConfigStore.LoadWarning);
            }
            startupStep.Restart();
            _screens = Screens.Enumerate();
            _screensSignature = Screens.Signature(_screens);
            Log.Info("displays: " + ScreenText.DescribeAll(_screens, Screens.IsDuplicate(_screens)));
            Log.Info("startup: screen enumeration " + startupStep.ElapsedMilliseconds + " ms");
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

            Log.Info("startup: cache/manifest ready " + StartupElapsedMs() + " ms after process timer start");
            startupStep.Restart();
            // Reconfigure the player that either the bootstrapper or our own
            // early-start thread created. The decision to keep it is made after
            // the handoff event is signalled, so we cannot race an adoption.
            if (_player == null) _player = new MpvPlayer(_appRoot, _config);
            else _player.Reconfigure(_config);

            // Each extra display gets its own player. A screen mirrors the main
            // loop by default; an independent loop is built on demand. Duplicate
            // mode drives every target from one desktop monitor, so it never
            // gets extra players.
            RebuildScreenPlayers();
            Log.Info("startup: players initialized " + startupStep.ElapsedMilliseconds + " ms");

            // The lock screen and tray are not needed for the first wallpaper
            // frame. Starting them later keeps them from competing with the
            // first cold disk read.
            StartLockScreenWorker();
            StartScreensaverWorker();

            if (imagesMode)
            {
                // No mpv, no WorkerW layer and no loop work at all. The still
                // image goes up immediately and the rotation timer takes over.
                // A stale video hint can make the bootstrapper start mpv before
                // the real config is read; stop it here if that happened.
                try { _player.Stop(); } catch { /* best-effort */ }
                DeleteEarlyMpvHandoff();
                _adoptedEarlyMpvPid = 0;
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
                SaveStartupHint(true, _currentImage ?? "");
                SetStartupPriority(false);
                Log.Info("startup priority restored to Normal");
                Ui.Start(_appRoot);
                if (earlyStartDone != null) try { earlyStartDone.Dispose(); } catch { }
                try { orphanScanDone.Dispose(); } catch { }
                return WatchLoop();
            }

            if (_progman == IntPtr.Zero) _progman = DesktopHost.WaitForProgman(30000);
            if (_progman == IntPtr.Zero)
            {
                Log.Error("Progman not found, the desktop is not ready");
            }
            else
            {
                Log.Info("Progman = 0x" + _progman.ToString("X"));
                // Wait for the bootstrapper handoff (or our own managed early
                // start) before deciding whether the running player matches the
                // real config. This removes the race where a mismatch stopped a
                // player that had not been adopted yet.
                if (earlyStartDone != null)
                {
                    try { earlyStartDone.WaitOne(30000); } catch { /* best-effort */ }
                }
                bool earlyMainMatches = earlyVideoStarted &&
                    !string.IsNullOrEmpty(_mediaPath) &&
                    string.Equals(_mediaPath, earlyHintMedia, StringComparison.OrdinalIgnoreCase) &&
                    string.IsNullOrEmpty(_config.ExtraMpvArgs) &&
                    string.Equals((_config.MpvPath ?? "").Trim(), @"mpv\mpv.exe", StringComparison.OrdinalIgnoreCase);
                bool mainStartedEarly = false;
                if (earlyVideoStarted && !earlyMainMatches)
                {
                    Log.Info("startup: early mpv hint mismatch, using normal startup");
                    try { _player.Stop(); } catch { /* best-effort */ }
                    _adoptedEarlyMpvPid = 0;
                }
                else
                {
                    mainStartedEarly = _player.IsRunning;
                    if (mainStartedEarly) Log.Info("startup: reusing early-started mpv");
                }
                DeleteEarlyMpvHandoff();
                try { orphanScanDone.WaitOne(30000); } catch { /* best-effort */ }
                if (!string.IsNullOrEmpty(_mediaPath) && !mainStartedEarly)
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

            SaveStartupHint(false, _mediaPath ?? "");
            SetStartupPriority(false);
            Log.Info("startup priority restored to Normal");
            StartWatcher(folder);
            StartImageWatcher(imageFolder);
            Ui.Start(_appRoot);
            if (earlyStartDone != null) try { earlyStartDone.Dispose(); } catch { }
            try { orphanScanDone.Dispose(); } catch { }
            return WatchLoop();
        }

        // Runs the early startup at High so the app outranks the other logon apps
        // for CPU and reaches the desktop sooner, then drops back to Normal so it
        // never competes with the user's foreground apps afterwards. High needs
        // no privilege (only Realtime does, and Windows caps that at High anyway).
        private static void SetStartupPriority(bool boosted)
        {
            try
            {
                using (Process self = Process.GetCurrentProcess())
                {
                    self.PriorityClass = boosted ? ProcessPriorityClass.High : ProcessPriorityClass.Normal;
                }
            }
            catch { /* best-effort: priority is a hint, not required */ }
        }
    }
}
