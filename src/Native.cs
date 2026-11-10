using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace LiveBackground
{
    public static class Native
    {
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_NOOWNERZORDER = 0x0200;
        public const uint GW_CHILD = 5;
        public const uint GW_HWNDNEXT = 2;
        public static readonly IntPtr HWND_TOP = IntPtr.Zero;

        public const uint WM_SPAWN_WORKER = 0x052C;
        public const uint SMTO_NORMAL = 0x0000;

        public const uint SPI_SETDESKWALLPAPER = 0x0014;
        public const uint SPIF_UPDATEINIFILE = 0x01;
        public const uint SPIF_SENDCHANGE = 0x02;

        public const int GWL_STYLE = -16;
        public const int GWL_EXSTYLE = -20;
        public const int WS_CHILD = 0x40000000;
        public const int WS_POPUP = unchecked((int)0x80000000);
        public const int WS_CAPTION = 0x00C00000;
        public const int WS_THICKFRAME = 0x00040000;
        public const int SW_HIDE = 0;
        public const int SW_SHOW = 5;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam,
            uint flags, uint timeout, out IntPtr result);

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int SystemParametersInfo(uint uiAction, uint uiParam, string pvParam, uint fWinIni);

        [DllImport("shell32.dll")]
        public static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyIcon(IntPtr hIcon);
    }

    public enum WallpaperFit
    {
        Fill,
        Fit,
        Stretch,
        Center,
        Tile,
        Span
    }

    // Finds the desktop layers and keeps the video window between the wallpaper
    // and the icons. This is the logic that was proven in the original launcher.
    public static class DesktopHost
    {
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static readonly object SpawnSync = new object();
        private static long _lastSpawnAttempt = -10000;

        public static IntPtr WaitForProgman(int timeoutMs)
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                IntPtr progman = Native.FindWindow("Progman", null);
                if (progman != IntPtr.Zero) return progman;
                // Poll quickly: at logon Progman usually appears within a
                // second, and a 100 ms poll can add a visible delay.
                Thread.Sleep(25);
            }
            return IntPtr.Zero;
        }

        private static string ClassOf(IntPtr hWnd)
        {
            StringBuilder sb = new StringBuilder(256);
            Native.GetClassName(hWnd, sb, 256);
            return sb.ToString();
        }

        private static bool CoversScreen(IntPtr hWnd)
        {
            Native.RECT r;
            if (!Native.GetWindowRect(hWnd, out r)) return false;
            return (r.Right - r.Left) >= Native.GetSystemMetrics(0) * 0.9
                && (r.Bottom - r.Top) >= Native.GetSystemMetrics(1) * 0.9;
        }

        private static List<IntPtr> ChildrenInZOrder(IntPtr parent)
        {
            List<IntPtr> list = new List<IntPtr>();
            IntPtr child = Native.GetWindow(parent, Native.GW_CHILD);
            int guard = 0;
            while (child != IntPtr.Zero && guard++ < 256)
            {
                list.Add(child);
                child = Native.GetWindow(child, Native.GW_HWNDNEXT);
            }
            return list;
        }

        public static IntPtr FindMpvWindow(IntPtr progman)
        {
            foreach (IntPtr h in ChildrenInZOrder(progman))
            {
                if (ClassOf(h) == "mpv") return h;
            }
            return IntPtr.Zero;
        }

        public static IntPtr FindWallpaperLayer(IntPtr progman)
        {
            foreach (IntPtr h in ChildrenInZOrder(progman))
            {
                if (ClassOf(h) == "WorkerW" && Native.IsWindowVisible(h) && CoversScreen(h)) return h;
            }
            return IntPtr.Zero;
        }

        public static IntPtr FindDesktopIcons(IntPtr progman)
        {
            foreach (IntPtr h in ChildrenInZOrder(progman))
            {
                if (ClassOf(h) == "SHELLDLL_DefView") return h;
            }
            return IntPtr.Zero;
        }

        // Keeps an app-owned wallpaper window directly below the desktop icons,
        // above the real wallpaper. Used by the crossfade image layer.
        public static bool PlaceBelowIcons(IntPtr progman, IntPtr hwnd)
        {
            if (progman == IntPtr.Zero || hwnd == IntPtr.Zero) return false;
            IntPtr icons = FindDesktopIcons(progman);
            IntPtr insertAfter = icons != IntPtr.Zero ? icons : Native.HWND_TOP;
            return Native.SetWindowPos(hwnd, insertAfter, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);
        }

        // Reads how Windows is scaling the desktop wallpaper. The value is used
        // only to draw the app-owned image layer the same way; these registry
        // values are never modified.
        public static WallpaperFit ReadWallpaperFit()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", false))
                {
                    if (key != null)
                    {
                        string style = key.GetValue("WallpaperStyle") as string ?? "";
                        string tile = key.GetValue("TileWallpaper") as string ?? "";
                        if (tile == "1") return WallpaperFit.Tile;
                        if (style == "22") return WallpaperFit.Span;
                        if (style == "10") return WallpaperFit.Fill;
                        if (style == "6") return WallpaperFit.Fit;
                        if (style == "2") return WallpaperFit.Stretch;
                        if (style == "0") return WallpaperFit.Center;
                    }
                }
            }
            catch (Exception ex) { Log.Warn("could not read the wallpaper fit mode: " + ex.Message); }
            return WallpaperFit.Fill;
        }

        public static bool EnsureWallpaperLayer(IntPtr progman)
        {
            // The startup path warms this layer on a worker thread while mpv
            // loads. Never block the watchdog if that warmup is in progress.
            if (!Monitor.TryEnter(SpawnSync)) return false;
            try
            {
                if (FindWallpaperLayer(progman) != IntPtr.Zero) return true;
                if (Clock.ElapsedMilliseconds - _lastSpawnAttempt < 2000) return false;
                _lastSpawnAttempt = Clock.ElapsedMilliseconds;

                IntPtr result;
                Native.SendMessageTimeout(progman, Native.WM_SPAWN_WORKER, IntPtr.Zero, IntPtr.Zero,
                    Native.SMTO_NORMAL, 2000, out result);

                for (int i = 0; i < 10; i++)
                {
                    if (FindWallpaperLayer(progman) != IntPtr.Zero)
                    {
                        Log.Info("created the wallpaper layer");
                        return true;
                    }
                    Thread.Sleep(50);
                }
                Log.Warn("wallpaper layer still missing");
                return false;
            }
            finally
            {
                Monitor.Exit(SpawnSync);
            }
        }

        // Returns true only when the video window is really part of the desktop
        // layer, so the startup log no longer claims "ready" before placement.
        public static bool EnsurePlacement(IntPtr progman, IntPtr mpvWnd)
        {
            List<IntPtr> kids = ChildrenInZOrder(progman);
            int mpvIndex = kids.IndexOf(mpvWnd);
            if (mpvIndex < 0) return false;

            IntPtr icons = FindDesktopIcons(progman);
            int desiredIndex;
            IntPtr insertAfter;

            if (icons != IntPtr.Zero)
            {
                desiredIndex = kids.IndexOf(icons) + 1;
                insertAfter = icons;
            }
            else
            {
                desiredIndex = 0;
                insertAfter = Native.HWND_TOP;
            }

            if (mpvIndex == desiredIndex) return true;

            Native.SetWindowPos(mpvWnd, insertAfter, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);

            List<IntPtr> after = ChildrenInZOrder(progman);
            int nowIndex = after.IndexOf(mpvWnd);
            Log.Info(string.Format("placed video below the icons (was index {0}, now {1} of {2})",
                mpvIndex, nowIndex, after.Count));
            return nowIndex >= 0;
        }

        // Chooses the still image for the desktop: a custom picture when the
        // user picked one, otherwise the first frame of the loop. Returns ""
        // when the desktop wallpaper is switched off or nothing is available.
        public static string WallpaperFor(string appRoot, AppConfig config, string framePath)
        {
            if (config == null || !config.DesktopWallpaper) return "";

            if (string.Equals(config.DesktopWallpaperMode, "custom", StringComparison.OrdinalIgnoreCase))
            {
                string custom = Util.ResolvePath(appRoot, config.DesktopWallpaperImage);
                if (!string.IsNullOrEmpty(config.DesktopWallpaperImage) && File.Exists(custom)) return custom;
                Log.Warn("custom desktop wallpaper is missing, using the loop frame instead");
            }

            if (!string.IsNullOrEmpty(framePath) && File.Exists(framePath)) return framePath;
            return "";
        }

        public static void SetDesktopWallpaper(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            // The wallpaper style is deliberately left alone. The crossfade
            // layer reads the user's Windows fit mode instead of changing it.
            try
            {
                Native.SystemParametersInfo(Native.SPI_SETDESKWALLPAPER, 0, path,
                    Native.SPIF_UPDATEINIFILE | Native.SPIF_SENDCHANGE);
            }
            catch (Exception ex) { Log.Warn("could not set desktop wallpaper: " + ex.Message); }
        }
    }
}
