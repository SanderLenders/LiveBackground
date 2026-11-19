using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace LiveBackground
{
    // Owns the notification-area icon. The wallpaper itself is a console-less
    // process with no window, so the tray icon is its only visible handle.
    public class TrayApp : ApplicationContext
    {
        private readonly string _appRoot;
        private readonly Control _invoker;
        private NotifyIcon _icon;
        private ContextMenuStrip _menu;
        private ToolStripMenuItem _pauseItem;
        private readonly object _settingsSync = new object();
        private System.Windows.Threading.Dispatcher _settingsDispatcher;
        private BuildProgressForm _progress;
        private bool _closed;

        public TrayApp(string appRoot)
        {
            _appRoot = appRoot;

            // A control whose handle lives on this (the tray) thread. Messages
            // posted to it are pumped by Application.Run, so callbacks from the
            // wallpaper thread always run here instead of inline on that thread.
            _invoker = new Control();
            IntPtr forceHandle = _invoker.Handle;

            _menu = new ContextMenuStrip();
            _menu.ShowImageMargin = false;

            ToolStripMenuItem title = new ToolStripMenuItem("LiveBackground");
            title.Enabled = false;
            _menu.Items.Add(title);
            _menu.Items.Add(new ToolStripSeparator());

            _pauseItem = new ToolStripMenuItem("Pause wallpaper");
            _pauseItem.Click += delegate { TogglePause(); };
            _menu.Items.Add(_pauseItem);

            ToolStripMenuItem rebuild = new ToolStripMenuItem("Rebuild now");
            rebuild.Click += delegate { Program.RequestRebuild(); };
            _menu.Items.Add(rebuild);

            ToolStripMenuItem settings = new ToolStripMenuItem("Settings...");
            settings.Click += delegate { ShowSettings(); };
            _menu.Items.Add(settings);

            _menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem videos = new ToolStripMenuItem("Open video folder");
            videos.Click += delegate { OpenFolder(Util.ResolvePath(_appRoot, Program.Config.VideoFolder)); };
            _menu.Items.Add(videos);

            ToolStripMenuItem images = new ToolStripMenuItem("Open image folder");
            images.Click += delegate { OpenFolder(Util.ResolvePath(_appRoot, Program.Config.ImageFolder)); };
            _menu.Items.Add(images);

            ToolStripMenuItem logs = new ToolStripMenuItem("Open logs folder");
            logs.Click += delegate { OpenFolder(Path.Combine(_appRoot, "logs")); };
            _menu.Items.Add(logs);

            _menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem exit = new ToolStripMenuItem("Exit");
            exit.Click += delegate { Program.RequestExit(); };
            _menu.Items.Add(exit);

            _icon = new NotifyIcon();
            _icon.Icon = CreateIcon();
            _icon.Text = "LiveBackground";
            _icon.ContextMenuStrip = _menu;
            _icon.DoubleClick += delegate { ShowSettings(); };
            _icon.Visible = true;

            RefreshMenu();
        }

        public void RefreshMenu()
        {
            Post(delegate
            {
                if (_closed) return;
                _pauseItem.Text = Program.IsPaused ? "Resume wallpaper" : "Pause wallpaper";
            });
        }

        public void ShowSettings()
        {
            Post(delegate
            {
                if (_closed) return;
                lock (_settingsSync)
                {
                    if (_settingsDispatcher != null)
                    {
                        try
                        {
                            _settingsDispatcher.BeginInvoke(new Action(delegate
                            {
                                try
                                {
                                    if (System.Windows.Application.Current != null)
                                    {
                                        foreach (System.Windows.Window w in System.Windows.Application.Current.Windows)
                                        {
                                            if (w is SettingsWindow sw)
                                            {
                                                if (sw.WindowState == System.Windows.WindowState.Minimized)
                                                    sw.WindowState = System.Windows.WindowState.Normal;
                                                sw.Activate();
                                                sw.Topmost = true;
                                                sw.Topmost = false;
                                                sw.Focus();
                                                return;
                                            }
                                        }
                                    }
                                }
                                catch { }
                            }));
                            return;
                        }
                        catch
                        {
                            _settingsDispatcher = null;
                        }
                    }

                    Thread thread = new Thread(delegate()
                    {
                        try
                        {
                            if (System.Windows.Application.Current == null)
                            {
                                new SettingsApp { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
                            }
                            SettingsWindow window = new SettingsWindow(_appRoot);
                            window.Closed += delegate
                            {
                                lock (_settingsSync) { _settingsDispatcher = null; }
                                try { System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvokeShutdown(System.Windows.Threading.DispatcherPriority.Background); } catch { }
                            };
                            lock (_settingsSync) { _settingsDispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher; }
                            window.Show();
                            window.Activate();
                            System.Windows.Threading.Dispatcher.Run();
                        }
                        catch (Exception ex)
                        {
                            try { Log.Warn("could not open settings window: " + ex.Message); } catch { }
                            lock (_settingsSync) { _settingsDispatcher = null; }
                        }
                    });
                    thread.IsBackground = true;
                    thread.SetApartmentState(ApartmentState.STA);
                    thread.Name = "livebackground-settings";
                    thread.Start();
                }
            });
        }

        public void Notify(string title, string text)
        {
            Post(delegate
            {
                if (_closed || _icon == null) return;
                _icon.BalloonTipTitle = title;
                _icon.BalloonTipText = text;
                _icon.ShowBalloonTip(4000);
            });
        }

        // ---- background build card -------------------------------------

        public void BuildProgressStart(string title)
        {
            Post(delegate
            {
                if (_closed) return;
                if (_progress == null || _progress.IsDisposed)
                {
                    _progress = new BuildProgressForm();
                }
                _progress.Begin(title);
                if (!_progress.Visible) _progress.Show();
            });
        }

        public void BuildProgressUpdate(double fraction, string status)
        {
            Post(delegate
            {
                if (_closed || _progress == null || _progress.IsDisposed) return;
                _progress.SetProgress(fraction, status);
            });
        }

        public void BuildProgressFinish(bool success, string message)
        {
            Post(delegate
            {
                if (_closed || _progress == null || _progress.IsDisposed) return;
                _progress.Complete(success, message);
            });
        }

        public void Close()
        {
            Post(delegate { CloseCore(); });
        }

        private void CloseCore()
        {
            if (_closed) return;
            _closed = true;
            lock (_settingsSync)
            {
                try
                {
                    if (_settingsDispatcher != null)
                    {
                        _settingsDispatcher.BeginInvoke(new Action(delegate
                        {
                            try
                            {
                                if (System.Windows.Application.Current != null)
                                {
                                                                            var windows = new System.Collections.Generic.List<System.Windows.Window>();
                                        foreach (System.Windows.Window w in System.Windows.Application.Current.Windows) windows.Add(w);
                                        foreach (System.Windows.Window w in windows)
                                    {
                                        try { w.Close(); } catch { }
                                    }
                                }
                            }
                            catch { }
                        }));
                    }
                }
                catch { }
            }
            if (_progress != null && !_progress.IsDisposed)
            {
                _progress.Hide();
                _progress.Dispose();
                _progress = null;
            }
            if (_icon != null)
            {
                _icon.Visible = false;
                _icon.Dispose();
                _icon = null;
            }
            if (_menu != null)
            {
                _menu.Dispose();
                _menu = null;
            }
            if (_invoker != null)
            {
                try { _invoker.Dispose(); } catch { /* best-effort dispose on shutdown */ }
            }
            ExitThread();
        }

        private void TogglePause()
        {
            Program.SetPaused(!Program.IsPaused);
            if (Program.IsPaused) Notify("LiveBackground", "Paused. The desktop wallpaper is showing.");
            else Notify("LiveBackground", "Resumed.");
            RefreshMenu();
        }

        private static void OpenFolder(string folder)
        {
            try
            {
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                Process.Start("explorer.exe", Util.Quote(folder));
            }
            catch (Exception ex)
            {
                Log.Warn("could not open " + folder + ": " + ex.Message);
            }
        }

        private void Post(SendOrPostCallback callback)
        {
            if (_invoker != null && _invoker.IsHandleCreated)
            {
                try
                {
                    _invoker.BeginInvoke(callback, new object[] { null });
                    return;
                }
                catch { /* best-effort: fall back to a direct call below */ }
            }
            callback(null);
        }

        // The tray shows the same icon the exe carries (set from
        // assets\LiveBackground.ico at build time) so the tray, the Start menu
        // and the taskbar all match. If the executable icon cannot be read,
        // fall back to a small drawn planet.
        private static Icon CreateIcon()
        {
            try
            {
                Icon app = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (app != null) return app;
            }
            catch (Exception ex)
            {
                Log.Warn("could not read the application icon: " + ex.Message);
            }
            return DrawFallbackIcon();
        }

        private static Icon DrawFallbackIcon()
        {
            using (Bitmap bitmap = new Bitmap(32, 32))
            {
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    using (LinearGradientBrush brush = new LinearGradientBrush(
                        new Rectangle(0, 0, 32, 32),
                        Color.FromArgb(255, 150, 90, 255),
                        Color.FromArgb(255, 30, 10, 70),
                        45f))
                    {
                        g.FillEllipse(brush, 1, 1, 30, 30);
                    }
                    using (Pen ring = new Pen(Color.FromArgb(220, 255, 255, 255), 1.6f))
                    {
                        g.DrawEllipse(ring, 7, 10, 18, 8);
                    }
                    using (SolidBrush star = new SolidBrush(Color.White))
                    {
                        g.FillEllipse(star, 14, 13, 4, 4);
                    }
                }
                IntPtr handle = bitmap.GetHicon();
                try
                {
                    using (Icon temp = Icon.FromHandle(handle))
                    {
                        return (Icon)temp.Clone();
                    }
                }
                finally
                {
                    Native.DestroyIcon(handle);
                }
            }
        }
    }

    // Static entry points used by the wallpaper process. All of these are safe
    // to call from any thread and do nothing if the tray could not start.
    public static class Ui
    {
        private static TrayApp _tray;

        // A build can be requested a fraction of a second before the tray
        // thread has finished starting. The card request is held here and
        // replayed once the tray is ready, so the first encode still shows
        // its card. A build that finishes before then simply clears it.
        private static readonly object ProgressSync = new object();
        private static string _pendingProgressTitle;
        private static double _pendingProgressFraction;
        private static string _pendingProgressStatus;
        private static string _pendingNotifyTitle;
        private static string _pendingNotifyText;

        public static void Start(string appRoot)
        {
            if (_tray != null) return;
            Thread thread = new Thread(delegate()
            {
                try
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);

                    // An unexpected exception in a menu or settings event
                    // handler would otherwise reach the default WinForms crash
                    // dialog. Log it and tell the user, the way the rest of the
                    // app reports trouble, and keep the tray running.
                    Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                    Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
                    {
                        Log.Error("unhandled error in the user interface", e.Exception);
                        Notify("LiveBackground", "Something went wrong. The details were saved to the log.");
                    };

                    TrayApp tray = new TrayApp(appRoot);
                    TrayReady(tray);
                    Application.Run(tray);
                }
                catch (Exception ex)
                {
                    Log.Warn("the tray icon could not be created: " + ex.Message);
                    lock (ProgressSync) { _tray = null; }
                }
            });
            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Name = "livebackground-tray";
            thread.Start();
        }

        private static void TrayReady(TrayApp tray)
        {
            string title;
            double fraction;
            string status;
            lock (ProgressSync)
            {
                _tray = tray;
                title = _pendingProgressTitle;
                fraction = _pendingProgressFraction;
                status = _pendingProgressStatus;
                _pendingProgressTitle = null;
                _pendingProgressFraction = 0;
                _pendingProgressStatus = null;
            }
            if (!string.IsNullOrEmpty(title))
            {
                tray.BuildProgressStart(title);
                if (!string.IsNullOrEmpty(status)) tray.BuildProgressUpdate(fraction, status);
            }
            string notifyTitle;
            string notifyText;
            lock (ProgressSync)
            {
                notifyTitle = _pendingNotifyTitle;
                notifyText = _pendingNotifyText;
                _pendingNotifyTitle = null;
                _pendingNotifyText = null;
            }
            if (!string.IsNullOrEmpty(notifyTitle)) tray.Notify(notifyTitle, notifyText);
        }

        public static bool ShowSettings()
        {
            TrayApp tray = _tray;
            if (tray == null) return false;
            tray.ShowSettings();
            return true;
        }

        public static void RefreshMenu()
        {
            TrayApp tray = _tray;
            if (tray != null) tray.RefreshMenu();
        }

        public static void Notify(string title, string text)
        {
            TrayApp tray = _tray;
            if (tray != null) { tray.Notify(title, text); return; }
            // The tray thread may not have finished starting yet; hold the
            // message and replay it in TrayReady.
            lock (ProgressSync) { _pendingNotifyTitle = title; _pendingNotifyText = text; }
        }

        public static void BuildProgressStart(string title)
        {
            TrayApp tray = _tray;
            if (tray != null)
            {
                tray.BuildProgressStart(title);
                return;
            }
            lock (ProgressSync)
            {
                _pendingProgressTitle = title;
                _pendingProgressFraction = 0;
                _pendingProgressStatus = null;
            }
        }

        public static void BuildProgressUpdate(double fraction, string status)
        {
            TrayApp tray = _tray;
            if (tray != null)
            {
                tray.BuildProgressUpdate(fraction, status);
                return;
            }
            lock (ProgressSync)
            {
                if (_pendingProgressTitle == null) return;
                _pendingProgressFraction = fraction;
                _pendingProgressStatus = status;
            }
        }

        public static void BuildProgressFinish(bool success, string message)
        {
            TrayApp tray = _tray;
            if (tray != null)
            {
                tray.BuildProgressFinish(success, message);
                return;
            }
            // The build finished before the tray existed; there is nothing
            // to show, so cancel the pending card instead of leaving it.
            lock (ProgressSync) { _pendingProgressTitle = null; }
        }

        public static void Stop()
        {
            TrayApp tray = _tray;
            _tray = null;
            if (tray != null) tray.Close();
        }
    }
}



