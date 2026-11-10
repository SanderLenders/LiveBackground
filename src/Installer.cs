using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Microsoft.Win32;

namespace LiveBackground
{
    [DataContract]
    public class InstallState
    {
        [DataMember(Name = "installedUtc")] public string InstalledUtc;
        [DataMember(Name = "wallpaperBackup")] public string WallpaperBackup;
        [DataMember(Name = "wallpaperSource")] public string WallpaperSource;
        [DataMember(Name = "lockScreenBackup")] public string LockScreenBackup;
        [DataMember(Name = "usedTask")] public bool UsedTask;
        [DataMember(Name = "seededVideos")] public bool SeededVideos;

        public static string PathFor(string appRoot) { return Path.Combine(appRoot, "install-state.json"); }

        public static InstallState Load(string appRoot)
        {
            try
            {
                if (File.Exists(PathFor(appRoot))) return JsonStore.DeserializeFile<InstallState>(PathFor(appRoot));
            }
            catch (Exception ex) { Log.Warn("could not read install state: " + ex.Message); }
            return new InstallState();
        }

        public void Save(string appRoot)
        {
            try { JsonStore.SerializeFile(PathFor(appRoot), this); }
            catch (Exception ex) { Log.Warn("could not save install state: " + ex.Message); }
        }
    }

    public static class Installer
    {
        public static string BackupDir(string appRoot) { return Path.Combine(appRoot, "backup"); }
        public static string AppExe(string appRoot) { return Path.Combine(Paths.ProgramRoot, "LiveBackground.exe"); }
        public static string SetupExe(string appRoot) { return Path.Combine(Paths.ProgramRoot, "LiveBackgroundCli.exe"); }
        public static string StopFlag(string appRoot) { return Path.Combine(appRoot, "stop.flag"); }
        public static string QuitFlag(string appRoot) { return Path.Combine(appRoot, "quit.flag"); }
        public static string SettingsFlag(string appRoot) { return Path.Combine(appRoot, "settings.flag"); }
        public static string PauseFlag(string appRoot) { return Path.Combine(appRoot, "pause.flag"); }
        public static string ResumeFlag(string appRoot) { return Path.Combine(appRoot, "resume.flag"); }
        public static string RebuildFlag(string appRoot) { return Path.Combine(appRoot, "rebuild.flag"); }
        public static string BundledVideoDir(string appRoot) { return Path.Combine(Paths.ProgramRoot, "videos"); }

        public static int Install(string appRoot, AppConfig config, bool createStartup, Action<string> log)
        {
            InstallState state = InstallState.Load(appRoot);
            try { Prepare(appRoot, config, state, log); }
            catch (Exception ex)
            {
                log("ERROR: could not prepare the user library: " + ex.Message);
                return 1;
            }

            // Build (or reuse) the seamless loop.
            MediaManifest manifest;
            if (MediaPipeline.IsCacheValid(appRoot, config, out manifest))
            {
                log(string.Format("using cached loop ({0:0.0} s)", manifest.OutputDuration));
            }
            else
            {
                log("building the seamless loop, this can take a few minutes...");
                BuildResult build = MediaPipeline.Build(appRoot, config, log, null);
                if (!build.Success)
                {
                    if (File.Exists(MediaPipeline.LoopPath(appRoot)))
                    {
                        log("WARNING: " + build.Message);
                        log("keeping the previous loop");
                    }
                    else
                    {
                        log("ERROR: " + build.Message);
                        log("The app will still start, but no video can play until a loop is built.");
                    }
                }
                else
                {
                    log(build.Message);
                }
            }

            // Static desktop frame so login looks right before the video starts.
            string frame = MediaPipeline.FramePath(appRoot);
            string wallpaper = DesktopHost.WallpaperFor(appRoot, config, frame);
            if (!string.IsNullOrEmpty(wallpaper))
            {
                DesktopHost.SetDesktopWallpaper(wallpaper);
                log("desktop wallpaper set to " + Path.GetFileName(wallpaper));
            }

            ApplyLockScreen(appRoot, config, state, log);

            StopApp(appRoot, log, false);
            StartApp(appRoot, log);
            CreateSettingsShortcut(appRoot, log);
            if (createStartup)
            {
                CreateStartup(appRoot, config, state, log);
            }
            else
            {
                log("skipping the logon task (--no-startup)");
            }
            state.Save(appRoot);

            log("");
            log("Install complete.");
            if (!File.Exists(MediaPipeline.LoopPath(appRoot)))
            {
                log("Add video files to " + Util.ResolvePath(appRoot, config.VideoFolder) +
                    " and run: LiveBackgroundCli.exe rebuild");
            }
            return 0;
        }

