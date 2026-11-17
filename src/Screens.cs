using System;
using System.Globalization;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;

namespace LiveBackground
{
    // One physical display as Windows reports it. Coordinates are physical
    // pixels because the process opts into per-monitor DPI awareness before it
    // touches any window, so monitors with different scaling never mix units.
    public sealed class ScreenInfo
    {
        public int Index;
        public string Device = "";
        public Rectangle Bounds;
        public bool Primary;

        public string Describe()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0} {1}x{2} at {3},{4}{5}",
                string.IsNullOrEmpty(Device) ? "screen" : Device,
                Bounds.Width, Bounds.Height, Bounds.X, Bounds.Y,
                Primary ? " (primary)" : "");
        }
    }

    // Display layout service. It exists for one reason: the wallpaper has to be
    // positioned per monitor, and the layout can change while the app runs.
    public static class Screens
    {
        // Ordered deterministically: primary first, then left to right.
        public static List<ScreenInfo> Enumerate()
        {
            List<ScreenInfo> list = new List<ScreenInfo>();
            try
            {
                Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
                    delegate(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data)
                    {
                        Native.MONITORINFOEX info = new Native.MONITORINFOEX();
                        info.cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFOEX>();
                        if (!Native.GetMonitorInfo(monitor, ref info)) return true;
                        ScreenInfo screen = new ScreenInfo();
                        screen.Device = info.szDevice ?? "";
                        screen.Bounds = Rectangle.FromLTRB(info.rcMonitor.Left, info.rcMonitor.Top,
                            info.rcMonitor.Right, info.rcMonitor.Bottom);
                        screen.Primary = (info.dwFlags & 1) != 0;
                        if (screen.Bounds.Width > 0 && screen.Bounds.Height > 0) list.Add(screen);
                        return true;
                    }, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                Log.Warn("could not enumerate the displays: " + ex.Message);
            }

            if (list.Count == 0)
            {
                // Never leave the wallpaper without a screen: fall back to the
                // primary metrics the old code used.
                ScreenInfo fallback = new ScreenInfo();
                fallback.Bounds = new Rectangle(0, 0,
                    Math.Max(1, Native.GetSystemMetrics(0)),
                    Math.Max(1, Native.GetSystemMetrics(1)));
                fallback.Primary = true;
                list.Add(fallback);
            }

            list.Sort(delegate(ScreenInfo a, ScreenInfo b)
            {
                if (a.Primary != b.Primary) return a.Primary ? -1 : 1;
                if (a.Bounds.X != b.Bounds.X) return a.Bounds.X.CompareTo(b.Bounds.X);
                return a.Bounds.Y.CompareTo(b.Bounds.Y);
            });
            for (int i = 0; i < list.Count; i++) list[i].Index = i;
            return list;
        }

        // Changes whenever a monitor is added, removed, moved or resized.
        public static string Signature(List<ScreenInfo> screens)
        {
            if (screens == null) return "";
            StringBuilder sb = new StringBuilder();
            foreach (ScreenInfo screen in screens)
            {
                sb.Append(screen.Device).Append(':')
                  .Append(screen.Bounds.X).Append(',').Append(screen.Bounds.Y).Append(',')
                  .Append(screen.Bounds.Width).Append('x').Append(screen.Bounds.Height)
                  .Append('|');
            }
            return sb.ToString();
        }

        // Windows "Duplicate these displays" drives two targets from one
        // desktop monitor, so EnumDisplayMonitors reports a single screen.
        // The display config still lists both active paths, which is the
        // reliable tell. The bounds check is a fallback for drivers that
        // surface both monitors with identical coordinates instead.
        public static bool IsDuplicate(List<ScreenInfo> screens)
        {
            int monitors = screens == null ? 0 : screens.Count;
            int paths = ActivePathCount();
            if (paths > 0 && paths > monitors) return true;

            if (screens != null)
            {
                for (int i = 0; i < screens.Count; i++)
                {
                    for (int j = i + 1; j < screens.Count; j++)
                    {
                        if (screens[i].Bounds == screens[j].Bounds) return true;
                    }
                }
            }
            return false;
        }

        private static int ActivePathCount()
        {
            try
            {
                uint paths;
                uint modes;
                int result = Native.GetDisplayConfigBufferSizes(
                    Native.QDC_ONLY_ACTIVE_PATHS, out paths, out modes);
                if (result == 0) return (int)paths;
                Log.Warn("GetDisplayConfigBufferSizes returned " + result);
            }
            catch (Exception ex)
            {
                Log.Warn("could not read the display config: " + ex.Message);
            }
            return 0;
        }

        // Switches Windows between Extended (true) and Duplicate (false).
        // DisplaySwitch.exe is the fallback for drivers that reject the
        // topology call. Never called automatically: the user chooses it.
        public static bool SetTopology(bool extend)
        {
            uint flags = (extend ? Native.SDC_TOPOLOGY_EXTEND : Native.SDC_TOPOLOGY_CLONE)
                | Native.SDC_APPLY;
            try
            {
                int result = Native.SetDisplayConfig(0, IntPtr.Zero, 0, IntPtr.Zero, flags);
                if (result == 0) return true;
                Log.Warn("SetDisplayConfig(" + (extend ? "extend" : "clone") +
                    ") returned " + result);
            }
            catch (Exception ex)
            {
                Log.Warn("SetDisplayConfig threw: " + ex.Message);
            }

            try
            {
                string exe = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System),
                    "DisplaySwitch.exe");
                if (File.Exists(exe))
                {
                    ProcessStartInfo info = new ProcessStartInfo(exe,
                        extend ? "/extend" : "/clone");
                    info.UseShellExecute = false;
                    info.CreateNoWindow = true;
                    Process.Start(info);
                    Log.Info("display mode changed with DisplaySwitch.exe");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.Warn("DisplaySwitch fallback failed: " + ex.Message);
            }
            return false;
        }
    }
}
