using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    public partial class SettingsForm : Form
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
        private CheckBox _screensaverVideo;
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

        // Multi Monitor: the shared Windows display-mode row, plus one
        // settings slot per extra screen ("2nd Screen", "3rd Screen"). Each
        // slot is remembered by monitor device name, so its settings survive
        // replugging and reordering.
        private TabPage _secondTab;
        private Label _multiStatus;
        private TabControl _screenTabs;
        private ScreenSlot _slot2;
        private ScreenSlot _slot3;
        // Display-mode row: Windows Extended vs Duplicate. The radios reflect
        // what Windows currently does and only change it when clicked.
        private RadioButton _secondExtend;
        private RadioButton _secondDuplicate;
        private Button _secondDisplaySettings;
        private bool _secondDuplicateLayout;
        private bool _updatingTopology;
        private bool _hasSecondScreen;
        // Re-checks the display layout while the window is open, so plugging a
        // screen or changing the Windows mode updates the tab without a save.
        private System.Windows.Forms.Timer _screenTimer;
        private string _lastScreenSignature = "";
        private List<ScreenInfo> _screens = new List<ScreenInfo>();

        private const int TbmGetThumbRect = 0x0419;

        [StructLayout(LayoutKind.Sequential)]
        private struct TrackbarRect
        {
            public int Left, Top, Right, Bottom;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam,
            out TrackbarRect lParam);

        // Subtle, Windows 11-inspired light palette (no dark mode).
        private static readonly Color UiBackground = Color.FromArgb(243, 243, 243);
        private static readonly Color UiSurface = Color.White;
        private static readonly Color UiText = Color.FromArgb(28, 28, 28);
        private static readonly Color UiMutedText = Color.FromArgb(96, 96, 96);
        private static readonly Color UiAccent = Color.FromArgb(0, 120, 212);
        private static readonly Color UiBorder = Color.FromArgb(229, 229, 229);
        private static readonly Color UiButtonFace = Color.FromArgb(251, 251, 251);
        private static readonly Color UiButtonHover = Color.FromArgb(246, 246, 246);
        private static readonly Color UiInputBack = Color.White;
        private static readonly Color DropIdleColor = Color.FromArgb(250, 250, 250);
        private static readonly Color DropHotColor = Color.FromArgb(230, 241, 251);
        private static readonly Color WarnColor = Color.FromArgb(157, 93, 0);

        private readonly List<string> _clipPaths = new List<string>();
        private readonly List<double> _clipDurations = new List<double>();
        private int[] _rowToEffective = Array.Empty<int>();
        private List<double> _effectiveDurations = new List<double>();
        private bool _loading;
        // In "Single clip" mode the list checkboxes act like radio buttons, so the
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
            try
            {
                List<ScreenInfo> initial = Screens.Enumerate();
                _screens = initial;
                _hasSecondScreen = initial.Count >= 2;
                _secondDuplicateLayout = Screens.IsDuplicate(initial);
                // Bind an old config's single second-screen block to the first
                // extra monitor, exactly like the running app does at startup.
                _config.BindLegacyScreens(initial);
            }
            catch { _hasSecondScreen = false; _secondDuplicateLayout = false; }

            // The layout below uses fixed pixel coordinates. The app is
            // per-monitor DPI aware, so on a display that is not at 100% the
            // font grows while a fixed window does not, and labels start to
            // clip. Scaling the layout by DPI keeps the proportions correct.
            // The matching AutoScaleDimensions call is at the very end of the
            // constructor; see the note there.
            AutoScaleMode = AutoScaleMode.Dpi;

            Text = "LiveBackground settings";
            // Match the tray and shortcut icon; otherwise Alt-Tab and the
            // taskbar show the generic WinForms icon.
            try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { /* best-effort: the default form icon is fine if this fails */ }
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = true;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(660, 620);
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            BackColor = UiBackground;
            ForeColor = UiText;
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
            _secondTab = new TabPage("Multi Monitor");
            _windowsTab = new TabPage("Other");
            // Only the two editors for the current mode are shown. The pill
            // swaps these first two pages; Multi Monitor and Other stay put.
            _tabs.TabPages.Add(_videosTab);
            _tabs.TabPages.Add(_playbackTab);
            _tabs.TabPages.Add(_secondTab);
            _tabs.TabPages.Add(_windowsTab);

            BuildVideosTab(_videosTab);
            BuildMultiMonitorTab(_secondTab);
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

            ApplyModernStyle(this);

            // A light poll, running only while this window is open. Two Win32
            // calls every 1.5 s is far cheaper than the watchdog's own 2 s
            // layout check, and it keeps the Second screen tab honest.
            _screenTimer = new System.Windows.Forms.Timer();
            _screenTimer.Interval = 1500;
            _screenTimer.Tick += ScreenTimerTick;

            // Design size for the pixel layout above. This has to be the last
            // thing the constructor does: WinForms reads it when the window
            // handle is first created (that is, when the dialog is shown) and
            // scales every control by current-DPI / 96. Set any earlier and the
            // value is overwritten before the controls exist, so nothing scales.
            AutoScaleDimensions = new SizeF(96F, 96F);
        }

        // Keeps the main row at four tabs: the current mode's two editors,
        // then Multi Monitor and Other. Called only when the pill changes.
        private void UpdateModeTabs()
        {
            if (_tabs == null) return;
            bool images = _wallpaperImages.Checked;
            TabPage first = images ? _imagesTab : _videosTab;
            TabPage second = images ? _slideshowTab : _playbackTab;
            if (_tabs.TabPages.Count == 4 && _tabs.TabPages[0] == first &&
                _tabs.TabPages[1] == second) return;

            TabPage selected = _tabs.SelectedTab;
            _tabs.TabPages.Clear();
            _tabs.TabPages.Add(first);
            _tabs.TabPages.Add(second);
            _tabs.TabPages.Add(_secondTab);
            _tabs.TabPages.Add(_windowsTab);
            // Stay on Multi Monitor or Other while the pill switches; a mode
            // editor that just disappeared is replaced by the new first page.
            bool keep = selected == _windowsTab ||
                (selected == _secondTab && !TabGreyed(_tabs.TabPages.IndexOf(_secondTab)));
            _tabs.SelectedTab = keep ? selected : first;
            _tabs.Invalidate();
        }

        // Tabs that belong to the other wallpaper mode cannot be opened. Multi
        // Monitor and Other remain available in both modes.
        private bool TabEnabled(int index)
        {
            if (_tabs == null || index < 0 || index >= _tabs.TabPages.Count) return true;
            TabPage page = _tabs.TabPages[index];
            bool images = _wallpaperImages != null && _wallpaperImages.Checked;
            if (page == _videosTab || page == _playbackTab) return !images;
            if (page == _imagesTab || page == _slideshowTab) return images;
            return true;
        }

        // A tab that is currently unusable is greyed and cannot be selected.
        // The Multi Monitor tab stays usable while Windows is duplicating, so it
        // is only greyed when there is just one screen and no duplicate layout.
        private bool TabGreyed(int index)
        {
            if (_tabs == null || index < 0 || index >= _tabs.TabPages.Count) return false;
            if (!TabEnabled(index)) return true;
            return _tabs.TabPages[index] == _secondTab &&
                !_hasSecondScreen && !_secondDuplicateLayout;
        }

        private void TabSelecting(object sender, TabControlCancelEventArgs e)
        {
            if (TabGreyed(e.TabPageIndex)) e.Cancel = true;
        }

        private void TabDrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _tabs.TabPages.Count) return;
            Rectangle bounds = _tabs.GetTabRect(e.Index);
            using (SolidBrush back = new SolidBrush(UiBackground))
            {
                e.Graphics.FillRectangle(back, bounds);
            }

            // The active tab is drawn in bold with an accent underline; greyed
            // tabs stay visible but cannot be selected.
            bool enabled = !TabGreyed(e.Index);
            bool selected = e.Index == _tabs.SelectedIndex;
            using (Font font = selected ? new Font(e.Font, FontStyle.Bold) : new Font(e.Font, FontStyle.Regular))
            {
                TextRenderer.DrawText(e.Graphics, _tabs.TabPages[e.Index].Text, font, bounds,
                    enabled ? UiText : UiMutedText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            if (selected)
            {
                using (Pen pen = new Pen(UiAccent, 2))
                {
                    e.Graphics.DrawLine(pen, bounds.Left + 6, bounds.Bottom - 2,
                        bounds.Right - 7, bounds.Bottom - 2);
                }
            }
        }

        private void UpdateModeAvailability()
        {
            if (_tabs == null) return;
            bool images = _wallpaperImages.Checked;
            UpdateModeTabs();
            if (TabGreyed(_tabs.SelectedIndex))
            {
                _tabs.SelectedTab = images ? _imagesTab : _videosTab;
            }
            if (_slot2 != null) _slot2.SetWallpaperMode(images);
            if (_slot3 != null) _slot3.SetWallpaperMode(images);
            UpdateScreenOptionTabs();
            _tabs.Invalidate();
            UpdateDesktopControls();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            UpdateVideoToggle();
            RefreshClips();
            RefreshImages();
            // The display layout can change while the app runs, so re-check it
            // when the window opens and then keep polling while it is visible.
            RefreshScreenState();
            AlignSlideshowMarks();
            UpdateModeTabs();
            _tabs.SelectedTab = _wallpaperImages.Checked ? _imagesTab : _videosTab;
            _tabs.Invalidate();
            if (_screenTimer != null) _screenTimer.Start();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Make any probe thread that is still running drop its result
            // instead of touching the controls while they are torn down.
            _probeGeneration++;
            if (_screenTimer != null)
            {
                _screenTimer.Stop();
                _screenTimer.Dispose();
                _screenTimer = null;
            }
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

            // "Single clip" leads, then the playlist: the narrower choice sits
            // first so the pair reads in the order the eye expects.
            _modeSingle = new RadioButton();
            _modeSingle.Text = "Single clip";
            _modeSingle.SetBounds(16, 72, 80, 22);
            _modeSingle.CheckedChanged += delegate { UpdateModeControls(); };
            page.Controls.Add(_modeSingle);

            _modePlaylist = new RadioButton();
            _modePlaylist.Text = "All clips in this order";
            _modePlaylist.SetBounds(104, 72, 180, 22);
            _modePlaylist.CheckedChanged += delegate { UpdateModeControls(); };
            page.Controls.Add(_modePlaylist);

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
            _clipHint.ForeColor = UiMutedText;

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

        // The "Multi Monitor" tab: the shared Windows display-mode row, plus
        // one settings slot per extra screen ("2nd Screen", "3rd Screen"). Each
        // slot is remembered by monitor device name, so its settings survive
        // replugging and reordering.
        private void BuildMultiMonitorTab(TabPage page)
        {
            // Windows display mode. The radios show what Windows is doing and
            // only change it when the user clicks one; the app never switches
            // on its own.
            Label displayLabel = MakeLabel(page, "Windows display mode:", 16, 16, 146);
            displayLabel.Font = new Font(Font, FontStyle.Bold);

            _secondExtend = new RadioButton();
            _secondExtend.Text = "Extended";
            _secondExtend.SetBounds(166, 14, 100, 22);
            _secondExtend.Click += SecondTopologyClicked;
            page.Controls.Add(_secondExtend);

            _secondDuplicate = new RadioButton();
            _secondDuplicate.Text = "Duplicate";
            _secondDuplicate.SetBounds(270, 14, 110, 22);
            _secondDuplicate.Click += SecondTopologyClicked;
            page.Controls.Add(_secondDuplicate);

            _secondDisplaySettings = new Button();
            _secondDisplaySettings.Text = "Display settings";
            _secondDisplaySettings.SetBounds(474, 12, 130, 26);
            _secondDisplaySettings.Click += delegate { OpenDisplaySettings(); };
            page.Controls.Add(_secondDisplaySettings);
            if (_tips != null)
            {
                _tips.SetToolTip(_secondDisplaySettings,
                    "Open the Windows display settings to arrange the screens.");
                _tips.SetToolTip(_secondExtend,
                    "Separate screens; each extra screen can have its own loop.");
                _tips.SetToolTip(_secondDuplicate, "Every screen shows the same picture.");
            }

            _multiStatus = MakeLabel(page, "", 16, 44, 588);
            _multiStatus.ForeColor = UiMutedText;

            _screenTabs = new TabControl();
            _screenTabs.SetBounds(8, 74, 612, 412);
            _screenTabs.DrawMode = TabDrawMode.OwnerDrawFixed;
            _screenTabs.Selecting += ScreenTabSelecting;
            _screenTabs.DrawItem += ScreenTabDrawItem;
            page.Controls.Add(_screenTabs);

            _slot2 = new ScreenSlot(this, 2);
            _slot3 = new ScreenSlot(this, 3);
            _screenTabs.TabPages.Add(_slot2.Build());
            _screenTabs.TabPages.Add(_slot3.Build());
        }

        // ---------------------------------------------------------------
        // Multiple-monitor state
        // ---------------------------------------------------------------

        private void UpdateMultiControls()
        {
            if (_secondExtend == null) return;
            bool duplicate = _secondDuplicateLayout;
            bool twoMonitors = _hasSecondScreen || duplicate;

            // The display-mode row stays usable while duplicating - that is how
            // the user gets back to Extended.
            _secondExtend.Enabled = twoMonitors;
            _secondDuplicate.Enabled = twoMonitors;
            if (_secondDisplaySettings != null) _secondDisplaySettings.Enabled = true;

            if (_multiStatus != null)
            {
                if (duplicate)
                {
                    _multiStatus.Text = "Windows is duplicating the display: every screen shows " +
                        "the same picture. Choose \"Extended\" above to give the extra screens " +
                        "their own loops.";
                }
                else if (!_hasSecondScreen)
                {
                    _multiStatus.Text =
                        "Only one screen is connected. Connect another screen to use these settings.";
                }
                else
                {
                    _multiStatus.Text = "";
                }
            }

            if (_slot2 != null) _slot2.UpdateControls();
            if (_slot3 != null) _slot3.UpdateControls();
            if (_screenTabs != null) _screenTabs.Invalidate();
        }

        // Assigns the connected extra monitors to the two slots (primary first,
        // then left to right) and reloads a slot only when its monitor changed.
        private void BindSlots()
        {
            if (_slot2 == null || _slot3 == null) return;
            List<ScreenInfo> extras = new List<ScreenInfo>();
            if (!_secondDuplicateLayout && _screens != null)
            {
                foreach (ScreenInfo screen in _screens)
                {
                    if (screen.Primary) continue;
                    if (extras.Count >= 2) break;
                    extras.Add(screen);
                }
            }
            _slot2.Bind(extras.Count > 0 ? extras[0].Device : "");
            _slot3.Bind(extras.Count > 1 ? extras[1].Device : "");
            // A third screen that just went away must not leave its tab open.
            if (_screenTabs != null && _screenTabs.SelectedIndex == 1 && !_slot3.HasScreen)
            {
                _screenTabs.SelectedIndex = 0;
            }
        }

        // The "3rd Screen" sub-tab cannot be opened while no third screen is
        // connected; the "2nd Screen" tab is always available so its settings
        // stay reachable.
        private void ScreenTabSelecting(object sender, TabControlCancelEventArgs e)
        {
            ScreenSlot slot = e.TabPage == (_slot2 == null ? null : _slot2.Page) ? _slot2
                : e.TabPage == (_slot3 == null ? null : _slot3.Page) ? _slot3 : null;
            if (slot != null && !slot.HasScreen) e.Cancel = true;
        }

        private void ScreenTabDrawItem(object sender, DrawItemEventArgs e)
        {
            if (_screenTabs == null || e.Index < 0 || e.Index >= _screenTabs.TabPages.Count) return;
            Rectangle bounds = _screenTabs.GetTabRect(e.Index);
            using (SolidBrush back = new SolidBrush(UiBackground))
            {
                e.Graphics.FillRectangle(back, bounds);
            }
            ScreenSlot slot = e.Index == 0 ? _slot2 : _slot3;
            bool enabled = slot != null && slot.HasScreen;
            bool selected = e.Index == _screenTabs.SelectedIndex;
            using (Font font = selected ? new Font(e.Font, FontStyle.Bold) : new Font(e.Font, FontStyle.Regular))
            {
                TextRenderer.DrawText(e.Graphics, _screenTabs.TabPages[e.Index].Text, font, bounds,
                    enabled ? UiText : UiMutedText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            if (selected)
            {
                using (Pen pen = new Pen(UiAccent, 2))
                {
                    e.Graphics.DrawLine(pen, bounds.Left + 6, bounds.Bottom - 2,
                        bounds.Right - 7, bounds.Bottom - 2);
                }
            }
        }

        // Validates one screen's own-loop settings, showing a message box when
        // something is missing. Returns true when the slot is fine to apply.
        private bool ValidateSlot(ScreenSlot slot)
        {
            if (slot == null || !slot.HasScreen || _secondDuplicateLayout || slot.Same.Checked) return true;
            if (slot.FolderBox.Text.Trim().Length == 0)
            {
                MessageBox.Show(this, "Choose a video folder for the " + slot.Ordinal() + " screen.",
                    "LiveBackground", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            if (slot.ModePlaylist.Checked && slot.ClipList.CheckedItems.Count == 0)
            {
                MessageBox.Show(this, "Tick at least one clip for the " + slot.Ordinal() + " screen.",
                    "LiveBackground", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            if (slot.ModeSingle.Checked && slot.SingleIndex() < 0)
            {
                MessageBox.Show(this, "Tick the clip the " + slot.Ordinal() + " screen should play.",
                    "LiveBackground", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            return true;
        }

        // ---------------------------------------------------------------
        // Display mode (Extended / Duplicate)
        // ---------------------------------------------------------------

        // Re-reads the layout and refreshes the tab. Cheap enough to run on a
        // timer while the window is open.
        private void RefreshScreenState()
        {
            List<ScreenInfo> screens;
            try { screens = Screens.Enumerate(); }
            catch { screens = new List<ScreenInfo>(); }

            bool hasSecond = screens.Count >= 2;
            bool duplicate = false;
            try { duplicate = Screens.IsDuplicate(screens); } catch { /* best-effort: an unknown layout counts as not duplicated */ }
            string signature = Screens.Signature(screens) + "|dup=" + duplicate;

            _lastScreenSignature = signature;
            _screens = screens;
            _hasSecondScreen = hasSecond;
            _secondDuplicateLayout = duplicate;
            BindSlots();

            if (_secondExtend != null)
            {
                _updatingTopology = true;
                try
                {
                    // In Duplicate mode Windows reports a single monitor, so
                    // "not duplicating" is the Extended state.
                    _secondExtend.Checked = !duplicate;
                    _secondDuplicate.Checked = duplicate;
                }
                finally { _updatingTopology = false; }
            }

            UpdateMultiControls();
            if (_tabs != null && _tabs.SelectedIndex >= 0 && TabGreyed(_tabs.SelectedIndex))
            {
                bool images = _wallpaperImages != null && _wallpaperImages.Checked;
                _tabs.SelectedTab = images ? _imagesTab : _videosTab;
            }
        }

        private void ScreenTimerTick(object sender, EventArgs e)
        {
            List<ScreenInfo> screens;
            string signature;
            bool duplicate;
            try
            {
                screens = Screens.Enumerate();
                duplicate = Screens.IsDuplicate(screens);
                signature = Screens.Signature(screens) + "|dup=" + duplicate;
            }
            catch { return; }

            if (string.Equals(signature, _lastScreenSignature, StringComparison.Ordinal)) return;
            Log.Info("settings: display layout changed while the window was open");
            _lastScreenSignature = signature;
            _screens = screens;
            _hasSecondScreen = screens.Count >= 2;
            _secondDuplicateLayout = duplicate;
            BindSlots();

            if (_secondExtend != null)
            {
                _updatingTopology = true;
                try
                {
                    _secondExtend.Checked = !duplicate;
                    _secondDuplicate.Checked = duplicate;
                }
                finally { _updatingTopology = false; }
            }
            UpdateMultiControls();
            if (_tabs != null && _tabs.SelectedIndex >= 0 && TabGreyed(_tabs.SelectedIndex))
            {
                bool images = _wallpaperImages != null && _wallpaperImages.Checked;
                _tabs.SelectedTab = images ? _imagesTab : _videosTab;
            }
            _tabs.Invalidate();
        }

        // A radio click is the only thing that changes the Windows display
        // mode. Never automatic, and only when the click really changes it.
        private void SecondTopologyClicked(object sender, EventArgs e)
        {
            if (_updatingTopology || _secondExtend == null || _secondDuplicate == null) return;
            bool extend = sender == _secondExtend;
            if (extend == !_secondDuplicateLayout) return;

            Log.Info("settings: display mode change requested: " +
                (extend ? "Extended" : "Duplicate"));
            bool ok;
            try { ok = Screens.SetTopology(extend); }
            catch (Exception ex)
            {
                Log.Warn("display mode change failed: " + ex.Message);
                ok = false;
            }
            if (!ok)
            {
                MessageBox.Show(this,
                    "Windows would not change the display mode. Use the Display settings " +
                    "button to change it there.",
                    "LiveBackground", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            // Windows applies this asynchronously; the timer picks up the
            // result, and this immediate re-read catches the common case.
            RefreshScreenState();
        }

        private static void OpenDisplaySettings()
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo("ms-settings:display");
                info.UseShellExecute = true;
                Process.Start(info);
            }
            catch (Exception ex)
            {
                Log.Warn("could not open the display settings: " + ex.Message);
            }
        }

        // One extra screen's settings, drawn as a sub-tab of the "Multiple
        // Monitor" tab. A slot owns every control for one screen and reads and
        // writes only that screen's entry in the config, matched by monitor
        // device name, so the extra screens never disturb each other.
        private sealed class ScreenSlot
        {
            private readonly SettingsForm _form;
            private readonly int _number;
            public TabPage Page;
            public string Device = "";
            public bool HasScreen;
            private Panel _videoPane;
            public ScreenImagesSlot Images;

            public CheckBox Same;
            public TextBox FolderBox;
            public RadioButton ModePlaylist;
            public RadioButton ModeSingle;
            public ListView ClipList;
            private Label _status;
            private Label _hint;
            private Button _moveUp;
            private Button _moveDown;
            private Button _sortAz;
            private Button _refresh;
            private Button _open;
            private Button _rebuild;

            private readonly List<string> _paths = new List<string>();
            private readonly List<double> _durations = new List<double>();
            // The playlist ticks as they were in playlist mode, kept while the
            // list shows the single-clip choice instead.
            private List<PlaylistEntry> _playlist = new List<PlaylistEntry>();
            private bool _loading;
            private string _singleName = "";
            private bool _lastSingleMode;
            private int _probeGeneration;

            public ScreenSlot(SettingsForm form, int number)
            {
                _form = form;
                _number = number;
            }

            // "2nd" / "3rd", used in the tab title and in every message.
            public string Ordinal()
            {
                if (_number == 2) return "2nd";
                if (_number == 3) return "3rd";
                return _number + "th";
            }

            // Builds the sub-tab and returns it, ready for the tab control. The
            // controls mirror the Videos tab minus the drop panel and the Fade
            // column: the fade always follows the Playback tab, like the main
            // loop.
            public TabPage Build()
            {
                Page = new TabPage(Ordinal() + " Screen");
                _videoPane = new Panel();
                _videoPane.SetBounds(0, 0, 604, 379);
                Page.Controls.Add(_videoPane);

                Same = new CheckBox();
                Same.Text = "Use the same video as the main screen";
                Same.SetBounds(12, 12, 420, 24);
                Same.CheckedChanged += delegate { UpdateControls(); };
                _videoPane.Controls.Add(Same);

                _status = MakeLabel(_videoPane, "", 12, 40, 576);
                _status.ForeColor = UiMutedText;

                Label folderLabel = MakeLabel(_videoPane, "Video folder", 12, 70, 200);
                folderLabel.Font = new Font(_form.Font, FontStyle.Bold);

                FolderBox = new TextBox();
                FolderBox.SetBounds(12, 92, 456, 24);
                _videoPane.Controls.Add(FolderBox);

                Button browse = new Button();
                browse.Text = "Browse...";
                browse.SetBounds(480, 90, 104, 27);
                browse.Click += delegate { BrowseFolder(); };
                _videoPane.Controls.Add(browse);

                // "Single clip" leads here too, matching the main Videos tab.
                ModeSingle = new RadioButton();
                ModeSingle.Text = "Single clip";
                ModeSingle.SetBounds(12, 126, 80, 22);
                ModeSingle.CheckedChanged += delegate { UpdateModeControls(); };
                _videoPane.Controls.Add(ModeSingle);

                ModePlaylist = new RadioButton();
                ModePlaylist.Text = "All clips in this order";
                ModePlaylist.SetBounds(100, 126, 180, 22);
                ModePlaylist.CheckedChanged += delegate { UpdateModeControls(); };
                _videoPane.Controls.Add(ModePlaylist);

                ClipList = new ListView();
                ClipList.SetBounds(12, 154, 572, 118);
                ClipList.View = View.Details;
                ClipList.CheckBoxes = true;
                ClipList.FullRowSelect = true;
                ClipList.GridLines = true;
                ClipList.HideSelection = false;
                ClipList.MultiSelect = false;
                ClipList.Columns.Add("Clip", 452);
                ClipList.Columns.Add("Length", 92);
                ClipList.ItemCheck += ItemCheck;
                _videoPane.Controls.Add(ClipList);

                _moveUp = new Button();
                _moveUp.Text = "Move up";
                _moveUp.SetBounds(12, 280, 90, 28);
                _moveUp.Click += delegate { Move(-1); };
                _videoPane.Controls.Add(_moveUp);

                _moveDown = new Button();
                _moveDown.Text = "Move down";
                _moveDown.SetBounds(108, 280, 100, 28);
                _moveDown.Click += delegate { Move(1); };
                _videoPane.Controls.Add(_moveDown);

                _sortAz = new Button();
                _sortAz.Text = "Sort A-Z";
                _sortAz.SetBounds(214, 280, 90, 28);
                _sortAz.Click += delegate { Sort(); };
                _videoPane.Controls.Add(_sortAz);

                _refresh = new Button();
                _refresh.Text = "Refresh";
                _refresh.SetBounds(12, 314, 100, 28);
                _refresh.Click += delegate { CapturePlaylist(); RefreshClips(); };
                _videoPane.Controls.Add(_refresh);

                _open = new Button();
                _open.Text = "Open folder";
                _open.SetBounds(118, 314, 110, 28);
                _open.Click += delegate { OpenFolder(); };
                _videoPane.Controls.Add(_open);

                _rebuild = new Button();
                _rebuild.Text = "Rebuild now";
                _rebuild.SetBounds(234, 314, 110, 28);
                _rebuild.Click += _form.RebuildNowClicked;
                _videoPane.Controls.Add(_rebuild);

                _hint = MakeLabel(_videoPane, "", 12, 348, 572);
                _hint.Height = 30;
                _hint.ForeColor = UiMutedText;

                Images = new ScreenImagesSlot(_form, _number);
                Panel imagePane = Images.Build();
                imagePane.SetBounds(0, 0, 604, 379);
                imagePane.Visible = false;
                Page.Controls.Add(imagePane);

                _form.ApplyModernStyle(Page);
                return Page;
            }

            // The same outer tab serves Videos and Images. Both editors stay
            // alive, so switching modes never discards a half-finished edit.
            public void SetWallpaperMode(bool images)
            {
                if (_videoPane != null) _videoPane.Visible = !images;
                if (Images != null && Images.Panel != null) Images.Panel.Visible = images;
                if (Images != null) Images.UpdateSameLabel();
                UpdateControls();
            }

            // Binds the slot to a connected monitor. Reloads only when the
            // monitor really changed, so a half-finished edit is never lost
            // while another screen is plugged or unplugged.
            public void Bind(string device)
            {
                if (device == null) device = "";
                bool has = device.Length > 0;
                if (Images != null) Images.Bind(device);
                if (string.Equals(device, Device, StringComparison.OrdinalIgnoreCase) && has == HasScreen)
                {
                    return;
                }
                Device = device;
                HasScreen = has;
                if (HasScreen) LoadFromConfig();
                UpdateControls();
            }

            // Reads this screen's stored settings into the controls. A screen
            // with no entry of its own shows the migrated or default settings.
            public void LoadFromConfig()
            {
                if (FolderBox == null) return;
                ScreenSettings settings = _form._config.ScreenFor(Device);
                _loading = true;
                try
                {
                    Same.Checked = settings.SameAsMain;
                    FolderBox.Text = settings.Folder ?? "";
                    bool single = string.Equals(settings.PlaybackMode, "single",
                        StringComparison.OrdinalIgnoreCase);
                    _lastSingleMode = single;
                    _singleName = settings.SingleClip ?? "";
                    ModeSingle.Checked = single;
                    ModePlaylist.Checked = !single;
                }
                finally { _loading = false; }
                _playlist = new List<PlaylistEntry>();
                if (settings.Playlist != null)
                {
                    foreach (PlaylistEntry entry in settings.Playlist)
                    {
                        if (entry != null) _playlist.Add(new PlaylistEntry(entry.Name, entry.Enabled));
                    }
                }
                RefreshClips();
                UpdateControls();
            }

            // Writes the controls back into this screen's entry in the config.
            // A mirroring screen only stores the switch; its folder, mode and
            // playlist are kept so they are ready when mirroring is turned off.
            public void CaptureInto(AppConfig config)
            {
                if (config == null || !HasScreen || string.IsNullOrEmpty(Device)) return;
                ScreenSettings settings = config.ScreenByDevice(Device, true);
                if (settings == null) return;
                settings.Device = Device;
                settings.SameAsMain = Same.Checked;
                if (Images != null) Images.CaptureInto(settings);
                if (Same.Checked) return;

                string folder = FolderBox.Text.Trim();
                if (folder.Length > 0) settings.Folder = folder;
                settings.PlaybackMode = ModeSingle.Checked ? "single" : "playlist";
                settings.SingleClip = string.IsNullOrEmpty(_singleName)
                    ? settings.SingleClip
                    : _singleName;
                // In single mode the ticks show the chosen clip, so write back
                // the playlist saved when the mode was switched.
                List<PlaylistEntry> playlist = ModeSingle.Checked
                    ? _playlist
                    : CurrentPlaylist();
                if (playlist.Count == 0 && _playlist.Count > 0) playlist = _playlist;
                settings.Playlist = playlist;
            }

            // Enables only what makes sense: the whole slot is inert while no
            // screen is connected or Windows is duplicating.
            public void UpdateControls()
            {
                if (Same == null) return;
                bool duplicate = _form._secondDuplicateLayout;
                bool active = HasScreen && !duplicate && !Same.Checked;
                bool single = ModeSingle != null && ModeSingle.Checked;

                Same.Enabled = HasScreen && !duplicate;
                FolderBox.Enabled = active;
                ModePlaylist.Enabled = active;
                ModeSingle.Enabled = active;
                ClipList.Enabled = active;
                _moveUp.Enabled = active && !single;
                _moveDown.Enabled = active && !single;
                _sortAz.Enabled = active && !single;
                _refresh.Enabled = active;
                _open.Enabled = active;
                _rebuild.Enabled = HasScreen && !duplicate;

                if (_status != null)
                {
                    if (duplicate)
                    {
                        _status.Text = "Windows is duplicating the display: every screen shows " +
                            "the same picture. Choose \"Extended\" above to give this screen its " +
                            "own loop.";
                    }
                    else if (!HasScreen)
                    {
                        _status.Text = "No " + Ordinal() + " screen is connected.";
                    }
                    else
                    {
                        // The controls already explain the normal state; only
                        // warnings and unavailable-screen states need text.
                        _status.Text = "";
                    }
                }
                if (Images != null) Images.UpdateControls();
                _form.UpdateScreenOptionTabs();
            }

            // Switching between "All clips" and "Single clip" reshapes the ticks,
            // exactly like the Videos tab.
            private void UpdateModeControls()
            {
                if (_loading || ModeSingle == null) return;
                bool single = ModeSingle.Checked;
                bool changed = single != _lastSingleMode;
                _lastSingleMode = single;
                if (changed)
                {
                    if (single)
                    {
                        // Keep the playlist safe before the ticks start showing
                        // the single-clip choice.
                        if (ClipList.Items.Count > 0) _playlist = CurrentPlaylist();
                        ApplySingleTicks();
                    }
                    else
                    {
                        ApplyPlaylistTicks();
                    }
                }
                UpdateControls();
            }

            // "Single clip" mode: the ticks behave like radio buttons.
            private void ItemCheck(object sender, ItemCheckEventArgs e)
            {
                if (_loading || !ModeSingle.Checked) return;
                if (e.NewValue != CheckState.Checked)
                {
                    e.NewValue = CheckState.Checked;
                    return;
                }
                _loading = true;
                for (int i = 0; i < ClipList.Items.Count; i++)
                {
                    if (i != e.Index) ClipList.Items[i].Checked = false;
                }
                _loading = false;
                if (e.Index >= 0 && e.Index < ClipList.Items.Count)
                {
                    _singleName = ClipList.Items[e.Index].Text;
                }
            }

            public int SingleIndex()
            {
                return PlaylistState.CheckedIndex(ListChecked(ClipList));
            }

            private void SelectSingleRow()
            {
                int index = PlaylistState.PreferredIndex(ListNames(ClipList), _singleName);
                SetListChecked(ClipList, PlaylistState.SingleTicks(ClipList.Items.Count, index));
                _singleName = index >= 0 ? ClipList.Items[index].Text : "";
            }

            private void ApplySingleTicks()
            {
                _loading = true;
                SelectSingleRow();
                _loading = false;
            }

            private void ApplyPlaylistTicks()
            {
                _loading = true;
                SetListChecked(ClipList,
                    PlaylistState.PlaylistTicks(ListNames(ClipList), _playlist, false));
                _loading = false;
            }

            private void CapturePlaylist()
            {
                if (!ModeSingle.Checked) _playlist = CurrentPlaylist();
            }

            private List<PlaylistEntry> CurrentPlaylist()
            {
                return PlaylistState.Capture(ListNames(ClipList), ListChecked(ClipList));
            }

            private void Move(int delta)
            {
                if (ClipList.SelectedIndices.Count == 0) return;
                int index = ClipList.SelectedIndices[0];
                int[] order = PlaylistState.SwapOrder(ClipList.Items.Count, index, delta);
                if (order == null) return;
                ApplyOrder(order, index + delta);
            }

            private void Sort()
            {
                if (ClipList.Items.Count < 2) return;
                ApplyOrder(PlaylistState.SortedOrder(ListNames(ClipList)), -1);
            }

            private void ApplyOrder(int[] order, int selectIndex)
            {
                if (order.Length != ClipList.Items.Count) return;
                _loading = true;
                ListViewItem[] items = new ListViewItem[order.Length];
                for (int i = 0; i < order.Length; i++)
                {
                    int old = order[i];
                    ListViewItem source = ClipList.Items[old];
                    ListViewItem item = new ListViewItem(source.Text);
                    item.Checked = source.Checked;
                    item.SubItems.Add(source.SubItems[1].Text);
                    items[i] = item;
                }
                List<string> paths = PlaylistState.Reorder(_paths, order, "");
                List<double> durations = PlaylistState.Reorder(_durations, order, -1);
                ClipList.BeginUpdate();
                ClipList.Items.Clear();
                ClipList.Items.AddRange(items);
                ClipList.EndUpdate();
                _paths.Clear();
                _paths.AddRange(paths);
                _durations.Clear();
                _durations.AddRange(durations);
                _loading = false;
                if (selectIndex >= 0 && selectIndex < ClipList.Items.Count)
                {
                    ClipList.Items[selectIndex].Selected = true;
                    ClipList.Items[selectIndex].Focused = true;
                    ClipList.EnsureVisible(selectIndex);
                }
            }

            public void RefreshClips()
            {
                if (ClipList == null) return;
                _loading = true;
                ClipList.Items.Clear();
                _paths.Clear();
                _durations.Clear();

                string folder = Util.ResolvePath(_form._appRoot, FolderBox.Text);
                List<string> all = MediaPipeline.ScanVideos(folder);
                if (all.Count == 0)
                {
                    _loading = false;
                    _hint.Text = "No videos found in " + folder +
                        ". Add files there and press Refresh.";
                    return;
                }

                bool hasPlaylist = _playlist != null && _playlist.Count > 0;
                Dictionary<string, PlaylistEntry> byName =
                    new Dictionary<string, PlaylistEntry>(StringComparer.OrdinalIgnoreCase);
                if (_playlist != null)
                {
                    foreach (PlaylistEntry entry in _playlist)
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
                if (_playlist != null)
                {
                    foreach (PlaylistEntry entry in _playlist)
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
                    ClipList.Items.Add(item);
                    _paths.Add(file);
                    _durations.Add(-1);
                }

                if (ModeSingle.Checked) SelectSingleRow();
                _loading = false;
                _hint.Text = "";

                string ffprobe = Paths.Tool(_form._config.FfprobePath);
                List<string> probePaths = new List<string>(_paths);
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
                        _form.BeginInvoke(new Action(delegate()
                        {
                            if (generation != _probeGeneration || _form.IsDisposed ||
                                !_form.IsHandleCreated) return;
                            ApplyDurations(durations);
                        }));
                    }
                    catch { /* best-effort: the form may have closed while probing */ }
                });
                thread.IsBackground = true;
                thread.Name = "livebackground-probe" + _number;
                thread.Start();
            }

            private void ApplyDurations(Dictionary<string, double> durations)
            {
                if (_form.IsDisposed || ClipList == null || ClipList.IsDisposed) return;
                while (_durations.Count < ClipList.Items.Count) _durations.Add(-1);
                if (_durations.Count > ClipList.Items.Count)
                {
                    _durations.RemoveRange(ClipList.Items.Count,
                        _durations.Count - ClipList.Items.Count);
                }
                for (int i = 0; i < ClipList.Items.Count; i++)
                {
                    ListViewItem item = ClipList.Items[i];
                    double duration;
                    if (!durations.TryGetValue(item.Text, out duration)) duration = -1;
                    _durations[i] = duration;
                    item.SubItems[1].Text = duration < 0
                        ? "unreadable"
                        : duration.ToString("0.0", CultureInfo.InvariantCulture) + " s";
                }
            }

            private void BrowseFolder()
            {
                using (FolderBrowserDialog dialog = new FolderBrowserDialog())
                {
                    dialog.Description = "Choose the folder with the videos for the " +
                        Ordinal() + " screen";
                    dialog.ShowNewFolderButton = true;
                    string current = Util.ResolvePath(_form._appRoot, FolderBox.Text);
                    string start = _form.InitialPickerFolder(FolderBox.Text, true);
                    if (Directory.Exists(start)) dialog.SelectedPath = start;
                    if (dialog.ShowDialog(_form) == DialogResult.OK)
                    {
                        _form._lastPickerFolder = dialog.SelectedPath;
                        if (!string.Equals(dialog.SelectedPath, current, StringComparison.OrdinalIgnoreCase))
                        {
                            // A different folder gets a fresh playlist, exactly
                            // like the Videos tab.
                            _playlist = new List<PlaylistEntry>();
                            _singleName = "";
                        }
                        FolderBox.Text = dialog.SelectedPath;
                        RefreshClips();
                    }
                }
            }

            private void OpenFolder()
            {
                try
                {
                    string folder = Util.ResolvePath(_form._appRoot, FolderBox.Text);
                    if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                    System.Diagnostics.Process.Start("explorer.exe", Util.Quote(folder));
                }
                catch (Exception ex)
                {
                    MessageBox.Show(_form, "Could not open the folder: " + ex.Message,
                        "LiveBackground", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
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

            Label resLabel = MakeLabel(page, "Resolution", 16, 350, 110);
            _resolution = new ComboBox();
            _resolution.SetBounds(16, 372, 170, 24);
            _resolution.DropDownStyle = ComboBoxStyle.DropDownList;
            page.Controls.Add(_resolution);

            Label fpsLabel = MakeLabel(page, "Frame rate", 210, 350, 110);
            _fps = new ComboBox();
            _fps.SetBounds(210, 372, 100, 24);
            _fps.DropDownStyle = ComboBoxStyle.DropDownList;
            page.Controls.Add(_fps);

            Label qualityLabel = MakeLabel(page, "Quality", 330, 350, 110);
            _quality = new ComboBox();
            _quality.SetBounds(330, 372, 240, 24);
            _quality.DropDownStyle = ComboBoxStyle.DropDownList;
            page.Controls.Add(_quality);

            _screensaverActive = new CheckBox();
            _screensaverActive.Text = "Screensaver Dim and Lock";
            _screensaverActive.SetBounds(16, 420, 310, 24);
            _screensaverActive.CheckedChanged += ScreensaverChoiceChanged;
            page.Controls.Add(_screensaverActive);

            _screensaverVideo = new CheckBox();
            _screensaverVideo.Text = "Screensaver Video Background";
            _screensaverVideo.SetBounds(16, 448, 310, 24);
            _screensaverVideo.CheckedChanged += ScreensaverChoiceChanged;
            page.Controls.Add(_screensaverVideo);

            // The Windows screen-saver page is where the idle timeout for the
            // video option is configured, so put a shortcut right here.
            Button screensaverButton = new Button();
            screensaverButton.Text = "Screensaver settings...";
            screensaverButton.SetBounds(340, 433, 190, 27);
            screensaverButton.Click += delegate { OpenScreensaverSettings(); };
            page.Controls.Add(screensaverButton);

            BuildScreenFadeTabs(page);
        }

        // The two screensaver choices are mutually exclusive: one lets Windows
        // dim and lock, the other keeps the screen on and shows only the video.
        // Setting the other one to false raises its event again with itself as
        // the sender, which stops the chain here.
        private void ScreensaverChoiceChanged(object sender, EventArgs e)
        {
            if (_screensaverActive == null || _screensaverVideo == null) return;
            if (sender == _screensaverActive && _screensaverActive.Checked)
            {
                _screensaverVideo.Checked = false;
            }
            else if (sender == _screensaverVideo && _screensaverVideo.Checked)
            {
                _screensaverActive.Checked = false;
            }
        }

        private static void OpenScreensaverSettings()
        {
            try
            {
                Process.Start("control.exe", "desk.cpl,,@screensaver");
            }
            catch (Exception ex)
            {
                Log.Warn("could not open the Windows screen-saver settings: " + ex.Message);
            }
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

            // "One image" leads, matching the Videos tab's "Single clip".
            _imageModeSingle = new RadioButton();
            _imageModeSingle.Text = "One image";
            _imageModeSingle.SetBounds(16, 72, 100, 22);
            _imageModeSingle.CheckedChanged += delegate { UpdateImageModeControls(); };
            page.Controls.Add(_imageModeSingle);

            _imageModeSlideshow = new RadioButton();
            _imageModeSlideshow.Text = "Slideshow";
            _imageModeSlideshow.SetBounds(124, 72, 100, 22);
            _imageModeSlideshow.CheckedChanged += delegate { UpdateImageModeControls(); };
            page.Controls.Add(_imageModeSlideshow);

            _imageSingleHint = MakeLabel(page, "Pick one image in the list below", 244, 72, 360);
            _imageSingleHint.ForeColor = UiMutedText;
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
            _imageHint.ForeColor = UiMutedText;

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
            _imageIntervalValue.ForeColor = UiMutedText;

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
            _imageSecondsLabel.ForeColor = UiMutedText;

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
            _imageCrossfadeValue.ForeColor = UiMutedText;

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
            _desktopFrame.Text = "First frame of this screen's loop";
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

            BuildDesktopFallbackTabs();
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
            else
            {
                DesktopSlot slot = panel.Tag as DesktopSlot;
                if (slot != null) slot.ApplyImage(file);
            }
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
            Color offTrack = Color.FromArgb(210, 210, 210);
            using (GraphicsPath path = RoundedRect(bounds, bounds.Height / 2))
            using (SolidBrush track = new SolidBrush(offTrack))
            using (SolidBrush on = new SolidBrush(UiAccent))
            using (Pen border = new Pen(UiBorder))
            {
                e.Graphics.FillPath(track, path);
                e.Graphics.DrawPath(border, path);

                int pad = 3;
                int size = Math.Max(6, bounds.Height - pad * 2);
                int x = images ? bounds.Right - pad - size : bounds.Left + pad;
                Rectangle thumb = new Rectangle(x, bounds.Top + pad, size, size);

                using (SolidBrush fill = new SolidBrush(Color.White))
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
            label.ForeColor = UiMutedText;
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
            _screensaverVideo.Checked = _config.ScreensaverVideo;
            // A hand-edited config with both set is resolved in favour of the
            // video option, which is also what the UI enforces.
            if (_screensaverVideo.Checked) _screensaverActive.Checked = false;
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
            catch { /* best-effort: a missing shortcut reads as not installed */ }
            _startWithWindows.Checked = startup;

            // The per-screen settings are loaded into the "Multi Monitor"
            // slots when the tab is bound to the connected monitors (OnShown).
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
            _fadeValue.ForeColor = max > 5.0 ? WarnColor : UiText;

            int count = _effectiveDurations.Count;
            if (count == 0)
            {
                _fadeEffective.ForeColor = UiMutedText;
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

            _fadeEffective.ForeColor = max > 5.0 ? WarnColor : UiText;
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
        // Checkable media lists
        // ---------------------------------------------------------------

        // Thin adapters between the four ListView media lists (main Videos,
        // main Images and the two per-screen editors, all nested types of this
        // form) and the window-free PlaylistState core.

        private static List<string> ListNames(ListView list)
        {
            List<string> names = new List<string>();
            for (int i = 0; i < list.Items.Count; i++) names.Add(list.Items[i].Text);
            return names;
        }

        private static List<bool> ListChecked(ListView list)
        {
            List<bool> flags = new List<bool>();
            for (int i = 0; i < list.Items.Count; i++) flags.Add(list.Items[i].Checked);
            return flags;
        }

        // Applies ticks from PlaylistState. The caller owns the "loading" flag.
        private static void SetListChecked(ListView list, bool[] ticks)
        {
            int count = Math.Min(list.Items.Count, ticks == null ? 0 : ticks.Length);
            for (int i = 0; i < count; i++) list.Items[i].Checked = ticks[i];
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

            _moveUp.Enabled = !single;
            _moveDown.Enabled = !single;
            _sortAz.Enabled = !single;
            RebuildEffective();
            UpdateClipFades();
            UpdateFadeLabels();
        }

        // "Single clip" mode: the checkboxes behave like radio buttons. Checking
        // one clip clears the others and the checked clip becomes the choice.
        private void ClipItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (_loading || !_modeSingle.Checked) return;

            if (e.NewValue != CheckState.Checked)
            {
                // Single clip must stay selected, so a second click on the ticked
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
            int index = PlaylistState.PreferredIndex(ListNames(_clipList), _singleName);
            SetListChecked(_clipList, PlaylistState.SingleTicks(_clipList.Items.Count, index));
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
            _loading = true;
            SetListChecked(_clipList,
                PlaylistState.PlaylistTicks(ListNames(_clipList), _config.Playlist, false));
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
            // The per-screen mirror switch names what the main screen shows, so
            // keep its noun in step with this choice.
            if (_slot2 != null && _slot2.Images != null) _slot2.Images.UpdateSameLabel();
            if (_slot3 != null && _slot3.Images != null) _slot3.Images.UpdateSameLabel();
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
            int index = PlaylistState.PreferredIndex(ListNames(_imageList), _singleImageName);
            SetListChecked(_imageList, PlaylistState.SingleTicks(_imageList.Items.Count, index));
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
            _loadingImages = true;
            SetListChecked(_imageList,
                PlaylistState.PlaylistTicks(ListNames(_imageList), _config.ImagePlaylist, true));
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
            return PlaylistState.Capture(ListNames(_imageList), ListChecked(_imageList));
        }

        private void MoveImage(int delta)
        {
            if (_imageList.SelectedIndices.Count == 0) return;
            int index = _imageList.SelectedIndices[0];
            int[] order = PlaylistState.SwapOrder(_imageList.Items.Count, index, delta);
            if (order == null) return;
            ApplyImageOrder(order, index + delta);
        }

        private void SortImages()
        {
            if (_imageList.Items.Count < 2) return;
            ApplyImageOrder(PlaylistState.SortedOrder(ListNames(_imageList)), -1);
        }

        private void ApplyImageOrder(int[] order, int selectIndex)
        {
            if (order.Length != _imageList.Items.Count) return;
            _loadingImages = true;

            ListViewItem[] items = new ListViewItem[order.Length];
            for (int i = 0; i < order.Length; i++)
            {
                int old = order[i];
                ListViewItem source = _imageList.Items[old];
                ListViewItem item = new ListViewItem(source.Text);
                item.Checked = source.Checked;
                item.SubItems.Add(source.SubItems[1].Text);
                items[i] = item;
            }
            List<string> paths = PlaylistState.Reorder(_imagePaths, order, "");

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
            int step = CrossfadeState.Step(fine);
            _imageCrossfadeBar.SmallChange = step;
            _imageCrossfadeBar.LargeChange = step * 2;
            _imageCrossfadeBar.TickFrequency = step;
            int snapped = CrossfadeState.Snap(_imageCrossfadeBar.Value, fine);
            if (snapped != _imageCrossfadeBar.Value) _imageCrossfadeBar.Value = snapped;
            UpdateCrossfadeLabel();
        }

        private int SnapImageCrossfade(int value)
        {
            return CrossfadeState.Snap(value, _imageFineSteps != null && _imageFineSteps.Checked);
        }

        private double SelectedCrossfadeSeconds()
        {
            if (_imageCrossfadeBar == null) return 2.0;
            return _imageCrossfadeBar.Value / 2.0;
        }

        private void UpdateCrossfadeLabel()
        {
            if (_imageCrossfadeBar == null || _imageCrossfadeValue == null) return;
            _imageCrossfadeValue.Text = CrossfadeState.Text(_imageCrossfadeBar.Value, "Off (instant)");
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
            return PlaylistState.CheckedIndex(ListChecked(_clipList));
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
            return PlaylistState.Capture(ListNames(_clipList), ListChecked(_clipList));
        }

        private void MoveClip(int delta)
        {
            if (_clipList.SelectedIndices.Count == 0) return;
            int index = _clipList.SelectedIndices[0];
            int[] order = PlaylistState.SwapOrder(_clipList.Items.Count, index, delta);
            if (order == null) return;
            ApplyOrder(order, index + delta);
        }

        private void SortClips()
        {
            if (_clipList.Items.Count < 2) return;
            ApplyOrder(PlaylistState.SortedOrder(ListNames(_clipList)), -1);
        }

        private void ApplyOrder(int[] order, int selectIndex)
        {
            if (order.Length != _clipList.Items.Count) return;
            _loading = true;

            ListViewItem[] items = new ListViewItem[order.Length];
            for (int i = 0; i < order.Length; i++)
            {
                int old = order[i];
                ListViewItem source = _clipList.Items[old];
                ListViewItem item = new ListViewItem(source.Text);
                item.Checked = source.Checked;
                item.SubItems.Add(source.SubItems[1].Text);
                item.SubItems.Add(source.SubItems[2].Text);
                items[i] = item;
            }
            List<string> paths = PlaylistState.Reorder(_clipPaths, order, "");
            List<double> durations = PlaylistState.Reorder(_clipDurations, order, -1);

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
            if (_desktopSlot2 != null) UpdateScreenOptionTabs();
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
            _rowToEffective = Array.Empty<int>();

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
                catch { /* best-effort: the form may have closed while probing */ }
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

            // Only touch the startup task once the settings are safely on disk;
            // a failed save must not leave a logon task the config does not
            // agree with.
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
            Log.Info("settings: Rebuild now requested");
            AppConfig staged = CollectConfig();
            if (staged == null)
            {
                Log.Info("settings: Rebuild now cancelled, the settings are not valid");
                return;
            }
            Program.ApplyConfig(staged);
            Log.Info("settings: Rebuild now applied to the running wallpaper");
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

            // Each connected extra screen only needs its own selection when it
            // is not mirroring the main loop. Skipped in Images mode, where the
            // Multi Monitor tab is not editable.
            if (!imagesMode)
            {
                if (!ValidateSlot(_slot2)) return null;
                if (!ValidateSlot(_slot3)) return null;
            }
            else
            {
                if (_slot2 != null && !_slot2.Images.Validate()) return null;
                if (_slot3 != null && !_slot3.Images.Validate()) return null;
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
            updated.DesktopWallpaperMode = imagesMode
                ? _config.DesktopWallpaperMode
                : (useCustomWallpaper ? "custom" : "frame");
            updated.DesktopWallpaperImage = useCustomWallpaper
                ? desktopImage
                : _config.DesktopWallpaperImage;
            updated.ScreensaverActive = _screensaverActive.Checked;
            updated.ScreensaverVideo = _screensaverVideo.Checked;

            // Each connected extra screen keeps its own entry, matched by
            // device name. Both video and image settings are captured so an
            // inactive mode never loses a half-finished edit. Disconnected
            // screens are left untouched.
            if (_slot2 != null) _slot2.CaptureInto(updated);
            if (_slot3 != null) _slot3.CaptureInto(updated);
            if (_fadeSlot2 != null) _fadeSlot2.CaptureInto(updated);
            if (_fadeSlot3 != null) _fadeSlot3.CaptureInto(updated);
            if (!imagesMode && _desktopWallpaper.Checked)
            {
                if (_desktopSlot2 != null && !_desktopSlot2.CaptureAndValidate(updated)) return null;
                if (_desktopSlot3 != null && !_desktopSlot3.CaptureAndValidate(updated)) return null;
            }

            updated.Normalize();

            return updated;
        }

        private void ApplyModernStyle(Control root)
        {
            if (root == null) return;

            if (ReferenceEquals(root, _wallpaperVideo))
            {
                // Custom-painted mode pill: keep its transparent/special look.
            }
            else if (root is Form form)
            {
                form.BackColor = UiBackground;
                form.ForeColor = UiText;
            }
            else if (root is TabPage tabPage)
            {
                tabPage.UseVisualStyleBackColor = false;
                tabPage.BackColor = UiBackground;
                tabPage.ForeColor = UiText;
            }
            else if (root is TabControl)
            {
                root.BackColor = UiBackground;
                root.ForeColor = UiText;
            }
            else if (root is GroupBox groupBox)
            {
                groupBox.BackColor = UiBackground;
                groupBox.ForeColor = UiText;
                groupBox.FlatStyle = FlatStyle.Flat;
            }
            else if (root is Button button)
            {
                if (button.FlatStyle != FlatStyle.Flat || button.BackColor != Color.Transparent)
                {
                    // Preserve the custom transparent/flat pill, but modernize regular buttons.
                    button.FlatStyle = FlatStyle.Flat;
                    button.FlatAppearance.BorderColor = UiBorder;
                    button.FlatAppearance.BorderSize = 1;
                    button.FlatAppearance.MouseOverBackColor = UiButtonHover;
                    button.FlatAppearance.MouseDownBackColor = UiBorder;
                    button.BackColor = UiButtonFace;
                    button.ForeColor = UiText;
                    button.UseVisualStyleBackColor = false;
                }
            }
            else if (root is TextBox textBox)
            {
                textBox.BorderStyle = BorderStyle.FixedSingle;
                textBox.BackColor = textBox.ReadOnly ? UiButtonFace : UiInputBack;
                textBox.ForeColor = UiText;
            }
            else if (root is ComboBox comboBox)
            {
                comboBox.BackColor = UiInputBack;
                comboBox.ForeColor = UiText;
            }
            else if (root is ListView listView)
            {
                listView.BorderStyle = BorderStyle.FixedSingle;
                listView.BackColor = UiSurface;
                listView.ForeColor = UiText;
            }
            else if (root is NumericUpDown numericUpDown)
            {
                numericUpDown.BorderStyle = BorderStyle.FixedSingle;
                numericUpDown.BackColor = UiInputBack;
                numericUpDown.ForeColor = UiText;
            }
            else if (root is CheckBox || root is RadioButton)
            {
                // Keep native control glyphs, but blend background with subtle grey surfaces.
                if (!ReferenceEquals(root, _wallpaperVideo))
                {
                    root.BackColor = UiBackground;
                    root.ForeColor = UiText;
                }
            }
            else if (root is Panel panel)
            {
                bool dropZone = panel.BackColor == DropIdleColor || panel.BackColor == DropHotColor;
                if (!dropZone && panel.BackColor != Color.Transparent)
                {
                    panel.BackColor = UiBackground;
                }
                if (!dropZone)
                {
                    panel.ForeColor = UiText;
                }
            }
            else if (root is Label)
            {
                // Preserve explicit hint/warning/state colors and transparent backgrounds
                // (e.g. drop-zone overlay labels). Other labels inherit the page color.
                if (root.BackColor == Color.Transparent)
                {
                    root.BackColor = Color.Transparent;
                }
            }

            foreach (Control child in root.Controls)
            {
                ApplyModernStyle(child);
            }
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
