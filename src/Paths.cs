using System;
using System.IO;

namespace LiveBackground
{
    // The program lives in one folder and the user's library in another. A
    // portable copy (marked with portable.txt) keeps everything together; an
    // installed copy under Program Files keeps the program read-only and puts
    // config, cache, logs, videos, images and backups in the user's profile.
    public static class Paths
    {
        public static string ProgramRoot { get; private set; }
        public static string DataRoot { get; private set; }

        public static void Init(string programRoot)
        {
            ProgramRoot = Path.GetFullPath(programRoot);
            if (File.Exists(Path.Combine(ProgramRoot, "portable.txt")))
            {
                DataRoot = ProgramRoot;
                return;
            }

            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (IsUnder(ProgramRoot, programFiles) || IsUnder(ProgramRoot, programFilesX86))
            {
                DataRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "LiveBackground");
            }
            else
            {
                DataRoot = ProgramRoot;
            }
        }

        // Tool paths (mpv, ffmpeg) are part of the program and resolve against
        // the program folder. User content resolves against the data folder.
        public static string Tool(string relative)
        {
            return Util.ResolvePath(ProgramRoot, relative);
        }

        public static string Data(string relative)
        {
            return Util.ResolvePath(DataRoot, relative);
        }

        public static bool IsPortable
        {
            get
            {
                return !string.IsNullOrEmpty(ProgramRoot) &&
                    File.Exists(Path.Combine(ProgramRoot, "portable.txt"));
            }
        }

        // A portable copy keeps the video library inside its own folder too.
        // Only the untouched default is redirected; a folder the user chose
        // explicitly is left alone.
        public static void ApplyDefaults(AppConfig config)
        {
            if (config == null || !IsPortable) return;
            if (string.IsNullOrEmpty(config.VideoFolder) ||
                string.Equals(config.VideoFolder, @"%USERPROFILE%\LiveBackground\Videos",
                    StringComparison.OrdinalIgnoreCase))
            {
                config.VideoFolder = Path.Combine(ProgramRoot, "videos");
            }
            if (string.IsNullOrEmpty(config.SecondScreenFolder) ||
                string.Equals(config.SecondScreenFolder, @"%USERPROFILE%\LiveBackground\Videos",
                    StringComparison.OrdinalIgnoreCase))
            {
                config.SecondScreenFolder = Path.Combine(ProgramRoot, "videos");
            }
            if (config.ExtraScreens != null)
            {
                foreach (ScreenSettings screen in config.ExtraScreens)
                {
                    if (screen == null) continue;
                    if (string.IsNullOrEmpty(screen.Folder) ||
                        string.Equals(screen.Folder, @"%USERPROFILE%\LiveBackground\Videos",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        screen.Folder = Path.Combine(ProgramRoot, "videos");
                    }
                    if (string.IsNullOrEmpty(screen.ImageFolder) ||
                        string.Equals(screen.ImageFolder, @"%USERPROFILE%\LiveBackground\images\wallpapers",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        screen.ImageFolder = Path.Combine(ProgramRoot, "images", "wallpapers");
                    }
                }
            }
            if (string.IsNullOrEmpty(config.ImageFolder) ||
                string.Equals(config.ImageFolder, @"%USERPROFILE%\LiveBackground\images\wallpapers",
                    StringComparison.OrdinalIgnoreCase))
            {
                config.ImageFolder = Path.Combine(ProgramRoot, "images", "wallpapers");
            }
        }

        private static bool IsUnder(string path, string root)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(root)) return false;
            string normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            string normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
        }
    }
}
