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
                    lock (ImageSync)
                    {
                        _imageListDirty = true;
                        _nextImageAt = DateTime.UtcNow;
                    }
                    Log.Info("image folder changed: " + e.ChangeType + " " + Path.GetFileName(e.FullPath));
                };
                RenamedEventHandler renamed = delegate(object sender, RenamedEventArgs e)
                {
                    if (!MediaPipeline.IsImage(e.FullPath) && !MediaPipeline.IsImage(e.OldFullPath)) return;
                    lock (ImageSync)
                    {
                        _imageListDirty = true;
                        _nextImageAt = DateTime.UtcNow;
                    }
                    Log.Info("image folder renamed: " + Path.GetFileName(e.OldFullPath) + " -> " + Path.GetFileName(e.FullPath));
                };

                watcher.Created += changed;
                watcher.Changed += changed;
                watcher.Deleted += changed;
                watcher.Renamed += renamed;
                watcher.Error += delegate(object sender, ErrorEventArgs e)
                {
                    lock (ImageSync)
                    {
                        _imageListDirty = true;
                        _nextImageAt = DateTime.UtcNow;
                    }
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
