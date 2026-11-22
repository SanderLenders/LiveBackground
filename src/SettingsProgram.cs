using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace LiveBackground
{
    public static class SettingsProgram
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
            using (Mutex mutex = new Mutex(true, @"Local\LiveBackgroundSettings", out created))
            {
                if (!created)
                {
                    try
                    {
                        IntPtr hwnd = Native.FindWindow(null, "LiveBackground settings");
                        if (hwnd != IntPtr.Zero)
                        {
                            if (Native.IsIconic(hwnd)) Native.ShowWindow(hwnd, Native.SW_RESTORE);
                            Native.SetForegroundWindow(hwnd);
                        }
                    }
                    catch { /* best-effort: another instance is already open */ }
                    return 0;
                }

                try
                {
                    if (Application.Current == null)
                    {
                        new SettingsApp { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    }
                    SettingsWindow window = new SettingsWindow(appRoot);
                    window.Closed += delegate
                    {
                        try { Dispatcher.CurrentDispatcher.BeginInvokeShutdown(System.Windows.Threading.DispatcherPriority.Background); } catch { }
                    };
                    window.Show();
                    window.Activate();
                    Dispatcher.Run();
                }
                catch (Exception ex)
                {
                    try { Log.Error("settings window failed", ex); } catch { }
                    return 1;
                }
            }
            return 0;
        }
    }
}
