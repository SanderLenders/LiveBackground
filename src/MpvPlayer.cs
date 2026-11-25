using System;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Text;

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

            // Read the settings once from a single snapshot, so a settings apply
            // during start-up cannot mix two config objects into one command.
            AppConfig config = _config;
            string extra = config.ExtraMpvArgs ?? "";
            bool verboseMpvLog = extra.IndexOf("--log-file", StringComparison.OrdinalIgnoreCase) >= 0;

            // mpv's debug log is useful for diagnostics but expensive on cold start.
            // Default off for speed; users can add --log-file in ExtraMpvArgs.
            if (verboseMpvLog)
            {
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
            }

            string shaderCacheDir = Path.Combine(_appRoot, "cache", "mpv-shaders");
            try { Directory.CreateDirectory(shaderCacheDir); } catch { /* best-effort: shader cache is an optimization */ }

            string args = string.Format(CultureInfo.InvariantCulture,
                "--wid={0} --loop-file=inf --no-config --load-scripts=no --media-controls=no --no-audio " +
                "--no-osc --no-osd-bar --no-input-default-bindings --no-border --gpu-api=d3d11 --hwdec=auto --panscan=1.0 " +
                "--force-window=yes --terminal=no --no-resume-playback --sub-auto=no --audio-file-auto=no " +
                // Inhibit the OS screensaver while the app manages idle actions
                // (hide chrome or lock) on its own timer.
                "--stop-screensaver=yes" +
                // mpv stays at Normal priority. The main app alone is boosted
                // to High during the logon race; an AboveNormal video process
                // would keep competing with the user's apps for the whole loop.
                " --cache=no " +
                "--demuxer-lavf-analyzeduration=0 --demuxer-lavf-probesize=32768 " +
                "--gpu-shader-cache=yes --gpu-shader-cache-dir={1}" +
                "{2} {3}{4}",
                progman.ToInt64(),
                Util.Quote(shaderCacheDir),
                verboseMpvLog ? " --log-file=" + Util.Quote(LogPath) : "",
                Util.Quote(mediaPath),
                string.IsNullOrEmpty(extra) ? "" : " " + extra);

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

        // Takes over an mpv the native bootstrapper started before the managed
        // app was up. Validate the PID carefully: it must still be our mpv exe,
        // and it must be recent. A stale/reused PID is never killed.
        public bool Adopt(int pid, string mediaPath)
        {
            if (pid <= 0 || string.IsNullOrEmpty(mediaPath) || !File.Exists(mediaPath)) return false;
            string expected = Paths.Tool(AppConfigDefaults.DefaultMpvPath);
            Process process;
            try { process = Process.GetProcessById(pid); }
            catch { return false; }

            bool looksLikeMpv = false;
            try
            {
                DateTime startedUtc = process.StartTime.ToUniversalTime();
                string actual = "";
                try { actual = process.MainModule.FileName; }
                catch
                {
                    IntPtr handle = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
                    if (handle != IntPtr.Zero)
                    {
                        try
                        {
                            StringBuilder text = new StringBuilder(1024);
                            uint length = (uint)text.Capacity;
                            if (Native.QueryFullProcessImageName(handle, 0, text, ref length)) actual = text.ToString();
                        }
                        finally { Native.CloseHandle(handle); }
                    }
                }
                looksLikeMpv = string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
                if (!looksLikeMpv) { process.Dispose(); return false; }
                if ((DateTime.UtcNow - startedUtc).TotalMinutes > 5)
                {
                    try { if (!process.HasExited) process.Kill(); } catch { /* best-effort */ }
                    process.Dispose();
                    return false;
                }
                lock (_sync)
                {
                    if (_process != null) { process.Dispose(); return false; }
                    _process = process;
                    _window = IntPtr.Zero;
                    _lastStart = startedUtc;
                }
                return true;
            }
            catch
            {
                process.Dispose();
                return false;
            }
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
