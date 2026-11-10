using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Principal;
using System.Text;

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

        public static string Quote(string value)
        {
            if (value == null) value = "";
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        public static bool IsAdmin()
        {
            try
            {
                WindowsIdentity identity = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        public static string CurrentUser()
        {
            try { return WindowsIdentity.GetCurrent().Name; }
            catch { return Environment.UserDomainName + "\\" + Environment.UserName; }
        }

        public static string FormatDuration(double seconds)
        {
            if (seconds < 0) seconds = 0;
            int total = (int)Math.Round(seconds);
            return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", total / 60, total % 60);
        }

        public static void CopyDirectory(string source, string destination, Action<string> progress)
        {
            if (!Directory.Exists(source)) return;
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.GetFiles(source))
            {
                string target = Path.Combine(destination, Path.GetFileName(file));
                if (progress != null) progress("copy " + Path.GetFileName(file));
                File.Copy(file, target, true);
            }
            foreach (string dir in Directory.GetDirectories(source))
            {
                CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)), progress);
            }
        }

        public static void SafeDelete(string path)
        {
            try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); }
            catch { }
        }

        public static void SafeKill(Process process)
        {
            try
            {
                if (process != null && !process.HasExited) process.Kill();
            }
            catch { }
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
            catch { }
            return ".img";
        }
    }

    public class NaturalComparer : IComparer<string>
    {
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
