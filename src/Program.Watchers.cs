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
        // Folder watchers
        //
        // The video folder and the image folder each used to carry their own
        // hand-written FileSystemWatcher block; FolderWatcher now owns the
        // watcher itself. These wrappers keep the app's state changes and the
        // log wording exactly as they were, and remember the watched folder so
        // the watch loop can tell when the settings point somewhere new.
        // ---------------------------------------------------------------

        private static void StartWatcher(string folder)
        {
            _videoWatcher.Start(folder);
            Log.Info("watching " + folder);
        }

        private static void StartImageWatcher(string folder)
        {
            _imageWatcher.Start(folder);
            Log.Info("watching images in " + folder);
        }

        // A video appeared, changed, vanished or moved: rebuild after a short
        // settling delay, and give the prefetch a fresh 30-second window.
        private static void OnVideoFolderChanged()
        {
            _dirty = true;
            _lastWatcherEvent = DateTime.UtcNow;
            _prefetchDone = false;
            _prefetchAfter = DateTime.UtcNow.AddSeconds(30);
        }

        // A watcher error is treated as a change, so the folder is re-scanned.
        private static void OnVideoFolderError()
        {
            _dirty = true;
            _lastWatcherEvent = DateTime.UtcNow;
        }

        // A picture appeared, changed, vanished or moved: re-read the list on
        // the next slideshow tick. A watcher error does the same.
        private static void OnImageFolderChanged()
        {
            lock (ImageSync)
            {
                _imageListDirty = true;
                _nextImageAt = DateTime.UtcNow;
            }
        }

        // The folder the video watcher is on, or "" when there is none.
        private static string WatchedVideoFolder { get { return _videoWatcher.Folder; } }

        // The folder the image watcher is on, or "" when there is none.
        private static string WatchedImageFolder { get { return _imageWatcher.Folder; } }

        // True while the video folder is actually being watched.
        private static bool VideoWatcherActive { get { return _videoWatcher.IsActive; } }
    }
}
