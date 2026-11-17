using System;
using System.IO;

namespace LiveBackground
{
    // One FileSystemWatcher, wrapped: create the folder if needed, watch it, and
    // report the changes that matter through callbacks. The caller decides what
    // a change means for its own state, so this class holds none of the app's
    // globals.
    //
    // The interesting-file filter, the log wording and the error handling match
    // the two hand-written watchers this replaces (the video folder and the
    // image folder). A change is logged only when the file is interesting, so a
    // burst of unrelated writes stays quiet.
    public sealed class FolderWatcher : IDisposable
    {
        private readonly string _label;                 // "video folder" / "image folder"
        private readonly Func<string, bool> _isInteresting;
        private readonly Action _onChanged;
        private readonly Action _onError;
        private FileSystemWatcher _watcher;

        public FolderWatcher(string label, Func<string, bool> isInteresting,
            Action onChanged, Action onError)
        {
            _label = label;
            _isInteresting = isInteresting;
            _onChanged = onChanged;
            _onError = onError;
            Folder = "";
        }

        // The folder being watched, or "" when there is no watcher.
        public string Folder { get; private set; }

        // True while a real FileSystemWatcher is running.
        public bool IsActive { get { return _watcher != null; } }

        // Starts (or restarts) the watcher on "folder". Returns true when the
        // watcher is running. A folder that cannot be watched is still
        // remembered, so the caller does not retry it on every tick.
        public bool Start(string folder)
        {
            Stop();
            try
            {
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                FileSystemWatcher watcher = new FileSystemWatcher(folder);
                watcher.IncludeSubdirectories = false;
                watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size;
                watcher.EnableRaisingEvents = true;

                FileSystemEventHandler changed = delegate(object sender, FileSystemEventArgs e)
                {
                    if (!_isInteresting(e.FullPath)) return;
                    _onChanged();
                    Log.Info(_label + " changed: " + e.ChangeType + " " + Path.GetFileName(e.FullPath));
                };
                RenamedEventHandler renamed = delegate(object sender, RenamedEventArgs e)
                {
                    if (!_isInteresting(e.FullPath) && !_isInteresting(e.OldFullPath)) return;
                    _onChanged();
                    Log.Info(_label + " renamed: " + Path.GetFileName(e.OldFullPath) +
                        " -> " + Path.GetFileName(e.FullPath));
                };

                watcher.Created += changed;
                watcher.Changed += changed;
                watcher.Deleted += changed;
                watcher.Renamed += renamed;
                watcher.Error += delegate(object sender, ErrorEventArgs e)
                {
                    _onError();
                    Log.Warn(_label + " watcher error: " + e.GetException().Message);
                };
                _watcher = watcher;
                Folder = folder;
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("could not watch the " + _label + ": " + ex.Message);
                Folder = folder;
                return false;
            }
        }

        // Stops the watcher and forgets the folder.
        public void Stop()
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
            Folder = "";
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
