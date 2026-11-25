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
        // ---------------------------------------------------------------
        // Image wallpaper rotation
        // ---------------------------------------------------------------

        private static void UpdateImageSlideshow()
        {
            if (_paused) return;

            // One owner at a time for the main-screen image state: the apply
            // thread also drives transitions (see ApplyConfigCore) and the tray
            // calls RequestNextImage.
            lock (ImageSync)
            {
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
                _nextImageAt = DateTime.UtcNow.AddSeconds(SlideshowTiming.ImageIntervalSeconds(_config));
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
                ImageShowMainAndMirrors(chosen, advance ? SlideshowTiming.ImageCrossfadeSeconds(_config) : 0, !advance);
                _surfaceImage = chosen;
            }

            _currentImage = chosen;
            _nextImageAt = DateTime.UtcNow.AddSeconds(SlideshowTiming.ImageIntervalSeconds(_config));
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

        // Advances each extra screen that currently wants pictures. In the
        // normal Images layout a screen that mirrors the main slideshow is
        // still painted by ApplyImage; a screen with its own image settings has
        // its own state. With a per-screen override in Video mode, even a
        // mirror needs its own state because the main image tick is not running.
        private static void UpdateExtraImageSlideshow()
        {
            // Save/apply runs on the settings UI thread while the watchdog is
            // also advancing pictures. Serialize the state transition so one
            // screen never gets two folder watchers at the same time.
            lock (ImageSync)
            {
                SyncScreenImageStates();
                bool globalImages = IsImagesMode;
                foreach (ScreenImageState state in _screenImages)
                {
                    bool effectiveImages = ScreenUsesImages(state.Device);
                    bool wantsImage = effectiveImages || (globalImages && !ScreenVideoIsLive(state.Device));
                    if (!wantsImage)
                    {
                        StopScreenImageWatcher(state);
                        bool owned = state.Active || !string.IsNullOrEmpty(state.SurfaceImage);
                        state.Active = false;
                        state.CurrentImage = "";
                        state.SurfaceImage = "";
                        // Only hide a layer this state painted. A customized
                        // Video-mode fallback owns its own layer and must stay
                        // visible while the video is paused or rebuilding.
                        if (owned && state.ScreenIndex >= 0 && state.ScreenIndex < _imageSurfaces.Count &&
                            _imageSurfaces[state.ScreenIndex].IsStarted)
                        {
                            _imageSurfaces[state.ScreenIndex].Hide();
                        }
                        continue;
                    }

                    ScreenSettings settings = _config.ScreenFor(state.Device);
                    if (settings.ImagesSameAsMain && globalImages)
                    {
                        // ApplyImage owns this layer while the main pill is
                        // Images, including a screen flipped to video whose
                        // cached loop is not playing yet.
                        StopScreenImageWatcher(state);
                        state.Active = false;
                        state.CurrentImage = "";
                        state.SurfaceImage = "";
                        continue;
                    }

                    // A mirror in Video mode uses the main image settings; an
                    // independent screen keeps the settings saved in its tab.
                    AppConfig own = settings.ImagesSameAsMain ? _config : _config.ScreenConfig(settings);
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
                string message = "no images for " + ScreenText.LabelForIndex(state.ScreenIndex) + " in " + folder;
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
                Log.Info(ScreenText.LabelForIndex(state.ScreenIndex) + " image: " + Path.GetFileName(chosen));
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
                const int maxExtraScreens = 3;
                foreach (ScreenInfo screen in _screens)
                {
                    if (screen.Primary) continue;
                    if (extras.Count >= maxExtraScreens) break;
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

        // A layer is owned by something other than the main slideshow when a
        // video is actually playing on it, or when that screen has its own
        // picture settings. A flipped screen waiting for a cached loop is not
        // owned, so the main picture keeps covering it until video appears.
        private static bool IsIndependentImageIndex(int index)
        {
            if (_config == null) return false;
            lock (ImageSync)
            {
                foreach (ScreenImageState state in _screenImages)
                {
                    if (state.ScreenIndex != index) continue;
                    return ScreenVideoIsLive(state.Device) ||
                        !_config.ScreenFor(state.Device).ImagesSameAsMain;
                }
                return false;
            }
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
                Log.Info("watching " + ScreenText.LabelForIndex(state.ScreenIndex) + " images in " + folder);
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
        // was explicitly customized needs an app-owned image layer. A screen
        // flipped to Images also owns its layer, even in a Video desktop.
        private static void UpdateFallbackSurfaces()
        {
            if (IsImagesMode || _config == null || _screens == null) return;
            lock (ImageSync)
            {
                bool any = false;
                foreach (ScreenPlayer screen in _screenPlayers)
                {
                    if (ScreenUsesImages(screen) || NeedsFallbackSurface(screen))
                    {
                        any = true;
                        break;
                    }
                }
                if (!any)
                {
                    ResetExtraImageStates();
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

                // The primary and mirroring video screens use the native
                // wallpaper. Image screens and customized fallbacks are ours.
                for (int i = 0; i < _imageSurfaces.Count; i++)
                {
                    bool managed = false;
                    foreach (ScreenPlayer screen in _screenPlayers)
                    {
                        if (screen.ScreenIndex == i &&
                            (ScreenUsesImages(screen) || NeedsFallbackSurface(screen)))
                        {
                            managed = true;
                            break;
                        }
                    }
                    if (!managed && _imageSurfaces[i].IsStarted) _imageSurfaces[i].Hide();
                }

                foreach (ScreenPlayer screen in _screenPlayers)
                {
                    if (ScreenUsesImages(screen))
                    {
                        // The slideshow state owns this layer; drop any stale
                        // fallback flag from before the checkbox was ticked.
                        HideFallbackSurface(screen);
                        continue;
                    }
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

                // Image screens advanced here even though the main image tick
                // belongs to the Images pill and is not running. The owned gate
                // above leaves customized fallback layers alone.
                UpdateExtraImageSlideshow();
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
                for (int i = 0; i < _imageSurfaces.Count; i++)
                {
                    if (IsIndependentImageIndex(i)) continue;
                    if (_imageSurfaces[i].IsStarted) _imageSurfaces[i].EnsureVisible();
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

    }
}
