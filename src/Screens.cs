using System;
using System.Collections.Generic;
using System.Drawing;
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
            return string.Format("{0} {1}x{2} at {3},{4}{5}",
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
                        info.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.MONITORINFOEX));
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

        // The picker in the settings window names screens by this text, so the
        // UI and the log always agree on "Screen 1" and "Screen 2".
        public static string Label(ScreenInfo screen)
        {
            if (screen == null) return "";
            return "Screen " + (screen.Index + 1) + " (" + screen.Bounds.Width + "x" +
                screen.Bounds.Height + ")";
        }
    }
}
