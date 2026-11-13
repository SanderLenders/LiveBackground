using System;
using System.Diagnostics;
using System.IO;

namespace LiveBackground
{
    // Owns the single mpv process that paints the video on the desktop.
    public class MpvPlayer
    {
        private readonly string _appRoot;
        private readonly AppConfig _config;
        private readonly string _logName;
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

        public Process Process { get { return _process; } }
        public IntPtr Window { get { return _window; } set { _window = value; } }
        public bool IsRunning { get { return _process != null && !_process.HasExited; } }
        public DateTime StartedUtc { get { return _lastStart; } }

        public string MpvExe
        {
            get { return Paths.Tool(_config.MpvPath); }
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
            try { Directory.CreateDirectory(logDir); } catch { }

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
            catch { }

            string args = string.Format(
                "--wid={0} --loop-file=inf --no-config --load-scripts=no --media-controls=no --no-audio " +
                "--no-osc --no-osd-bar --no-input-default-bindings --no-border --hwdec=auto --panscan=1.0 " +
                "--force-window=yes --terminal=no --no-resume-playback --sub-auto=no --audio-file-auto=no " +
                "--stop-screensaver=" + (_config.ScreensaverActive ? "no" : "yes") +
                " --cache=no --priority=abovenormal " +
                "--demuxer-lavf-analyzeduration=0 --demuxer-lavf-probesize=32768 " +
                "--log-file={1} \"{2}\"{3}",
                progman.ToInt64(),
                Util.Quote(LogPath),
                mediaPath,
                string.IsNullOrEmpty(_config.ExtraMpvArgs) ? "" : " " + _config.ExtraMpvArgs);

            Log.Info("starting mpv: " + args);
            _process = ProcessRunner.StartDetached(MpvExe, args, Path.GetDirectoryName(MpvExe));
            _window = IntPtr.Zero;
            _lastStart = DateTime.UtcNow;
            return _process != null;
        }

        public void Stop()
        {
            if (_process == null) return;
            try
            {
                if (!_process.HasExited)
                {
                    Log.Info("stopping mpv, pid " + _process.Id);
                    _process.Kill();
                    _process.WaitForExit(5000);
                }
            }
            catch (Exception ex) { Log.Warn("could not stop mpv: " + ex.Message); }
            finally
            {
                try { _process.Dispose(); } catch { }
                _process = null;
                _window = IntPtr.Zero;
            }
        }

        public bool Restart(IntPtr progman, string mediaPath)
        {
            return Start(progman, mediaPath);
        }

        public bool StartedRecently(int seconds)
        {
            return (DateTime.UtcNow - _lastStart).TotalSeconds < seconds;
        }
    }
}