        // Creates the user library and records the current desktop/lock screen.
        // Shared by the installer and the first launch of an installed copy.
        public static void Prepare(string appRoot, AppConfig config, InstallState state, Action<string> log)
        {
            Directory.CreateDirectory(appRoot);
            Directory.CreateDirectory(BackupDir(appRoot));
            Directory.CreateDirectory(Path.Combine(appRoot, "cache"));
            Directory.CreateDirectory(Path.Combine(appRoot, "logs"));
            Directory.CreateDirectory(Path.Combine(appRoot, "images"));

            string videoFolder = Util.ResolvePath(appRoot, config.VideoFolder);
            log("video folder: " + videoFolder);
            Directory.CreateDirectory(videoFolder);
            SeedVideos(appRoot, videoFolder, state, log);

            string imageFolder = Util.ResolvePath(appRoot, config.ImageFolder);
            log("image folder: " + imageFolder);
            Directory.CreateDirectory(imageFolder);

            // Save what was on screen before the app changes anything.
            if (string.IsNullOrEmpty(state.WallpaperBackup) || !File.Exists(state.WallpaperBackup))
            {
                BackupCurrentWallpaper(appRoot, state, log);
            }
            if (string.IsNullOrEmpty(state.LockScreenBackup) || !File.Exists(state.LockScreenBackup))
            {
                BackupLockScreen(appRoot, state, log);
            }
            state.InstalledUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            state.Save(appRoot);
        }

        // Runs once when an installed copy has no config yet. The loop itself
        // is built in the background by the normal startup path.
        public static void FirstRun(string appRoot, AppConfig config, Action<string> log)
        {
            InstallState state = InstallState.Load(appRoot);
            Prepare(appRoot, config, state, log);
            ApplyLockScreen(appRoot, config, state, log);
            log("first run: user library ready at " + appRoot);
        }

        public static int Uninstall(string appRoot, AppConfig config, bool removeFiles, Action<string> log)
        {
            InstallState state = InstallState.Load(appRoot);
            StopApp(appRoot, log, true);
            RemoveStartup(appRoot, config, log);
            RemoveSettingsShortcut(log);

            if (!string.IsNullOrEmpty(state.LockScreenBackup) && File.Exists(state.LockScreenBackup))
            {
                string error;
                if (WinRtLockScreen.TrySet(state.LockScreenBackup, out error))
                    log("lock screen restored");
                else
                    log("WARNING: could not restore the lock screen: " + error);
            }
            else
            {
                log("no lock-screen backup to restore");
            }

            if (!string.IsNullOrEmpty(state.WallpaperBackup) && File.Exists(state.WallpaperBackup))
            {
                DesktopHost.SetDesktopWallpaper(state.WallpaperBackup);
                log("desktop wallpaper restored");
            }

            if (removeFiles)
            {
                log("cleaning up " + appRoot);
                ScheduleFolderDelete(appRoot, config, log);
            }

            log("");
            log("Uninstall complete.");
            return 0;
        }

        public static int Rebuild(string appRoot, AppConfig config, Action<string> log, Action<double> progress)
        {
            string folder = Util.ResolvePath(appRoot, config.VideoFolder);
            log("rebuilding loop from " + folder);
            BuildResult build = MediaPipeline.Build(appRoot, config, log, progress);
            if (!build.Success)
            {
                log("ERROR: " + build.Message);
                return 1;
            }
            log(build.Message);

            string wallpaper = DesktopHost.WallpaperFor(appRoot, config, build.FramePath);
            if (!string.IsNullOrEmpty(wallpaper)) DesktopHost.SetDesktopWallpaper(wallpaper);

            // Restart the running app so it picks up the new loop.
            if (IsAppRunning(appRoot))
            {
                log("restarting the wallpaper");
                StopApp(appRoot, log, true);
                StartApp(appRoot, log);
            }
            return 0;
        }

