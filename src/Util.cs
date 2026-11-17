using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace LiveBackground
{
    public static class Util
    {
        public static string ResolvePath(string appRoot, string path)
        {
            // A missing root (for example when a caller runs before Paths.Init
            // has set the program folder) must never crash Path.Combine; fall
            // back to where this executable lives.
            if (string.IsNullOrEmpty(appRoot))
                appRoot = AppDomain.CurrentDomain.BaseDirectory;
            if (string.IsNullOrEmpty(path)) return appRoot;
            string expanded = Environment.ExpandEnvironmentVariables(path);
            if (!Path.IsPathRooted(expanded)) expanded = Path.Combine(appRoot, expanded);
            return Path.GetFullPath(expanded);
        }

        // Windows command-line quoting. A backslash that precedes a quote has to
        // be doubled, and a run of backslashes before the closing quote must be
        // doubled too, otherwise the closing quote is escaped away. This is what
        // CommandLineToArgvW (and every program that uses it) expects.
        public static string Quote(string value)
        {
            if (string.IsNullOrEmpty(value)) return "\"\"";
            StringBuilder sb = new StringBuilder(value.Length + 2);
            sb.Append('"');
            int backslashes = 0;
            foreach (char c in value)
            {
                if (c == '\\')
                {
                    backslashes++;
                    continue;
                }
                if (c == '"')
                {
                    sb.Append('\\', backslashes * 2 + 1);
                    sb.Append('"');
                    backslashes = 0;
                    continue;
                }
                if (backslashes > 0)
                {
                    sb.Append('\\', backslashes);
                    backslashes = 0;
                }
                sb.Append(c);
            }
            sb.Append('\\', backslashes * 2);
            sb.Append('"');
            return sb.ToString();
        }

        public static string CurrentUser()
        {
            try { return WindowsIdentity.GetCurrent().Name; }
            catch { return Environment.UserDomainName + "\\" + Environment.UserName; }
        }

        // True when this process runs with an administrator token. Used to
        // decide whether a machine-wide scheduled task can be written directly
        // or needs an elevation prompt first.
        public static bool IsElevated()
        {
            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch { return false; }
        }

        // The app and its text are English by design, so pin the user-interface
        // culture to English as well. This only affects the language of the
        // messages the .NET Framework itself produces (for example the text of
        // an exception we show after our own English sentence); all number and
        // date formatting already uses the invariant culture. Called at the top
        // of both entry points, before any other thread starts, so every thread
        // inherits it. Windows' own dialogs and button labels are drawn by the
        // operating system and are not affected by this.
        public static void UseEnglishUiCulture()
        {
            try
            {
                CultureInfo english = CultureInfo.GetCultureInfo("en-US");
                Thread.CurrentThread.CurrentUICulture = english;
                CultureInfo.DefaultThreadCurrentUICulture = english;
            }
            catch { /* best-effort: fall back to the system language */ }
        }

        public static void SafeDelete(string path)
        {
            try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); }
            catch { /* best-effort delete; a locked file is left for later */ }
        }

        public static string DetectImageExtension(string path)
        {
            try
            {
                byte[] header = new byte[8];
                using (FileStream fs = File.OpenRead(path))
                {
                    int read = fs.Read(header, 0, header.Length);
                    if (read >= 2 && header[0] == 0x42 && header[1] == 0x4D) return ".bmp";
                    if (read >= 4 && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47) return ".png";
                    if (read >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF) return ".jpg";
                    if (read >= 3 && header[0] == 0x47 && header[1] == 0x49 && header[2] == 0x46) return ".gif";
                }
            }
            catch { /* best-effort header read; an unknown file reads as .img */ }
            return ".img";
        }
    }

    public class NaturalComparer : IComparer<string>
    {
        // The comparer holds no state, so one shared instance replaces the
        // fresh allocation that the sort delegates used to make per comparison.
        public static readonly NaturalComparer Instance = new NaturalComparer();

        public int Compare(string x, string y)
        {
            if (x == null) return y == null ? 0 : -1;
            if (y == null) return 1;
            int i = 0;
            int j = 0;
            while (i < x.Length && j < y.Length)
            {
                if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
                {
                    int si = i;
                    int sj = j;
                    while (i < x.Length && char.IsDigit(x[i])) i++;
                    while (j < y.Length && char.IsDigit(y[j])) j++;
                    string nx = x.Substring(si, i - si).TrimStart('0');
                    string ny = y.Substring(sj, j - sj).TrimStart('0');
                    if (nx.Length == 0) nx = "0";
                    if (ny.Length == 0) ny = "0";
                    if (nx.Length != ny.Length) return nx.Length.CompareTo(ny.Length);
                    int cmp = string.Compare(nx, ny, StringComparison.OrdinalIgnoreCase);
                    if (cmp != 0) return cmp;
                }
                else
                {
                    int cmp = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
                    if (cmp != 0) return cmp;
                    i++;
                    j++;
                }
            }
            return (x.Length - i).CompareTo(y.Length - j);
        }
    }
}
