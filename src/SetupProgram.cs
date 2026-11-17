using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace LiveBackground
{
    public static class SetupProgram
    {
        private static string _appRoot;

        public static int Main(string[] args)
        {
            Util.UseEnglishUiCulture();
            string programRoot = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            Paths.Init(programRoot);
            _appRoot = Paths.DataRoot;
            Log.Init(_appRoot, "setup.log");

            string command = args != null && args.Length > 0 ? args[0].ToLowerInvariant() : "help";
            try
            {
                switch (command)
                {
                    case "install": return Install(args);
                    case "uninstall": return Uninstall(args);
                    case "rebuild": return Rebuild();
                    case "start": return Start();
                    case "stop": return Stop();
                    case "status": return Status();
                    case "install-task": return InstallTask(args);
                    case "remove-task": return RemoveTask();
                    case "set-lockscreen": return SetLockScreen(args);
                    case "restore-lockscreen": return RestoreLockScreen();
                    case "build-only": return BuildOnly();
                    case "help":
                    case "--help":
                    case "-h":
                    case "/?":
                        Help();
                        return 0;
                    default:
                        Console.WriteLine("Unknown command: " + command);
                        Console.WriteLine();
                        Help();
                        return 1;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine("ERROR: " + ex.GetType().Name + ": " + ex.Message);
                Log.Error("setup command failed: " + command, ex);
                return 1;
            }
        }

        private static int Install(string[] args)
        {
            Banner("LiveBackground - install");
            bool createStartup = !CommandLine.HasArg(args, "--no-startup");
            AppConfig config = ConfigStore.LoadOrCreate(_appRoot);
            int result = Installer.Install(_appRoot, config, createStartup, Console.WriteLine);
            if (result == 0)
            {
                Console.WriteLine();
                Console.WriteLine("Your wallpaper is running now and will start automatically at logon.");
                Console.WriteLine("Drop more videos into:");
                Console.WriteLine("  " + Util.ResolvePath(_appRoot, config.VideoFolder));
                Console.WriteLine("The loop rebuilds by itself a few seconds after the folder changes.");
            }
            return result;
        }

        private static int Uninstall(string[] args)
        {
            Banner("LiveBackground - uninstall");
            bool remove = CommandLine.HasArg(args, "--remove") || CommandLine.HasArg(args, "/remove");
            AppConfig config = ConfigStore.LoadOrCreate(_appRoot);
            int result = Installer.Uninstall(_appRoot, config, remove, Console.WriteLine);
            return result;
        }

        private static int Rebuild()
        {
            Banner("LiveBackground - rebuild");
            AppConfig config = ConfigStore.LoadOrCreate(_appRoot);
            int lastPercent = -1;
            Action<double> progress = delegate(double fraction)
            {
                int percent = (int)Math.Round(fraction * 100);
                if (percent == lastPercent) return;
                lastPercent = percent;
                Console.Write("\r  rendering... {0,3}%", percent);
            };
            int result = Installer.Rebuild(_appRoot, config, Console.WriteLine, progress);
            if (lastPercent >= 0) Console.WriteLine();
            return result;
        }

        private static int BuildOnly()
        {
            Banner("LiveBackground - build loop");
            AppConfig config = ConfigStore.LoadOrCreate(_appRoot);
            int lastPercent = -1;
            BuildResult build = MediaPipeline.Build(_appRoot, config, Console.WriteLine, delegate(double fraction)
            {
                int percent = (int)Math.Round(fraction * 100);
                if (percent == lastPercent) return;
                lastPercent = percent;
                Console.Write("\r  rendering... {0,3}%", percent);
            });
            if (lastPercent >= 0) Console.WriteLine();
            if (!build.Success) { Console.WriteLine("ERROR: " + build.Message); return 1; }
            Console.WriteLine(build.Message);
            return 0;
        }

        private static int Start()
        {
            AppConfig config = ConfigStore.LoadOrCreate(_appRoot);
            Installer.StartApp(_appRoot, Console.WriteLine);
            return 0;
        }

        private static int Stop()
        {
            AppConfig config = ConfigStore.LoadOrCreate(_appRoot);
            Installer.StopApp(_appRoot, Console.WriteLine, true);
            Console.WriteLine("wallpaper stopped");
            return 0;
        }

        private static int Status()
        {
            AppConfig config = ConfigStore.LoadOrCreate(_appRoot);
            string folder = Util.ResolvePath(_appRoot, config.VideoFolder);
            List<string> videos = MediaPipeline.ScanVideos(folder);
            MediaManifest manifest = MediaPipeline.LoadManifest(_appRoot);
            MediaManifest check;
            bool valid = MediaPipeline.IsCacheValid(_appRoot, config, out check);
            InstallState state = InstallState.Load(_appRoot);

            Console.WriteLine("LiveBackground");
            Console.WriteLine("  program folder  : " + Paths.ProgramRoot);
            Console.WriteLine("  data folder     : " + _appRoot);
            Console.WriteLine("  config          : " + ConfigStore.PathFor(_appRoot));
            Console.WriteLine("  video folder    : " + folder);
            Console.WriteLine("  videos          : " + videos.Count);
            foreach (string video in videos) Console.WriteLine("                    " + Path.GetFileName(video));
            Console.WriteLine("  loop            : " + (File.Exists(MediaPipeline.LoopPath(_appRoot)) ? "yes" : "no"));
            if (manifest != null)
            {
                Console.WriteLine("  loop duration   : " + manifest.OutputDuration.ToString("0.0", CultureInfo.InvariantCulture) + " s");
                Console.WriteLine("  loop resolution : " + manifest.Width + "x" + manifest.Height + " @ " + manifest.Fps + " fps");
                Console.WriteLine("  built           : " + manifest.BuiltUtc);
            }
            Console.WriteLine("  cache current   : " + (valid ? "yes" : "no"));
            Console.WriteLine("  app running     : " + (Installer.IsAppRunning(_appRoot) ? "yes" : "no"));
            Console.WriteLine("  mpv running     : " + (Installer.IsMpvRunning(_appRoot) ? "yes" : "no"));
            Console.WriteLine("  logon task      : " + (StartupTask.Exists(config.TaskName) ? "yes (" + config.TaskName + ")" : "no"));
            Console.WriteLine("  watchdog task   : " + (StartupTask.Exists(StartupTask.WatchdogName(config.TaskName)) ? "yes" : "no"));
            Console.WriteLine("  startup shortcut: " + (File.Exists(Shortcut.StartupShortcutPath(config.TaskName)) ? "yes" : "no"));
            Console.WriteLine("  lock-screen mode: " + config.LockScreenMode);
            Console.WriteLine("  lock-screen save: " + (string.IsNullOrEmpty(state.LockScreenBackup) ? "-" : state.LockScreenBackup));
            Console.WriteLine("  previous wallp. : " + (string.IsNullOrEmpty(state.WallpaperBackup) ? "-" : state.WallpaperBackup));
            return 0;
        }

        private static int InstallTask(string[] args)
        {
            // The account the logon task should belong to. The installer and
            // the first launch pass nothing (the current account is right); the
            // settings window passes the signed-in account explicitly, so a
            // standard user's task is not registered against the administrator
            // who approved the elevation prompt.
            string userId = args != null && args.Length > 1 ? args[1] : null;
            // Do not create config.json here: the wallpaper's first launch does
            // that and also prepares the user library and wallpaper backups.
            AppConfig config = ConfigStore.Load(_appRoot);
            string error;
            if (CreateTasks(config, userId, out error))
            {
                Console.WriteLine("logon and watchdog tasks created: " + config.TaskName);
                return 0;
            }

            // A task registered by an earlier elevated run keeps a permission
            // that a non-elevated rewrite may not touch, so on an update the
            // direct attempt above can fail with "Access is denied". Retry once
            // with an elevation prompt; the installer runs this entry as the
            // signed-in user, so the task still belongs to the right account.
            if (!Util.IsElevated())
            {
                Console.WriteLine("creating the logon and watchdog tasks (Windows will ask for permission)...");
                int result = StartupTask.CreateElevated(Installer.SetupExe(_appRoot), Paths.ProgramRoot, out error);
                if (result == 0)
                {
                    Console.WriteLine("logon and watchdog tasks created: " + config.TaskName);
                    return 0;
                }
                if (result == 2) error = "elevation was cancelled";
            }
            Console.WriteLine("ERROR: " + error);
            return 1;
        }

        // Creates both tasks for userId (or the current account when it is
        // null). Returns false with a message in error on the first failure.
        private static bool CreateTasks(AppConfig config, string userId, out string error)
        {
            bool ok = StartupTask.Create(_appRoot, config.TaskName, Installer.AppExe(_appRoot), out error, userId);
            return ok && StartupTask.CreateWatchdog(_appRoot, config.TaskName, Installer.AppExe(_appRoot), out error, userId);
        }

        private static int RemoveTask()
        {
            AppConfig config = ConfigStore.Load(_appRoot);
            string error;
            bool ok = StartupTask.Delete(config.TaskName, out error);
            bool watchdog = StartupTask.Delete(StartupTask.WatchdogName(config.TaskName), out error);
            if (ok && watchdog)
            {
                Console.WriteLine("logon and watchdog tasks removed: " + config.TaskName);
                return 0;
            }
            Console.WriteLine("ERROR: " + error);
            return 1;
        }

        private static int SetLockScreen(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: LiveBackgroundCli.exe set-lockscreen \"C:\\path\\image.jpg\"");
                return 1;
            }
            string image = args[1];
            if (!File.Exists(image))
            {
                Console.WriteLine("File not found: " + image);
                return 1;
            }
            string full = Path.GetFullPath(image);
            AppConfig config = ConfigStore.LoadOrCreate(_appRoot);
            config.LockScreenMode = "custom";
            config.LockScreenImage = full;
            ConfigStore.Save(_appRoot, config);

            string error;
            if (WinRtLockScreen.TrySet(full, out error))
            {
                Console.WriteLine("lock screen set to " + full);
                return 0;
            }
            Console.WriteLine("ERROR: " + error);
            return 1;
        }

        private static int RestoreLockScreen()
        {
            InstallState state = InstallState.Load(_appRoot);
            if (string.IsNullOrEmpty(state.LockScreenBackup) || !File.Exists(state.LockScreenBackup))
            {
                Console.WriteLine("No lock-screen backup was found.");
                return 1;
            }
            string error;
            if (WinRtLockScreen.TrySet(state.LockScreenBackup, out error))
            {
                Console.WriteLine("lock screen restored to " + state.LockScreenBackup);
                return 0;
            }
            Console.WriteLine("ERROR: " + error);
            return 1;
        }

        private static void Help()
        {
            Console.WriteLine("LiveBackground setup");
            Console.WriteLine();
            Console.WriteLine("  install                    install and start the wallpaper");
            Console.WriteLine("  uninstall [--remove]       stop and remove the wallpaper");
            Console.WriteLine("                             (--remove also clears the app's cache,");
            Console.WriteLine("                             logs and backups; your videos, pictures");
            Console.WriteLine("                             and settings are kept)");
            Console.WriteLine("  rebuild                    rebuild the seamless loop now");
            Console.WriteLine("  build-only                 build the loop without restarting");
            Console.WriteLine("  start                      start the wallpaper");
            Console.WriteLine("  stop                       stop the wallpaper");
            Console.WriteLine("  status                     show the current state");
            Console.WriteLine("  set-lockscreen <image>     use a specific lock-screen image");
            Console.WriteLine("  restore-lockscreen         restore the saved lock screen");
            Console.WriteLine("  install-task               create the logon task (elevated)");
            Console.WriteLine("  remove-task                remove the logon task (elevated)");
            Console.WriteLine();
        }

        private static void Banner(string text)
        {
            Console.WriteLine("==============================================");
            Console.WriteLine("  " + text);
            Console.WriteLine("==============================================");
            Console.WriteLine();
        }
    }
}
