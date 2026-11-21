using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Text;

namespace LiveBackground
{
    public static class Log
    {
        private static readonly object Sync = new object();
        private static string _logPath;
        private static string _logDir;
        private static StreamWriter _writer;
        private static long _written;
        private static int _infoSinceFlush;
        private static DateTime _lastFlushUtc = DateTime.MinValue;

        public static void Init(string appRoot)
        {
            Init(appRoot, "app.log");
        }

        public static void Init(string appRoot, string fileName)
        {
            lock (Sync)
            {
                CloseWriter();
                _logDir = Path.Combine(appRoot, "logs");
                _logPath = Path.Combine(_logDir, fileName);
                _written = 0;
                try
                {
                    Directory.CreateDirectory(_logDir);
                    if (File.Exists(_logPath) && new FileInfo(_logPath).Length > 1024 * 1024)
                    {
                        Rotate();
                    }
                }
                catch { /* best-effort: logging must never fail startup */ }
                OpenWriter();
            }
        }

        public static void Info(string message) { Write("INFO", message); }
        public static void Warn(string message) { Write("WARN", message); }
        public static void Error(string message) { Write("ERROR", message); }

        public static void Error(string message, Exception ex)
        {
            if (ex == null) { Error(message); return; }
            Write("ERROR", message + " :: " + ex.GetType().Name + ": " + ex.Message);
            Write("ERROR", ex.StackTrace);
        }

        [SuppressMessage("Maintainability", "CA1508:Avoid dead conditional code",
            Justification = "OpenWriter can leave _writer null when the log file cannot be opened.")]
        private static void Write(string level, string message)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + " [" + level + "] " + message + Environment.NewLine;
            lock (Sync)
            {
                try
                {
                    if (_writer == null) OpenWriter();
                    if (_writer == null) return;

                    // Keep the file bounded without opening and closing it for
                    // every line. On a cold logon those opens are expensive.
                    if (_written > 1024 * 1024)
                    {
                        CloseWriter();
                        Rotate();
                        OpenWriter();
                        if (_writer == null) return;
                    }

                    // Helper invocations (--settings, --stop, the watchdog)
                    // open the same log from another process. Each process
                    // keeps its own file position, so without this seek a
                    // longer-lived writer would overwrite a newer process's
                    // lines instead of appending after them.
                    try { _writer.BaseStream.Seek(0, SeekOrigin.End); } catch { /* best-effort: if the seek fails the writer still appends */ }

                    _writer.Write(line);
                    _written += line.Length;
                    bool flushNow = level != "INFO" || _infoSinceFlush >= 25 ||
                        (DateTime.UtcNow - _lastFlushUtc).TotalSeconds >= 5;
                    if (level == "INFO") _infoSinceFlush++;
                    if (flushNow)
                    {
                        _writer.Flush();
                        _infoSinceFlush = 0;
                        _lastFlushUtc = DateTime.UtcNow;
                    }
                }
                catch
                {
                    // Logging must never take the wallpaper down.
                    CloseWriter();
                }
            }
        }

        private static void OpenWriter()
        {
            if (string.IsNullOrEmpty(_logPath)) return;
            try
            {
                Directory.CreateDirectory(_logDir);
                FileStream stream = new FileStream(_logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                _writer = new StreamWriter(stream, new UTF8Encoding(false));
                _writer.AutoFlush = false;
                _written = stream.Length;
                _infoSinceFlush = 0;
                _lastFlushUtc = DateTime.UtcNow;
            }
            catch
            {
                CloseWriter();
            }
        }

        private static void CloseWriter()
        {
            if (_writer == null) return;
            try { _writer.Dispose(); } catch { /* best-effort dispose on shutdown */ }
            _writer = null;
        }

        private static void Rotate()
        {
            try
            {
                if (string.IsNullOrEmpty(_logPath) || !File.Exists(_logPath)) return;
                string name = Path.GetFileNameWithoutExtension(_logPath);
                string old = Path.Combine(_logDir, name + ".old.log");
                if (File.Exists(old)) File.Delete(old);
                File.Move(_logPath, old);
                _written = 0;
            }
            catch { /* best-effort: if the move fails the log keeps growing */ }
        }
    }
}
