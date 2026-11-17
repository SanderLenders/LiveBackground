using System;
using System.Collections.Generic;
using System.Text;

namespace LiveBackground
{
    // Window-free names for the log and the progress card, so the wording for a
    // screen always matches the settings window. Nothing here touches a window
    // or any running state, so the whole of it runs under unit test.
    public static class ScreenText
    {
        // "second screen" / "third screen" for the log and the progress card,
        // matching the names the settings window uses.
        public static string LabelForIndex(int index)
        {
            if (index == 1) return "second screen";
            if (index == 2) return "third screen";
            return "screen " + (index + 1);
        }

        // A stable log file name per monitor: "mpv-<device>.log" with anything
        // that is not a letter or digit turned into an underscore.
        public static string LogName(string device)
        {
            StringBuilder sb = new StringBuilder("mpv-");
            if (!string.IsNullOrEmpty(device))
            {
                foreach (char c in device)
                {
                    sb.Append(char.IsLetterOrDigit(c) ? c : '_');
                }
            }
            sb.Append(".log");
            return sb.ToString();
        }

        // A comma-separated description of every connected screen. The caller
        // passes whether Windows is duplicating the desktop, so this stays free
        // of any native display call and can be tested on its own.
        public static string DescribeAll(List<ScreenInfo> screens, bool windowsIsDuplicating)
        {
            if (screens == null || screens.Count == 0) return "none";
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < screens.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(screens[i].Describe());
            }
            if (screens.Count > 3) sb.Append(" (only the first three are used)");
            if (windowsIsDuplicating) sb.Append(" (Windows is duplicating)");
            return sb.ToString();
        }
    }
}
