using System;
using System.Collections.Generic;

namespace LiveBackground
{
    // The window-free core behind the four checkable media lists in the
    // settings window (main Videos, main Images and the two per-screen
    // editors). The lists look alike but each remembers a different default
    // for files that are no longer in the folder:
    //
    //   * video clips that have gone missing stay unticked, so a deleted clip
    //     never silently rejoins the show;
    //   * pictures that have gone missing stay ticked, because the picture
    //     folder is a library the user curates elsewhere.
    //
    // Keeping the arithmetic here means the four call sites share one tested
    // implementation instead of four hand-copied ones. Nothing in this class
    // touches a window, so the whole of it runs under unit test.
    public static class PlaylistState
    {
        // 0, 1, 2, ... count - 1: the "no change" order that a sort or a move
        // starts from.
        public static int[] IdentityOrder(int count)
        {
            if (count <= 0) return Array.Empty<int>();
            int[] order = new int[count];
            for (int i = 0; i < count; i++) order[i] = i;
            return order;
        }

        // The row order that "Sort A-Z" produces: the natural order used
        // everywhere else in the app, so "clip 2" sorts before "clip 10".
        public static int[] SortedOrder(IList<string> names)
        {
            int[] order = IdentityOrder(names == null ? 0 : names.Count);
            if (order.Length > 1)
            {
                Array.Sort(order, delegate(int a, int b)
                {
                    return NaturalComparer.Instance.Compare(names[a], names[b]);
                });
            }
            return order;
        }

        // The order that moves row "index" by "delta" places, or null when the
        // move would leave the list.
        public static int[] SwapOrder(int count, int index, int delta)
        {
            int target = index + delta;
            if (index < 0 || index >= count || target < 0 || target >= count) return null;
            int[] order = IdentityOrder(count);
            order[index] = target;
            order[target] = index;
            return order;
        }

        // Reads the list back into playlist entries, in the order shown.
        public static List<PlaylistEntry> Capture(IList<string> names, IList<bool> checkedFlags)
        {
            List<PlaylistEntry> entries = new List<PlaylistEntry>();
            if (names == null) return entries;
            for (int i = 0; i < names.Count; i++)
            {
                bool on = checkedFlags != null && i < checkedFlags.Count && checkedFlags[i];
                entries.Add(new PlaylistEntry(names[i], on));
            }
            return entries;
        }

        // The ticks to show in playlist mode. The playlist wins by name (the
        // first entry carrying a name counts); anything not named there falls
        // back to "missingDefault".
        public static bool[] PlaylistTicks(IList<string> names, IEnumerable<PlaylistEntry> playlist,
            bool missingDefault)
        {
            Dictionary<string, bool> enabled =
                new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            if (playlist != null)
            {
                foreach (PlaylistEntry entry in playlist)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.Name)) continue;
                    if (!enabled.ContainsKey(entry.Name)) enabled[entry.Name] = entry.Enabled;
                }
            }

            bool[] ticks = new bool[names == null ? 0 : names.Count];
            for (int i = 0; i < ticks.Length; i++)
            {
                bool on;
                ticks[i] = enabled.TryGetValue(names[i], out on) ? on : missingDefault;
            }
            return ticks;
        }

        // The first ticked row, or -1 when nothing is ticked.
        public static int CheckedIndex(IList<bool> checkedFlags)
        {
            if (checkedFlags == null) return -1;
            for (int i = 0; i < checkedFlags.Count; i++)
            {
                if (checkedFlags[i]) return i;
            }
            return -1;
        }

        // True when at least one entry is present and ticked.
        public static bool HasTicked(IEnumerable<PlaylistEntry> playlist)
        {
            if (playlist == null) return false;
            foreach (PlaylistEntry entry in playlist)
            {
                if (entry != null && entry.Enabled) return true;
            }
            return false;
        }

        // The row to tick in single mode: the remembered name (matched without
        // regard to case), or the first row when that name is gone.
        public static int PreferredIndex(IList<string> names, string preferred)
        {
            if (names == null || names.Count == 0) return -1;
            if (!string.IsNullOrEmpty(preferred))
            {
                for (int i = 0; i < names.Count; i++)
                {
                    if (string.Equals(names[i], preferred, StringComparison.OrdinalIgnoreCase)) return i;
                }
            }
            return 0;
        }

        // Exactly one ticked row, so single mode reads like radio buttons.
        public static bool[] SingleTicks(int count, int index)
        {
            bool[] ticks = new bool[count < 0 ? 0 : count];
            if (index >= 0 && index < ticks.Length) ticks[index] = true;
            return ticks;
        }

        // A defensive copy of playlist entries; null entries are dropped.
        public static List<PlaylistEntry> CopyEntries(IEnumerable<PlaylistEntry> entries)
        {
            List<PlaylistEntry> copy = new List<PlaylistEntry>();
            if (entries == null) return copy;
            foreach (PlaylistEntry entry in entries)
            {
                if (entry != null) copy.Add(new PlaylistEntry(entry.Name, entry.Enabled));
            }
            return copy;
        }

        // Reorders "source" into the row order "order". A row whose source slot
        // is missing falls back to "fallback" rather than throwing.
        public static List<T> Reorder<T>(IList<T> source, int[] order, T fallback)
        {
            List<T> result = new List<T>();
            if (order == null) return result;
            for (int i = 0; i < order.Length; i++)
            {
                int old = order[i];
                result.Add(source != null && old >= 0 && old < source.Count ? source[old] : fallback);
            }
            return result;
        }
    }
}
