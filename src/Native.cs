using System;
using System.Globalization;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
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

        // GetWindowLongPtrW / SetWindowLongPtrW do not exist on 32-bit Windows
        // (they are macros to the *W 32-bit calls there), so pick the right one
        // at runtime instead of throwing EntryPointNotFoundException.
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
        private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr GetWindowLong64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
        private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLong64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        public static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
        {
            return IntPtr.Size == 8 ? GetWindowLong64(hWnd, nIndex)
                : new IntPtr(GetWindowLong32(hWnd, nIndex));
        }

        public static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        {
            return IntPtr.Size == 8 ? SetWindowLong64(hWnd, nIndex, dwNewLong)
                : new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));
        }

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
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam,
            uint flags, uint timeout, out IntPtr result);

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int nIndex);

        public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, IntPtr lprcMonitor, IntPtr dwData);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
        }

        [DllImport("user32.dll")]
        public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

        // Display topology. QDC_ONLY_ACTIVE_PATHS counts the targets Windows is
        // actually driving, which is how Duplicate mode (two targets, one
        // desktop monitor) is told apart from Extended (two targets, two
        // monitors). SetDisplayConfig applies a topology without the app having
        // to build a full path/mode table.
        public const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;
        public const uint SDC_TOPOLOGY_CLONE = 0x00000002;
        public const uint SDC_TOPOLOGY_EXTEND = 0x00000004;
        public const uint SDC_APPLY = 0x00000080;
        public const uint SDC_ALLOW_CHANGES = 0x00000400;

        [DllImport("user32.dll")]
        public static extern int GetDisplayConfigBufferSizes(uint flags,
            out uint numPathArrayElements, out uint numModeInfoArrayElements);

        [DllImport("user32.dll")]
        public static extern int SetDisplayConfig(uint numPathArrayElements, IntPtr pathArray,
            uint numModeInfoArrayElements, IntPtr modeInfoArray, uint flags);

        [DllImport("user32.dll")]
        public static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

        [DllImport("user32.dll")]
        public static extern int MapWindowPoints(IntPtr hWndFrom, IntPtr hWndTo, ref RECT lpPoints, uint cPoints);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        // Top-level window enumeration. FindWindow only returns the first
        // taskbar; a multi-monitor desktop has one Shell_SecondaryTrayWnd per
        // extra screen and all of them have to be hidden or restored.
        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        // Last user input, used by the video screensaver's idle timer. dwTime
        // is a 32-bit tick count, so it is compared with Environment.TickCount.
        [StructLayout(LayoutKind.Sequential)]
        public struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern int SystemParametersInfo(uint uiAction, uint uiParam, string pvParam, uint fWinIni);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool LockWorkStation();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyIcon(IntPtr hIcon);

        // ---- job object: own the lifetime of the child processes ----

        [StructLayout(LayoutKind.Sequential)]
        public struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        public const int JobObjectExtendedLimitInformation = 9;
        public const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetInformationJobObject(IntPtr hJob, int jobObjectInfoClass,
            IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr hObject);

        // Creates a job whose children are killed when its last handle closes,
        // and puts this process in it. Every process the wallpaper starts after
        // this (the bundled mpv, ffmpeg) is therefore reaped by the OS when the
        // wallpaper exits, even on a hard kill. Returns the job handle, which
        // the caller must keep open for the life of the process (closing it
        // would kill us); IntPtr.Zero when the OS refuses, in which case the
        // caller simply carries on without it.
        public static IntPtr CreateKillOnCloseJob()
        {
            IntPtr job = IntPtr.Zero;
            IntPtr info = IntPtr.Zero;
            try
            {
                job = CreateJobObject(IntPtr.Zero, null);
                if (job == IntPtr.Zero) return IntPtr.Zero;

                JOBOBJECT_EXTENDED_LIMIT_INFORMATION limits = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                limits.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
                int length = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
                info = Marshal.AllocHGlobal(length);
                Marshal.StructureToPtr(limits, info, false);
                if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, info, (uint)length))
                {
                    CloseHandle(job);
                    return IntPtr.Zero;
                }
                if (!AssignProcessToJobObject(job, GetCurrentProcess()))
                {
                    CloseHandle(job);
                    return IntPtr.Zero;
                }
                return job;
            }
            catch
            {
                if (job != IntPtr.Zero) { try { CloseHandle(job); } catch { /* best-effort */ } }
                return IntPtr.Zero;
            }
            finally
            {
                if (info != IntPtr.Zero) Marshal.FreeHGlobal(info);
            }
        }
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

        public static IntPtr FindMpvWindow(IntPtr progman, uint processId)
        {
            foreach (IntPtr h in ChildrenInZOrder(progman))
            {
                if (ClassOf(h) != "mpv") continue;
                if (processId == 0) return h;
                uint owner;
                Native.GetWindowThreadProcessId(h, out owner);
                if (owner == processId) return h;
            }
            return IntPtr.Zero;
        }

        // Per-monitor DPI awareness keeps every coordinate in physical pixels,
        // which is what the per-screen placement math assumes. Windows 10 1703+
        // supports PMv2; older builds fall back to system awareness.
        public static void EnablePerMonitorDpi()
        {
            try
            {
                if (Native.SetProcessDpiAwarenessContext(new IntPtr(-4))) return;
            }
            catch { /* best-effort: fall through to the older DPI API below */ }
            try { Native.SetProcessDPIAware(); } catch { /* best-effort: DPI awareness is a nicety, not required */ }
        }

        // Sizes and moves a wallpaper window onto one physical monitor. The
        // bounds are screen coordinates; the window is a child of Progman, so
        // they are translated into Progman's client space first.
        public static void PlaceOnScreen(IntPtr progman, IntPtr hwnd, Rectangle bounds)
        {
            if (progman == IntPtr.Zero || hwnd == IntPtr.Zero) return;
            if (bounds.Width <= 0 || bounds.Height <= 0) return;

            Native.RECT current;
            if (Native.GetWindowRect(hwnd, out current))
            {
                bool matches = current.Left == bounds.Left && current.Top == bounds.Top &&
                    current.Right == bounds.Right && current.Bottom == bounds.Bottom;
                if (!matches)
                {
                    Native.RECT target;
                    target.Left = bounds.Left;
                    target.Top = bounds.Top;
                    target.Right = bounds.Right;
                    target.Bottom = bounds.Bottom;
                    Native.MapWindowPoints(IntPtr.Zero, progman, ref target, 2);
                    if (!Native.SetWindowPos(hwnd, IntPtr.Zero, target.Left, target.Top,
                            target.Right - target.Left, target.Bottom - target.Top,
                            Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER))
                    {
                        Log.Warn("could not move a wallpaper window onto its screen (error " +
                            Marshal.GetLastWin32Error() + ")");
                    }
                }
            }
            EnsurePlacement(progman, hwnd);
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
            bool ok = Native.SetWindowPos(hwnd, insertAfter, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);
            if (!ok)
            {
                Log.Warn("could not place the image layer below the icons (error " +
                    Marshal.GetLastWin32Error() + ")");
            }
            return ok;
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
            int iconsIndex = icons != IntPtr.Zero ? kids.IndexOf(icons) : -1;

            // The video only has to sit behind the desktop icons, above the
            // real wallpaper. With one player per screen two video windows
            // share that strip, so any index below the icons is already
            // correct. Insisting on the single slot directly under the icons
            // made the players push each other back and forth on every poll,
            // which churned the z-order and flooded the log.
            //
            // It must still stay in front of the wallpaper layer that paints
            // the desktop picture. Re-placing another player can push a video
            // window down behind that layer, which hides it completely: the
            // screen then shows the frozen desktop picture instead of the
            // video, while this check used to call the placement correct.
            IntPtr layer = FindWallpaperLayer(progman);
            int layerIndex = layer != IntPtr.Zero ? kids.IndexOf(layer) : -1;
            bool hiddenByLayer = layerIndex > iconsIndex && mpvIndex > layerIndex;
            if (mpvIndex > iconsIndex && !hiddenByLayer) return true;

            IntPtr insertAfter = icons != IntPtr.Zero ? icons : Native.HWND_TOP;
            bool moved = Native.SetWindowPos(mpvWnd, insertAfter, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);

            List<IntPtr> after = ChildrenInZOrder(progman);
            int nowIndex = after.IndexOf(mpvWnd);
            Log.Info(string.Format(CultureInfo.InvariantCulture, "placed video below the icons (was index {0}, now {1} of {2}){3}",
                mpvIndex, nowIndex, after.Count,
                moved ? "" : " (SetWindowPos failed, error " + Marshal.GetLastWin32Error() + ")"));
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

        public static bool SetDesktopWallpaper(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
            // The wallpaper style is deliberately left alone. The crossfade
            // layer reads the user's Windows fit mode instead of changing it.
            try
            {
                int ok = Native.SystemParametersInfo(Native.SPI_SETDESKWALLPAPER, 0, path,
                    Native.SPIF_UPDATEINIFILE | Native.SPIF_SENDCHANGE);
                if (ok == 0)
                {
                    Log.Warn("could not set the desktop wallpaper (error " +
                        Marshal.GetLastWin32Error() + ")");
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("could not set desktop wallpaper: " + ex.Message);
                return false;
            }
        }
    }

    // Hides the Windows shell chrome (taskbars and desktop icons) so the
    // wallpaper video is the only thing on screen. Nothing is terminated: the
    // taskbars and the desktop icon view are ordinary windows that ShowWindow
    // can put back instantly. Show() is deliberately idempotent and is also
    // called at startup and by the watchdog, so a hard kill while hidden can
    // never leave a user without a taskbar.
    public static class ShellChrome
    {
        private const string TaskbarClass = "Shell_TrayWnd";
        private const string SecondaryTaskbarClass = "Shell_SecondaryTrayWnd";

        private static void SetTaskbars(int show)
        {
            Native.EnumWindows(delegate(IntPtr handle, IntPtr lParam)
            {
                StringBuilder name = new StringBuilder(64);
                Native.GetClassName(handle, name, name.Capacity);
                string cls = name.ToString();
                if (string.Equals(cls, TaskbarClass, StringComparison.Ordinal) ||
                    string.Equals(cls, SecondaryTaskbarClass, StringComparison.Ordinal))
                {
                    Native.ShowWindow(handle, show);
                }
                return true;
            }, IntPtr.Zero);
        }

        private static void SetDesktopIcons(int show)
        {
            IntPtr progman = Native.FindWindow("Progman", null);
            if (progman == IntPtr.Zero) return;
            IntPtr icons = DesktopHost.FindDesktopIcons(progman);
            if (icons != IntPtr.Zero) Native.ShowWindow(icons, show);
        }

        public static void Hide()
        {
            SetTaskbars(Native.SW_HIDE);
            SetDesktopIcons(Native.SW_HIDE);
        }

        public static void Show()
        {
            SetTaskbars(Native.SW_SHOW);
            SetDesktopIcons(Native.SW_SHOW);
        }
    }
}
