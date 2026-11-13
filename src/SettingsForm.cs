using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace LiveBackground
{
    // Headless app + tray icon + this one dialog. Everything is built in code
    // because the project is compiled with the built-in csc and has no
    // designer or resource files.
    public class SettingsForm : Form
    {
        private const int FadeMin = 5;      // tenths of a second
        private const int FadeMax = 100;
        private const int CoarseMin = 10;

        private readonly string _appRoot;
        private readonly AppConfig _config;

        private TabControl _tabs;
        private TabPage _videosTab;
        private TabPage _playbackTab;
        private TabPage _imagesTab;
        private TabPage _slideshowTab;
        private TabPage _windowsTab;
        private RadioButton _wallpaperVideo;
        private RadioButton _wallpaperImages;

        private TextBox _folderBox;
        private RadioButton _modePlaylist;
        private RadioButton _modeSingle;
        private Label _singleHint;
        private ListView _clipList;
        private Label _clipHint;
        private Button _moveUp;
        private Button _moveDown;
        private Button _sortAz;
        private Button _videoToggle;
        private ToolTip _tips;
        private TrackBar _fadeBar;
        private Label _fadeValue;
        private Label _fadeEffective;
        private CheckBox _fineSteps;
        private ComboBox _resolution;
        private ComboBox _fps;
        private ComboBox _quality;
        private CheckBox _startWithWindows;
        private RadioButton _lockFrame;
        private RadioButton _lockCustom;
        private RadioButton _lockImages;
        private TextBox _lockPath;
        private Button _lockBrowse;
        private Panel _lockDrop;
        private ComboBox _lockImageCombo;
        private CheckBox _desktopWallpaper;
        private CheckBox _screensaverActive;
        private GroupBox _desktopGroup;
        private RadioButton _desktopFrame;
        private RadioButton _desktopCustom;
        private TextBox _desktopPath;
        private Button _desktopBrowse;
        private Panel _desktopDrop;
        private Panel _dropPanel;
        private Label _dropLabel;

        private TextBox _imageFolderBox;
        private RadioButton _imageModeSlideshow;
        private RadioButton _imageModeSingle;
        private Label _imageSingleHint;
        private ListView _imageList;
        private Label _imageHint;
        private Button _imageMoveUp;
        private Button _imageMoveDown;
        private Button _imageSortAz;
        private Panel _imageDropPanel;
        private Label _imageDropLabel;
        private TrackBar _imageInterval;
        private NumericUpDown _imageCustomInterval;
        private CheckBox _imageCustomIntervalCheck;
        private Label _imageIntervalValue;
        private Label _imageSecondsLabel;
        private Label[] _imageIntervalMarks;
        private TrackBar _imageCrossfadeBar;
        private Label _imageCrossfadeValue;
        private Label[] _imageCrossfadeMarks;
        private CheckBox _imageFineSteps;
        private CheckBox _imageShuffle;
        private bool _layingOutTicks;

        private const int TbmGetThumbRect = 0x0419;

        [StructLayout(LayoutKind.Sequential)]
        private struct TrackbarRect
        {
            public int Left, Top, Right, Bottom;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam,
            out TrackbarRect lParam);

        private static readonly Color DropIdleColor = Color.FromArgb(249, 250, 252);
        private static readonly Color DropHotColor = Color.FromArgb(228, 239, 255);
        private static readonly Color WarnColor = Color.FromArgb(176, 106, 0);

        private readonly List<string> _clipPaths = new List<string>();
        private readonly List<double> _clipDurations = new List<double>();
        private int[] _rowToEffective = new int[0];
        private List<double> _effectiveDurations = new List<double>();
        private bool _loading;
        // In "One clip" mode the list checkboxes act like radio buttons, so the
        // form remembers the chosen clip itself instead of a drop-down.
        private string _singleName = "";
        private bool _lastSingleMode;
        private readonly List<int> _resolutionWidths = new List<int>();
        private readonly List<int> _resolutionHeights = new List<int>();
        private int _probeGeneration;

        private readonly List<string> _imagePaths = new List<string>();
        private bool _loadingImages;
        private string _singleImageName = "";
        private bool _lastImageSingleMode;
        private string _lastPickerFolder = "";
        private static readonly int[] IntervalPresets = { 5, 10, 15, 20, 30, 60 };

        private static readonly int[] Resolutions = { 1280, 1920, 2560, 3840 };
        private static readonly int[] FrameRates = { 24, 30, 60 };
        private static readonly int[] CrfValues = { 16, 18, 20, 23 };

        public SettingsForm(string appRoot)
        {
            _appRoot = appRoot;
            _config = Program.Config.Clone();

            Text = "LiveBackground settings";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = true;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(660, 620);
            Font = SystemFonts.MessageBoxFont;
            _tips = new ToolTip();

            Label modeLabel = new Label();
            modeLabel.Text = "Wallpaper mode:";
            modeLabel.SetBounds(16, 14, 100, 20);
            Controls.Add(modeLabel);

            Label videoLabel = new Label();
            videoLabel.Text = "Video";
            videoLabel.TextAlign = ContentAlignment.MiddleRight;
            videoLabel.AutoSize = false;
            videoLabel.SetBounds(106, 14, 52, 20);
            videoLabel.Cursor = Cursors.Hand;
            videoLabel.TabStop = true;
            videoLabel.Click += delegate { SetWallpaperMode(false); };
            videoLabel.KeyDown += ModeLabelKey;
            Controls.Add(videoLabel);

            _wallpaperVideo = new RadioButton();
            _wallpaperVideo.Text = "";
            _wallpaperVideo.Appearance = Appearance.Button;
            _wallpaperVideo.AutoSize = false;
            _wallpaperVideo.SetBounds(162, 10, 56, 26);
            _wallpaperVideo.FlatStyle = FlatStyle.Flat;
            _wallpaperVideo.FlatAppearance.BorderSize = 0;
            _wallpaperVideo.FlatAppearance.CheckedBackColor = Color.Transparent;
            _wallpaperVideo.FlatAppearance.MouseDownBackColor = Color.Transparent;
            _wallpaperVideo.FlatAppearance.MouseOverBackColor = Color.Transparent;
            _wallpaperVideo.BackColor = Color.Transparent;
            _wallpaperVideo.Cursor = Cursors.Hand;
            _wallpaperVideo.TabStop = true;
            // The whole pill is a switch: a click flips to the other mode.
            // The labels beside it still pick Video or Images directly.
            // MouseClick fires only for real pointer clicks, which keeps
            // the keyboard path below a plain toggle. AutoCheck stays off
            // so the radios never change their own state; SetWallpaperMode
            // owns both radio buttons and reads the images radio because
            // the pill itself is the video radio (checked = video mode).
            _wallpaperVideo.AutoCheck = false;
            _wallpaperVideo.MouseClick += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                SetWallpaperMode(!_wallpaperImages.Checked);
            };
            _wallpaperVideo.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Space)
                {
                    SetWallpaperMode(!_wallpaperImages.Checked);
                    e.Handled = true;
                }
            };
            _wallpaperVideo.Paint += ModePillPaint;
            _wallpaperVideo.CheckedChanged += delegate { UpdateModeAvailability(); };
            Controls.Add(_wallpaperVideo);

            Label imagesLabel = new Label();
            imagesLabel.Text = "Images";
            imagesLabel.SetBounds(222, 14, 54, 20);
            imagesLabel.Cursor = Cursors.Hand;
            imagesLabel.TabStop = true;
            imagesLabel.Click += delegate { SetWallpaperMode(true); };
            imagesLabel.KeyDown += ModeLabelKey;
            Controls.Add(imagesLabel);

            // Keep the hidden partner radio so WinForms' mutual exclusion still
            // gives us a single Checked state to read throughout the form.
            // AutoCheck off as well: arrow keys must not be able to check it
            // behind the user's back - only SetWallpaperMode moves the mode.
            _wallpaperImages = new RadioButton();
            _wallpaperImages.Text = "";
            _wallpaperImages.SetBounds(-200, -200, 1, 1);
            _wallpaperImages.TabStop = false;
            _wallpaperImages.AutoCheck = false;
            _wallpaperImages.CheckedChanged += delegate { UpdateModeAvailability(); };
            Controls.Add(_wallpaperImages);

            _tabs = new TabControl();
            _tabs.SetBounds(12, 40, 636, 520);
            _tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
            _tabs.Selecting += TabSelecting;
            _tabs.DrawItem += TabDrawItem;
            Controls.Add(_tabs);

            _videosTab = new TabPage("Videos");
            _playbackTab = new TabPage("Playback");
            _imagesTab = new TabPage("Images");
            _slideshowTab = new TabPage("Slideshow");
            _windowsTab = new TabPage("Other");
            _tabs.TabPages.Add(_videosTab);
            _tabs.TabPages.Add(_playbackTab);
            _tabs.TabPages.Add(_imagesTab);
            _tabs.TabPages.Add(_slideshowTab);
            _tabs.TabPages.Add(_windowsTab);

            BuildVideosTab(_videosTab);
            BuildPlaybackTab(_playbackTab);
            BuildImagesTab(_imagesTab);
            BuildSlideshowTab(_slideshowTab);
            BuildWindowsTab(_windowsTab);

            Button save = new Button();
            save.Text = "Save and apply";
            save.SetBounds(414, 572, 118, 30);
            save.Click += SaveClicked;
            Controls.Add(save);
            AcceptButton = save;

            Button cancel = new Button();
            cancel.Text = "Cancel";
            cancel.SetBounds(538, 572, 110, 30);
            // The window is modeless (Show, not ShowDialog), so a DialogResult
            // alone would never close it. Close explicitly; anything already
            // applied with "Rebuild now" stays on the desktop by design.
            cancel.Click += delegate { Close(); };
            Controls.Add(cancel);
            CancelButton = cancel;

            LoadSettings();
        }

        private bool TabEnabled(int index)
        {
            bool images = _wallpaperImages != null && _wallpaperImages.Checked;
            if (index == 0 || index == 1) return !images;
            if (index == 2 || index == 3) return images;
            return true;
        }

        private void TabSelecting(object sender, TabControlCancelEventArgs e)
        {
            if (!TabEnabled(e.TabPageIndex)) e.Cancel = true;
        }

        private void TabDrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _tabs.TabPages.Count) return;
            Rectangle bounds = _tabs.GetTabRect(e.Index);
            using (SolidBrush back = new SolidBrush(SystemColors.Control))
            {
                e.Graphics.FillRectangle(back, bounds);
            }

            // The active tab is drawn in bold so it stays obvious that the
            // greyed tabs belong to the other wallpaper mode.
            bool enabled = TabEnabled(e.Index);
            bool selected = e.Index == _tabs.SelectedIndex;
            using (Font font = selected ? new Font(e.Font, FontStyle.Bold) : new Font(e.Font, FontStyle.Regular))
            {
                TextRenderer.DrawText(e.Graphics, _tabs.TabPages[e.Index].Text, font, bounds,
                    enabled ? SystemColors.ControlText : SystemColors.GrayText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            if (selected)
            {
                using (Pen pen = new Pen(Color.FromArgb(70, 110, 180)))
                {
                    e.Graphics.DrawLine(pen, bounds.Left + 2, bounds.Bottom - 1,
                        bounds.Right - 3, bounds.Bottom - 1);
                }
            }
        }

        private void UpdateModeAvailability()
        {
            if (_tabs == null) return;
            bool images = _wallpaperImages.Checked;
            if (!TabEnabled(_tabs.SelectedIndex))
            {
                _tabs.SelectedTab = images ? _imagesTab : _videosTab;
            }
            _tabs.Invalidate();
            UpdateDesktopControls();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            UpdateVideoToggle();
            RefreshClips();
            RefreshImages();
            AlignSlideshowMarks();
            _tabs.SelectedTab = _wallpaperImages.Checked ? _imagesTab : _videosTab;
            _tabs.Invalidate();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Make any probe thread that is still running drop its result
            // instead of touching the controls while they are torn down.
            _probeGeneration++;
            base.OnFormClosing(e);
        }

        private void BuildVideosTab(TabPage page)
        {
            Label label = MakeLabel(page, "Video folder", 16, 14, 200);
            label.Font = new Font(Font, FontStyle.Bold);

            _folderBox = new TextBox();
            _folderBox.SetBounds(16, 36, 470, 24);
            page.Controls.Add(_folderBox);

            Button browse = new Button();
            browse.Text = "Browse...";
            browse.SetBounds(494, 34, 110, 27);
            browse.Click += delegate { BrowseFolder(); };
            page.Controls.Add(browse);

            _modePlaylist = new RadioButton();
            _modePlaylist.Text = "All clips in this order";
            _modePlaylist.SetBounds(16, 72, 180, 22);
            _modePlaylist.CheckedChanged += delegate { UpdateModeControls(); };
            page.Controls.Add(_modePlaylist);

            _modeSingle = new RadioButton();
            _modeSingle.Text = "One clip";
            _modeSingle.SetBounds(204, 72, 80, 22);
            _modeSingle.CheckedChanged += delegate { UpdateModeControls(); };
            page.Controls.Add(_modeSingle);

            _singleHint = MakeLabel(page, "Pick one clip in the list below", 290, 72, 314);
            _singleHint.ForeColor = SystemColors.GrayText;
            _singleHint.Visible = false;

            _clipList = new ListView();
            _clipList.SetBounds(16, 104, 400, 202);
            _clipList.View = View.Details;
            _clipList.CheckBoxes = true;
            _clipList.FullRowSelect = true;
            _clipList.GridLines = true;
            _clipList.HideSelection = false;
            _clipList.MultiSelect = false;
            _clipList.Columns.Add("Clip", 195);
            _clipList.Columns.Add("Length", 85);
            _clipList.Columns.Add("Fade", 80);
            _clipList.ItemCheck += ClipItemCheck;
            _clipList.ItemChecked += delegate
            {
                if (_loading) return;
                RebuildEffective();
                UpdateClipFades();
                UpdateFadeLabels();
            };
            page.Controls.Add(_clipList);

            _moveUp = new Button();
            _moveUp.Text = "Move up";
            _moveUp.SetBounds(16, 312, 90, 28);
            _moveUp.Click += delegate { MoveClip(-1); };
            page.Controls.Add(_moveUp);

            _moveDown = new Button();
            _moveDown.Text = "Move down";
            _moveDown.SetBounds(112, 312, 100, 28);
            _moveDown.Click += delegate { MoveClip(1); };
            page.Controls.Add(_moveDown);

            _sortAz = new Button();
            _sortAz.Text = "Sort A-Z";
            _sortAz.SetBounds(218, 312, 90, 28);
            _sortAz.Click += delegate { SortClips(); };
            page.Controls.Add(_sortAz);

            Button refresh = new Button();
            refresh.Text = "Refresh";
            refresh.SetBounds(16, 348, 100, 28);
            refresh.Click += delegate { CapturePlaylist(); RefreshClips(); };
            page.Controls.Add(refresh);

            Button openFolder = new Button();
            openFolder.Text = "Open folder";
            openFolder.SetBounds(124, 348, 110, 28);
            openFolder.Click += delegate { OpenFolder(); };
            page.Controls.Add(openFolder);

            Button rebuild = new Button();
            rebuild.Text = "Rebuild now";
            rebuild.SetBounds(242, 348, 110, 28);
            // Applies what the form currently shows to the running
            // wallpaper without saving or closing the window.
            rebuild.Click += RebuildNowClicked;
            page.Controls.Add(rebuild);

            _videoToggle = new Button();
            _videoToggle.SetBounds(360, 348, 120, 28);
            _videoToggle.Click += delegate { ToggleVideo(); };
            _tips.SetToolTip(_videoToggle,
                "Stops playback and releases the video file so you can delete or replace it. " +
                "The desktop wallpaper stays.");
            page.Controls.Add(_videoToggle);
            UpdateVideoToggle();

            _clipHint = MakeLabel(page, "", 16, 384, 588);
            _clipHint.Height = 46;
            _clipHint.ForeColor = SystemColors.GrayText;

            // Drop target: drag videos onto it, or click to pick files. Files
            // are copied into the folder shown above, so the running app's
            // watcher can rebuild the loop from them.
            _dropPanel = new Panel();
            _dropPanel.SetBounds(428, 104, 188, 202);
            _dropPanel.BackColor = DropIdleColor;
            _dropPanel.AllowDrop = true;
            _dropPanel.Cursor = Cursors.Hand;
            _dropPanel.Paint += DropPanelPaint;
            _dropPanel.DragEnter += DropDragEnter;
            _dropPanel.DragLeave += DropDragLeave;
            _dropPanel.DragDrop += DropDragDrop;
            _dropPanel.Click += delegate { BrowseVideos(); };
            page.Controls.Add(_dropPanel);

            _dropLabel = new Label();
            _dropLabel.Text = "Drag video files here\r\nor click to choose files";
            _dropLabel.TextAlign = ContentAlignment.MiddleCenter;
            _dropLabel.BackColor = Color.Transparent;
            _dropLabel.SetBounds(2, 2, 184, 198);
            _dropLabel.AllowDrop = true;
            _dropLabel.Cursor = Cursors.Hand;
            _dropLabel.DragEnter += DropDragEnter;
            _dropLabel.DragLeave += DropDragLeave;
            _dropLabel.DragDrop += DropDragDrop;
            _dropLabel.Click += delegate { BrowseVideos(); };
            _dropPanel.Controls.Add(_dropLabel);
        }

        private void DropPanelPaint(object sender, PaintEventArgs e)
        {
            Panel panel = sender as Panel;
            if (panel == null) return;
            using (Pen pen = new Pen(Color.FromArgb(145, 158, 178)))
            {
                pen.DashStyle = DashStyle.Dash;
                e.Graphics.DrawRectangle(pen, 0, 0, panel.Width - 1, panel.Height - 1);
            }
        }

        private void DropDragEnter(object sender, DragEventArgs e)
        {
            bool hasVideos = HasDroppedVideos(e.Data);
            e.Effect = hasVideos ? DragDropEffects.Copy : DragDropEffects.None;
            if (hasVideos) _dropPanel.BackColor = DropHotColor;
        }

        private void DropDragLeave(object sender, EventArgs e)
        {
            _dropPanel.BackColor = DropIdleColor;
        }

        private void DropDragDrop(object sender, DragEventArgs e)
        {
            _dropPanel.BackColor = DropIdleColor;
            string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files != null) AddVideos(files);
        }

        private static bool HasDroppedVideos(IDataObject data)
        {
            if (data == null || !data.GetDataPresent(DataFormats.FileDrop)) return false;
            string[] files = data.GetData(DataFormats.FileDrop) as string[];
            if (files == null) return false;
            foreach (string file in files)
            {
                if (File.Exists(file) && MediaPipeline.IsVideo(file)) return true;
                if (Directory.Exists(file) && MediaPipeline.ScanVideos(file).Count > 0) return true;
            }
            return false;
        }

        private void BrowseVideos()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Choose wallpaper videos";
                dialog.Filter = "Video files|*.mp4;*.mkv;*.mov;*.avi;*.webm;*.m4v;*.wmv;*.mpg;*.mpeg;*.ts;*.m2ts;*.flv;*.ogv;*.3gp;*.vob;*.mxf;*.dv;*.rm;*.rmvb;*.asf;*.wtv;*.f4v|All files|*.*";
                dialog.Multiselect = true;
                dialog.CheckFileExists = true;
                dialog.InitialDirectory = InitialPickerFolder(_folderBox.Text, true);
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    if (dialog.FileNames.Length > 0)
                        _lastPickerFolder = Path.GetDirectoryName(dialog.FileNames[0]);
                    AddVideos(dialog.FileNames);
                }
            }
        }

        // Copies dropped or picked videos into the folder currently shown in
        // the "Video folder" box. Existing files are never overwritten; a
        // second copy of "clip.mp4" becomes "clip (2).mp4". New clips are not
        // ticked, so they stay out of the playlist until the user adds them.
        private void AddVideos(string[] paths)
        {
            string folder = Util.ResolvePath(_appRoot, _folderBox.Text);
            try { Directory.CreateDirectory(folder); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not create the video folder: " + ex.Message,
                    "LiveBackground", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            List<string> sources = new List<string>();
            foreach (string path in paths)
            {
                if (File.Exists(path) && MediaPipeline.IsVideo(path)) sources.Add(path);
                else if (Directory.Exists(path)) sources.AddRange(MediaPipeline.ScanVideos(path));
            }

            if (sources.Count == 0)
            {
                MessageBox.Show(this, "No supported video files were found in what you dropped.",
                    "LiveBackground", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            int copied = 0;
            int skipped = 0;
            string firstError = "";
            foreach (string source in sources)
            {
                string sourceFull = Path.GetFullPath(source);
                if (string.Equals(Path.GetDirectoryName(sourceFull), folder,
                    StringComparison.OrdinalIgnoreCase))
                {
                    skipped++;
                    continue;
                }

                try
                {
                    string target = UniquePath(folder, Path.GetFileName(sourceFull));
                    File.Copy(sourceFull, target, false);
                    copied++;
                }
                catch (Exception ex)
                {
                    if (firstError.Length == 0) firstError = ex.Message;
                }
            }

            // Keep the order and ticks the user already made before refreshing.
            CapturePlaylist();
            RefreshClips();

            string message;
            if (copied == 0 && skipped > 0)
            {
                message = "Those videos are already in this folder.";
            }
            else if (copied == 0)
            {
                message = "Could not add the videos." +
                    (firstError.Length > 0 ? " " + firstError : "");
            }
            else
            {
                message = "Added " + copied + " video(s) to " + folder + ". " +
                    "They are in the list but not ticked yet, so they will not play until you tick them.";
                if (skipped > 0) message += " " + skipped + " already in the folder.";
                if (firstError.Length > 0) message += " One file failed: " + firstError;
            }
            _clipHint.Text = message;

            if (copied > 0)
            {
                Ui.Notify("LiveBackground", "Added " + copied + " video(s). Tick them in the playlist to play them.");
            }
        }

        private static string UniquePath(string folder, string fileName)
        {
            string target = Path.Combine(folder, fileName);
            if (!File.Exists(target)) return target;

            string baseName = Path.GetFileNameWithoutExtension(fileName);
            string extension = Path.GetExtension(fileName);
            for (int i = 2; i < 10000; i++)
            {
                target = Path.Combine(folder, baseName + " (" + i + ")" + extension);
                if (!File.Exists(target)) return target;
            }
            return Path.Combine(folder, baseName + " (" + DateTime.Now.Ticks + ")" + extension);
        }

        private void BuildPlaybackTab(TabPage page)
        {
            Label label = MakeLabel(page, "Crossfade", 16, 18, 200);
            label.Font = new Font(Font, FontStyle.Bold);

            _fadeValue = new Label();
            _fadeValue.SetBounds(460, 16, 144, 22);
            _fadeValue.TextAlign = ContentAlignment.MiddleRight;
            _fadeValue.Font = new Font(Font.FontFamily, Font.Size + 2f, FontStyle.Bold);
            page.Controls.Add(_fadeValue);

            _fadeBar = new TrackBar();
            _fadeBar.SetBounds(12, 42, 596, 46);
            _fadeBar.Minimum = FadeMin;
            _fadeBar.Maximum = FadeMax;
            _fadeBar.TickStyle = TickStyle.BottomRight;
            _fadeBar.ValueChanged += FadeChanged;
            page.Controls.Add(_fadeBar);

            _fineSteps = new CheckBox();
            _fineSteps.Text = "Allow half-second steps";
            // Starts below the trackbar's real height at 125% (see Slideshow).
            _fineSteps.SetBounds(16, 104, 320, 24);
            _fineSteps.CheckedChanged += delegate { ApplyFadeStepMode(); };
            page.Controls.Add(_fineSteps);

            _fadeEffective = new Label();
            _fadeEffective.SetBounds(16, 134, 588, 40);
            page.Controls.Add(_fadeEffective);

            Label resLabel = MakeLabel(page, "Resolution", 16, 232, 110);
            _resolution = new ComboBox();
            _resolution.SetBounds(16, 254, 170, 24);
            _resolution.DropDownStyle = ComboBoxStyle.DropDownList;
            page.Controls.Add(_resolution);

            Label fpsLabel = MakeLabel(page, "Frame rate", 210, 232, 110);
            _fps = new ComboBox();
            _fps.SetBounds(210, 254, 100, 24);
            _fps.DropDownStyle = ComboBoxStyle.DropDownList;
            page.Controls.Add(_fps);

            Label qualityLabel = MakeLabel(page, "Quality", 330, 232, 110);
            _quality = new ComboBox();
            _quality.SetBounds(330, 254, 240, 24);
            _quality.DropDownStyle = ComboBoxStyle.DropDownList;
            page.Controls.Add(_quality);

            _screensaverActive = new CheckBox();
            _screensaverActive.Text = "Screensaver active (let Windows dim and lock the screen)";
            _screensaverActive.SetBounds(16, 400, 588, 24);
            page.Controls.Add(_screensaverActive);

            Label screenHint = MakeLabel(page,
                "On (recommended): Windows dims, turns off and locks the screen according to " +
                "your power settings, even while the video plays. Off: the video keeps the " +
                "screen awake.",
                16, 428, 588);
            screenHint.Height = 44;
            screenHint.ForeColor = SystemColors.GrayText;
        }

        private void BuildImagesTab(TabPage page)
        {
            Label label = MakeLabel(page, "Image folder", 16, 14, 200);
            label.Font = new Font(Font, FontStyle.Bold);

            _imageFolderBox = new TextBox();
            _imageFolderBox.SetBounds(16, 36, 470, 24);
            page.Controls.Add(_imageFolderBox);

            Button browse = new Button();
            browse.Text = "Browse...";
            browse.SetBounds(494, 34, 110, 27);
            browse.Click += delegate { BrowseImageFolder(); };
            page.Controls.Add(browse);

            _imageModeSlideshow = new RadioButton();
            _imageModeSlideshow.Text = "Slideshow";
            _imageModeSlideshow.SetBounds(16, 72, 100, 22);
            _imageModeSlideshow.CheckedChanged += delegate { UpdateImageModeControls(); };
            page.Controls.Add(_imageModeSlideshow);

            _imageModeSingle = new RadioButton();
            _imageModeSingle.Text = "One image";
            _imageModeSingle.SetBounds(124, 72, 100, 22);
            _imageModeSingle.CheckedChanged += delegate { UpdateImageModeControls(); };
            page.Controls.Add(_imageModeSingle);

            _imageSingleHint = MakeLabel(page, "Pick one image in the list below", 244, 72, 360);
            _imageSingleHint.ForeColor = SystemColors.GrayText;
            _imageSingleHint.Visible = false;

            _imageList = new ListView();
            _imageList.SetBounds(16, 104, 400, 202);
            _imageList.View = View.Details;
            _imageList.CheckBoxes = true;
            _imageList.FullRowSelect = true;
            _imageList.GridLines = true;
            _imageList.HideSelection = false;
            _imageList.MultiSelect = false;
            _imageList.Columns.Add("Image", 285);
            _imageList.Columns.Add("Size", 90);
            _imageList.ItemCheck += ImageItemCheck;
            _imageList.ItemChecked += delegate
            {
                if (_loadingImages) return;
                UpdateImageHintText();
            };
            page.Controls.Add(_imageList);

            _imageMoveUp = new Button();
            _imageMoveUp.Text = "Move up";
            _imageMoveUp.SetBounds(16, 312, 90, 28);
            _imageMoveUp.Click += delegate { MoveImage(-1); };
            page.Controls.Add(_imageMoveUp);

            _imageMoveDown = new Button();
            _imageMoveDown.Text = "Move down";
            _imageMoveDown.SetBounds(112, 312, 100, 28);
            _imageMoveDown.Click += delegate { MoveImage(1); };
            page.Controls.Add(_imageMoveDown);

            _imageSortAz = new Button();
            _imageSortAz.Text = "Sort A-Z";
            _imageSortAz.SetBounds(218, 312, 90, 28);
            _imageSortAz.Click += delegate { SortImages(); };
            page.Controls.Add(_imageSortAz);

            Button refresh = new Button();
            refresh.Text = "Refresh";
            refresh.SetBounds(16, 348, 100, 28);
            refresh.Click += delegate { CaptureImagePlaylist(); RefreshImages(); };
            page.Controls.Add(refresh);

            Button openFolder = new Button();
            openFolder.Text = "Open folder";
            openFolder.SetBounds(124, 348, 110, 28);
            openFolder.Click += delegate { OpenImageFolder(); };
            page.Controls.Add(openFolder);

            Button next = new Button();
            next.Text = "Next image";
            next.SetBounds(242, 348, 110, 28);
            next.Click += delegate
            {
                Program.SetPaused(false);
                Program.RequestNextImage();
                UpdateVideoToggle();
            };
            page.Controls.Add(next);

            Button rebuild = new Button();
            rebuild.Text = "Rebuild now";
            rebuild.SetBounds(360, 348, 110, 28);
            // Mirrors the Videos tab: apply the staged settings to the
            // running wallpaper without saving or closing the window.
            // Images need no encoding, so this is instant.
            rebuild.Click += RebuildNowClicked;
            page.Controls.Add(rebuild);

            _imageHint = MakeLabel(page, "", 16, 384, 588);
            _imageHint.Height = 46;
            _imageHint.ForeColor = SystemColors.GrayText;

            _imageDropPanel = new Panel();
            _imageDropPanel.SetBounds(428, 104, 188, 202);
            _imageDropPanel.BackColor = DropIdleColor;
            _imageDropPanel.AllowDrop = true;
            _imageDropPanel.Cursor = Cursors.Hand;
            _imageDropPanel.Paint += DropPanelPaint;
            _imageDropPanel.DragEnter += ImagePanelDropEnter;
            _imageDropPanel.DragLeave += ImagePanelDropLeave;
            _imageDropPanel.DragDrop += ImagePanelDropDrop;
            _imageDropPanel.Click += delegate { BrowseImageFiles(); };
            page.Controls.Add(_imageDropPanel);

            _imageDropLabel = new Label();
            _imageDropLabel.Text = "Drag image files here\r\nor click to choose files";
            _imageDropLabel.TextAlign = ContentAlignment.MiddleCenter;
            _imageDropLabel.BackColor = Color.Transparent;
            _imageDropLabel.SetBounds(2, 2, 184, 198);
            _imageDropLabel.AllowDrop = true;
            _imageDropLabel.Cursor = Cursors.Hand;
            _imageDropLabel.DragEnter += ImagePanelDropEnter;
            _imageDropLabel.DragLeave += ImagePanelDropLeave;
            _imageDropLabel.DragDrop += ImagePanelDropDrop;
            _imageDropLabel.Click += delegate { BrowseImageFiles(); };
            _imageDropPanel.Controls.Add(_imageDropLabel);
        }

        private void BuildSlideshowTab(TabPage page)
        {
            Label label = MakeLabel(page, "Change the desktop image every", 16, 18, 300);
            label.Font = new Font(Font, FontStyle.Bold);

            _imageInterval = new TrackBar();
            _imageInterval.SetBounds(16, 46, 360, 45);
            _imageInterval.Minimum = 0;
            _imageInterval.Maximum = IntervalPresets.Length - 1;
            _imageInterval.TickFrequency = 1;
            _imageInterval.SmallChange = 1;
            _imageInterval.LargeChange = 1;
            _imageInterval.ValueChanged += delegate
            {
                if (_layingOutTicks) return;
                UpdateIntervalControls();
            };
            page.Controls.Add(_imageInterval);

            _imageIntervalValue = MakeLabel(page, "10 seconds", 382, 52, 120);
            _imageIntervalValue.ForeColor = SystemColors.GrayText;

            // The trackbar grows taller than the 45 px it is given once Windows
            // draws it at 125% (about 56 px); the number row must start below
            // that, otherwise the slider's white area hides the top of the text.
            // One label per snap point, later centred on the real thumb positions.
            string[] intervalMarkText = { "5 s", "10", "15", "20", "30", "1 min" };
            _imageIntervalMarks = new Label[intervalMarkText.Length];
            for (int i = 0; i < intervalMarkText.Length; i++)
            {
                _imageIntervalMarks[i] = MakeTickLabel(page, intervalMarkText[i], 104);
            }

            _imageCustomIntervalCheck = new CheckBox();
            _imageCustomIntervalCheck.Text = "Custom";
            // Wide enough for the full word at every display scale; with the
            // old 80 px the "m" was clipped on 125% displays.
            _imageCustomIntervalCheck.SetBounds(16, 132, 100, 24);
            _imageCustomIntervalCheck.CheckedChanged += delegate { UpdateIntervalControls(); };
            page.Controls.Add(_imageCustomIntervalCheck);

            _imageCustomInterval = new NumericUpDown();
            _imageCustomInterval.SetBounds(120, 132, 90, 24);
            _imageCustomInterval.Minimum = 1;
            _imageCustomInterval.Maximum = 86400;
            _imageCustomInterval.Value = 10;
            _imageCustomInterval.ValueChanged += delegate { UpdateIntervalControls(); };
            page.Controls.Add(_imageCustomInterval);

            _imageSecondsLabel = MakeLabel(page, "seconds (1-86400)", 216, 136, 150);
            _imageSecondsLabel.ForeColor = SystemColors.GrayText;

            Label fadeLabel = MakeLabel(page, "Crossfade between pictures", 16, 168, 300);
            fadeLabel.Font = new Font(Font, FontStyle.Bold);

            _imageCrossfadeBar = new TrackBar();
            _imageCrossfadeBar.SetBounds(16, 194, 360, 45);
            _imageCrossfadeBar.Minimum = 0;
            _imageCrossfadeBar.Maximum = 10;
            _imageCrossfadeBar.TickFrequency = 1;
            _imageCrossfadeBar.SmallChange = 1;
            _imageCrossfadeBar.LargeChange = 2;
            _imageCrossfadeBar.ValueChanged += delegate
            {
                if (_layingOutTicks) return;
                int snapped = SnapImageCrossfade(_imageCrossfadeBar.Value);
                if (snapped != _imageCrossfadeBar.Value)
                {
                    _imageCrossfadeBar.Value = snapped;
                    return;
                }
                UpdateCrossfadeLabel();
            };
            page.Controls.Add(_imageCrossfadeBar);

            _imageCrossfadeValue = MakeLabel(page, "2.0 s", 382, 200, 120);
            _imageCrossfadeValue.ForeColor = SystemColors.GrayText;

            _imageCrossfadeMarks = new Label[]
            {
                MakeTickLabel(page, "Off (instant)", 252),
                MakeTickLabel(page, "5 s", 252),
            };

            _imageFineSteps = new CheckBox();
            _imageFineSteps.Text = "Allow half-second steps";
            _imageFineSteps.SetBounds(16, 282, 320, 24);
            _imageFineSteps.CheckedChanged += delegate { ApplyImageCrossfadeStepMode(); };
            page.Controls.Add(_imageFineSteps);

            _imageShuffle = new CheckBox();
            _imageShuffle.Text = "Shuffle the order";
            _imageShuffle.SetBounds(16, 312, 300, 24);
            page.Controls.Add(_imageShuffle);

            UpdateIntervalControls();
            UpdateCrossfadeLabel();
        }

        private void BuildWindowsTab(TabPage page)
        {
            _startWithWindows = new CheckBox();
            _startWithWindows.Text = "Start LiveBackground when I sign in";
            _startWithWindows.SetBounds(16, 18, 400, 24);
            page.Controls.Add(_startWithWindows);

            GroupBox group = new GroupBox();
            group.Text = "Lock screen";
            group.SetBounds(16, 88, 588, 164);
            page.Controls.Add(group);

            _lockFrame = new RadioButton();
            _lockFrame.Text = "First frame of the video loop";
            _lockFrame.SetBounds(16, 18, 500, 24);
            _lockFrame.CheckedChanged += delegate { UpdateLockControls(); };
            group.Controls.Add(_lockFrame);

            _lockCustom = new RadioButton();
            _lockCustom.Text = "Use a custom image";
            _lockCustom.SetBounds(16, 42, 200, 24);
            _lockCustom.CheckedChanged += delegate { UpdateLockControls(); };
            group.Controls.Add(_lockCustom);

            _lockPath = new TextBox();
            _lockPath.SetBounds(36, 68, 330, 24);
            group.Controls.Add(_lockPath);

            _lockBrowse = new Button();
            _lockBrowse.Text = "Browse...";
            _lockBrowse.SetBounds(374, 66, 86, 27);
            _lockBrowse.Click += delegate { BrowseLockImage(); };
            group.Controls.Add(_lockBrowse);

            _lockDrop = MakeImageDropBox(group, 466, 66, 100, 27, "Drop image",
                delegate { BrowseLockImage(); });

            _lockImages = new RadioButton();
            _lockImages.Text = "Use an image from my image wallpapers";
            _lockImages.SetBounds(16, 94, 400, 24);
            _lockImages.CheckedChanged += delegate { UpdateLockControls(); };
            group.Controls.Add(_lockImages);

            _lockImageCombo = new ComboBox();
            _lockImageCombo.SetBounds(36, 118, 330, 24);
            _lockImageCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            group.Controls.Add(_lockImageCombo);

            _desktopWallpaper = new CheckBox();
            _desktopWallpaper.Text = "Set a desktop wallpaper image";
            _desktopWallpaper.SetBounds(16, 258, 500, 24);
            _desktopWallpaper.CheckedChanged += delegate { UpdateDesktopControls(); };
            page.Controls.Add(_desktopWallpaper);

            _desktopGroup = new GroupBox();
            _desktopGroup.Text = "Desktop wallpaper image";
            _desktopGroup.SetBounds(16, 286, 588, 132);
            page.Controls.Add(_desktopGroup);

            _desktopFrame = new RadioButton();
            _desktopFrame.Text = "First frame of the loop";
            _desktopFrame.SetBounds(16, 22, 460, 24);
            _desktopFrame.CheckedChanged += delegate { UpdateDesktopControls(); };
            _desktopGroup.Controls.Add(_desktopFrame);

            _desktopCustom = new RadioButton();
            _desktopCustom.Text = "Use a custom image";
            _desktopCustom.SetBounds(16, 48, 200, 24);
            _desktopCustom.CheckedChanged += delegate { UpdateDesktopControls(); };
            _desktopGroup.Controls.Add(_desktopCustom);

            _desktopPath = new TextBox();
            _desktopPath.SetBounds(36, 74, 330, 24);
            _desktopGroup.Controls.Add(_desktopPath);

            _desktopBrowse = new Button();
            _desktopBrowse.Text = "Browse...";
            _desktopBrowse.SetBounds(374, 72, 86, 27);
            _desktopBrowse.Click += delegate { BrowseDesktopImage(); };
            _desktopGroup.Controls.Add(_desktopBrowse);

            _desktopDrop = MakeImageDropBox(_desktopGroup, 466, 72, 100, 27, "Drop image",
                delegate { BrowseDesktopImage(); });
        }

        private Panel MakeImageDropBox(Control parent, int x, int y, int width, int height,
            string text, EventHandler click)
        {
            Panel panel = new Panel();
            panel.SetBounds(x, y, width, height);
            panel.BackColor = DropIdleColor;
            panel.AllowDrop = true;
            panel.Cursor = Cursors.Hand;
            panel.Paint += DropPanelPaint;
            panel.DragEnter += ImageDropEnter;
            panel.DragLeave += ImageDropLeave;
            panel.DragDrop += ImageDropDrop;
            panel.Click += click;
            parent.Controls.Add(panel);

            Label label = new Label();
            label.Text = text;
            label.TextAlign = ContentAlignment.MiddleCenter;
            label.BackColor = Color.Transparent;
            label.SetBounds(1, 1, width - 2, height - 2);
            label.AllowDrop = true;
            label.Cursor = Cursors.Hand;
            label.DragEnter += ImageDropEnter;
            label.DragLeave += ImageDropLeave;
            label.DragDrop += ImageDropDrop;
            label.Click += click;
            panel.Controls.Add(label);
            return panel;
        }

        private static Panel DropPanelOf(object sender)
        {
            Control control = sender as Control;
            if (control == null) return null;
            Panel panel = control as Panel;
            if (panel != null) return panel;
            return control.Parent as Panel;
        }

        private void ImageDropEnter(object sender, DragEventArgs e)
        {
            Panel panel = DropPanelOf(sender);
            bool hasImage = FirstDroppedImage(e.Data) != null;
            e.Effect = hasImage ? DragDropEffects.Copy : DragDropEffects.None;
            if (hasImage && panel != null) panel.BackColor = DropHotColor;
        }

        private void ImageDropLeave(object sender, EventArgs e)
        {
            Panel panel = DropPanelOf(sender);
            if (panel != null) panel.BackColor = DropIdleColor;
        }

        private void ImageDropDrop(object sender, DragEventArgs e)
        {
            Panel panel = DropPanelOf(sender);
            if (panel != null) panel.BackColor = DropIdleColor;
            string file = FirstDroppedImage(e.Data);
            if (file == null) return;
            if (panel == _lockDrop) ApplyLockImage(file);
            else if (panel == _desktopDrop) ApplyDesktopImage(file);
        }

        private static string FirstDroppedImage(IDataObject data)
        {
            if (data == null || !data.GetDataPresent(DataFormats.FileDrop)) return null;
            string[] files = data.GetData(DataFormats.FileDrop) as string[];
            if (files == null) return null;
            foreach (string file in files)
            {
                if (!File.Exists(file)) continue;
                if (MediaPipeline.IsImage(file)) return file;
            }
            return null;
        }

        private void ApplyLockImage(string source)
        {
            try
            {
                _lockPath.Text = ImportImageForUi(source, "lock-screen");
                _lockCustom.Checked = true;
                UpdateLockControls();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not use that image: " + ex.Message,
                    "LiveBackground", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ApplyDesktopImage(string source)
        {
            try
            {
                _desktopPath.Text = ImportImageForUi(source, "desktop-wallpaper");
                _desktopWallpaper.Checked = true;
                _desktopCustom.Checked = true;
                UpdateDesktopControls();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not use that image: " + ex.Message,
                    "LiveBackground", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // Copies an image into the app's own images folder and returns a path
        // relative to the app folder, so the setting survives moving the
        // original picture. Images already inside the folder are left alone.
        private string ImportImageRelative(string source, string prefix)
        {
            string imagesDir = Path.GetFullPath(Path.Combine(_appRoot, "images"));
            string full = Path.GetFullPath(source);
            string relativePrefix = "images" + Path.DirectorySeparatorChar;

            if (full.StartsWith(imagesDir + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            {
                return relativePrefix + Path.GetFileName(full);
            }

            Directory.CreateDirectory(imagesDir);
            string ext = Path.GetExtension(full).ToLowerInvariant();
            if (ext.Length == 0) ext = Util.DetectImageExtension(full);
            string target = Path.Combine(imagesDir, prefix + ext);
            string temp = target + ".tmp";
            File.Copy(full, temp, true);
            if (File.Exists(target)) File.Delete(target);
            File.Move(temp, target);

            // Keep only the current image for this slot.
            foreach (string old in Directory.GetFiles(imagesDir, prefix + ".*"))
            {
                if (!string.Equals(old, target, StringComparison.OrdinalIgnoreCase)) Util.SafeDelete(old);
            }
            return relativePrefix + prefix + ext;
        }

        private string ImportImageForUi(string source, string prefix)
        {
            return Path.Combine(_appRoot, ImportImageRelative(source, prefix));
        }

        private void ModeLabelKey(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
            {
                Label label = sender as Label;
                SetWallpaperMode(label != null && string.Equals(label.Text, "Images", StringComparison.OrdinalIgnoreCase));
                e.Handled = true;
            }
        }

        private void SetWallpaperMode(bool images)
        {
            _wallpaperImages.Checked = images;
            _wallpaperVideo.Checked = !images;
            if (_wallpaperVideo != null) _wallpaperVideo.Invalidate();
        }

        private void ModePillPaint(object sender, PaintEventArgs e)
        {
            RadioButton button = sender as RadioButton;
            if (button == null) return;

            bool images = _wallpaperImages != null && _wallpaperImages.Checked;
            Rectangle bounds = button.ClientRectangle;
            bounds.Inflate(-1, -1);

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = RoundedRect(bounds, bounds.Height / 2))
            using (SolidBrush track = new SolidBrush(Color.FromArgb(212, 216, 224)))
            using (SolidBrush on = new SolidBrush(Color.FromArgb(64, 120, 220)))
            using (Pen border = new Pen(Color.FromArgb(170, 176, 188)))
            {
                e.Graphics.FillPath(track, path);
                e.Graphics.DrawPath(border, path);

                int pad = 3;
                int size = Math.Max(6, bounds.Height - pad * 2);
                int x = images ? bounds.Right - pad - size : bounds.Left + pad;
                Rectangle thumb = new Rectangle(x, bounds.Top + pad, size, size);

                using (SolidBrush fill = new SolidBrush(images ? on.Color : Color.FromArgb(245, 246, 248)))
                {
                    e.Graphics.FillEllipse(fill, thumb);
                }
                e.Graphics.DrawEllipse(border, thumb);
            }

            if (button.Focused)
            {
                ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(button.ClientRectangle, -3, -3));
            }
        }

        private static GraphicsPath RoundedRect(Rectangle rect, int radius)
        {
            int d = Math.Max(1, Math.Min(radius, rect.Height / 2) * 2);
            GraphicsPath path = new GraphicsPath();
            path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static Label MakeLabel(Control parent, string text, int x, int y, int width)
        {
            Label label = new Label();
            label.Text = text;
            label.SetBounds(x, y, width, 20);
            parent.Controls.Add(label);
            return label;
        }

        private static Label MakeTickLabel(Control parent, string text, int y)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.Top = y;
            label.ForeColor = SystemColors.GrayText;
            parent.Controls.Add(label);
            return label;
        }

        // Lines the tick labels up with the slider's real thumb positions. The
        // trackbar draws its thumb inset by half a thumb width, and that size
        // depends on the display scale, so the positions are measured instead
        // of guessed from the control width.
        private void AlignSlideshowMarks()
        {
            if (_layingOutTicks) return;
            if (_imageInterval == null || _imageCrossfadeBar == null) return;
            _layingOutTicks = true;
            try
            {
                int[] intervalValues = new int[IntervalPresets.Length];
                for (int i = 0; i < intervalValues.Length; i++) intervalValues[i] = i;
                AlignTicks(_imageInterval, _imageIntervalMarks, intervalValues);
                AlignTicks(_imageCrossfadeBar, _imageCrossfadeMarks,
                    new[] { _imageCrossfadeBar.Minimum, _imageCrossfadeBar.Maximum });
            }
            finally
            {
                _layingOutTicks = false;
                UpdateIntervalControls();
                UpdateCrossfadeLabel();
            }
        }

        private static void AlignTicks(TrackBar bar, Label[] labels, int[] values)
        {
            if (labels == null || values == null) return;
            int old = bar.Value;
            for (int i = 0; i < labels.Length && i < values.Length; i++)
            {
                bar.Value = values[i];
                TrackbarRect r;
                SendMessage(bar.Handle, TbmGetThumbRect, IntPtr.Zero, out r);
                int center = bar.Left + (r.Left + r.Right) / 2;
                labels[i].Left = Math.Max(0, center - labels[i].Width / 2);
            }
            bar.Value = old;
        }

        private void LoadSettings()
        {
            _folderBox.Text = _config.VideoFolder;
            _imageFolderBox.Text = _config.ImageFolder;

            bool imagesMode = string.Equals(_config.WallpaperMode, "images",
                StringComparison.OrdinalIgnoreCase);
            _wallpaperImages.Checked = imagesMode;
            _wallpaperVideo.Checked = !imagesMode;

            bool imageSingle = string.Equals(_config.ImageMode, "single",
                StringComparison.OrdinalIgnoreCase);
            _lastImageSingleMode = imageSingle;
            _singleImageName = _config.ImageSingle ?? "";
            _imageModeSingle.Checked = imageSingle;
            _imageModeSlideshow.Checked = !imageSingle;
            UpdateImageModeControls();
            SetIntervalSelection(_config.ImageIntervalSeconds);
            _imageFineSteps.Checked = _config.ImageFineCrossfadeSteps;
            SetCrossfadeSelection(_config.ImageCrossfadeSeconds);
            _imageShuffle.Checked = _config.ImageShuffle;

            _fineSteps.Checked = _config.FineCrossfadeSteps;
            _fadeBar.Value = Clamp((int)Math.Round(_config.CrossfadeSeconds * 10.0), FadeMin, FadeMax);
            ApplyFadeStepMode();

            for (int i = 0; i < Resolutions.Length; i++)
            {
                _resolutionWidths.Add(Resolutions[i]);
                _resolutionHeights.Add(Resolutions[i] * 9 / 16);
            }
            if (_config.RenderWidth > 0 && _config.RenderHeight > 0 && !HasResolution(_config.RenderWidth, _config.RenderHeight))
            {
                _resolutionWidths.Add(_config.RenderWidth);
                _resolutionHeights.Add(_config.RenderHeight);
            }
            for (int i = 0; i < _resolutionWidths.Count; i++)
            {
                _resolution.Items.Add(_resolutionWidths[i] + " x " + _resolutionHeights[i]);
            }
            _resolution.SelectedIndex = 1;
            for (int i = 0; i < _resolutionWidths.Count; i++)
            {
                if (_resolutionWidths[i] == _config.RenderWidth &&
                    _resolutionHeights[i] == _config.RenderHeight) _resolution.SelectedIndex = i;
            }

            foreach (int rate in FrameRates) _fps.Items.Add(rate.ToString(CultureInfo.InvariantCulture));
            _fps.SelectedIndex = 1;
            for (int i = 0; i < FrameRates.Length; i++)
            {
                if (FrameRates[i] == _config.Fps) _fps.SelectedIndex = i;
            }

            _quality.Items.Add("High (CRF 16)");
            _quality.Items.Add("Balanced (CRF 18)");
            _quality.Items.Add("Smaller files (CRF 20)");
            _quality.Items.Add("Smallest (CRF 23)");
            _quality.SelectedIndex = 1;
            for (int i = 0; i < CrfValues.Length; i++)
            {
                if (CrfValues[i] == _config.Crf) _quality.SelectedIndex = i;
            }

            bool single = string.Equals(_config.PlaybackMode, "single", StringComparison.OrdinalIgnoreCase);
            _lastSingleMode = single;
            _singleName = _config.SingleClip ?? "";
            _modeSingle.Checked = single;
            _modePlaylist.Checked = !single;
            UpdateModeControls();

            bool customLock = string.Equals(_config.LockScreenMode, "custom", StringComparison.OrdinalIgnoreCase);
            bool imageLock = string.Equals(_config.LockScreenMode, "image", StringComparison.OrdinalIgnoreCase);
            _lockCustom.Checked = customLock;
            _lockImages.Checked = imageLock;
            // "frame" is the default, so anything unknown lands there too.
            if (!customLock && !imageLock) _lockFrame.Checked = true;
            _lockPath.Text = _config.LockScreenImage;
            UpdateLockControls();

            _desktopWallpaper.Checked = _config.DesktopWallpaper;
            _screensaverActive.Checked = _config.ScreensaverActive;
            bool customWallpaper = string.Equals(_config.DesktopWallpaperMode, "custom",
                StringComparison.OrdinalIgnoreCase);
            _desktopCustom.Checked = customWallpaper;
            _desktopFrame.Checked = !customWallpaper;
            _desktopPath.Text = _config.DesktopWallpaperImage;
            UpdateDesktopControls();
            UpdateModeAvailability();

            bool startup = false;
            try
            {
                startup = StartupTask.Exists(_config.TaskName) ||
                    File.Exists(Shortcut.StartupShortcutPath(_config.TaskName));
            }
            catch { }
            _startWithWindows.Checked = startup;
        }

        private void ApplyFadeStepMode()
        {
            bool fine = _fineSteps.Checked;
            int step = fine ? 5 : 10;
            _fadeBar.SmallChange = step;
            _fadeBar.LargeChange = step * 2;
            _fadeBar.TickFrequency = step;

            int snapped = SnapFade(_fadeBar.Value, step);
            if (!fine && snapped < CoarseMin) snapped = CoarseMin;
            if (snapped != _fadeBar.Value) _fadeBar.Value = snapped;
            UpdateFadeLabels();
        }

        private void FadeChanged(object sender, EventArgs e)
        {
            if (_fineSteps == null) return;
            int step = _fineSteps.Checked ? 5 : 10;
            int snapped = SnapFade(_fadeBar.Value, step);
            if (!_fineSteps.Checked && snapped < CoarseMin) snapped = CoarseMin;
            if (snapped != _fadeBar.Value)
            {
                _fadeBar.Value = snapped;
                return;
            }
            UpdateFadeLabels();
            UpdateClipFades();
        }

        private static int SnapFade(int value, int step)
        {
            int snapped = (int)Math.Round(value / (double)step, MidpointRounding.AwayFromZero) * step;
            return Clamp(snapped, FadeMin, FadeMax);
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private bool HasResolution(int width, int height)
        {
            for (int i = 0; i < _resolutionWidths.Count; i++)
            {
                if (_resolutionWidths[i] == width && _resolutionHeights[i] == height) return true;
            }
            return false;
        }

        private double FadeSeconds
        {
            get { return _fadeBar.Value / 10.0; }
        }

        private void UpdateFadeLabels()
        {
            double max = FadeSeconds;
            _fadeValue.Text = max.ToString("0.0", CultureInfo.InvariantCulture) + " s";
            _fadeValue.ForeColor = max > 5.0 ? WarnColor : SystemColors.ControlText;

            int count = _effectiveDurations.Count;
            if (count == 0)
            {
                _fadeEffective.ForeColor = SystemColors.GrayText;
                _fadeEffective.Text = _modeSingle != null && _modeSingle.Checked
                    ? "No clip selected. Tick a clip in the list."
                    : "No clips selected. Tick the clips you want to play.";
                return;
            }

            double smallest = double.MaxValue;
            double largest = 0;
            double totalFade = 0;
            double totalLength = 0;
            for (int i = 0; i < count; i++)
            {
                double fade = FadeFor(i, max);
                totalFade += fade;
                totalLength += _effectiveDurations[i];
                if (fade < smallest) smallest = fade;
                if (fade > largest) largest = fade;
            }

            string text;
            if (largest - smallest < 0.05)
            {
                text = string.Format(CultureInfo.InvariantCulture,
                    "Applied: {0:0.0} s to all {1} clip(s).", largest, count);
            }
            else
            {
                text = string.Format(CultureInfo.InvariantCulture,
                    "Applied: {0:0.0} to {1:0.0} s. The shortest clip limits it to {0:0.0} s.",
                    smallest, largest);
            }
            text += string.Format(CultureInfo.InvariantCulture,
                "  Loop length: {0:0.0} s.", totalLength - totalFade);
            if (max > 5.0 && largest < max - 0.05)
            {
                text += " Long fades are capped by the shortest clip.";
            }

            _fadeEffective.ForeColor = max > 5.0 ? WarnColor : SystemColors.ControlText;
            _fadeEffective.Text = text;
        }

        private double FadeFor(int index, double max)
        {
            int count = _effectiveDurations.Count;
            if (count == 0) return 0;
            double next = _effectiveDurations[(index + 1) % count];
            double value = max;
            if (value > _effectiveDurations[index] / 3.0) value = _effectiveDurations[index] / 3.0;
            if (value > next / 3.0) value = next / 3.0;
            if (value < 0.3) value = 0.3;
            return value;
        }

        // ---------------------------------------------------------------
        // Playlist state
        // ---------------------------------------------------------------

        private void UpdateModeControls()
        {
            if (_loading) return;
            bool single = _modeSingle.Checked;
            bool changed = single != _lastSingleMode;
            _lastSingleMode = single;

            if (changed)
            {
                if (single)
                {
                    // Keep the playlist safe before the same checkboxes start
                    // showing the single-clip choice.
                    if (_clipList.Items.Count > 0) _config.Playlist = CurrentPlaylist();
                    ApplySingleTicks();
                }
                else
                {
                    ApplyPlaylistTicks();
                }
                UpdateClipHintText();
            }

            if (_singleHint != null) _singleHint.Visible = single;
            _moveUp.Enabled = !single;
            _moveDown.Enabled = !single;
            _sortAz.Enabled = !single;
            RebuildEffective();
            UpdateClipFades();
            UpdateFadeLabels();
        }

        // "One clip" mode: the checkboxes behave like radio buttons. Checking
        // one clip clears the others and the checked clip becomes the choice.
        private void ClipItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (_loading || !_modeSingle.Checked) return;

            if (e.NewValue != CheckState.Checked)
            {
                // One clip must stay selected, so a second click on the ticked
                // clip leaves it ticked.
                e.NewValue = CheckState.Checked;
                return;
            }

            _loading = true;
            for (int i = 0; i < _clipList.Items.Count; i++)
            {
                if (i != e.Index) _clipList.Items[i].Checked = false;
            }
            _loading = false;

            if (e.Index >= 0 && e.Index < _clipList.Items.Count)
            {
                _singleName = _clipList.Items[e.Index].Text;
            }
        }

        // Checks exactly one row: the remembered clip, or the first clip when
        // that one is gone. The caller owns the _loading flag.
        private void SelectSingleRow()
        {
            int index = -1;
            if (!string.IsNullOrEmpty(_singleName))
            {
                for (int i = 0; i < _clipList.Items.Count; i++)
                {
                    if (string.Equals(_clipList.Items[i].Text, _singleName,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        index = i;
                        break;
                    }
                }
            }
            if (index < 0 && _clipList.Items.Count > 0) index = 0;

            for (int i = 0; i < _clipList.Items.Count; i++)
            {
                _clipList.Items[i].Checked = (i == index);
            }
            _singleName = index >= 0 ? _clipList.Items[index].Text : "";
        }

        private void ApplySingleTicks()
        {
            _loading = true;
            SelectSingleRow();
            _loading = false;
        }

        // Back in playlist mode, show the ticks the playlist had before the
        // single-clip choice replaced them.
        private void ApplyPlaylistTicks()
        {
            Dictionary<string, bool> enabled =
                new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            if (_config.Playlist != null)
            {
                foreach (PlaylistEntry entry in _config.Playlist)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.Name)) continue;
                    if (!enabled.ContainsKey(entry.Name)) enabled[entry.Name] = entry.Enabled;
                }
            }

            _loading = true;
            for (int i = 0; i < _clipList.Items.Count; i++)
            {
                bool on;
                _clipList.Items[i].Checked =
                    enabled.TryGetValue(_clipList.Items[i].Text, out on) ? on : false;
            }
            _loading = false;
        }

        private void UpdateClipHintText()
        {
            if (_clipHint == null) return;
            // The help paragraph was removed; the label stays for status messages.
            _clipHint.Text = "";
        }

        // ---------------------------------------------------------------
        // Image list and slideshow
        // ---------------------------------------------------------------

        private void UpdateImageModeControls()
        {
            if (_imageModeSingle == null || _imageModeSlideshow == null) return;
            bool single = _imageModeSingle.Checked;
            bool changed = single != _lastImageSingleMode;
            _lastImageSingleMode = single;

            if (changed)
            {
                if (single)
                {
                    if (_imageList.Items.Count > 0) _config.ImagePlaylist = CurrentImagePlaylist();
                    ApplySingleImageTicks();
                }
                else
                {
                    ApplyImagePlaylistTicks();
                }
                UpdateImageHintText();
            }

            if (_imageSingleHint != null) _imageSingleHint.Visible = single;
            _imageMoveUp.Enabled = !single;
            _imageMoveDown.Enabled = !single;
            _imageSortAz.Enabled = !single;
            if (_imageShuffle != null) _imageShuffle.Enabled = !single;
        }

        private void ImageItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (_loadingImages || !_imageModeSingle.Checked) return;

            if (e.NewValue != CheckState.Checked)
            {
                // One image must stay selected, so a second click on the ticked
                // image leaves it ticked.
                e.NewValue = CheckState.Checked;
                return;
            }

            _loadingImages = true;
            for (int i = 0; i < _imageList.Items.Count; i++)
            {
                if (i != e.Index) _imageList.Items[i].Checked = false;
            }
            _loadingImages = false;

            if (e.Index >= 0 && e.Index < _imageList.Items.Count)
            {
                _singleImageName = _imageList.Items[e.Index].Text;
            }
        }

        private void SelectSingleImageRow()
        {
            int index = -1;
            if (!string.IsNullOrEmpty(_singleImageName))
            {
                for (int i = 0; i < _imageList.Items.Count; i++)
                {
                    if (string.Equals(_imageList.Items[i].Text, _singleImageName,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        index = i;
                        break;
                    }
                }
            }
            if (index < 0 && _imageList.Items.Count > 0) index = 0;

            for (int i = 0; i < _imageList.Items.Count; i++)
            {
                _imageList.Items[i].Checked = (i == index);
            }
            _singleImageName = index >= 0 ? _imageList.Items[index].Text : "";
        }

        private void ApplySingleImageTicks()
        {
            _loadingImages = true;
            SelectSingleImageRow();
            _loadingImages = false;
        }

        private void ApplyImagePlaylistTicks()
        {
            Dictionary<string, bool> enabled =
                new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            if (_config.ImagePlaylist != null)
            {
                foreach (PlaylistEntry entry in _config.ImagePlaylist)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.Name)) continue;
                    if (!enabled.ContainsKey(entry.Name)) enabled[entry.Name] = entry.Enabled;
                }
            }

            _loadingImages = true;
            for (int i = 0; i < _imageList.Items.Count; i++)
            {
                bool on;
                _imageList.Items[i].Checked =
                    enabled.TryGetValue(_imageList.Items[i].Text, out on) ? on : true;
            }
            _loadingImages = false;
        }

        private void UpdateImageHintText()
        {
            if (_imageHint == null) return;
            // The help paragraph was removed; the label stays for status messages.
            _imageHint.Text = "";
        }

        private void CaptureImagePlaylist()
        {
            if (!_imageModeSingle.Checked && _imageList.Items.Count > 0)
            {
                _config.ImagePlaylist = CurrentImagePlaylist();
            }
            if (!string.IsNullOrEmpty(_singleImageName)) _config.ImageSingle = _singleImageName;
        }

        private List<PlaylistEntry> CurrentImagePlaylist()
        {
            List<PlaylistEntry> entries = new List<PlaylistEntry>();
            for (int i = 0; i < _imageList.Items.Count; i++)
            {
                entries.Add(new PlaylistEntry(_imageList.Items[i].Text, _imageList.Items[i].Checked));
            }
            return entries;
        }

        private void MoveImage(int delta)
        {
            if (_imageList.SelectedIndices.Count == 0) return;
            int index = _imageList.SelectedIndices[0];
            int target = index + delta;
            if (target < 0 || target >= _imageList.Items.Count) return;

            int[] order = new int[_imageList.Items.Count];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            order[index] = target;
            order[target] = index;
            ApplyImageOrder(order, target);
        }

        private void SortImages()
        {
            int count = _imageList.Items.Count;
            if (count < 2) return;
            int[] order = new int[count];
            for (int i = 0; i < count; i++) order[i] = i;
            Array.Sort(order, delegate(int a, int b)
            {
                return new NaturalComparer().Compare(_imageList.Items[a].Text, _imageList.Items[b].Text);
            });
            ApplyImageOrder(order, -1);
        }

        private void ApplyImageOrder(int[] order, int selectIndex)
        {
            if (order.Length != _imageList.Items.Count) return;
            _loadingImages = true;

            ListViewItem[] items = new ListViewItem[order.Length];
            List<string> paths = new List<string>();
            for (int i = 0; i < order.Length; i++)
            {
                int old = order[i];
                ListViewItem source = _imageList.Items[old];
                ListViewItem item = new ListViewItem(source.Text);
                item.Checked = source.Checked;
                item.SubItems.Add(source.SubItems[1].Text);
                items[i] = item;
                paths.Add(old < _imagePaths.Count ? _imagePaths[old] : "");
            }

            _imageList.BeginUpdate();
            _imageList.Items.Clear();
            _imageList.Items.AddRange(items);
            _imageList.EndUpdate();

            _imagePaths.Clear();
            _imagePaths.AddRange(paths);

            _loadingImages = false;
            UpdateImageHintText();

            if (selectIndex >= 0 && selectIndex < _imageList.Items.Count)
            {
                _imageList.Items[selectIndex].Selected = true;
                _imageList.Items[selectIndex].Focused = true;
                _imageList.EnsureVisible(selectIndex);
            }
        }

        private void RefreshImages()
        {
            _loadingImages = true;
            _imageList.Items.Clear();
            _imagePaths.Clear();

            string folder = Util.ResolvePath(_appRoot, _imageFolderBox.Text);
            List<string> all = MediaPipeline.ScanImages(folder);

            if (all.Count == 0)
            {
                _loadingImages = false;
                _imageHint.Text = "No images found in " + folder +
                    ". Add pictures there and press Refresh.";
                PopulateLockImageCombo(folder);
                return;
            }

            bool hasPlaylist = _config.ImagePlaylist != null && _config.ImagePlaylist.Count > 0;
            Dictionary<string, PlaylistEntry> byName =
                new Dictionary<string, PlaylistEntry>(StringComparer.OrdinalIgnoreCase);
            if (_config.ImagePlaylist != null)
            {
                foreach (PlaylistEntry entry in _config.ImagePlaylist)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.Name)) continue;
                    if (!byName.ContainsKey(entry.Name)) byName[entry.Name] = entry;
                }
            }

            Dictionary<string, string> filesByName =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in all) filesByName[Path.GetFileName(file)] = file;

            List<string> ordered = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (_config.ImagePlaylist != null)
            {
                foreach (PlaylistEntry entry in _config.ImagePlaylist)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.Name)) continue;
                    string name = entry.Name.Trim();
                    if (!seen.Add(name)) continue;
                    string file;
                    if (filesByName.TryGetValue(name, out file)) ordered.Add(file);
                }
            }
            foreach (string file in all)
            {
                string name = Path.GetFileName(file);
                if (seen.Add(name)) ordered.Add(file);
            }

            foreach (string file in ordered)
            {
                string name = Path.GetFileName(file);
                PlaylistEntry entry;
                // New pictures start ticked. Pictures that were deliberately
                // unticked keep their saved state.
                bool enabled = !hasPlaylist;
                if (byName.TryGetValue(name, out entry)) enabled = entry.Enabled;
                else enabled = true;

                ListViewItem item = new ListViewItem(name);
                item.Checked = enabled;
                item.SubItems.Add(FormatImageSize(file));
                _imageList.Items.Add(item);
                _imagePaths.Add(file);
            }

            if (_imageModeSingle.Checked) SelectSingleImageRow();

            _loadingImages = false;
            UpdateImageHintText();
            PopulateLockImageCombo(folder);
        }

        private static string FormatImageSize(string file)
        {
            try
            {
                long bytes = new FileInfo(file).Length;
                if (bytes >= 1024L * 1024L)
                {
                    return (bytes / (1024.0 * 1024.0)).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
                }
                return (bytes / 1024.0).ToString("0", CultureInfo.InvariantCulture) + " KB";
            }
            catch { return ""; }
        }

        private void PopulateLockImageCombo(string folder)
        {
            if (_lockImageCombo == null) return;
            string wanted = _lockImageCombo.SelectedItem as string;
            if (string.IsNullOrEmpty(wanted)) wanted = Path.GetFileName(_config.LockScreenImage);

            _lockImageCombo.BeginUpdate();
            _lockImageCombo.Items.Clear();
            foreach (string file in MediaPipeline.ScanImages(folder))
            {
                _lockImageCombo.Items.Add(Path.GetFileName(file));
            }
            _lockImageCombo.EndUpdate();

            int index = -1;
            for (int i = 0; i < _lockImageCombo.Items.Count; i++)
            {
                if (string.Equals(_lockImageCombo.Items[i].ToString(), wanted,
                    StringComparison.OrdinalIgnoreCase))
                {
                    index = i;
                    break;
                }
            }
            if (index >= 0) _lockImageCombo.SelectedIndex = index;
            else if (_lockImageCombo.Items.Count > 0 && _lockImageCombo.SelectedIndex < 0)
                _lockImageCombo.SelectedIndex = 0;
            UpdateLockControls();
        }

        private void BrowseImageFolder()
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Choose the folder that holds your wallpaper images";
                dialog.ShowNewFolderButton = true;
                string current = Util.ResolvePath(_appRoot, _imageFolderBox.Text);
                string start = InitialPickerFolder(_imageFolderBox.Text, true);
                if (Directory.Exists(start)) dialog.SelectedPath = start;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _lastPickerFolder = dialog.SelectedPath;
                    if (!string.Equals(dialog.SelectedPath, current, StringComparison.OrdinalIgnoreCase))
                    {
                        _config.ImagePlaylist = new List<PlaylistEntry>();
                        _config.ImageSingle = "";
                    }
                    _imageFolderBox.Text = dialog.SelectedPath;
                    RefreshImages();
                }
            }
        }

        private void BrowseImageFiles()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Choose wallpaper images";
                dialog.Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp|All files|*.*";
                dialog.Multiselect = true;
                dialog.CheckFileExists = true;
                dialog.InitialDirectory = InitialPickerFolder(_imageFolderBox.Text, true);
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    if (dialog.FileNames.Length > 0)
                        _lastPickerFolder = Path.GetDirectoryName(dialog.FileNames[0]);
                    AddImages(dialog.FileNames);
                }
            }
        }

        private void AddImages(string[] paths)
        {
            string folder = Util.ResolvePath(_appRoot, _imageFolderBox.Text);
            try { Directory.CreateDirectory(folder); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not create the image folder: " + ex.Message,
                    "LiveBackground", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            List<string> sources = new List<string>();
            foreach (string path in paths)
            {
                if (File.Exists(path) && MediaPipeline.IsImage(path)) sources.Add(path);
                else if (Directory.Exists(path)) sources.AddRange(MediaPipeline.ScanImages(path));
            }

            if (sources.Count == 0)
            {
                MessageBox.Show(this, "No supported image files were found in what you dropped.",
                    "LiveBackground", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            int copied = 0;
            int skipped = 0;
            string firstError = "";
            foreach (string source in sources)
            {
                string sourceFull = Path.GetFullPath(source);
                if (string.Equals(Path.GetDirectoryName(sourceFull), folder,
                    StringComparison.OrdinalIgnoreCase))
                {
                    skipped++;
                    continue;
                }

                try
                {
                    string target = UniquePath(folder, Path.GetFileName(sourceFull));
                    File.Copy(sourceFull, target, false);
                    copied++;
                }
                catch (Exception ex)
                {
                    if (firstError.Length == 0) firstError = ex.Message;
                }
            }

            // Keep the ticks and order the user already made before refreshing.
            CaptureImagePlaylist();
            RefreshImages();

            string message;
            if (copied == 0 && skipped > 0)
            {
                message = "Those images are already in this folder.";
            }
            else if (copied == 0)
            {
                message = "Could not add the images." +
                    (firstError.Length > 0 ? " " + firstError : "");
            }
            else
            {
                message = "Added " + copied + " image(s) to " + folder + ". " +
                    (_imageModeSingle.Checked
                        ? "They are in the list; pick the one you want."
                        : "They are ticked, so they join the slideshow.");
                if (skipped > 0) message += " " + skipped + " already in the folder.";
                if (firstError.Length > 0) message += " One file failed: " + firstError;
            }
            _imageHint.Text = message;

            if (copied > 0)
            {
                Ui.Notify("LiveBackground", "Added " + copied + " image(s).");
            }
        }

        private void OpenImageFolder()
        {
            string folder = Util.ResolvePath(_appRoot, _imageFolderBox.Text);
            if (!Directory.Exists(folder))
            {
                try { Directory.CreateDirectory(folder); }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Could not open the folder: " + ex.Message,
                        "LiveBackground", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }
            try { System.Diagnostics.Process.Start("explorer.exe", Util.Quote(folder)); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not open the folder: " + ex.Message,
                    "LiveBackground", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ImagePanelDropEnter(object sender, DragEventArgs e)
        {
            bool hasImage = FirstDroppedImage(e.Data) != null;
            e.Effect = hasImage ? DragDropEffects.Copy : DragDropEffects.None;
            if (hasImage) _imageDropPanel.BackColor = DropHotColor;
        }

        private void ImagePanelDropLeave(object sender, EventArgs e)
        {
            if (_imageDropPanel != null) _imageDropPanel.BackColor = DropIdleColor;
        }

        private void ImagePanelDropDrop(object sender, DragEventArgs e)
        {
            if (_imageDropPanel != null) _imageDropPanel.BackColor = DropIdleColor;
            if (e.Data == null || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files != null) AddImages(files);
        }

        private void UpdateIntervalControls()
        {
            if (_imageInterval == null || _imageCustomInterval == null) return;
            bool custom = _imageCustomIntervalCheck != null && _imageCustomIntervalCheck.Checked;
            _imageInterval.Enabled = !custom;
            _imageCustomInterval.Visible = custom;
            _imageCustomInterval.Enabled = custom;
            if (_imageSecondsLabel != null) _imageSecondsLabel.Visible = custom;
            if (_imageIntervalValue != null)
            {
                _imageIntervalValue.Text = custom
                    ? "Custom: " + ((int)_imageCustomInterval.Value) + " seconds"
                    : FormatIntervalSeconds(IntervalPresets[Clamp(_imageInterval.Value, 0, IntervalPresets.Length - 1)]);
            }
        }

        private void SetIntervalSelection(int seconds)
        {
            if (_imageInterval == null) return;
            int index = -1;
            for (int i = 0; i < IntervalPresets.Length; i++)
            {
                if (IntervalPresets[i] == seconds) { index = i; break; }
            }
            if (index >= 0)
            {
                _imageInterval.Value = index;
                if (_imageCustomIntervalCheck != null) _imageCustomIntervalCheck.Checked = false;
                _imageCustomInterval.Value = Clamp(seconds, 1, 86400);
            }
            else
            {
                // Snap the slider to the closest preset for when Custom is turned off.
                int nearest = 0;
                int best = int.MaxValue;
                for (int i = 0; i < IntervalPresets.Length; i++)
                {
                    int diff = Math.Abs(IntervalPresets[i] - seconds);
                    if (diff < best) { best = diff; nearest = i; }
                }
                _imageInterval.Value = nearest;
                if (_imageCustomIntervalCheck != null) _imageCustomIntervalCheck.Checked = true;
                _imageCustomInterval.Value = Clamp(seconds, 1, 86400);
            }
            UpdateIntervalControls();
        }

        private int SelectedIntervalSeconds()
        {
            if (_imageCustomIntervalCheck != null && _imageCustomIntervalCheck.Checked)
            {
                return (int)_imageCustomInterval.Value;
            }
            int index = Clamp(_imageInterval.Value, 0, IntervalPresets.Length - 1);
            return IntervalPresets[index];
        }

        private void SetCrossfadeSelection(double seconds)
        {
            if (_imageCrossfadeBar == null) return;
            if (double.IsNaN(seconds) || seconds < 0) seconds = 0;
            if (seconds > 5.0) seconds = 5.0;
            ApplyImageCrossfadeStepMode();
            _imageCrossfadeBar.Value = SnapImageCrossfade(
                Clamp((int)Math.Round(seconds * 2.0), 0, 10));
            UpdateCrossfadeLabel();
        }

        // The picture crossfade runs from 0 to 5 seconds. With the
        // half-second option off the slider snaps to whole seconds; on it
        // moves in 0.5 s steps. The trackbar always stores half-seconds,
        // so a coarse value is simply an even one.
        private void ApplyImageCrossfadeStepMode()
        {
            if (_imageCrossfadeBar == null) return;
            bool fine = _imageFineSteps != null && _imageFineSteps.Checked;
            _imageCrossfadeBar.SmallChange = fine ? 1 : 2;
            _imageCrossfadeBar.LargeChange = fine ? 2 : 4;
            _imageCrossfadeBar.TickFrequency = fine ? 1 : 2;
            int snapped = SnapImageCrossfade(_imageCrossfadeBar.Value);
            if (snapped != _imageCrossfadeBar.Value) _imageCrossfadeBar.Value = snapped;
            UpdateCrossfadeLabel();
        }

        private int SnapImageCrossfade(int value)
        {
            value = Clamp(value, 0, 10);
            bool fine = _imageFineSteps != null && _imageFineSteps.Checked;
            if (!fine && (value % 2) != 0) value = (value + 1) / 2 * 2;
            return Clamp(value, 0, 10);
        }

        private double SelectedCrossfadeSeconds()
        {
            if (_imageCrossfadeBar == null) return 2.0;
            return _imageCrossfadeBar.Value / 2.0;
        }

        private void UpdateCrossfadeLabel()
        {
            if (_imageCrossfadeBar == null || _imageCrossfadeValue == null) return;
            double seconds = _imageCrossfadeBar.Value / 2.0;
            _imageCrossfadeValue.Text = seconds < 0.05 ? "Off (instant)" : seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s";
        }

        private static string FormatIntervalSeconds(int seconds)
        {
            return seconds == 60 ? "1 minute" : seconds + " seconds";
        }

        private string InitialPickerFolder(string current, bool currentIsFolder)
        {
            string dir = "";
            if (!string.IsNullOrEmpty(current))
            {
                string full = Util.ResolvePath(_appRoot, current);
                dir = currentIsFolder ? full : Path.GetDirectoryName(full);
            }
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) dir = _lastPickerFolder;
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                dir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                dir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return dir;
        }

        private static string RelativeToAppRoot(string appRoot, string full)
        {
            string root = Path.GetFullPath(appRoot).TrimEnd(Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            string path = Path.GetFullPath(full);
            if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return path.Substring(root.Length);
            }
            return path;
        }

        // Pauses mpv so the desktop wallpaper shows and the video file is no
        // longer locked. Useful before deleting or replacing a clip.
        private void ToggleVideo()
        {
            Program.SetPaused(!Program.IsPaused);
            UpdateVideoToggle();
            Ui.Notify("LiveBackground", Program.IsPaused
                ? "Video stopped. You can delete or replace files now; the desktop wallpaper is showing."
                : "Video started.");
        }

        private void UpdateVideoToggle()
        {
            if (_videoToggle == null) return;
            _videoToggle.Text = Program.IsPaused ? "Start video" : "Stop video";
        }

        private void RebuildEffective()
        {
            _effectiveDurations = new List<double>();
            _rowToEffective = new int[_clipList.Items.Count];
            for (int i = 0; i < _rowToEffective.Length; i++) _rowToEffective[i] = -1;

            if (_modeSingle != null && _modeSingle.Checked)
            {
                int index = SingleIndex();
                if (index >= 0 && index < _clipDurations.Count && _clipDurations[index] >= 1.0)
                {
                    _rowToEffective[index] = 0;
                    _effectiveDurations.Add(_clipDurations[index]);
                }
                return;
            }

            for (int i = 0; i < _clipList.Items.Count && i < _clipDurations.Count; i++)
            {
                if (!_clipList.Items[i].Checked) continue;
                if (_clipDurations[i] < 1.0) continue;
                _rowToEffective[i] = _effectiveDurations.Count;
                _effectiveDurations.Add(_clipDurations[i]);
            }
        }

        private int SingleIndex()
        {
            for (int i = 0; i < _clipList.Items.Count; i++)
            {
                if (_clipList.Items[i].Checked) return i;
            }
            return -1;
        }

        private void CapturePlaylist()
        {
            // In single mode the ticks are the single-clip choice, so they must
            // not overwrite the playlist captured when the mode was switched.
            if (!_modeSingle.Checked) _config.Playlist = CurrentPlaylist();
            if (!string.IsNullOrEmpty(_singleName)) _config.SingleClip = _singleName;
        }

        private List<PlaylistEntry> CurrentPlaylist()
        {
            List<PlaylistEntry> entries = new List<PlaylistEntry>();
            for (int i = 0; i < _clipList.Items.Count; i++)
            {
                entries.Add(new PlaylistEntry(_clipList.Items[i].Text, _clipList.Items[i].Checked));
            }
            return entries;
        }

        private void MoveClip(int delta)
        {
            if (_clipList.SelectedIndices.Count == 0) return;
            int index = _clipList.SelectedIndices[0];
            int target = index + delta;
            if (target < 0 || target >= _clipList.Items.Count) return;

            int[] order = new int[_clipList.Items.Count];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            order[index] = target;
            order[target] = index;
            ApplyOrder(order, target);
        }

        private void SortClips()
        {
            int count = _clipList.Items.Count;
            if (count < 2) return;
            int[] order = new int[count];
            for (int i = 0; i < count; i++) order[i] = i;
            Array.Sort(order, delegate(int a, int b)
            {
                return new NaturalComparer().Compare(_clipList.Items[a].Text, _clipList.Items[b].Text);
            });
            ApplyOrder(order, -1);
        }

        private void ApplyOrder(int[] order, int selectIndex)
        {
            if (order.Length != _clipList.Items.Count) return;
            _loading = true;

            ListViewItem[] items = new ListViewItem[order.Length];
            List<string> paths = new List<string>();
            List<double> durations = new List<double>();
            for (int i = 0; i < order.Length; i++)
            {
                int old = order[i];
                ListViewItem source = _clipList.Items[old];
                ListViewItem item = new ListViewItem(source.Text);
                item.Checked = source.Checked;
                item.SubItems.Add(source.SubItems[1].Text);
                item.SubItems.Add(source.SubItems[2].Text);
                items[i] = item;
                paths.Add(old < _clipPaths.Count ? _clipPaths[old] : "");
                durations.Add(old < _clipDurations.Count ? _clipDurations[old] : -1);
            }

            _clipList.BeginUpdate();
            _clipList.Items.Clear();
            _clipList.Items.AddRange(items);
            _clipList.EndUpdate();

            _clipPaths.Clear();
            _clipPaths.AddRange(paths);
            _clipDurations.Clear();
            _clipDurations.AddRange(durations);

            _loading = false;
            RebuildEffective();
            UpdateClipFades();
            UpdateFadeLabels();

            if (selectIndex >= 0 && selectIndex < _clipList.Items.Count)
            {
                _clipList.Items[selectIndex].Selected = true;
                _clipList.Items[selectIndex].Focused = true;
                _clipList.EnsureVisible(selectIndex);
            }
        }

        private void UpdateLockControls()
        {
            if (_lockCustom == null || _lockPath == null) return;
            bool custom = _lockCustom.Checked;
            bool image = _lockImages != null && _lockImages.Checked;
            _lockPath.Enabled = custom;
            _lockBrowse.Enabled = custom;
            if (_lockDrop != null) _lockDrop.Enabled = custom;
            if (_lockImageCombo != null)
            {
                _lockImageCombo.Enabled = image && _lockImageCombo.Items.Count > 0;
            }
        }

        private void UpdateDesktopControls()
        {
            if (_desktopWallpaper == null) return;
            bool images = _wallpaperImages != null && _wallpaperImages.Checked;
            _desktopWallpaper.Enabled = !images;
            if (_desktopGroup != null) _desktopGroup.Enabled = !images;
            if (images) return;

            bool enabled = _desktopWallpaper.Checked;
            bool custom = enabled && _desktopCustom.Checked;
            _desktopFrame.Enabled = enabled;
            _desktopCustom.Enabled = enabled;
            _desktopPath.Enabled = custom;
            _desktopBrowse.Enabled = custom;
            if (_desktopDrop != null) _desktopDrop.Enabled = custom;
        }

        private void BrowseFolder()
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Choose the folder that holds your wallpaper videos";
                dialog.ShowNewFolderButton = true;
                string current = Util.ResolvePath(_appRoot, _folderBox.Text);
                string start = InitialPickerFolder(_folderBox.Text, true);
                if (Directory.Exists(start)) dialog.SelectedPath = start;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _lastPickerFolder = dialog.SelectedPath;
                    if (!string.Equals(dialog.SelectedPath, current, StringComparison.OrdinalIgnoreCase))
                    {
                        // A different folder gets a fresh playlist: everything
                        // in it starts ticked. The old order belongs to the old
                        // folder and would only confuse the list.
                        _config.Playlist = new List<PlaylistEntry>();
                        _config.SingleClip = "";
                    }
                    _folderBox.Text = dialog.SelectedPath;
                    RefreshClips();
                }
            }
        }

        private void BrowseLockImage()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Choose a lock-screen image";
                dialog.Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp";
                dialog.CheckFileExists = true;
                dialog.InitialDirectory = InitialPickerFolder(_lockPath.Text, false);
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _lastPickerFolder = Path.GetDirectoryName(dialog.FileName);
                    ApplyLockImage(dialog.FileName);
                }
            }
        }

        private void BrowseDesktopImage()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Choose a desktop wallpaper image";
                dialog.Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp";
                dialog.CheckFileExists = true;
                dialog.InitialDirectory = InitialPickerFolder(_desktopPath.Text, false);
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _lastPickerFolder = Path.GetDirectoryName(dialog.FileName);
                    ApplyDesktopImage(dialog.FileName);
                }
            }
        }

        private void OpenFolder()
        {
            try
            {
                string folder = Util.ResolvePath(_appRoot, _folderBox.Text);
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                System.Diagnostics.Process.Start("explorer.exe", Util.Quote(folder));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not open the folder: " + ex.Message,
                    "LiveBackground", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // ---------------------------------------------------------------
        // Clip list and duration probing
        // ---------------------------------------------------------------

        private void RefreshClips()
        {
            _loading = true;
            _clipList.Items.Clear();
            _clipPaths.Clear();
            _clipDurations.Clear();
            _effectiveDurations = new List<double>();
            _rowToEffective = new int[0];

            string folder = Util.ResolvePath(_appRoot, _folderBox.Text);
            List<string> all = MediaPipeline.ScanVideos(folder);

            if (all.Count == 0)
            {
                _loading = false;
                RebuildEffective();
                UpdateFadeLabels();
                _clipHint.Text = "No videos found in " + folder +
                    ". Add files there and press Refresh.";
                return;
            }

            bool hasPlaylist = _config.Playlist != null && _config.Playlist.Count > 0;
            Dictionary<string, PlaylistEntry> byName =
                new Dictionary<string, PlaylistEntry>(StringComparer.OrdinalIgnoreCase);
            if (_config.Playlist != null)
            {
                foreach (PlaylistEntry entry in _config.Playlist)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.Name)) continue;
                    if (!byName.ContainsKey(entry.Name)) byName[entry.Name] = entry;
                }
            }

            Dictionary<string, string> filesByName =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in all) filesByName[Path.GetFileName(file)] = file;

            List<string> ordered = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (_config.Playlist != null)
            {
                foreach (PlaylistEntry entry in _config.Playlist)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.Name)) continue;
                    string name = entry.Name.Trim();
                    if (!seen.Add(name)) continue;
                    string file;
                    if (filesByName.TryGetValue(name, out file)) ordered.Add(file);
                }
            }
            foreach (string file in all)
            {
                string name = Path.GetFileName(file);
                if (seen.Add(name)) ordered.Add(file);
            }

            foreach (string file in ordered)
            {
                string name = Path.GetFileName(file);
                PlaylistEntry entry;
                bool enabled = !hasPlaylist;
                if (byName.TryGetValue(name, out entry)) enabled = entry.Enabled;

                ListViewItem item = new ListViewItem(name);
                item.Checked = enabled;
                item.SubItems.Add("...");
                item.SubItems.Add("");
                _clipList.Items.Add(item);
                _clipPaths.Add(file);
                _clipDurations.Add(-1);
            }

            if (_modeSingle.Checked) SelectSingleRow();

            _loading = false;
            RebuildEffective();
            UpdateClipFades();
            UpdateFadeLabels();

            UpdateClipHintText();

            string ffprobe = Paths.Tool(_config.FfprobePath);
            List<string> probePaths = new List<string>(_clipPaths);
            int generation = ++_probeGeneration;

            Thread thread = new Thread(delegate()
            {
                Dictionary<string, double> durations =
                    new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                foreach (string clip in probePaths)
                {
                    double duration;
                    string error;
                    if (MediaPipeline.ProbeDuration(ffprobe, clip, out duration, out error))
                    {
                        durations[Path.GetFileName(clip)] = duration;
                    }
                    else
                    {
                        durations[Path.GetFileName(clip)] = -1;
                    }
                }
                try
                {
                    BeginInvoke(new Action(delegate()
                    {
                        if (generation != _probeGeneration || IsDisposed || !IsHandleCreated) return;
                        ApplyDurations(durations);
                    }));
                }
                catch { }
            });
            thread.IsBackground = true;
            thread.Name = "livebackground-probe";
            thread.Start();
        }

        private void ApplyDurations(Dictionary<string, double> durations)
        {
            if (IsDisposed || _clipList == null || _clipList.IsDisposed) return;

            while (_clipDurations.Count < _clipList.Items.Count) _clipDurations.Add(-1);
            if (_clipDurations.Count > _clipList.Items.Count)
            {
                _clipDurations.RemoveRange(_clipList.Items.Count,
                    _clipDurations.Count - _clipList.Items.Count);
            }

            for (int i = 0; i < _clipList.Items.Count; i++)
            {
                ListViewItem item = _clipList.Items[i];
                double duration;
                if (!durations.TryGetValue(item.Text, out duration)) duration = -1;
                _clipDurations[i] = duration;
                if (duration < 0)
                {
                    item.SubItems[1].Text = "unreadable";
                }
                else
                {
                    item.SubItems[1].Text = duration.ToString("0.0", CultureInfo.InvariantCulture) + " s";
                }
            }

            RebuildEffective();
            UpdateClipFades();
            UpdateFadeLabels();
        }

        private void UpdateClipFades()
        {
            for (int i = 0; i < _clipList.Items.Count; i++)
            {
                if (i >= _rowToEffective.Length) continue;
                int effective = _rowToEffective[i];
                if (effective < 0)
                {
                    if (i >= _clipDurations.Count || _clipDurations[i] < 0)
                    {
                        _clipList.Items[i].SubItems[2].Text = "";
                    }
                    else if (_clipDurations[i] < 1.0)
                    {
                        _clipList.Items[i].SubItems[2].Text = "skipped";
                    }
                    else
                    {
                        _clipList.Items[i].SubItems[2].Text =
                            (_modeSingle != null && _modeSingle.Checked) ? "" : "off";
                    }
                    continue;
                }
                double fade = FadeFor(effective, FadeSeconds);
                _clipList.Items[i].SubItems[2].Text =
                    fade.ToString("0.0", CultureInfo.InvariantCulture) + " s";
            }
        }

        // ---------------------------------------------------------------
        // Save
        // ---------------------------------------------------------------

        private void SaveClicked(object sender, EventArgs e)
        {
            // Everything the controls currently say, validated exactly
            // like "Rebuild now" does. Null means a message box has
            // already explained the problem.
            AppConfig updated = CollectConfig();
            if (updated == null) return;

            bool startupChanged = _startWithWindows.Checked != IsStartupEnabled(updated.TaskName);
            if (startupChanged)
            {
                InstallState state = InstallState.Load(_appRoot);
                Action<string> log = delegate(string message) { Log.Info("settings: " + message); };
                if (_startWithWindows.Checked)
                {
                    Installer.CreateStartup(_appRoot, updated, state, log);
                    state.Save(_appRoot);
                }
                else
                {
                    Installer.RemoveStartup(_appRoot, updated, log);
                }
            }

            try
            {
                ConfigStore.Save(_appRoot, updated);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not save the settings: " + ex.Message,
                    "LiveBackground", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            Program.ApplyConfig(updated);
            Close();
        }

        // "Rebuild now" in the settings window: apply what the form
        // currently shows to the running wallpaper, but keep the window
        // open and leave saving to "Save and apply". A cached video
        // selection swaps in instantly; a new one is encoded while the
        // progress card follows it. Image settings apply instantly.
        private void RebuildNowClicked(object sender, EventArgs e)
        {
            AppConfig staged = CollectConfig();
            if (staged == null) return;
            Program.ApplyConfig(staged);
        }

        // Reads the whole form into the config that "Save and apply"
        // writes and "Rebuild now" applies to the running wallpaper,
        // without touching the startup task or the disk. Returns null -
        // after showing a message box - when something is invalid.
        private AppConfig CollectConfig()
        {
            bool imagesMode = _wallpaperImages.Checked;
            string folder = _folderBox.Text.Trim();
            string imageFolder = _imageFolderBox.Text.Trim();

            if (imagesMode)
            {
                if (imageFolder.Length == 0)
                {
                    MessageBox.Show(this, "Choose an image folder first.", "LiveBackground",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return null;
                }
                string resolvedImages = Util.ResolvePath(_appRoot, imageFolder);
                if (MediaPipeline.ScanImages(resolvedImages).Count == 0)
                {
                    MessageBox.Show(this, "Add at least one picture to " + resolvedImages +
                        " and press Refresh.", "LiveBackground",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return null;
                }
                if (_imageList.CheckedItems.Count == 0)
                {
                    MessageBox.Show(this, _imageModeSingle.Checked
                            ? "Tick the image you want to show."
                            : "Tick at least one image for the slideshow.",
                        "LiveBackground", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return null;
                }
            }
            else
            {
                if (folder.Length == 0)
                {
                    MessageBox.Show(this, "Choose a video folder first.", "LiveBackground",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return null;
                }

                if (_modePlaylist.Checked && _clipList.CheckedItems.Count == 0)
                {
                    MessageBox.Show(this, "Tick at least one clip in the playlist.",
                        "LiveBackground", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return null;
                }
                if (_modeSingle.Checked && SingleIndex() < 0)
                {
                    MessageBox.Show(this, "Tick the clip you want to play.",
                        "LiveBackground", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return null;
                }
            }

            // "First frame of the video loop" needs no stored path; it follows
            // whatever the wallpaper builds.
            string lockMode = "frame";
            string lockImage = "";
            if (_lockCustom.Checked)
            {
                string image = _lockPath.Text.Trim();
                string full = Util.ResolvePath(_appRoot, image);
                if (image.Length == 0 || !File.Exists(full))
                {
                    MessageBox.Show(this, "Choose an existing image for the lock screen.",
                        "LiveBackground", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return null;
                }
                try
                {
                    lockImage = ImportImageRelative(full, "lock-screen");
                    lockMode = "custom";
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Could not copy the lock-screen image: " + ex.Message,
                        "LiveBackground", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return null;
                }
            }
            else if (_lockImages.Checked)
            {
                string selected = _lockImageCombo.SelectedItem as string;
                if (string.IsNullOrEmpty(selected))
                {
                    MessageBox.Show(this, "Add pictures to your image folder first, then pick one " +
                        "for the lock screen.", "LiveBackground",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return null;
                }
                string full = Path.Combine(Util.ResolvePath(_appRoot,
                    imageFolder.Length == 0 ? _config.ImageFolder : imageFolder), selected);
                if (!File.Exists(full))
                {
                    MessageBox.Show(this, "That lock-screen image no longer exists. Pick another one.",
                        "LiveBackground", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return null;
                }
                lockMode = "image";
                lockImage = RelativeToAppRoot(_appRoot, full);
            }

            // Remember the image ticks and order before cloning the config. In
            // one-image mode the ticks are the chosen picture, so the playlist
            // captured when the mode was switched is kept.
            CaptureImagePlaylist();

            bool useCustomWallpaper = !imagesMode && _desktopWallpaper.Checked && _desktopCustom.Checked;
            string desktopImage = _config.DesktopWallpaperImage;
            if (useCustomWallpaper)
            {
                string image = _desktopPath.Text.Trim();
                string full = Util.ResolvePath(_appRoot, image);
                if (image.Length == 0 || !File.Exists(full))
                {
                    MessageBox.Show(this, "Choose an existing image for the desktop wallpaper.",
                        "LiveBackground", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return null;
                }
                try
                {
                    desktopImage = ImportImageRelative(full, "desktop-wallpaper");
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Could not copy the desktop image: " + ex.Message,
                        "LiveBackground", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return null;
                }
            }

            int resIndex = _resolution.SelectedIndex;
            if (resIndex < 0) resIndex = 0;

            AppConfig updated = _config.Clone();
            updated.WallpaperMode = imagesMode ? "images" : "video";
            updated.VideoFolder = folder.Length == 0 ? _config.VideoFolder : folder;
            updated.ImageFolder = imageFolder.Length == 0 ? _config.ImageFolder : imageFolder;
            updated.CrossfadeSeconds = FadeSeconds;
            updated.FineCrossfadeSteps = _fineSteps.Checked;
            updated.RenderWidth = _resolutionWidths[resIndex];
            updated.RenderHeight = _resolutionHeights[resIndex];
            updated.Fps = FrameRates[_fps.SelectedIndex];
            updated.Crf = CrfValues[_quality.SelectedIndex];
            updated.PlaybackMode = _modeSingle.Checked ? "single" : "playlist";
            updated.SingleClip = string.IsNullOrEmpty(_singleName) ? _config.SingleClip : _singleName;
            // In single mode the ticks show the chosen clip, not the playlist,
            // so write back the playlist captured when the mode was switched.
            List<PlaylistEntry> videoPlaylist = _modeSingle.Checked
                ? (_config.Playlist ?? new List<PlaylistEntry>())
                : CurrentPlaylist();
            if (videoPlaylist.Count == 0 && _config.Playlist != null)
            {
                // An empty video folder must not wipe a playlist saved earlier
                // while the user is working in Images mode.
                videoPlaylist = _config.Playlist;
            }
            updated.Playlist = videoPlaylist;
            updated.ImageMode = _imageModeSingle.Checked ? "single" : "slideshow";
            updated.ImageSingle = string.IsNullOrEmpty(_singleImageName)
                ? _config.ImageSingle
                : _singleImageName;
            updated.ImageIntervalSeconds = SelectedIntervalSeconds();
            updated.ImageCrossfadeSeconds = SelectedCrossfadeSeconds();
            updated.ImageFineCrossfadeSteps = _imageFineSteps.Checked;
            updated.ImageShuffle = _imageShuffle.Checked && !_imageModeSingle.Checked;
            updated.LockScreenMode = lockMode;
            updated.LockScreenImage = lockImage;
            updated.DesktopWallpaper = imagesMode
                ? _config.DesktopWallpaper
                : _desktopWallpaper.Checked;
            updated.DesktopWallpaperMode = useCustomWallpaper ? "custom" : "frame";
            updated.DesktopWallpaperImage = useCustomWallpaper
                ? desktopImage
                : _config.DesktopWallpaperImage;
            updated.ScreensaverActive = _screensaverActive.Checked;
            updated.Normalize();

            return updated;
        }

        private static bool IsStartupEnabled(string taskName)
        {
            try
            {
                return StartupTask.Exists(taskName) ||
                    File.Exists(Shortcut.StartupShortcutPath(taskName));
            }
            catch { return false; }
        }
    }
}
