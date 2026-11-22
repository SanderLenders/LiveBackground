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
        private BuildProgressForm _progress;
        private bool _closed;
        private System.Windows.Forms.Timer _exitWatch;

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
            rebuild.Click += delegate { WriteFlag(Installer.RebuildFlag(_appRoot), "rebuild"); };
            _menu.Items.Add(rebuild);

            ToolStripMenuItem settings = new ToolStripMenuItem("Settings...");
            settings.Click += delegate { ShowSettings(); };
            _menu.Items.Add(settings);

            _menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem videos = new ToolStripMenuItem("Open video folder");
            videos.Click += delegate { OpenConfigFolder("VideoFolder"); };
            _menu.Items.Add(videos);

            ToolStripMenuItem images = new ToolStripMenuItem("Open image folder");
            images.Click += delegate { OpenConfigFolder("ImageFolder"); };
            _menu.Items.Add(images);

            ToolStripMenuItem logs = new ToolStripMenuItem("Open logs folder");
            logs.Click += delegate { OpenFolder(Path.Combine(_appRoot, "logs")); };
            _menu.Items.Add(logs);

            _menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem exit = new ToolStripMenuItem("Exit");
            exit.Click += delegate { WriteFlag(Installer.QuitFlag(_appRoot), "quit"); WriteFlag(Installer.StopFlag(_appRoot), "stop"); };
            _menu.Items.Add(exit);

            _icon = new NotifyIcon();
            _icon.Icon = CreateIcon();
            _icon.Text = "LiveBackground";
            _icon.ContextMenuStrip = _menu;
            _icon.DoubleClick += delegate { ShowSettings(); };
            _icon.Visible = true;

            RefreshMenu();

            _exitWatch = new System.Windows.Forms.Timer { Interval = 1000 };
            _exitWatch.Tick += delegate { CheckExitFlag(); };
            _exitWatch.Start();
        }

        public void RefreshMenu()
        {
            Post(delegate
            {
                if (_closed) return;
                _pauseItem.Text = IsPausedState() ? "Resume wallpaper" : "Pause wallpaper";
            });
        }

        public void ShowSettings()
        {
            Post(delegate
            {
                if (_closed) return;
                try
                {
                    string exe = Path.Combine(Paths.ProgramRoot, "LiveBackgroundSettings.exe");
                    if (!File.Exists(exe))
                    {
                        Log.Warn("settings exe not found: " + exe);
                        return;
                    }
                    Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Paths.ProgramRoot });
                }
                catch (Exception ex)
                {
                    try { Log.Warn("could not open settings: " + ex.Message); } catch { }
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
            if (_exitWatch != null)
            {
                try { _exitWatch.Stop(); _exitWatch.Dispose(); } catch { }
                _exitWatch = null;
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
            try { Util.SafeDelete(Installer.TrayExitFlag(_appRoot)); } catch { }
            ExitThread();
        }

        private void CheckExitFlag()
        {
            try
            {
                if (File.Exists(Installer.TrayExitFlag(_appRoot)) || File.Exists(Installer.QuitFlag(_appRoot)))
                {
                    CloseCore();
                }
            }
            catch { }
        }

        private void TogglePause()
        {
            bool paused = IsPausedState();
            if (paused) WriteFlag(Installer.ResumeFlag(_appRoot), "resume");
            else WriteFlag(Installer.PauseFlag(_appRoot), "pause");
            Notify("LiveBackground", paused ? "Resumed." : "Paused. The desktop wallpaper is showing.");
            RefreshMenu();
        }

        private bool IsPausedState()
        {
            try { return File.Exists(Installer.PauseStateFile(_appRoot)); } catch { return false; }
        }

        private static void WriteFlag(string path, string content)
        {
            try { File.WriteAllText(path, content); } catch { /* best-effort */ }
        }

        private void OpenConfigFolder(string field)
        {
            try
            {
                AppConfig cfg = ConfigStore.LoadOrCreate(_appRoot);
                string path = field == "ImageFolder" ? cfg.ImageFolder : cfg.VideoFolder;
                OpenFolder(Util.ResolvePath(_appRoot, path));
            }
            catch (Exception ex) { Log.Warn("could not open folder: " + ex.Message); }
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

    // Static entry points used by the wallpaper process. The tray UI runs in a
    // separate LiveBackgroundTray.exe process so the wallpaper process does not pay
    // WinForms initialization cost on the startup-critical path.
    public static class Ui
    {
        private static int _trayStarted;

        public static void Start(string appRoot)
        {
            if (Interlocked.CompareExchange(ref _trayStarted, 1, 0) != 0) return;
            try
            {
                string trayExe = Path.Combine(Paths.ProgramRoot, "LiveBackgroundTray.exe");
                if (!File.Exists(trayExe)) return;
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = trayExe,
                    WorkingDirectory = Paths.ProgramRoot,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref _trayStarted, 0);
                try { Log.Warn("could not start tray: " + ex.Message); } catch { }
            }
        }

        public static bool ShowSettings()
        {
            try
            {
                string exe = Path.Combine(Paths.ProgramRoot, "LiveBackgroundSettings.exe");
                if (!File.Exists(exe)) return false;
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Paths.ProgramRoot });
                return true;
            }
            catch (Exception ex)
            {
                try { Log.Warn("could not open settings: " + ex.Message); } catch { }
                return false;
            }
        }

        public static void RefreshMenu() { }
        public static void Notify(string title, string text) { try { Log.Info("notify: " + title + " - " + text); } catch { } }
        public static void BuildProgressStart(string title) { try { Log.Info(title); } catch { } }
        public static void BuildProgressUpdate(double fraction, string status) { }
        public static void BuildProgressFinish(bool success, string message) { try { Log.Info(message); } catch { } }

        public static void Stop()
        {
            try { File.WriteAllText(Installer.TrayExitFlag(Paths.DataRoot), "exit"); } catch { }
        }
    }
}
