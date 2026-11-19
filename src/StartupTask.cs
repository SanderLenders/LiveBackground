using System;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace LiveBackground
{
    public static class Shortcut
    {
        public static bool Create(string shortcutPath, string targetPath, string arguments,
            string workingDirectory, string description, out string error)
        {
            error = "";
            try
            {
                string dir = Path.GetDirectoryName(shortcutPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) { error = "WScript.Shell is not available"; return false; }
                object shell = Activator.CreateInstance(shellType);
                object link = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell,
                    new object[] { shortcutPath }, CultureInfo.InvariantCulture);

                Type linkType = link.GetType();
                linkType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, new object[] { targetPath }, CultureInfo.InvariantCulture);
                linkType.InvokeMember("Arguments", BindingFlags.SetProperty, null, link, new object[] { arguments ?? "" }, CultureInfo.InvariantCulture);
                linkType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, link, new object[] { workingDirectory ?? "" }, CultureInfo.InvariantCulture);
                linkType.InvokeMember("Description", BindingFlags.SetProperty, null, link, new object[] { description ?? "" }, CultureInfo.InvariantCulture);
                linkType.InvokeMember("WindowStyle", BindingFlags.SetProperty, null, link, new object[] { 7 }, CultureInfo.InvariantCulture);
                linkType.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null, CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static void Remove(string shortcutPath)
        {
            try { if (File.Exists(shortcutPath)) File.Delete(shortcutPath); }
            catch (Exception ex) { Log.Warn("could not remove shortcut: " + ex.Message); }
        }

        public static string StartupFolder
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup));
            }
        }

        public static string StartupShortcutPath(string name)
        {
            return Path.Combine(StartupFolder, name + ".lnk");
        }
    }

    public static class StartupTask
    {
        public const string DefaultTaskName = "LiveBackground";

        public static string WatchdogName(string taskName)
        {
            return taskName + " watchdog";
        }

        public static bool Exists(string taskName)
        {
            ProcessResult result = ProcessRunner.Run(Schtasks, "/query /tn " + Util.Quote(taskName), null, 30000);
            return result.ExitCode == 0;
        }

        public static bool Create(string appRoot, string taskName, string appExe, out string error)
        {
            return Create(appRoot, taskName, appExe, out error, null);
        }

        // userId registers the logon task for a specific account instead of
        // whoever is running this code. The settings window uses it so the task
        // belongs to the signed-in user, not to the administrator who approved
        // the elevation prompt.
        public static bool Create(string appRoot, string taskName, string appExe, out string error, string userId)
        {
            return CreateFromXml(taskName, BuildXml(taskName, appExe, userId), out error);
        }

        // A second, very small task that checks every few minutes whether the
        // tray app disappeared and starts it again after a crash.
        public static bool CreateWatchdog(string appRoot, string taskName, string appExe, out string error)
        {
            return CreateWatchdog(appRoot, taskName, appExe, out error, null);
        }

        public static bool CreateWatchdog(string appRoot, string taskName, string appExe, out string error, string userId)
        {
            return CreateFromXml(WatchdogName(taskName), BuildWatchdogXml(WatchdogName(taskName), appExe, userId), out error);
        }

        private static bool CreateFromXml(string taskName, string xml, out string error)
        {
            error = "";
            string xmlPath = Path.Combine(Path.GetTempPath(), "livebackground-task-" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                File.WriteAllText(xmlPath, xml, System.Text.Encoding.Unicode);
                ProcessResult result = ProcessRunner.Run(Schtasks,
                    "/create /tn " + Util.Quote(taskName) + " /xml " + Util.Quote(xmlPath) + " /f",
                    null, 60000);
                if (result.ExitCode == 0) return true;
                error = Trim(result.AllOutput);
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                Util.SafeDelete(xmlPath);
            }
        }

        public static bool Delete(string taskName, out string error)
        {
            error = "";
            ProcessResult result = ProcessRunner.Run(Schtasks,
                "/delete /tn " + Util.Quote(taskName) + " /f", null, 60000);
            if (result.ExitCode == 0) return true;
            // "The task does not exist" is a success. Decide that from a query
            // rather than from the error text, which Windows localizes.
            if (!Exists(taskName)) return true;
            error = Trim(result.AllOutput);
            return false;
        }

        // Runs "setup.exe install-task" with a UAC prompt. Returns:
        //   0 = success, 1 = failure, 2 = user cancelled elevation.
        public static int CreateElevated(string setupExe, string appRoot, out string error)
        {
            error = "";
            try
            {
                // The logon task must belong to the person who is signed in,
                // not to whichever administrator approves the elevation
                // prompt. Pass that account explicitly so the elevated helper
                // registers the task for it.
                string arguments = "install-task " + Util.Quote(Util.CurrentUser());
                System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo(setupExe, arguments);
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                psi.WorkingDirectory = appRoot;
                psi.WindowStyle = System.Diagnostics.ProcessWindowStyle.Normal;
                using (System.Diagnostics.Process process = System.Diagnostics.Process.Start(psi))
                {
                    process.WaitForExit();
                    if (process.ExitCode == 0) return 0;
                    error = "elevated task creation failed (exit " + process.ExitCode + ")";
                    return 1;
                }
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                if (ex.NativeErrorCode == 1223)
                {
                    error = "elevation was cancelled";
                    return 2;
                }
                error = ex.Message;
                return 1;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return 1;
            }
        }

        public static int DeleteElevated(string setupExe, string appRoot, out string error)
        {
            return RunElevated(setupExe, "remove-task", appRoot, out error);
        }

        private static int RunElevated(string setupExe, string arguments, string appRoot, out string error)
        {
            error = "";
            try
            {
                System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo(setupExe, arguments);
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                psi.WorkingDirectory = appRoot;
                psi.WindowStyle = System.Diagnostics.ProcessWindowStyle.Normal;
                using (System.Diagnostics.Process process = System.Diagnostics.Process.Start(psi))
                {
                    process.WaitForExit();
                    if (process.ExitCode == 0) return 0;
                    error = "elevated command failed (exit " + process.ExitCode + ")";
                    return 1;
                }
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                if (ex.NativeErrorCode == 1223)
                {
                    error = "elevation was cancelled";
                    return 2;
                }
                error = ex.Message;
                return 1;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return 1;
            }
        }

        // Priority 1 (High) makes the logon task start the app at High from its
        // very first instruction, so it outranks the other logon apps for CPU
        // during the logon storm; Run() drops it back to Normal once the
        // wallpaper is up. High needs no elevation (only Realtime does).
        private static string BuildXml(string taskName, string appExe, string userId)
        {
            string user = Xml(string.IsNullOrEmpty(userId) ? Util.CurrentUser() : userId);
            string command = Xml(appExe);
            return
                "<?xml version=\"1.0\" encoding=\"UTF-16\"?>" + Environment.NewLine +
                "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">" + Environment.NewLine +
                "  <RegistrationInfo>" + Environment.NewLine +
                "    <Description>Starts LiveBackground at logon.</Description>" + Environment.NewLine +
                "    <URI>\\" + Xml(taskName) + "</URI>" + Environment.NewLine +
                "  </RegistrationInfo>" + Environment.NewLine +
                "  <Triggers>" + Environment.NewLine +
                "    <LogonTrigger>" + Environment.NewLine +
                "      <Enabled>true</Enabled>" + Environment.NewLine +
                "      <UserId>" + user + "</UserId>" + Environment.NewLine +
                "    </LogonTrigger>" + Environment.NewLine +
                "  </Triggers>" + Environment.NewLine +
                "  <Principals>" + Environment.NewLine +
                "    <Principal id=\"Author\">" + Environment.NewLine +
                "      <UserId>" + user + "</UserId>" + Environment.NewLine +
                "      <LogonType>InteractiveToken</LogonType>" + Environment.NewLine +
                "      <RunLevel>LeastPrivilege</RunLevel>" + Environment.NewLine +
                "    </Principal>" + Environment.NewLine +
                "  </Principals>" + Environment.NewLine +
                "  <Settings>" + Environment.NewLine +
                "    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>" + Environment.NewLine +
                "    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>" + Environment.NewLine +
                "    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>" + Environment.NewLine +
                "    <AllowHardTerminate>true</AllowHardTerminate>" + Environment.NewLine +
                "    <StartWhenAvailable>true</StartWhenAvailable>" + Environment.NewLine +
                "    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>" + Environment.NewLine +
                "    <IdleSettings>" + Environment.NewLine +
                "      <StopOnIdleEnd>false</StopOnIdleEnd>" + Environment.NewLine +
                "      <RestartOnIdle>false</RestartOnIdle>" + Environment.NewLine +
                "    </IdleSettings>" + Environment.NewLine +
                "    <AllowStartOnDemand>true</AllowStartOnDemand>" + Environment.NewLine +
                "    <Enabled>true</Enabled>" + Environment.NewLine +
                "    <Hidden>false</Hidden>" + Environment.NewLine +
                "    <RunOnlyIfIdle>false</RunOnlyIfIdle>" + Environment.NewLine +
                "    <WakeToRun>false</WakeToRun>" + Environment.NewLine +
                "    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>" + Environment.NewLine +
                "    <Priority>1</Priority>" + Environment.NewLine +
                "  </Settings>" + Environment.NewLine +
                "  <Actions Context=\"Author\">" + Environment.NewLine +
                "    <Exec>" + Environment.NewLine +
                "      <Command>" + command + "</Command>" + Environment.NewLine +
                "      <Arguments>--startup</Arguments>" + Environment.NewLine +
                "    </Exec>" + Environment.NewLine +
                "  </Actions>" + Environment.NewLine +
                "</Task>" + Environment.NewLine;
        }

        private static string BuildWatchdogXml(string taskName, string appExe, string userId)
        {
            string user = Xml(string.IsNullOrEmpty(userId) ? Util.CurrentUser() : userId);
            string command = Xml(appExe);
            return
                "<?xml version=\"1.0\" encoding=\"UTF-16\"?>" + Environment.NewLine +
                "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">" + Environment.NewLine +
                "  <RegistrationInfo>" + Environment.NewLine +
                "    <Description>Restarts LiveBackground if it has stopped unexpectedly.</Description>" + Environment.NewLine +
                "    <URI>\\" + Xml(taskName) + "</URI>" + Environment.NewLine +
                "  </RegistrationInfo>" + Environment.NewLine +
                "  <Triggers>" + Environment.NewLine +
                "    <LogonTrigger>" + Environment.NewLine +
                "      <Enabled>true</Enabled>" + Environment.NewLine +
                "      <UserId>" + user + "</UserId>" + Environment.NewLine +
                "      <Repetition>" + Environment.NewLine +
                "        <Interval>PT5M</Interval>" + Environment.NewLine +
                "        <StopAtDurationEnd>false</StopAtDurationEnd>" + Environment.NewLine +
                "      </Repetition>" + Environment.NewLine +
                "    </LogonTrigger>" + Environment.NewLine +
                "  </Triggers>" + Environment.NewLine +
                "  <Principals>" + Environment.NewLine +
                "    <Principal id=\"Author\">" + Environment.NewLine +
                "      <UserId>" + user + "</UserId>" + Environment.NewLine +
                "      <LogonType>InteractiveToken</LogonType>" + Environment.NewLine +
                "      <RunLevel>LeastPrivilege</RunLevel>" + Environment.NewLine +
                "    </Principal>" + Environment.NewLine +
                "  </Principals>" + Environment.NewLine +
                "  <Settings>" + Environment.NewLine +
                "    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>" + Environment.NewLine +
                "    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>" + Environment.NewLine +
                "    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>" + Environment.NewLine +
                "    <AllowHardTerminate>true</AllowHardTerminate>" + Environment.NewLine +
                "    <StartWhenAvailable>true</StartWhenAvailable>" + Environment.NewLine +
                "    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>" + Environment.NewLine +
                "    <IdleSettings>" + Environment.NewLine +
                "      <StopOnIdleEnd>false</StopOnIdleEnd>" + Environment.NewLine +
                "      <RestartOnIdle>false</RestartOnIdle>" + Environment.NewLine +
                "    </IdleSettings>" + Environment.NewLine +
                "    <AllowStartOnDemand>true</AllowStartOnDemand>" + Environment.NewLine +
                "    <Enabled>true</Enabled>" + Environment.NewLine +
                "    <Hidden>true</Hidden>" + Environment.NewLine +
                "    <RunOnlyIfIdle>false</RunOnlyIfIdle>" + Environment.NewLine +
                "    <WakeToRun>false</WakeToRun>" + Environment.NewLine +
                "    <ExecutionTimeLimit>PT5M</ExecutionTimeLimit>" + Environment.NewLine +
                "    <Priority>6</Priority>" + Environment.NewLine +
                "  </Settings>" + Environment.NewLine +
                "  <Actions Context=\"Author\">" + Environment.NewLine +
                "    <Exec>" + Environment.NewLine +
                "      <Command>" + command + "</Command>" + Environment.NewLine +
                "      <Arguments>--watchdog</Arguments>" + Environment.NewLine +
                "    </Exec>" + Environment.NewLine +
                "  </Actions>" + Environment.NewLine +
                "</Task>" + Environment.NewLine;
        }

        private static string Xml(string value)
        {
            if (value == null) return "";
            return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                .Replace("\"", "&quot;").Replace("'", "&apos;");
        }

        private static string Schtasks
        {
            get { return Path.Combine(Environment.SystemDirectory, "schtasks.exe"); }
        }

        private static string Trim(string text)
        {
            if (text == null) return "";
            text = text.Trim();
            if (text.Length > 600) text = text.Substring(0, 600);
            return text;
        }
    }
}
