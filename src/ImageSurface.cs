using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace LiveBackground
{
    // App-owned still-image layer that lives directly below the desktop icons.
    // It draws the current picture opaquely and only blends for the duration of
    // a transition, so between pictures it costs no CPU at all.
    //
    // The window is hosted on its own STA thread because it needs a message
    // loop for WM_PAINT and WM_DISPLAYCHANGE while the main thread runs the
    // wallpaper watchdog.
    public sealed class ImageSurface : IDisposable
    {
        private readonly object _sync = new object();
        private Thread _thread;
        private ApplicationContext _context;
        private SurfaceForm _form;
        private readonly ManualResetEvent _ready = new ManualResetEvent(false);
        private bool _started;
        private bool _shutdown;

        // Written by the main thread, read by the surface thread's poll timer.
        private IntPtr _progman = IntPtr.Zero;
        private Rectangle _bounds = Rectangle.Empty;
        private string _requestPath;
        private double _requestFade;
        private bool _requestImmediate;
        private bool _requestHide;
        private bool _wantVisible;

        public bool IsStarted { get { return _started; } }

        public void Start(IntPtr progman)
        {
            Start(progman, new Rectangle(0, 0,
                Math.Max(1, Native.GetSystemMetrics(0)),
                Math.Max(1, Native.GetSystemMetrics(1))));
        }

        // bounds is the physical monitor this layer covers. One surface is
        // created per screen, so the same picture can appear on both.
        public void Start(IntPtr progman, Rectangle bounds)
        {
            lock (_sync)
            {
                _progman = progman;
                if (bounds.Width > 0 && bounds.Height > 0) _bounds = bounds;
                if (_started) return;
                _started = true;
            }

            _thread = new Thread(ThreadMain);
            _thread.IsBackground = true;
            _thread.Name = "livebackground-imagesurface";
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();

            if (!_ready.WaitOne(5000))
            {
                Log.Warn("the image crossfade layer did not become ready in time");
            }
        }

        // Moves the layer to a new Progman window (Explorer restart).
        public void Attach(IntPtr progman)
        {
            if (progman == IntPtr.Zero) return;
            lock (_sync)
            {
                _progman = progman;
            }
        }

        public void Show(string path, double fadeSeconds, bool immediate)
        {
            if (string.IsNullOrEmpty(path)) return;
            lock (_sync)
            {
                _requestPath = path;
                _requestFade = fadeSeconds;
                _requestImmediate = immediate;
                _requestHide = false;
                _wantVisible = true;
            }
        }

        public void EnsureVisible()
        {
            lock (_sync)
            {
                if (!_started || _shutdown || _wantVisible) return;
                _wantVisible = true;
            }
        }

        public void Hide()
        {
            lock (_sync)
            {
                if (!_wantVisible) return;
                _wantVisible = false;
                _requestHide = true;
            }
        }

        public void Shutdown()
        {
            Thread thread;
            lock (_sync)
            {
                if (_shutdown) return;
                _shutdown = true;
                thread = _thread;
            }
            if (thread != null)
            {
                try { thread.Join(4000); } catch { /* best-effort: the poll thread stops on its own */ }
            }
        }

        public void Dispose()
        {
            Shutdown();
        }

        private void ThreadMain()
        {
            try
            {
                SurfaceForm form = new SurfaceForm(this, _bounds);
                _form = form;
                form.CreateSurface();
                _context = new ApplicationContext();
                _ready.Set();
                Application.Run(_context);
            }
            catch (Exception ex)
            {
                Log.Error("image crossfade layer failed", ex);
            }
            finally
            {
                _form = null;
                _context = null;
                _ready.Set();
            }
        }

        // Called on the surface thread by the poll timer. Takes one snapshot of
        // the requested state and applies it without touching the paint path
        // when nothing changed.
        private void Poll()
        {
            SurfaceForm form = _form;
            if (form == null) return;

            bool shutdown;
            IntPtr progman;
            string path;
            double fade;
            bool immediate;
            bool hide;
            bool visible;
            lock (_sync)
            {
                shutdown = _shutdown;
                progman = _progman;
                path = _requestPath;
                _requestPath = null;
                fade = _requestFade;
                immediate = _requestImmediate;
                hide = _requestHide;
                _requestHide = false;
                visible = _wantVisible;
            }

            if (shutdown)
            {
                form.DisposeSurface();
                ApplicationContext context = _context;
                if (context != null) context.ExitThread();
                return;
            }

            if (progman != IntPtr.Zero) form.Attach(progman);
            if (path != null) form.SetImage(path, fade, immediate);
            if (hide || !visible) form.HideSurface();
            else form.ShowSurface();
        }

        private sealed class SurfaceForm : Form
        {
            private const int WM_DISPLAYCHANGE = 0x007E;
            private const int WM_ERASEBKGND = 0x0014;

            private readonly ImageSurface _owner;
            private readonly System.Windows.Forms.Timer _pollTimer;
            private readonly System.Windows.Forms.Timer _fadeTimer;

            private Bitmap _current;
            private Bitmap _next;
            private string _currentSource = "";
            private bool _fading;
            private DateTime _fadeStart;
            private double _fadeDuration;
            private bool _visible;
            private IntPtr _attachedParent = IntPtr.Zero;
            private readonly Rectangle _bounds;
            private int _width;
            private int _height;

            public SurfaceForm(ImageSurface owner, Rectangle bounds)
            {
                _owner = owner;
                _bounds = bounds.Width > 0 && bounds.Height > 0
                    ? bounds
                    : new Rectangle(0, 0, Math.Max(1, Native.GetSystemMetrics(0)),
                        Math.Max(1, Native.GetSystemMetrics(1)));
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                BackColor = Color.Black;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);

                _width = Math.Max(1, _bounds.Width);
                _height = Math.Max(1, _bounds.Height);
                Bounds = new Rectangle(_bounds.X, _bounds.Y, _width, _height);

                _pollTimer = new System.Windows.Forms.Timer();
                _pollTimer.Interval = 100;
                _pollTimer.Tick += delegate { try { _owner.Poll(); } catch (Exception ex) { Log.Error("image layer poll failed", ex); } };

                _fadeTimer = new System.Windows.Forms.Timer();
                _fadeTimer.Interval = 33;
                _fadeTimer.Tick += FadeTick;
            }

            public void CreateSurface()
            {
                IntPtr handle = Handle;
                _pollTimer.Start();
            }

            public void Attach(IntPtr progman)
            {
                if (progman == IntPtr.Zero) return;
                if (!Native.IsWindow(Handle))
                {
                    // Explorer was restarted and took the old child window with
                    // it. WinForms still thinks the handle is alive, so rebuild
                    // it before reparenting.
                    try { RecreateHandle(); } catch { /* best-effort handle rebuild before reparenting */ }
                    _attachedParent = IntPtr.Zero;
                    _visible = false;
                }
                IntPtr parent = Native.GetParent(Handle);
                if (parent == progman && _attachedParent == progman) return;

                int style = (int)Native.GetWindowLongPtr(Handle, Native.GWL_STYLE).ToInt64();
                style = (style & ~(Native.WS_POPUP | Native.WS_CAPTION | Native.WS_THICKFRAME)) | Native.WS_CHILD;
                Native.SetWindowLongPtr(Handle, Native.GWL_STYLE, new IntPtr(style));
                Native.SetParent(Handle, progman);
                Native.RECT target;
                target.Left = _bounds.X;
                target.Top = _bounds.Y;
                target.Right = _bounds.X + _width;
                target.Bottom = _bounds.Y + _height;
                Native.MapWindowPoints(IntPtr.Zero, progman, ref target, 2);
                if (!Native.SetWindowPos(Handle, IntPtr.Zero, target.Left, target.Top,
                        target.Right - target.Left, target.Bottom - target.Top,
                        Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER))
                {
                    Log.Warn("could not size the image layer onto its screen (error " +
                        System.Runtime.InteropServices.Marshal.GetLastWin32Error() + ")");
                }
                DesktopHost.PlaceBelowIcons(progman, Handle);
                _attachedParent = progman;
                Log.Info("image crossfade layer attached to the desktop");
            }

            public void ShowSurface()
            {
                if (_visible) return;
                Native.ShowWindow(Handle, Native.SW_SHOW);
                _visible = true;
            }

            public void HideSurface()
            {
                if (!_visible) return;
                Native.ShowWindow(Handle, Native.SW_HIDE);
                _visible = false;
            }

            public void SetImage(string path, double fadeSeconds, bool immediate)
            {
                Bitmap next;
                try
                {
                    next = LoadScaled(path, _width, _height);
                }
                catch (Exception ex)
                {
                    Log.Warn("could not load the crossfade image " + Path.GetFileName(path) + ": " + ex.Message);
                    return;
                }

                _currentSource = path;
                if (immediate || _current == null || fadeSeconds < 0.05)
                {
                    DisposeBitmap(ref _current);
                    _current = next;
                    DisposeBitmap(ref _next);
                    StopFade();
                    Invalidate();
                    return;
                }

                DisposeBitmap(ref _next);
                _next = next;
                _fadeStart = DateTime.UtcNow;
                _fadeDuration = Math.Max(0.05, fadeSeconds);
                _fading = true;
                _fadeTimer.Start();
                Invalidate();
            }

            public void DisposeSurface()
            {
                try { _pollTimer.Stop(); } catch { /* best-effort: the timer may already be disposed */ }
                try { _fadeTimer.Stop(); } catch { /* best-effort: the timer may already be disposed */ }
                StopFade();
                DisposeBitmap(ref _current);
                DisposeBitmap(ref _next);
            }

            private void StopFade()
            {
                _fading = false;
                _fadeTimer.Stop();
            }

            private void FadeTick(object sender, EventArgs e)
            {
                if (!_fading) { _fadeTimer.Stop(); return; }
                double progress = (DateTime.UtcNow - _fadeStart).TotalSeconds / _fadeDuration;
                if (progress >= 1.0)
                {
                    DisposeBitmap(ref _current);
                    _current = _next;
                    _next = null;
                    StopFade();
                }
                Invalidate();
            }

            protected override void OnPaintBackground(PaintEventArgs e)
            {
                // The current picture is always opaque and covers the whole
                // window, so there is nothing to erase first.
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics graphics = e.Graphics;
                if (_current == null)
                {
                    graphics.Clear(Color.Black);
                    return;
                }

                graphics.DrawImageUnscaled(_current, 0, 0);
                if (!_fading || _next == null) return;

                float alpha = (float)((DateTime.UtcNow - _fadeStart).TotalSeconds / _fadeDuration);
                if (alpha <= 0) return;
                if (alpha > 1) alpha = 1;
                using (ImageAttributes attributes = new ImageAttributes())
                {
                    ColorMatrix matrix = new ColorMatrix();
                    matrix.Matrix33 = alpha;
                    attributes.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
                    graphics.DrawImage(_next, new Rectangle(0, 0, _width, _height),
                        0, 0, _next.Width, _next.Height, GraphicsUnit.Pixel, attributes);
                }
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_DISPLAYCHANGE)
                {
                    ResizeSurface();
                }
                else if (m.Msg == WM_ERASEBKGND)
                {
                    // Painting is handled in WM_PAINT.
                    m.Result = new IntPtr(1);
                    return;
                }
                base.WndProc(ref m);
            }

            private void ResizeSurface()
            {
                // The Program recreates the layer with new bounds when the
                // display layout changes, so here we only repaint the current
                // picture at the size this surface was built for.
                if (string.IsNullOrEmpty(_currentSource)) return;
                try
                {
                    Bitmap next = LoadScaled(_currentSource, _width, _height);
                    DisposeBitmap(ref _current);
                    DisposeBitmap(ref _next);
                    _current = next;
                    StopFade();
                    Invalidate();
                }
                catch (Exception ex) { Log.Warn("could not resize the crossfade image: " + ex.Message); }
            }

            private static Bitmap LoadScaled(string path, int width, int height)
            {
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (Image source = Image.FromStream(stream))
                using (Bitmap oriented = Orient(source))
                {
                    Bitmap target = new Bitmap(width, height, PixelFormat.Format24bppRgb);
                    try
                    {
                        using (Graphics graphics = Graphics.FromImage(target))
                        {
                            graphics.Clear(Color.Black);
                            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            graphics.CompositingQuality = CompositingQuality.HighQuality;
                            graphics.SmoothingMode = SmoothingMode.HighQuality;
                            DrawFit(graphics, oriented, width, height, DesktopHost.ReadWallpaperFit());
                        }
                        return target;
                    }
                    catch
                    {
                        target.Dispose();
                        throw;
                    }
                }
            }

            // Windows applies the EXIF orientation of phone photos. Do the same
            // so the app-owned layer matches the real wallpaper.
            private static Bitmap Orient(Image source)
            {
                RotateFlipType rotation = RotateFlipType.RotateNoneFlipNone;
                try
                {
                    if (Array.IndexOf(source.PropertyIdList, 0x0112) >= 0)
                    {
                        PropertyItem item = source.GetPropertyItem(0x0112);
                        int value = item != null && item.Value != null && item.Value.Length >= 2
                            ? item.Value[0]
                            : 1;
                        switch (value)
                        {
                            case 2: rotation = RotateFlipType.RotateNoneFlipX; break;
                            case 3: rotation = RotateFlipType.Rotate180FlipNone; break;
                            case 4: rotation = RotateFlipType.Rotate180FlipX; break;
                            case 5: rotation = RotateFlipType.Rotate90FlipX; break;
                            case 6: rotation = RotateFlipType.Rotate90FlipNone; break;
                            case 7: rotation = RotateFlipType.Rotate270FlipX; break;
                            case 8: rotation = RotateFlipType.Rotate270FlipNone; break;
                        }
                    }
                }
                catch { /* best-effort EXIF read; a photo without EXIF shows unrotated */ }

                Bitmap copy = new Bitmap(source);
                if (rotation != RotateFlipType.RotateNoneFlipNone) copy.RotateFlip(rotation);
                return copy;
            }

            private static void DrawFit(Graphics graphics, Image image, int width, int height, WallpaperFit fit)
            {
                if (fit == WallpaperFit.Tile)
                {
                    for (int y = 0; y < height; y += Math.Max(1, image.Height))
                    {
                        for (int x = 0; x < width; x += Math.Max(1, image.Width))
                        {
                            graphics.DrawImageUnscaled(image, x, y);
                        }
                    }
                    return;
                }

                if (fit == WallpaperFit.Stretch)
                {
                    graphics.DrawImage(image, new Rectangle(0, 0, width, height));
                    return;
                }

                if (fit == WallpaperFit.Center)
                {
                    graphics.DrawImageUnscaled(image, (width - image.Width) / 2, (height - image.Height) / 2);
                    return;
                }

                double scaleX = (double)width / image.Width;
                double scaleY = (double)height / image.Height;
                double scale = fit == WallpaperFit.Fit ? Math.Min(scaleX, scaleY) : Math.Max(scaleX, scaleY);
                int drawWidth = Math.Max(1, (int)Math.Ceiling(image.Width * scale));
                int drawHeight = Math.Max(1, (int)Math.Ceiling(image.Height * scale));
                int drawX = (width - drawWidth) / 2;
                int drawY = (height - drawHeight) / 2;
                graphics.DrawImage(image, new Rectangle(drawX, drawY, drawWidth, drawHeight),
                    0, 0, image.Width, image.Height, GraphicsUnit.Pixel);
            }

            private static void DisposeBitmap(ref Bitmap bitmap)
            {
                if (bitmap == null) return;
                try { bitmap.Dispose(); } catch { /* best-effort dispose; GDI+ may already have released it */ }
                bitmap = null;
            }
        }
    }
}