        public static void StartApp(string appRoot, Action<string> log)
        {
            string exe = AppExe(appRoot);
            if (!File.Exists(exe)) { log("ERROR: " + exe + " not found"); return; }
            if (IsAppRunning(appRoot)) { log("wallpaper is already running"); return; }

            Util.SafeDelete(StopFlag(appRoot));

            // Smart App Control can take a moment to notice that the signing
            // certificate was just trusted. Retry a few times so a first-run
            // block is not fatal.
            const int attempts = 4;
            for (int attempt = 1; attempt <= attempts; attempt++)
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo(exe, "--startup")
                    {
                        // Shell execution does not pass the caller's console
                        // handles to the child, so a script that runs the
                        // installer does not wait for the wallpaper to exit.
                        UseShellExecute = true,
                        WorkingDirectory = Paths.ProgramRoot
                    };
                    Process process = Process.Start(psi);
                    if (process != null)
                    {
                        System.Threading.Thread.Sleep(900);
                        bool exited = false;
                        try { exited = process.HasExited; } catch { }
                        if (!exited)
                        {
                            log("wallpaper started (pid " + process.Id + ")");
                            return;
                        }
                        log("start attempt " + attempt + " exited immediately, retrying...");
                    }
                }
                catch (Exception ex)
                {
                    log("start attempt " + attempt + " failed: " + ex.Message);
                }

                if (attempt < attempts) System.Threading.Thread.Sleep(1500);
            }

