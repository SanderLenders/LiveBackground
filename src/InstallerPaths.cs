using System.IO;

namespace LiveBackground
{
    // Path containment used by uninstall. A directory the user pointed at
    // outside the app's own tree must never be offered for deletion, even when
    // it happens to share a name prefix with the app folder.
    public static class InstallerPaths
    {
        // True when path is a descendant of root (not root itself).
        public static bool IsInside(string root, string path)
        {
            if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(path)) return false;
            string r;
            string p;
            try { r = Path.GetFullPath(root); p = Path.GetFullPath(path); }
            catch { return false; }
            if (r.Length == 0) return false;
            // A drive root is its own prefix once a separator is appended, so
            // reject equality first: only descendants of root count as inside.
            if (string.Equals(r.TrimEnd(Path.DirectorySeparatorChar),
                p.TrimEnd(Path.DirectorySeparatorChar), System.StringComparison.OrdinalIgnoreCase)) return false;
            if (r[r.Length - 1] != Path.DirectorySeparatorChar) r += Path.DirectorySeparatorChar;
            return p.StartsWith(r, System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
