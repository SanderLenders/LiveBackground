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
        // Displays and the extra screens
        // ---------------------------------------------------------------

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
            Log.Info("display layout changed: " + ScreenText.DescribeAll(now, Screens.IsDuplicate(now)));
        }

        // Creates, keeps or retires one player per extra screen (at most three),
        // so the player list always matches the connected monitors. Players are
        // matched by device name, so a re-ordered monitor keeps its settings.
        private static void RebuildScreenPlayers()
        {
            const int maxExtraScreens = 3;
            bool duplicate = _screens != null && Screens.IsDuplicate(_screens);
            List<ScreenInfo> extras = new List<ScreenInfo>();
            if (!duplicate && _screens != null)
            {
                foreach (ScreenInfo screen in _screens)
                {
                    if (screen.Primary) continue;
                    if (extras.Count >= maxExtraScreens) break;
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
                    player.Player = new MpvPlayer(_appRoot, _config, ScreenText.LogName(player.Device));
                    next.Add(player);
                }
                player.Number = number;
                player.ScreenIndex = screen.Index;
                RefreshScreenMediaPath(player);
                if (ScreenUsesImages(player))
                {
                    if (player.Player != null && player.Player.IsRunning) player.Player.Stop();
                    player.ForceBuild = false;
                }
                else if (string.IsNullOrEmpty(player.MediaPath) &&
                    !_config.ScreenFor(player.Device).SameAsMain && !IsImagesMode)
                {
                    // Global Images never builds a new loop. A screen flipped
                    // to video there can only use a loop that is already
                    // cached.
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
            if (ScreenUsesImages(screen))
            {
                screen.MediaPath = "";
                screen.FramePath = "";
                return;
            }
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

        // Bring every extra screen in line with the current pill/checkbox
        // combination. RebuildScreenPlayers stops images screens and points
        // video screens at the right loop; this also starts a video screen
        // that should be playing, including one flipped while the main pill
        // says Images.
        private static void SyncScreenPlayersToMode(bool start)
        {
            RebuildScreenPlayers();
            foreach (ScreenPlayer screen in _screenPlayers)
            {
                if (screen == null || screen.Player == null) continue;
                if (!ScreenUsesVideo(screen))
                {
                    if (screen.Player.IsRunning) screen.Player.Stop();
                    continue;
                }
                RefreshScreenMediaPath(screen);
                if (!start || _paused || _progman == IntPtr.Zero ||
                    string.IsNullOrEmpty(screen.MediaPath) || !File.Exists(screen.MediaPath))
                {
                    continue;
                }
                if (screen.Player.IsRunning) screen.Player.Stop();
                screen.Player.Start(_progman, screen.MediaPath);
            }
        }

        // The per-screen half of the watchdog, shared by the video and images
        // branches. Images screens stop their player; video screens are placed
        // on their monitor and get the same bounded restart attempts as before.
        private static void WatchScreenPlayers()
        {
            foreach (ScreenPlayer screen in _screenPlayers)
            {
                if (screen == null || screen.Player == null) continue;
                if (ScreenUsesImages(screen))
                {
                    if (screen.Player.IsRunning) screen.Player.Stop();
                    continue;
                }
                if (screen.Player.IsRunning)
                {
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
        }

        // "second screen" / "third screen" for the log and the progress card,
        // matching the names the settings window uses.
        private static string ScreenLabel(ScreenPlayer screen)
        {
            if (screen == null) return "screen";
            return ScreenText.LabelForIndex(screen.Number - 1);
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
                : PlaylistState.HasTicked(config.Playlist);
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

    }
}
