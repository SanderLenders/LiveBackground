using System;
using System.Globalization;
using System.Diagnostics;
using System.IO;

namespace LiveBackground
{
    // Owns the single mpv process that paints the video on the desktop.
    public class MpvPlayer
    {
        private readonly string _appRoot;
        // The live config is replaced (not mutated) whenever settings are
        // applied, so this is re-pointed by Reconfigure. A reference read is
        // atomic and the published object is never mutated again, so reading it
        // needs no lock.
        private volatile AppConfig _config;
        private readonly string _logName;
        // The watchdog thread and the settings UI thread both touch the player,
        // so every field below is read and written under this lock. Without it a
        // "is it running?" check can race a Stop that disposes the process.
        private readonly object _sync = new object();
        private Process _process;
        private IntPtr _window;
        private DateTime _lastStart = DateTime.MinValue;

        public MpvPlayer(string appRoot, AppConfig config)
            : this(appRoot, config, "mpv.log")
        {
        }

        // The second screen gets its own log so the two players never write to
        // the same file.
        public MpvPlayer(string appRoot, AppConfig config, string logName)
        {
            _appRoot = appRoot;
            _config = config;
            _logName = string.IsNullOrEmpty(logName) ? "mpv.log" : logName;
        }

        public Process Process { get { lock (_sync) { return _process; } } }
        public IntPtr Window { get { lock (_sync) { return _window; } } set { lock (_sync) { _window = value; } } }
        public bool IsRunning { get { lock (_sync) { return _process != null && !_process.HasExited; } } }
        public DateTime StartedUtc { get { lock (_sync) { return _lastStart; } } }

        public string MpvExe
        {
            get { return Paths.Tool(_config.MpvPath); }
        }

        // Called after a settings apply swaps in a new config object, so the
        // player keeps using the current settings.
        public void Reconfigure(AppConfig config)
        {
            if (config != null) _config = config;
        }

        public string LogPath
        {
            get { return Path.Combine(_appRoot, "logs", _logName); }
        }

        public bool Start(IntPtr progman, string mediaPath)
        {
            Stop();
            if (!File.Exists(MpvExe))
            {
                Log.Error("mpv not found: " + MpvExe);
                return false;
            }
            if (string.IsNullOrEmpty(mediaPath) || !File.Exists(mediaPath))
            {
                Log.Error("media not found: " + mediaPath);
                return false;
            }

            string logDir = Path.GetDirectoryName(LogPath);
            try { Directory.CreateDirectory(logDir); } catch { /* best-effort: the log directory is optional */ }

            // mpv writes a verbose log and only truncates it when it starts.
            // Keep one previous run instead of letting the file grow forever;
            // mpv's log file is always written at debug level, so rotation is
            // the only bound we get.
            try
            {
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 4 * 1024 * 1024)
                {
                    string old = Path.Combine(logDir, _logName.Replace(".log", ".old.log"));
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(LogPath, old);
                }
            }
            catch { /* best-effort: a log that cannot be rotated keeps appending */ }

            // Read the settings once from a single snapshot, so a settings apply
            // during start-up cannot mix two config objects into one command.
            AppConfig config = _config;
            string args = string.Format(CultureInfo.InvariantCulture,
                "--wid={0} --loop-file=inf --no-config --load-scripts=no --media-controls=no --no-audio " +
                "--no-osc --no-osd-bar --no-input-default-bindings --no-border --hwdec=auto --panscan=1.0 " +
                "--force-window=yes --terminal=no --no-resume-playback --sub-auto=no --audio-file-auto=no " +
                // Inhibit the OS screensaver while the app manages idle actions
                // (hide chrome or lock) on its own timer.
                "--stop-screensaver=yes" +
                " --cache=no --priority=abovenormal " +
                "--demuxer-lavf-analyzeduration=0 --demuxer-lavf-probesize=32768 " +
                "--log-file={1} {2}{3}",
                progman.ToInt64(),
                Util.Quote(LogPath),
                Util.Quote(mediaPath),
                string.IsNullOrEmpty(config.ExtraMpvArgs) ? "" : " " + config.ExtraMpvArgs);

            Log.Info("starting mpv: " + args);
            Process started = ProcessRunner.StartDetached(MpvExe, args, Path.GetDirectoryName(MpvExe));
            lock (_sync)
            {
                _process = started;
                _window = IntPtr.Zero;
                _lastStart = DateTime.UtcNow;
            }
            return started != null;
        }

        public void Stop()
        {
            Process process;
            lock (_sync)
            {
                process = _process;
                _process = null;
                _window = IntPtr.Zero;
            }
            if (process == null) return;
            try
            {
                if (!process.HasExited)
                {
                    Log.Info("stopping mpv, pid " + process.Id);
                    process.Kill();
                    process.WaitForExit(5000);
                }
            }
            catch (Exception ex) { Log.Warn("could not stop mpv: " + ex.Message); }
            finally
            {
                try { process.Dispose(); } catch { /* best-effort dispose; the process is already stopped */ }
            }
        }

        public bool StartedRecently(int seconds)
        {
            lock (_sync) { return (DateTime.UtcNow - _lastStart).TotalSeconds < seconds; }
        }
    }
}