            log("ERROR: could not start the wallpaper.");
            log("If Windows Smart App Control blocked it, choose Allow when prompted, then run install.cmd again.");
        }

        public static void StopApp(string appRoot, Action<string> log, bool wait)
        {
            // Keep the scheduled watchdog from bringing the app back while an
            // installer or the user is deliberately stopping it.
            try { File.WriteAllText(QuitFlag(appRoot), "quit"); } catch { }

            bool found = false;
            foreach (Process process in FindProcesses(AppExe(appRoot)))
            {
                found = true;
                try
                {
                    log("stopping wallpaper (pid " + process.Id + ")");
                    process.Kill();
                }
                catch (Exception ex) { log("WARNING: could not stop pid " + process.Id + ": " + ex.Message); }
            }

            if (!found)
            {
                // Fallback for a locked-down process list: use the stop flag.
                try { File.WriteAllText(StopFlag(appRoot), "stop"); } catch { }
                if (wait) System.Threading.Thread.Sleep(1500);
            }

            // The launcher normally stops its own mpv, but make sure no orphan
            // instance of our bundled mpv keeps painting over the desktop.
            string mpv = Paths.Tool(AppConfigDefaults.DefaultMpvPath);
            foreach (Process process in FindProcesses(mpv))
            {
                try { process.Kill(); log("stopped orphan mpv (pid " + process.Id + ")"); }
                catch { }
            }
            Util.SafeDelete(StopFlag(appRoot));
        }

        public static bool IsAppRunning(string appRoot)
        {
            return FindProcesses(AppExe(appRoot)).Count > 0;
        }

        public static bool IsMpvRunning(string appRoot)
        {
            return FindProcesses(Paths.Tool(AppConfigDefaults.DefaultMpvPath)).Count > 0;
        }

        public static List<Process> FindProcesses(string exePath)
        {
            List<Process> matches = new List<Process>();
            string name = Path.GetFileNameWithoutExtension(exePath);
            Process[] candidates;
            try { candidates = Process.GetProcessesByName(name); }
            catch { return matches; }

            foreach (Process process in candidates)
            {
                try
                {
                    string path = process.MainModule.FileName;
                    if (string.Equals(path, exePath, StringComparison.OrdinalIgnoreCase)) matches.Add(process);
                    else process.Dispose();
                }
                catch { process.Dispose(); }
            }
            return matches;
        }

        public static void CreateStartup(string appRoot, AppConfig config, InstallState state, Action<string> log)
        {
            string shortcut = Shortcut.StartupShortcutPath(config.TaskName);
            string watchdog = StartupTask.WatchdogName(config.TaskName);
            if (StartupTask.Exists(config.TaskName) && StartupTask.Exists(watchdog))
            {
                Shortcut.Remove(shortcut);
                state.UsedTask = true;
                log("logon task already exists: " + config.TaskName);
                return;
            }

            log("creating the logon and watchdog tasks (Windows will ask for permission)...");
            string error;
            int result = StartupTask.CreateElevated(SetupExe(appRoot), Paths.ProgramRoot, out error);
            if (result == 0)
            {
                Shortcut.Remove(shortcut);
                state.UsedTask = true;
                log("logon task created: " + config.TaskName);
                return;
            }

            if (result == 2) log("permission was cancelled, using the Startup folder instead");
            else log("WARNING: could not create the logon task: " + error);
            log("falling back to a Startup folder shortcut (no crash watchdog)");

            string shortcutError;
            if (Shortcut.Create(shortcut, AppExe(appRoot), "--startup", Paths.ProgramRoot,
                "LiveBackground video wallpaper", out shortcutError))
            {
                state.UsedTask = false;
                log("Startup shortcut created: " + shortcut);
            }
            else
            {
                log("ERROR: could not create the Startup shortcut: " + shortcutError);
                log("Start the wallpaper manually with: LiveBackgroundCli.exe start");
            }
        }

        public static void RemoveStartup(string appRoot, AppConfig config, Action<string> log)
        {
            Shortcut.Remove(Shortcut.StartupShortcutPath(config.TaskName));

            string watchdog = StartupTask.WatchdogName(config.TaskName);
            bool mainExists = StartupTask.Exists(config.TaskName);
            bool watchdogExists = StartupTask.Exists(watchdog);
            if (!mainExists && !watchdogExists) return;

            string error;
            bool mainRemoved = !mainExists || StartupTask.Delete(config.TaskName, out error);
            bool watchdogRemoved = !watchdogExists || StartupTask.Delete(watchdog, out error);
            if (mainRemoved && watchdogRemoved)
            {
                log("logon and watchdog tasks removed");
                return;
            }

            log("removing the logon task needs permission...");
            int result = StartupTask.DeleteElevated(SetupExe(appRoot), Paths.ProgramRoot, out error);
            if (result == 0) log("logon and watchdog tasks removed");
            else log("WARNING: could not remove the logon task: " + error);
        }

        // The tray icon lives in the hidden-icons overflow, so also expose the
        // settings window from the Start Menu. It reopens the running app's
        // dialog through the settings flag.
        public static string SettingsShortcutPath()
        {
            string programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            return Path.Combine(programs, "LiveBackground settings.lnk");
        }

        public static void CreateSettingsShortcut(string appRoot, Action<string> log)
        {
            string error;
            if (Shortcut.Create(SettingsShortcutPath(), AppExe(appRoot), "--settings", Paths.ProgramRoot,
                "Open the LiveBackground settings", out error))
            {
                log("added a Start Menu shortcut for the settings");
            }
            else
            {
                log("WARNING: could not add the Start Menu shortcut: " + error);
            }
        }

        public static void RemoveSettingsShortcut(Action<string> log)
        {
            try
            {
                string path = SettingsShortcutPath();
                if (File.Exists(path))
                {
                    File.Delete(path);
                    log("removed the Start Menu shortcut");
                }
            }
            catch (Exception ex)
            {
                log("WARNING: could not remove the Start Menu shortcut: " + ex.Message);
            }
        }

        public static void ApplyLockScreen(string appRoot, AppConfig config, InstallState state, Action<string> log)
        {
            string target = "";
            if (string.Equals(config.LockScreenMode, "previous", StringComparison.OrdinalIgnoreCase))
            {
                target = state.WallpaperBackup;
            }
            else if (string.Equals(config.LockScreenMode, "custom", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(config.LockScreenMode, "image", StringComparison.OrdinalIgnoreCase))
            {
                target = Util.ResolvePath(appRoot, config.LockScreenImage);
            }

            if (string.IsNullOrEmpty(target) || !File.Exists(target))
            {
                log("lock screen left unchanged");
                return;
            }

            string error;
            if (WinRtLockScreen.TrySet(target, out error))
                log("lock screen set to " + Path.GetFileName(target));
            else
                log("WARNING: could not set the lock screen: " + error);
        }

        private static void SeedVideos(string appRoot, string videoFolder, InstallState state, Action<string> log)
        {
            if (MediaPipeline.ScanVideos(videoFolder).Count > 0) return;
            string bundled = BundledVideoDir(appRoot);
            if (!Directory.Exists(bundled)) return;

            List<string> samples = MediaPipeline.ScanVideos(bundled);
            if (samples.Count == 0) return;

            foreach (string sample in samples)
            {
                try
                {
                    string target = Path.Combine(videoFolder, Path.GetFileName(sample));
                    File.Copy(sample, target, false);
                    log("added sample video: " + Path.GetFileName(sample));
                }
                catch (Exception ex) { log("WARNING: could not copy sample video: " + ex.Message); }
            }
            state.SeededVideos = true;
        }

        private static void BackupCurrentWallpaper(string appRoot, InstallState state, Action<string> log)
        {
            string wallpaper = ReadWallpaperPath();
            if (string.IsNullOrEmpty(wallpaper) || !File.Exists(wallpaper)) return;

            string cache = MediaPipeline.CacheDir(appRoot);
            if (wallpaper.StartsWith(cache, StringComparison.OrdinalIgnoreCase)) return;

            string ext = Util.DetectImageExtension(wallpaper);
            if (ext == ".img") { log("could not identify the current wallpaper format, not backing it up"); return; }

            try
            {
                string target = Path.Combine(BackupDir(appRoot), "desktop-previous" + ext);
                File.Copy(wallpaper, target, true);
                state.WallpaperBackup = target;
                state.WallpaperSource = wallpaper;
                log("saved the previous desktop wallpaper");
            }
            catch (Exception ex) { log("WARNING: could not back up the wallpaper: " + ex.Message); }
        }

        private static void BackupLockScreen(string appRoot, InstallState state, Action<string> log)
        {
            string actual;
            string error;
            if (WinRtLockScreen.TryBackup(Path.Combine(BackupDir(appRoot), "lock-screen-original"), out actual, out error))
            {
                state.LockScreenBackup = actual;
                log("saved the current lock screen");
            }
            else
            {
                log("WARNING: could not save the current lock screen: " + error);
            }
        }

        private static string ReadWallpaperPath()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop"))
                {
                    if (key == null) return "";
                    object value = key.GetValue("Wallpaper");
                    return value == null ? "" : value.ToString();
                }
            }
            catch { return ""; }
        }

        private static void ScheduleFolderDelete(string appRoot, AppConfig config, Action<string> log)
        {
            // App-managed data only. The README promises an uninstall keeps
            // the user's videos, pictures and settings, so nothing of theirs
            // is ever deleted here: the folders that may hold their files are
            // removed with "rmdir /q", which only works when they are empty.
            List<string> wipe = new List<string>();
            wipe.Add(MediaPipeline.CacheDir(appRoot));
            wipe.Add(BackupDir(appRoot));
            wipe.Add(Path.Combine(appRoot, "logs"));

            List<string> ifEmpty = new List<string>();
            ifEmpty.Add(Util.ResolvePath(appRoot, config.VideoFolder));
            ifEmpty.Add(Util.ResolvePath(appRoot, config.ImageFolder));
            if (!Paths.IsPortable) ifEmpty.Add(appRoot);

            // Small marker files can go right now; only the directories need
            // the deferred command because this process still writes there.
            foreach (string flag in new string[] {
                StopFlag(appRoot), QuitFlag(appRoot), SettingsFlag(appRoot),
                PauseFlag(appRoot), ResumeFlag(appRoot), RebuildFlag(appRoot) })
            {
                Util.SafeDelete(flag);
            }

            try
            {
                StringBuilder text = new StringBuilder();
                text.Append("@echo off\r\n");
                text.Append("ping -n 3 127.0.0.1 >nul\r\n");
                foreach (string dir in wipe) text.Append("rmdir /s /q \"" + dir + "\"\r\n");
                foreach (string dir in ifEmpty) text.Append("rmdir /q \"" + dir + "\"\r\n");
                text.Append("del /q \"%~f0\"\r\n");

                string cmd = Path.Combine(Path.GetTempPath(), "livebackground-cleanup.cmd");
                File.WriteAllText(cmd, text.ToString());
                ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c \"" + cmd + "\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psi);
                log("removed the app's cache, logs and backups");
                log("kept your videos, pictures and settings in " + appRoot);
                log("delete that folder yourself if you want a clean slate");
            }
            catch (Exception ex) { Log.Warn("could not schedule folder removal: " + ex.Message); }
        }
    }

    public static class AppConfigDefaults
    {
        public const string DefaultMpvPath = @"mpv\mpv.exe";
    }
}
