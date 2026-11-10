using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace LiveBackground
{
    public class ProcessResult
    {
        public int ExitCode;
        public string StandardOutput = "";
        public string StandardError = "";
        public bool TimedOut;
        public bool Cancelled;

        public string AllOutput
        {
            get { return StandardOutput + StandardError; }
        }
    }

    public static class ProcessRunner
    {
        public static ProcessResult Run(string exe, string arguments, int timeoutMs)
        {
            return Run(exe, arguments, null, timeoutMs);
        }

        public static ProcessResult Run(string exe, string arguments, string workingDirectory, int timeoutMs)
        {
            ProcessResult result = new ProcessResult();
            if (!File.Exists(exe))
            {
                result.ExitCode = -1;
                result.StandardError = "not found: " + exe;
                return result;
            }

            ProcessStartInfo psi = new ProcessStartInfo(exe, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            if (!string.IsNullOrEmpty(workingDirectory)) psi.WorkingDirectory = workingDirectory;

            StringBuilder stdout = new StringBuilder();
            StringBuilder stderr = new StringBuilder();
            using (Process process = new Process())
            {
                process.StartInfo = psi;
                process.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
                {
                    if (e.Data != null) stdout.AppendLine(e.Data);
                };
                process.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
                {
                    if (e.Data != null) stderr.AppendLine(e.Data);
                };

                try
                {
                    process.Start();
                }
                catch (Exception ex)
                {
                    result.ExitCode = -1;
                    result.StandardError = "could not start " + exe + ": " + ex.Message;
                    return result;
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                bool exited;
                if (timeoutMs > 0) exited = process.WaitForExit(timeoutMs);
                else { process.WaitForExit(); exited = true; }

                if (!exited)
                {
                    result.TimedOut = true;
                    try { process.Kill(); } catch { }
                    try { process.WaitForExit(3000); } catch { }
                }

                result.ExitCode = process.HasExited ? process.ExitCode : -1;
                result.StandardOutput = stdout.ToString();
                result.StandardError = stderr.ToString();
            }
            return result;
        }

        // Same as Run, but reports each output line as it arrives. ffmpeg writes its
        // progress to stderr, which lets the installer show a live percentage.
        public static ProcessResult RunWithLineCallback(string exe, string arguments, string workingDirectory,
            int timeoutMs, Action<string> onLine)
        {
            return RunWithLineCallback(exe, arguments, workingDirectory, timeoutMs, onLine, null);
        }

        // The cancelled callback is polled while the process runs. When it
        // returns true the process is stopped and the result is marked
        // Cancelled, so a superseded build does not have to finish first.
        public static ProcessResult RunWithLineCallback(string exe, string arguments, string workingDirectory,
            int timeoutMs, Action<string> onLine, Func<bool> cancelled)
        {
            return RunWithLineCallback(exe, arguments, workingDirectory, timeoutMs, onLine, cancelled,
                ProcessPriorityClass.Normal);
        }

        // The priority is used by the background prefetch so it never makes the
        // desktop feel sluggish while it prepares loops for other clips.
        public static ProcessResult RunWithLineCallback(string exe, string arguments, string workingDirectory,
            int timeoutMs, Action<string> onLine, Func<bool> cancelled, ProcessPriorityClass priority)
        {
            ProcessResult result = new ProcessResult();
            if (!File.Exists(exe))
            {
                result.ExitCode = -1;
                result.StandardError = "not found: " + exe;
                return result;
            }

            ProcessStartInfo psi = new ProcessStartInfo(exe, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            if (!string.IsNullOrEmpty(workingDirectory)) psi.WorkingDirectory = workingDirectory;

            StringBuilder stdout = new StringBuilder();
            StringBuilder stderr = new StringBuilder();
            using (Process process = new Process())
            {
                process.StartInfo = psi;
                process.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
                {
                    if (e.Data == null) return;
                    stdout.AppendLine(e.Data);
                    if (onLine != null) { try { onLine(e.Data); } catch { } }
                };
                process.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
                {
                    if (e.Data == null) return;
                    stderr.AppendLine(e.Data);
                    if (onLine != null) { try { onLine(e.Data); } catch { } }
                };

                try { process.Start(); }
                catch (Exception ex)
                {
                    result.ExitCode = -1;
                    result.StandardError = "could not start " + exe + ": " + ex.Message;
                    return result;
                }

                if (priority != ProcessPriorityClass.Normal)
                {
                    try { process.PriorityClass = priority; } catch { }
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                // Wait in short slices so a superseded build can be stopped
                // without waiting for the encode to finish.
                DateTime deadline = timeoutMs > 0
                    ? DateTime.UtcNow.AddMilliseconds(timeoutMs)
                    : DateTime.MaxValue;
                bool exited = false;
                while (!exited)
                {
                    if (cancelled != null && cancelled())
                    {
                        result.Cancelled = true;
                        try { process.Kill(); } catch { }
                        break;
                    }

                    int slice = 250;
                    if (timeoutMs > 0)
                    {
                        int remaining = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                        if (remaining <= 0)
                        {
                            result.TimedOut = true;
                            try { process.Kill(); } catch { }
                            break;
                        }
                        if (remaining < slice) slice = remaining;
                    }
                    exited = process.WaitForExit(slice);
                }

                if (result.Cancelled || result.TimedOut)
                {
                    try { process.WaitForExit(3000); } catch { }
                }
                else
                {
                    // Flush the asynchronous output readers before reading them.
                    try { process.WaitForExit(); } catch { }
                }

                result.ExitCode = process.HasExited ? process.ExitCode : -1;
                result.StandardOutput = stdout.ToString();
                result.StandardError = stderr.ToString();
            }
            return result;
        }

        // Starts a long-running process and returns it. Used for mpv.
        public static Process StartDetached(string exe, string arguments, string workingDirectory)
        {
            if (!File.Exists(exe)) return null;
            ProcessStartInfo psi = new ProcessStartInfo(exe, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            if (!string.IsNullOrEmpty(workingDirectory)) psi.WorkingDirectory = workingDirectory;
            try { return Process.Start(psi); }
            catch (Exception ex)
            {
                Log.Error("could not start " + exe, ex);
                return null;
            }
        }
    }
}
