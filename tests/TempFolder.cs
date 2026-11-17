using System;
using System.IO;

namespace LiveBackground.Tests
{
    // A throw-away folder under the user's temp directory, removed when the
    // test finishes. Used by the tests that need real files (video scanning,
    // config round-trips).
    public sealed class TempFolder : IDisposable
    {
        public string Dir { get; private set; }

        public TempFolder()
        {
            Dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "lb-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Dir);
        }

        public string Write(string name, string content)
        {
            string full = System.IO.Path.Combine(Dir, name);
            File.WriteAllText(full, content);
            return full;
        }

        public void Dispose()
        {
            try { Directory.Delete(Dir, true); }
            catch { /* best-effort: a leftover temp folder is harmless */ }
        }
    }
}
