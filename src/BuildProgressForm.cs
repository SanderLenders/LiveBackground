using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace LiveBackground
{
    // Small card in the corner of the screen that shows what the background
    // build is doing. It is cosmetic only: the wallpaper keeps playing while
    // the loop is prepared, and the card fades away on its own when it is done.
    // It never takes focus, so it cannot interrupt what the user is doing.
    public class BuildProgressForm : Form
    {
        private const int Pad = 22;
        private const int BarHeight = 8;
        private const int Radius = 16;

        private static readonly Color CardColor = Color.FromArgb(30, 27, 46);
        private static readonly Color BorderColor = Color.FromArgb(66, 58, 100);
        private static readonly Color TrackColor = Color.FromArgb(45, 40, 70);
        private static readonly Color AccentColor = Color.FromArgb(124, 92, 255);
        private static readonly Color AccentColor2 = Color.FromArgb(198, 132, 252);
        private static readonly Color TextColor = Color.FromArgb(240, 238, 250);
        private static readonly Color DimColor = Color.FromArgb(158, 152, 184);
        private static readonly Color GoodColor = Color.FromArgb(110, 231, 183);
        private static readonly Color BadColor = Color.FromArgb(248, 113, 113);

        private readonly Timer _fadeTimer = new Timer();
        private readonly Timer _closeTimer = new Timer();

        private string _title = "LiveBackground";
        private string _status = "Preparing...";
        private double _fraction;
        private DateTime _startedUtc = DateTime.UtcNow;
        private bool _finishing;
        private bool _success;
        private bool _fadingOut;

        public BuildProgressForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(400, 130);
            DoubleBuffered = true;
            BackColor = CardColor;
            Opacity = 0;

            _fadeTimer.Interval = 16;
            _fadeTimer.Tick += delegate { FadeTick(); };

            _closeTimer.Interval = 1600;
            _closeTimer.Tick += delegate
            {
                _closeTimer.Stop();
                _fadingOut = true;
                _fadeTimer.Start();
            };

            ApplyRegion();
            PlaceInCorner();
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
                return cp;
            }
        }

        // Starts (or restarts) the card for a new build.
        public void Begin(string title)
        {
            _title = string.IsNullOrEmpty(title) ? "LiveBackground" : title;
            _status = "Preparing...";
            _fraction = 0;
            _startedUtc = DateTime.UtcNow;
            _finishing = false;
            _success = false;
            _fadingOut = false;

            _closeTimer.Stop();
            _fadeTimer.Start();
            ApplyRegion();
            PlaceInCorner();
            Invalidate();
        }

        public void SetProgress(double fraction, string status)
        {
            if (_finishing || IsDisposed) return;
            if (fraction < 0) fraction = 0;
            if (fraction > 1) fraction = 1;
            _fraction = fraction;
            if (!string.IsNullOrEmpty(status)) _status = status;
            Invalidate();
        }

        public void Complete(bool success, string message)
        {
            if (IsDisposed) return;
            _finishing = true;
            _success = success;
            if (success) _fraction = 1;
            _status = string.IsNullOrEmpty(message) ? (success ? "Ready" : "Failed") : message;
            Invalidate();

            _closeTimer.Stop();
            _closeTimer.Start();
        }

        private void FadeTick()
        {
            if (_fadingOut)
            {
                Opacity -= 0.12;
                if (Opacity <= 0.02)
                {
                    _fadeTimer.Stop();
                    Opacity = 0;
                    Hide();
                }
                return;
            }

            Opacity += 0.12;
            if (Opacity >= 1)
            {
                Opacity = 1;
                _fadeTimer.Stop();
            }
        }

        private void PlaceInCorner()
        {
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(
                area.Right - Width - 24,
                area.Bottom - Height - 24);
        }

        private void ApplyRegion()
        {
            using (GraphicsPath path = RoundedRect(new Rectangle(0, 0, Width, Height), Radius))
            {
                Region previous = Region;
                Region = new Region(path);
                if (previous != null) previous.Dispose();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(CardColor);

            using (GraphicsPath border = RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), Radius))
            using (Pen pen = new Pen(BorderColor, 1f))
            {
                g.DrawPath(pen, border);
            }

            using (Font titleFont = new Font("Segoe UI", 9f, FontStyle.Bold))
            using (SolidBrush accent = new SolidBrush(AccentColor2))
            {
                g.DrawString(_title.ToUpperInvariant(), titleFont, accent, new PointF(Pad, 15));
            }

            string percent = ((int)Math.Round(_fraction * 100)).ToString(CultureInfo.InvariantCulture) + "%";
            Color percentColor = _finishing ? (_success ? GoodColor : BadColor) : TextColor;
            using (Font percentFont = new Font("Segoe UI", 13.5f, FontStyle.Bold))
            using (SolidBrush brush = new SolidBrush(percentColor))
            using (StringFormat right = new StringFormat { Alignment = StringAlignment.Far })
            {
                g.DrawString(percent, percentFont, brush,
                    new RectangleF(Pad, 6, Width - Pad * 2, 28), right);
            }

            string status = _status;
            if (!_finishing)
            {
                string eta = FormatEta();
                if (eta.Length > 0) status = status + "  \u00b7  " + eta;
            }

            using (Font statusFont = new Font("Segoe UI", 8.75f))
            using (SolidBrush dim = new SolidBrush(DimColor))
            using (StringFormat trim = new StringFormat
            {
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            })
            {
                g.DrawString(status, statusFont, dim,
                    new RectangleF(Pad, 38, Width - Pad * 2, 20), trim);
            }

            RectangleF track = new RectangleF(Pad, Height - Pad - BarHeight, Width - Pad * 2, BarHeight);
            using (GraphicsPath trackPath = RoundedRect(track, BarHeight / 2f))
            using (SolidBrush trackBrush = new SolidBrush(TrackColor))
            {
                g.FillPath(trackBrush, trackPath);
            }

            if (_fraction > 0)
            {
                float width = Math.Max(BarHeight, (float)(track.Width * _fraction));
                RectangleF fill = new RectangleF(track.X, track.Y, width, track.Height);
                using (GraphicsPath fillPath = RoundedRect(fill, BarHeight / 2f))
                using (LinearGradientBrush gradient = new LinearGradientBrush(
                    new RectangleF(track.X, track.Y, Math.Max(1f, track.Width), track.Height),
                    AccentColor, AccentColor2, LinearGradientMode.Horizontal))
                {
                    g.FillPath(gradient, fillPath);
                }
            }
        }

        private string FormatEta()
        {
            if (_fraction <= 0.02) return "";
            double elapsed = (DateTime.UtcNow - _startedUtc).TotalSeconds;
            if (elapsed < 3) return "";

            double remaining = elapsed * (1 - _fraction) / _fraction;
            if (remaining < 1) return "almost done";
            if (remaining < 60)
            {
                return "about " + ((int)Math.Ceiling(remaining)).ToString(CultureInfo.InvariantCulture) + " s left";
            }
            int minutes = (int)Math.Ceiling(remaining / 60.0);
            return "about " + minutes.ToString(CultureInfo.InvariantCulture) + " min left";
        }

        private static GraphicsPath RoundedRect(RectangleF bounds, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float diameter = radius * 2f;
            if (diameter <= 0) diameter = 0.1f;

            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static GraphicsPath RoundedRect(Rectangle bounds, float radius)
        {
            return RoundedRect(new RectangleF(bounds.X, bounds.Y, bounds.Width, bounds.Height), radius);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_fadeTimer != null) _fadeTimer.Dispose();
                if (_closeTimer != null) _closeTimer.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
