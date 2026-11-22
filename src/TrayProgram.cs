using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace LiveBackground
{
    public static class TrayProgram
    {
        [STAThread]
        public static int Main(string[] args)
        {
            Util.UseEnglishUiCulture();
            string programRoot = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            Paths.Init(programRoot);
            string appRoot = Paths.DataRoot;
            Log.Init(appRoot);

            bool created;
            using (Mutex mutex = new Mutex(true, @"Local\LiveBackgroundTray", out created))
            {
                if (!created) return 0;
                try
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                    Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
                    {
                        try { Log.Error("unhandled error in tray ui", e.Exception); } catch { }
                    };
                    using (TrayApp tray = new TrayApp(appRoot))
                    {
                        Application.Run(tray);
                    }
                }
                catch (Exception ex)
                {
                    try { Log.Error("tray failed", ex); } catch { }
                    return 1;
                }
            }
            return 0;
        }
    }
}
