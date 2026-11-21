using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace LiveBackground
{
    public static partial class Program
    {

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
                            lock (ImageSync)
                            {
                                ShutdownImageSurfaces();
                                StartImageSurfaces(_imageProgman);
                                _surfaceImage = "";
                                if (!string.IsNullOrEmpty(_currentImage)) ApplyImage(false);
                            }
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
                            // The loop works now, so a later failure starts
                            // counting from zero again.
                            ResetBuildFailureBackoff();
                            ResetRestartBackoff();
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
                            _buildFailures++;
                            Log.Warn("rebuild failed: " + finished.Message);
                            Ui.BuildProgressFinish(false, "Build failed - see logs\\app.log");
                            if (_buildFailures >= MaxBuildFailures)
                            {
                                // Stop hammering ffmpeg. "Rebuild now" from the
                                // tray bypasses this wait, so the user can
                                // always retry by hand.
                                if (!_buildFailureStopped)
                                {
                                    _buildFailureStopped = true;
                                    Log.Warn("giving up after " + _buildFailures + " failed builds");
                                    Ui.Notify("LiveBackground",
                                        "The wallpaper video could not be built. See logs\\app.log, then press Rebuild now.");
                                }
                                _nextCheck = DateTime.UtcNow.AddMinutes(10);
                            }
                            else
                            {
                                // Back off a little more with each failure.
                                _nextCheck = DateTime.UtcNow.AddSeconds(60 * _buildFailures);
                            }
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
                            screen.Failures = 0;
                            screen.FailureNotified = false;
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
                            screen.Failures++;
                            Log.Warn(ScreenLabel(screen) + " rebuild failed: " + finished2.Message);
                            if (screen.Failures >= MaxBuildFailures)
                            {
                                if (!screen.FailureNotified)
                                {
                                    screen.FailureNotified = true;
                                    Ui.Notify("LiveBackground", ScreenLabel(screen) +
                                        ": the wallpaper video could not be built. See logs\\app.log.");
                                }
                                _nextCheck = DateTime.UtcNow.AddMinutes(10);
                            }
                            else
                            {
                                _nextCheck = DateTime.UtcNow.AddSeconds(60 * screen.Failures);
                            }
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
                            // A deliberate restart is not a crash, so it gets a
                            // fresh set of attempts.
                            ResetRestartBackoff();
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
                    if (!string.Equals(activeFolder, WatchedVideoFolder, StringComparison.OrdinalIgnoreCase))
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
                        else if (!VideoWatcherActive || DateTime.UtcNow >= _nextSafetyScan)
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
                        // A player that has stayed up for a while is healthy; a
                        // later failure may try again from a clean slate.
                        if (_playerRestarts > 0 &&
                            (DateTime.UtcNow - _player.StartedUtc).TotalSeconds > 20)
                        {
                            ResetRestartBackoff();
                        }
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
                        else if (_resumeFromPause)
                        {
                            // A player stopped by the pause command is not a
                            // crash; resuming is the expected path and gets a
                            // clean slate.
                            _resumeFromPause = false;
                            ResetRestartBackoff();
                            Log.Info("resuming the wallpaper video");
                            _player.Start(_progman, _mediaPath);
                            firstPlacementLogged = false;
                        }
                        else if (_playerRestartStopped)
                        {
                            // Already gave up on this loop; a settings change,
                            // a resume or a successful rebuild clears the flag.
                        }
                        else
                        {
                            // Count restarts that happen close together. A loop
                            // that kills mpv on every start would otherwise be
                            // retried forever.
                            DateTime now = DateTime.UtcNow;
                            if (now - _playerRestartWindow > TimeSpan.FromMinutes(2)) _playerRestarts = 0;
                            _playerRestartWindow = now;
                            _playerRestarts++;
                            if (_playerRestarts > MaxPlayerRestarts)
                            {
                                _playerRestartStopped = true;
                                Log.Warn("mpv keeps exiting; giving up after " + _playerRestarts + " restarts");
                                Ui.Notify("LiveBackground",
                                    "The wallpaper video could not play. Pick another clip or check logs\\app.log.");
                            }
                            else
                            {
                                Log.Warn("mpv is not running, restarting");
                                _player.Start(_progman, _mediaPath);
                                firstPlacementLogged = false;
                            }
                        }
                    }

                    // Each extra screen has its own player and watchdog: place
                    // it on its monitor and bring it back if it exited.
                    foreach (ScreenPlayer screen in _screenPlayers)
                    {
                        if (screen.Player == null) continue;
                        if (screen.Player.IsRunning)
                        {
                            // A screen player that has stayed up for a while is
                            // healthy; a later failure may try again.
                            if (screen.Restarts > 0 &&
                                (DateTime.UtcNow - screen.Player.StartedUtc).TotalSeconds > 20)
                            {
                                screen.Restarts = 0;
                                screen.RestartWindow = DateTime.MinValue;
                                screen.RestartStopped = false;
                            }
                            PlacePlayerOnScreen(screen.Player, screen.ScreenIndex);
                        }
                        else if (!_building && !_paused && _progman != IntPtr.Zero &&
                            !string.IsNullOrEmpty(screen.MediaPath) && File.Exists(screen.MediaPath))
                        {
                            if (screen.Player.StartedRecently(5))
                            {
                                // Give a just-started player a moment to appear.
                            }
                            else if (screen.RestartStopped)
                            {
                                // Already gave up on this screen's loop.
                            }
                            else
                            {
                                DateTime now = DateTime.UtcNow;
                                if (now - screen.RestartWindow > TimeSpan.FromMinutes(2)) screen.Restarts = 0;
                                screen.RestartWindow = now;
                                screen.Restarts++;
                                if (screen.Restarts > MaxPlayerRestarts)
                                {
                                    screen.RestartStopped = true;
                                    Log.Warn("the " + ScreenLabel(screen) + " player keeps exiting; giving up");
                                    Ui.Notify("LiveBackground", ScreenLabel(screen) +
                                        ": the wallpaper video could not play. Pick another clip or check logs\\app.log.");
                                }
                                else
                                {
                                    Log.Warn("the " + ScreenLabel(screen) + " player is not running, restarting");
                                    screen.Player.Start(_progman, screen.MediaPath);
                                }
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

        // Runs the selected screensaver action after the configured idle delay
        // (shared by both options): hide taskbar/desktop icons and open windows so
        // only the video wallpaper is visible, or dim and lock the workstation.
        // Restores on input. Runs on its own thread so the wallpaper watchdog is
        // never held up by window calls.
        private static void StartScreensaverWorker()
        {
            Thread thread = new Thread(delegate()
            {
                // Let mpv and the first frame win the disk race. The shell is
                // not needed until the user has been away for minutes anyway.
                Thread.Sleep(4000);
                bool hidden = false;
                bool windowsMinimizedByUs = false;
                bool lockFired = false;
                while (true)
                {
                    AppConfig config = _config;
                    try
                    {
                        bool hideMode = config != null && config.ScreensaverVideo;
                        bool dimLockMode = config != null && config.ScreensaverActive && !config.ScreensaverVideo;
                        int timeout = ScreensaverIdle.NormalizeTimeout(config != null ? config.ScreensaverHideSeconds : 0);
                        bool running = WallpaperRunning();
                        long idle = IdleMilliseconds();
                        bool due = ScreensaverIdle.IsDue(_paused, running, idle, timeout);

                        if (hideMode && due)
                        {
                            // Re-apply chrome hide every tick: an Explorer restart
                            // would otherwise bring a fresh taskbar back unnoticed.
                            ShellChrome.Hide();
                            if (!windowsMinimizedByUs)
                            {
                                MinimizeAllWindows();
                                windowsMinimizedByUs = true;
                            }
                            hidden = true;
                            lockFired = false;
                        }
                        else
                        {
                            if (hidden)
                            {
                                ShellChrome.Show();
                                hidden = false;
                            }
                            if (windowsMinimizedByUs)
                            {
                                UndoMinimizeAllWindows();
                                windowsMinimizedByUs = false;
                            }
                            if (dimLockMode && due)
                            {
                                if (!lockFired)
                                {
                                    lockFired = true;
                                    try { Native.LockWorkStation(); }
                                    catch (Exception ex) { Log.Warn("could not lock workstation: " + ex.Message); }
                                }
                            }
                            else
                            {
                                lockFired = false;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("screensaver worker failed: " + ex.Message);
                    }
                    Thread.Sleep(1000);
                }
            });
            thread.IsBackground = true;
            thread.Name = "livebackground-screensaver";
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        private static void MinimizeAllWindows()
        {
            try
            {
                Type shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null) return;
                object shell = Activator.CreateInstance(shellType);
                try
                {
                    shellType.InvokeMember("MinimizeAll", BindingFlags.InvokeMethod, null, shell, null, CultureInfo.InvariantCulture);
                }
                finally
                {
                    try { Marshal.FinalReleaseComObject(shell); } catch { }
                }
            }
            catch (Exception ex)
            {
                try { Log.Warn("could not minimize windows: " + ex.Message); } catch { }
            }
        }

        private static void UndoMinimizeAllWindows()
        {
            try
            {
                Type shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null) return;
                object shell = Activator.CreateInstance(shellType);
                try
                {
                    shellType.InvokeMember("UndoMinimizeALL", BindingFlags.InvokeMethod, null, shell, null, CultureInfo.InvariantCulture);
                }
                finally
                {
                    try { Marshal.FinalReleaseComObject(shell); } catch { }
                }
            }
            catch (Exception ex)
            {
                try { Log.Warn("could not restore windows: " + ex.Message); } catch { }
            }
        }



        private static long IdleMilliseconds()
        {
            Native.LASTINPUTINFO info = new Native.LASTINPUTINFO();
            info.cbSize = (uint)Marshal.SizeOf<Native.LASTINPUTINFO>();
            if (!Native.GetLastInputInfo(ref info)) return 0;
            int elapsed = unchecked(Environment.TickCount - (int)info.dwTime);
            return elapsed < 0 ? 0 : elapsed;
        }

        private static bool WallpaperRunning()
        {
            if (_player != null && _player.IsRunning) return true;
            List<ScreenPlayer> players = _screenPlayers;
            if (players != null)
            {
                foreach (ScreenPlayer screen in players)
                {
                    if (screen != null && screen.Player != null && screen.Player.IsRunning) return true;
                }
            }
            lock (ImageSync)
            {
                return _imageSurfaces.Count > 0;
            }
        }

    }
}
