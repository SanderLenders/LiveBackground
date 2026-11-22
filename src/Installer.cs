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
        [DataMember(Name = "bundledImages")] public string BundledImagesSignature;

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
        public static string BootstrapperExe(string appRoot) { return Path.Combine(Paths.ProgramRoot, "LiveBackgroundBootstrap.exe"); }
        public static string StartupExe(string appRoot)
        {
            string b = BootstrapperExe(appRoot);
            return File.Exists(b) ? b : AppExe(appRoot);
        }
        public static string StopFlag(string appRoot) { return Path.Combine(appRoot, "stop.flag"); }
        public static string QuitFlag(string appRoot) { return Path.Combine(appRoot, "quit.flag"); }
        public static string TrayExitFlag(string appRoot) { return Path.Combine(appRoot, "tray-exit.flag"); }
        public static string SettingsFlag(string appRoot) { return Path.Combine(appRoot, "settings.flag"); }
        public static string PauseFlag(string appRoot) { return Path.Combine(appRoot, "pause.flag"); }
        public static string ResumeFlag(string appRoot) { return Path.Combine(appRoot, "resume.flag"); }
        public static string PauseStateFile(string appRoot) { return Path.Combine(appRoot, "pause.state"); }
        public static string RebuildFlag(string appRoot) { return Path.Combine(appRoot, "rebuild.flag"); }
        public static string ApplyConfigFlag(string appRoot) { return Path.Combine(appRoot, "apply-config.flag"); }
        public static string BundledVideoDir(string appRoot) { return Path.Combine(Paths.ProgramRoot, "videos"); }
        public static string BundledImageDir(string appRoot) { return Path.Combine(Paths.ProgramRoot, "images", "wallpapers"); }

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
                log(string.Format(CultureInfo.InvariantCulture, "using cached loop ({0:0.0} s)", manifest.OutputDuration));
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

            // No live image here; the running app keeps the lock screen up to
            // date after the restart below.
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
            SyncBundledImages(appRoot, config, state, log);

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
            // A fresh install has no current picture yet; the default lock
            // screen is the loop's first frame anyway.
            ApplyLockScreen(appRoot, config, state, log);
            EnsureStartupOnFirstRun(appRoot, config, state, log);
            log("first run: user library ready at " + appRoot);
        }

        // A fresh copy starts with "Start LiveBackground when I sign in" on.
        // The installer normally creates the tasks before the first launch;
        // this covers portable copies and installs where that step did not
        // run. Creating a task for the current user does not need elevation,
        // so the first launch never shows a UAC prompt.
        private static void EnsureStartupOnFirstRun(string appRoot, AppConfig config,
            InstallState state, Action<string> log)
        {
            string watchdog = StartupTask.WatchdogName(config.TaskName);
            if (StartupTask.Exists(config.TaskName) && StartupTask.Exists(watchdog))
            {
                Shortcut.Remove(Shortcut.StartupShortcutPath(config.TaskName));
                state.UsedTask = true;
                state.Save(appRoot);
                return;
            }

            string error;
            bool ok = StartupTask.Create(appRoot, config.TaskName, StartupExe(appRoot), out error);
            if (ok)
            {
                bool watchdogOk = StartupTask.CreateWatchdog(appRoot, config.TaskName,
                    StartupExe(appRoot), out error);
                Shortcut.Remove(Shortcut.StartupShortcutPath(config.TaskName));
                state.UsedTask = true;
                state.Save(appRoot);
                log("logon task created: " + config.TaskName);
                if (!watchdogOk) log("WARNING: could not create the watchdog task: " + error);
                return;
            }

            log("WARNING: could not create the logon task: " + error);
            log("falling back to a Startup folder shortcut (no crash watchdog)");
            string shortcutError;
            if (Shortcut.Create(Shortcut.StartupShortcutPath(config.TaskName), StartupExe(appRoot),
                "--startup", Paths.ProgramRoot, "LiveBackground video wallpaper", out shortcutError))
            {
                state.UsedTask = false;
                state.Save(appRoot);
                log("Startup shortcut created: " + Shortcut.StartupShortcutPath(config.TaskName));
            }
            else
            {
                log("ERROR: could not create the Startup shortcut: " + shortcutError);
            }
        }

        public static int Uninstall(string appRoot, AppConfig config, bool removeFiles, Action<string> log)
        {
            InstallState state = InstallState.Load(appRoot);
            StopApp(appRoot, log, true);
            RemoveStartup(appRoot, config, log);
            RemoveSettingsShortcut(log);

            // The backup folder is the only copy of what the user had before
            // the app changed it, so it is kept whenever a restore failed.
            bool restoredEverything = true;
            if (!string.IsNullOrEmpty(state.LockScreenBackup) && File.Exists(state.LockScreenBackup))
            {
                string error;
                if (WinRtLockScreen.TrySet(state.LockScreenBackup, out error))
                    log("lock screen restored");
                else
                {
                    log("WARNING: could not restore the lock screen: " + error);
                    restoredEverything = false;
                }
            }
            else
            {
                log("no lock-screen backup to restore");
            }

            if (!string.IsNullOrEmpty(state.WallpaperBackup) && File.Exists(state.WallpaperBackup))
            {
                if (DesktopHost.SetDesktopWallpaper(state.WallpaperBackup))
                {
                    log("desktop wallpaper restored");
                }
                else
                {
                    log("WARNING: could not restore the desktop wallpaper");
                    restoredEverything = false;
                }
            }

            if (removeFiles)
            {
                log("cleaning up " + appRoot);
                ScheduleFolderDelete(appRoot, config, log, restoredEverything);
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
                        try { exited = process.HasExited; } catch { /* best-effort; a process that cannot be queried counts as running */ }
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
            try { File.WriteAllText(QuitFlag(appRoot), "quit"); } catch { /* best-effort; the stop flag below is the fallback */ }

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
                finally { process.Dispose(); }
            }

            if (!found)
            {
                // Fallback for a locked-down process list: use the stop flag.
                try { File.WriteAllText(StopFlag(appRoot), "stop"); } catch { /* best-effort; the process kill below is the fallback */ }
                if (wait) System.Threading.Thread.Sleep(1500);
            }

            // The launcher normally stops its own mpv, but make sure no orphan
            // instance of our bundled mpv keeps painting over the desktop.
            KillOrphanMpv(log);
            Util.SafeDelete(StopFlag(appRoot));
        }

        // Kills every bundled mpv that is not owned by a running app. The
        // launcher stops its own player on the way out, but a hard kill (a
        // crash, Task Manager, a power cut) leaves the mpv painting over the
        // desktop with no window to close. Startup calls this too, so a stale
        // player from a previous run never survives into the new one.
        // Returns the number of players stopped.
        public static int KillOrphanMpv(Action<string> log)
        {
            int killed = 0;
            string mpv = Paths.Tool(AppConfigDefaults.DefaultMpvPath);
            foreach (Process process in FindProcesses(mpv))
            {
                try
                {
                    process.Kill();
                    killed++;
                    if (log != null) log("stopped orphan mpv (pid " + process.Id + ")");
                }
                catch { /* best-effort; the process may have already exited */ }
                finally { process.Dispose(); }
            }
            return killed;
        }

        public static bool IsAppRunning(string appRoot)
        {
            List<Process> processes = FindProcesses(AppExe(appRoot));
            try { return processes.Count > 0; }
            finally { DisposeAll(processes); }
        }

        public static bool IsMpvRunning(string appRoot)
        {
            List<Process> processes = FindProcesses(Paths.Tool(AppConfigDefaults.DefaultMpvPath));
            try { return processes.Count > 0; }
            finally { DisposeAll(processes); }
        }

        // Closes the handles FindProcesses opened on the matching processes;
        // the callers only need the answer, not the handles.
        private static void DisposeAll(List<Process> processes)
        {
            foreach (Process process in processes)
            {
                try { process.Dispose(); } catch { /* best-effort dispose; the finalizer reclaims it anyway */ }
            }
        }

        public static List<Process> FindProcesses(string exePath)
        {
            List<Process> matches = new List<Process>();
            string name = Path.GetFileNameWithoutExtension(exePath);
            Process[] candidates;
            try { candidates = Process.GetProcessesByName(name); }
            catch { return matches; }

            int currentSession = -1;
            try { currentSession = Process.GetCurrentProcess().SessionId; } catch { /* best-effort: keep scanning all sessions */ }

            foreach (Process process in candidates)
            {
                try
                {
                    if (currentSession >= 0)
                    {
                        try { if (process.SessionId != currentSession) { process.Dispose(); continue; } }
                        catch { /* best-effort: fall through to path check */ }
                    }

                    string path = TryGetProcessPath(process);
                    if (!string.IsNullOrEmpty(path) &&
                        string.Equals(path, exePath, StringComparison.OrdinalIgnoreCase))
                    {
                        matches.Add(process);
                    }
                    else
                    {
                        process.Dispose();
                    }
                }
                catch { process.Dispose(); }
            }
            return matches;
        }

        private static string TryGetProcessPath(Process process)
        {
            IntPtr handle = IntPtr.Zero;
            try
            {
                handle = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)process.Id);
                if (handle != IntPtr.Zero)
                {
                    var sb = new System.Text.StringBuilder(1024);
                    uint size = (uint)sb.Capacity;
                    if (Native.QueryFullProcessImageName(handle, 0, sb, ref size) && size > 0)
                    {
                        return sb.ToString();
                    }
                }
            }
            catch { /* fall back below */ }
            finally
            {
                if (handle != IntPtr.Zero) try { Native.CloseHandle(handle); } catch { }
            }

            try { return process.MainModule.FileName; }
            catch { return null; }
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
            if (Shortcut.Create(shortcut, StartupExe(appRoot), "--startup", Paths.ProgramRoot,
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
            string settingsExe = Path.Combine(Paths.ProgramRoot, "LiveBackgroundSettings.exe");
            string settingsTarget = File.Exists(settingsExe) ? settingsExe : AppExe(appRoot);
            string settingsArgs = File.Exists(settingsExe) ? "" : "--settings";
            if (Shortcut.Create(SettingsShortcutPath(), settingsTarget, settingsArgs, Paths.ProgramRoot,
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

        public static void ApplyLockScreen(string appRoot, AppConfig config, InstallState state,
            Action<string> log)
        {
            string target = "";
            if (string.Equals(config.LockScreenMode, "frame", StringComparison.OrdinalIgnoreCase))
            {
                // The same still the desktop wallpaper uses behind the video,
                // so the lock screen always matches the loop that is playing.
                target = MediaPipeline.FramePath(appRoot);
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

        // The bundled pictures are the app's starting set. A fresh library gets
        // them all; after an update that changes them, a picture that shipped
        // earlier is brought up to date instead of lingering beside the new
        // one. Pictures the user added under other names are never touched.
        //
        // This runs only when the shipped set actually changed (a signature of
        // the bundled file names and sizes is kept in the install state), so a
        // picture the user replaces by hand afterwards stays put.
        public static void SyncBundledImages(string appRoot, AppConfig config,
            InstallState state, Action<string> log)
        {
            string bundled = BundledImageDir(appRoot);
            if (!Directory.Exists(bundled)) return;

            List<string> samples = MediaPipeline.ScanImages(bundled);
            if (samples.Count == 0) return;

            StringBuilder signature = new StringBuilder();
            foreach (string sample in samples)
            {
                FileInfo info = new FileInfo(sample);
                signature.Append(info.Name).Append(':').Append(info.Length).Append(';');
            }
            if (string.Equals(signature.ToString(), state.BundledImagesSignature, StringComparison.Ordinal))
            {
                return;
            }

            string imageFolder = Util.ResolvePath(appRoot, config.ImageFolder);
            try { Directory.CreateDirectory(imageFolder); }
            catch (Exception ex)
            {
                log("WARNING: could not create the image folder: " + ex.Message);
                return;
            }

            bool allCopied = true;
            foreach (string sample in samples)
            {
                try
                {
                    string name = Path.GetFileName(sample);
                    string target = Path.Combine(imageFolder, name);
                    if (File.Exists(target) && SameContent(sample, target)) continue;
                    bool existed = File.Exists(target);
                    File.Copy(sample, target, true);
                    log((existed ? "updated bundled picture: " : "added bundled picture: ") + name);
                }
                catch (Exception ex)
                {
                    allCopied = false;
                    log("WARNING: could not copy the bundled picture: " + ex.Message);
                }
            }
            // Only remember the shipped set once every picture is in place, so a
            // failed copy is retried on the next start instead of being written
            // off until the bundled set changes again.
            if (!allCopied)
            {
                log("WARNING: some bundled pictures could not be copied; retrying on the next start");
                return;
            }
            state.BundledImagesSignature = signature.ToString();
            state.Save(appRoot);
        }

        private static bool SameContent(string a, string b)
        {
            try
            {
                FileInfo fa = new FileInfo(a);
                FileInfo fb = new FileInfo(b);
                if (fa.Length != fb.Length) return false;
                using (FileStream sa = File.OpenRead(a))
                using (FileStream sb = File.OpenRead(b))
                {
                    // Compare in blocks instead of one byte at a time. These are
                    // full-size wallpaper JPEGs, so a per-byte read is needlessly
                    // slow. A short read is filled to a full block before the
                    // compare, so the two streams can never drift out of step.
                    byte[] left = new byte[64 * 1024];
                    byte[] right = new byte[64 * 1024];
                    while (true)
                    {
                        int got = ReadBlock(sa, left);
                        int gotOther = ReadBlock(sb, right);
                        if (got != gotOther) return false;
                        if (got == 0) return true;
                        for (int i = 0; i < got; i++)
                        {
                            if (left[i] != right[i]) return false;
                        }
                    }
                }
            }
            catch { return false; }
        }

        // Reads up to buffer.Length bytes, looping until the buffer is full or
        // the stream ends, so a short read never misaligns a comparison.
        private static int ReadBlock(FileStream stream, byte[] buffer)
        {
            int total = 0;
            while (total < buffer.Length)
            {
                int read = stream.Read(buffer, total, buffer.Length - total);
                if (read <= 0) break;
                total += read;
            }
            return total;
        }

        private static void BackupCurrentWallpaper(string appRoot, InstallState state, Action<string> log)
        {
            string wallpaper = ReadWallpaperPath();
            if (string.IsNullOrEmpty(wallpaper) || !File.Exists(wallpaper)) return;

            string cache = MediaPipeline.CacheDir(appRoot);
            if (wallpaper.StartsWith(cache, StringComparison.OrdinalIgnoreCase))
            {
                log("the desktop is already showing a LiveBackground frame, so there is nothing to save");
                return;
            }

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

        private static void ScheduleFolderDelete(string appRoot, AppConfig config, Action<string> log,
            bool removeBackups)
        {
            // App-managed data only. The README promises an uninstall keeps
            // the user's videos, pictures and settings, so nothing of theirs
            // is ever deleted here: the folders that may hold their files are
            // removed with "rmdir /q", which only works when they are empty.
            // The backup folder is skipped when a restore failed, because it is
            // then the only copy of the user's previous wallpaper or lock screen.
            List<string> wipe = new List<string>();
            wipe.Add(MediaPipeline.CacheDir(appRoot));
            if (removeBackups) wipe.Add(BackupDir(appRoot));
            wipe.Add(Path.Combine(appRoot, "logs"));

            List<string> ifEmpty = new List<string>();
            // Only ever offer to remove a folder that lives inside our own
            // folder. The user may have pointed the video or image folder at,
            // say, their own Videos library; "rmdir /q" only removes an empty
            // directory, but an empty directory of theirs is still theirs.
            foreach (string folder in new string[] {
                Util.ResolvePath(appRoot, config.VideoFolder),
                Util.ResolvePath(appRoot, config.ImageFolder) })
            {
                if (IsInside(appRoot, folder)) ifEmpty.Add(folder);
            }
            if (!Paths.IsPortable) ifEmpty.Add(appRoot);

            // Small marker files can go right now; only the directories need
            // the deferred command because this process still writes there.
            foreach (string flag in new string[] {
                StopFlag(appRoot), QuitFlag(appRoot), SettingsFlag(appRoot),
                PauseFlag(appRoot), ResumeFlag(appRoot), RebuildFlag(appRoot),
                ApplyConfigFlag(appRoot) })
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

                // A unique name so another local process cannot pre-create or
                // swap the file between the write and the run. The script
                // deletes itself at the end.
                string cmd = Path.Combine(Path.GetTempPath(),
                    "livebackground-cleanup-" + Guid.NewGuid().ToString("N") + ".cmd");
                File.WriteAllText(cmd, text.ToString());
                ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c \"" + cmd + "\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psi);
                log(removeBackups
                    ? "removed the app's cache, logs and backups"
                    : "removed the app's cache and logs; the backup folder was kept "
                        + "because the previous lock screen or wallpaper could not be restored");
                log("kept your videos, pictures and settings in " + appRoot);
                log("delete that folder yourself if you want a clean slate");
            }
            catch (Exception ex) { Log.Warn("could not schedule folder removal: " + ex.Message); }
        }

        // True when path is a descendant of root (not root itself). Used to
        // decide which folders an uninstall may offer to remove, so a folder
        // the user pointed at outside our own tree is never touched.
        private static bool IsInside(string root, string path)
        {
            if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(path)) return false;
            string r;
            string p;
            try { r = Path.GetFullPath(root); p = Path.GetFullPath(path); }
            catch { return false; }
            if (r.Length == 0) return false;
            if (r[r.Length - 1] != Path.DirectorySeparatorChar) r += Path.DirectorySeparatorChar;
            return p.StartsWith(r, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static class AppConfigDefaults
    {
        public const string DefaultMpvPath = @"mpv\mpv.exe";
    }
}
