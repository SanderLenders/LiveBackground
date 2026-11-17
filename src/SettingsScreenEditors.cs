using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace LiveBackground
{
    // Per-screen editors shared by Multi Monitor, Playback and Other. They
    // live in this partial file so the main Videos/Images editors stay simple.
    public partial class SettingsForm
    {
        private TabControl _fadeTabs;
        private CrossfadeSlot _fadeSlot2;
        private CrossfadeSlot _fadeSlot3;
        private TabControl _desktopTabs;
        private DesktopSlot _desktopSlot2;
        private DesktopSlot _desktopSlot3;

        private interface IScreenOptionTab
        {
            bool TabAvailable { get; }
        }

        // ---------------------------------------------------------------
        // Shared nested-tab behavior
        // ---------------------------------------------------------------

        private static bool OptionTabAvailable(TabPage page)
        {
            if (page == null) return false;
            IScreenOptionTab tab = page.Tag as IScreenOptionTab;
            return tab == null || tab.TabAvailable;
        }

        private void OptionTabSelecting(object sender, TabControlCancelEventArgs e)
        {
            if (!OptionTabAvailable(e.TabPage)) e.Cancel = true;
        }

        private void OptionTabDrawItem(object sender, DrawItemEventArgs e)
        {
            TabControl tabs = sender as TabControl;
            if (tabs == null || e.Index < 0 || e.Index >= tabs.TabPages.Count) return;
            Rectangle bounds = tabs.GetTabRect(e.Index);
            using (SolidBrush back = new SolidBrush(SystemColors.Control))
            {
                e.Graphics.FillRectangle(back, bounds);
            }
            bool enabled = OptionTabAvailable(tabs.TabPages[e.Index]);
            bool selected = e.Index == tabs.SelectedIndex;
            using (Font font = selected ? new Font(e.Font, FontStyle.Bold) : new Font(e.Font, FontStyle.Regular))
            {
                TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, font, bounds,
                    enabled ? SystemColors.ControlText : SystemColors.GrayText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        private static void EnsureOptionTabSelection(TabControl tabs)
        {
            if (tabs == null || tabs.TabPages.Count == 0) return;
            if (tabs.SelectedTab != null && OptionTabAvailable(tabs.SelectedTab)) return;
            foreach (TabPage page in tabs.TabPages)
            {
                if (!OptionTabAvailable(page)) continue;
                tabs.SelectedTab = page;
                return;
            }
            // With no available page, leave the first one visible but inert so
            // the user can still see that these settings exist.
        }

        private static TabControl MakeOptionTabs(Control parent, int x, int y, int width, int height,
            SettingsForm form)
        {
            TabControl tabs = new TabControl();
            tabs.SetBounds(x, y, width, height);
            tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
            tabs.Selecting += form.OptionTabSelecting;
            tabs.DrawItem += form.OptionTabDrawItem;
            parent.Controls.Add(tabs);
            return tabs;
        }

        private bool VideoMirrorsForEditor(int number, string device)
        {
            ScreenSlot slot = number == 2 ? _slot2 : _slot3;
            if (slot != null && slot.Same != null &&
                string.Equals(slot.Device, device, StringComparison.OrdinalIgnoreCase))
            {
                return slot.Same.Checked;
            }
            return _config.ScreenFor(device).SameAsMain;
        }

        // Called whenever a monitor or a mirror switch changes. Binding is
        // idempotent, so polling never reloads an editor being used.
        private void UpdateScreenOptionTabs()
        {
            if (_fadeSlot2 != null)
            {
                _fadeSlot2.Bind(_slot2 == null ? "" : _slot2.Device);
                _fadeSlot3.Bind(_slot3 == null ? "" : _slot3.Device);
                EnsureOptionTabSelection(_fadeTabs);
                _fadeTabs.Invalidate();
            }
            if (_desktopSlot2 != null)
            {
                _desktopSlot2.Bind(_slot2 == null ? "" : _slot2.Device);
                _desktopSlot3.Bind(_slot3 == null ? "" : _slot3.Device);
                EnsureOptionTabSelection(_desktopTabs);
                _desktopTabs.Invalidate();
            }
        }

        // ---------------------------------------------------------------
        // Playback: one video crossfade editor per extra screen
        // ---------------------------------------------------------------

        private void BuildScreenFadeTabs(TabPage page)
        {
            _fadeTabs = MakeOptionTabs(page, 16, 186, 588, 150, this);
            _fadeSlot2 = new CrossfadeSlot(this, 2);
            _fadeSlot3 = new CrossfadeSlot(this, 3);
            _fadeTabs.TabPages.Add(_fadeSlot2.Build());
            _fadeTabs.TabPages.Add(_fadeSlot3.Build());
            UpdateScreenOptionTabs();
        }

        private sealed class CrossfadeSlot : IScreenOptionTab
        {
            private readonly SettingsForm _form;
            private readonly int _number;
            private TabPage _page;
            private TrackBar _bar;
            private CheckBox _fine;
            private Label _value;
            private bool _loading;

            public string Device = "";
            public bool HasScreen;
            public bool TabAvailable
            {
                get
                {
                    return HasScreen && !_form._secondDuplicateLayout &&
                        !_form.VideoMirrorsForEditor(_number, Device);
                }
            }

            public CrossfadeSlot(SettingsForm form, int number)
            {
                _form = form;
                _number = number;
            }

            public TabPage Build()
            {
                _page = new TabPage(_number == 2 ? "2nd Screen" : "3rd Screen");
                _page.Tag = this;

                _value = new Label();
                _value.SetBounds(458, 12, 100, 22);
                _value.TextAlign = ContentAlignment.MiddleRight;
                _value.Font = new Font(_form.Font, FontStyle.Bold);
                _page.Controls.Add(_value);

                _bar = new TrackBar();
                _bar.SetBounds(12, 10, 432, 46);
                _bar.Minimum = FadeMin;
                _bar.Maximum = FadeMax;
                _bar.TickStyle = TickStyle.BottomRight;
                _bar.ValueChanged += FadeChanged;
                _page.Controls.Add(_bar);

                _fine = new CheckBox();
                _fine.Text = "Allow half-second steps";
                _fine.SetBounds(16, 70, 320, 24);
                _fine.CheckedChanged += delegate { ApplyStepMode(); };
                _page.Controls.Add(_fine);
                return _page;
            }

            public void Bind(string device)
            {
                if (device == null) device = "";
                bool has = device.Length > 0;
                if (string.Equals(device, Device, StringComparison.OrdinalIgnoreCase) && has == HasScreen)
                {
                    UpdateControls();
                    return;
                }
                Device = device;
                HasScreen = has;
                LoadFromConfig();
                UpdateControls();
            }

            private void LoadFromConfig()
            {
                if (_bar == null) return;
                ScreenSettings settings = _form._config.ScreenFor(Device);
                _loading = true;
                try
                {
                    _fine.Checked = settings.FineCrossfadeSteps ?? _form._config.FineCrossfadeSteps;
                    ApplyStepMode();
                    double seconds = settings.CrossfadeSeconds ?? _form._config.CrossfadeSeconds;
                    _bar.Value = Clamp((int)Math.Round(seconds * 10.0), FadeMin, FadeMax);
                    UpdateValue();
                }
                finally { _loading = false; }
            }

            public void CaptureInto(AppConfig config)
            {
                if (!HasScreen || !TabAvailable || _bar == null) return;
                ScreenSettings settings = config.ScreenByDevice(Device, true);
                settings.CrossfadeSeconds = _bar.Value / 10.0;
                settings.FineCrossfadeSteps = _fine.Checked;
            }

            private void UpdateControls()
            {
                bool enabled = TabAvailable;
                if (_bar != null) _bar.Enabled = enabled;
                if (_fine != null) _fine.Enabled = enabled;
                if (_value != null) _value.Enabled = enabled;
            }

            private void ApplyStepMode()
            {
                if (_bar == null) return;
                int step = _fine.Checked ? 5 : 10;
                _bar.SmallChange = step;
                _bar.LargeChange = step * 2;
                _bar.TickFrequency = step;
                int snapped = SnapFade(_bar.Value, step);
                if (!_fine.Checked && snapped < CoarseMin) snapped = CoarseMin;
                if (snapped != _bar.Value) _bar.Value = snapped;
                UpdateValue();
            }

            private void FadeChanged(object sender, EventArgs e)
            {
                if (_loading) return;
                int step = _fine.Checked ? 5 : 10;
                int snapped = SnapFade(_bar.Value, step);
                if (!_fine.Checked && snapped < CoarseMin) snapped = CoarseMin;
                if (snapped != _bar.Value)
                {
                    _bar.Value = snapped;
                    return;
                }
                UpdateValue();
            }

            private void UpdateValue()
            {
                if (_value != null) _value.Text = (_bar.Value / 10.0).ToString("0.0",
                    CultureInfo.InvariantCulture) + " s";
            }
        }

        // ---------------------------------------------------------------
        // Other: one desktop fallback editor per extra screen
        // ---------------------------------------------------------------

        private void BuildDesktopFallbackTabs()
        {
            _desktopGroup.Height = 196;
            _desktopTabs = MakeOptionTabs(_desktopGroup, 8, 18, 572, 170, this);

            // Keep the existing main controls and simply move them into the
            // Main Screen page, so all existing load/capture code still works.
            TabPage main = new TabPage("Main Screen");
            _desktopTabs.TabPages.Add(main);
            main.Controls.Add(_desktopFrame);
            main.Controls.Add(_desktopCustom);
            main.Controls.Add(_desktopPath);
            main.Controls.Add(_desktopBrowse);
            main.Controls.Add(_desktopDrop);
            _desktopFrame.SetBounds(16, 8, 460, 24);
            _desktopCustom.SetBounds(16, 34, 200, 24);
            _desktopPath.SetBounds(36, 60, 330, 24);
            _desktopBrowse.SetBounds(374, 58, 86, 27);
            _desktopDrop.SetBounds(466, 56, 100, 27);

            _desktopSlot2 = new DesktopSlot(this, 2);
            _desktopSlot3 = new DesktopSlot(this, 3);
            _desktopTabs.TabPages.Add(_desktopSlot2.Build());
            _desktopTabs.TabPages.Add(_desktopSlot3.Build());
            UpdateScreenOptionTabs();
        }

        private sealed class DesktopSlot : IScreenOptionTab
        {
            private readonly SettingsForm _form;
            private readonly int _number;
            private TabPage _page;
            private RadioButton _frame;
            private RadioButton _custom;
            private TextBox _path;
            private Button _browse;
            private Panel _drop;

            public string Device = "";
            public bool HasScreen;
            public bool TabAvailable
            {
                get
                {
                    return HasScreen && !_form._secondDuplicateLayout &&
                        !_form.VideoMirrorsForEditor(_number, Device);
                }
            }

            public DesktopSlot(SettingsForm form, int number)
            {
                _form = form;
                _number = number;
            }

            public TabPage Build()
            {
                _page = new TabPage(_number == 2 ? "2nd Screen" : "3rd Screen");
                _page.Tag = this;

                _frame = new RadioButton();
                _frame.Text = "First frame of this screen's loop";
                _frame.SetBounds(16, 8, 460, 24);
                _frame.CheckedChanged += delegate { UpdateControls(); };
                _page.Controls.Add(_frame);

                _custom = new RadioButton();
                _custom.Text = "Use a custom image";
                _custom.SetBounds(16, 34, 200, 24);
                _custom.CheckedChanged += delegate { UpdateControls(); };
                _page.Controls.Add(_custom);

                _path = new TextBox();
                _path.SetBounds(36, 60, 330, 24);
                _page.Controls.Add(_path);

                _browse = new Button();
                _browse.Text = "Browse...";
                _browse.SetBounds(374, 58, 86, 27);
                _browse.Click += delegate { Browse(); };
                _page.Controls.Add(_browse);

                _drop = _form.MakeImageDropBox(_page, 466, 56, 100, 27, "Drop image",
                    delegate { Browse(); });
                _drop.Tag = this;
                return _page;
            }

            public void Bind(string device)
            {
                if (device == null) device = "";
                bool has = device.Length > 0;
                if (string.Equals(device, Device, StringComparison.OrdinalIgnoreCase) && has == HasScreen)
                {
                    UpdateControls();
                    return;
                }
                Device = device;
                HasScreen = has;
                LoadFromConfig();
                UpdateControls();
            }

            private void LoadFromConfig()
            {
                if (_frame == null) return;
                ScreenSettings settings = _form._config.ScreenFor(Device);
                bool custom = string.Equals(settings.DesktopWallpaperMode, "custom",
                    StringComparison.OrdinalIgnoreCase) ||
                    (string.IsNullOrEmpty(settings.DesktopWallpaperMode) &&
                    string.Equals(_form._config.DesktopWallpaperMode, "custom", StringComparison.OrdinalIgnoreCase));
                _custom.Checked = custom;
                _frame.Checked = !custom;
                _path.Text = string.IsNullOrEmpty(settings.DesktopWallpaperMode)
                    ? _form._config.DesktopWallpaperImage
                    : settings.DesktopWallpaperImage;
            }

            private void UpdateControls()
            {
                bool enabled = TabAvailable && !_form._wallpaperImages.Checked;
                bool custom = enabled && _custom != null && _custom.Checked;
                if (_frame != null) _frame.Enabled = enabled;
                if (_custom != null) _custom.Enabled = enabled;
                if (_path != null) _path.Enabled = custom;
                if (_browse != null) _browse.Enabled = custom;
                if (_drop != null) _drop.Enabled = custom;
            }

            public bool CaptureAndValidate(AppConfig config)
            {
                if (!HasScreen || !TabAvailable) return true;
                ScreenSettings settings = config.ScreenByDevice(Device, true);
                settings.DesktopWallpaperMode = _custom.Checked ? "custom" : "frame";
                if (!_custom.Checked) return true;

                string image = _path.Text.Trim();
                string full = Util.ResolvePath(_form._appRoot, image);
                if (image.Length == 0 || !File.Exists(full))
                {
                    MessageBox.Show(_form, "Choose an existing desktop image for the " +
                        (_number == 2 ? "2nd" : "3rd") + " screen.", "LiveBackground",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
                try
                {
                    settings.DesktopWallpaperImage = _form.ImportImageRelative(full, ImportPrefix());
                    return true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(_form, "Could not copy the desktop image: " + ex.Message,
                        "LiveBackground", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
            }

            private string ImportPrefix()
            {
                return "desktop-wallpaper-" + MediaPipeline.ScreenKey(Device);
            }

            private void Browse()
            {
                using (OpenFileDialog dialog = new OpenFileDialog())
                {
                    dialog.Filter = "Image files|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp|All files|*.*";
                    string start = _form.InitialPickerFolder(_path.Text, false);
                    if (Directory.Exists(start)) dialog.InitialDirectory = start;
                    if (dialog.ShowDialog(_form) == DialogResult.OK) ApplyImage(dialog.FileName);
                }
            }

            public void ApplyImage(string source)
            {
                try
                {
                    _path.Text = _form.ImportImageForUi(source, ImportPrefix());
                    _form._desktopWallpaper.Checked = true;
                    _custom.Checked = true;
                    UpdateControls();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(_form, "Could not use that image: " + ex.Message,
                        "LiveBackground", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        // ---------------------------------------------------------------
        // Multi Monitor: independent image and slideshow settings
        // ---------------------------------------------------------------

        private sealed class ScreenImagesSlot
        {
            private readonly SettingsForm _form;
            private readonly int _number;
            public Panel Panel;
            public string Device = "";
            public bool HasScreen;

            private CheckBox _same;
            private TextBox _folderBox;
            private RadioButton _modeSlideshow;
            private RadioButton _modeSingle;
            private ListView _list;
            private Button _moveUp;
            private Button _moveDown;
            private Button _sortAz;
            private Button _refresh;
            private Button _open;
            private NumericUpDown _interval;
            private TrackBar _fadeBar;
            private Label _fadeValue;
            private CheckBox _fineSteps;
            private CheckBox _shuffle;
            private readonly List<Control> _ownControls = new List<Control>();

            private List<PlaylistEntry> _playlist = new List<PlaylistEntry>();
            private bool _loading;
            private string _singleName = "";
            private bool _lastSingleMode;

            public ScreenImagesSlot(SettingsForm form, int number)
            {
                _form = form;
                _number = number;
            }

            private string Ordinal { get { return _number == 2 ? "2nd" : "3rd"; } }

            public Panel Build()
            {
                Panel = new Panel();

                _same = new CheckBox();
                // The noun follows the main screen's Images mode; UpdateSameLabel
                // refreshes it when that mode or the wallpaper pill changes.
                _same.Text = "Use the same image as the main screen";
                _same.SetBounds(12, 12, 420, 24);
                _same.CheckedChanged += delegate { UpdateControls(); };
                Panel.Controls.Add(_same);

                Label folderLabel = MakeLabel(Panel, "Image folder", 12, 46, 200);
                folderLabel.Font = new Font(_form.Font, FontStyle.Bold);
                _folderBox = new TextBox();
                _folderBox.SetBounds(12, 68, 456, 24);
                Panel.Controls.Add(_folderBox);
                Button browse = new Button();
                browse.Text = "Browse...";
                browse.SetBounds(480, 66, 104, 27);
                browse.Click += delegate { BrowseFolder(); };
                Panel.Controls.Add(browse);

                // "One image" leads here too, matching the main Images tab and
                // both Videos panes: the narrower choice sits on the left.
                _modeSingle = new RadioButton();
                _modeSingle.Text = "One image";
                _modeSingle.SetBounds(12, 102, 100, 22);
                _modeSingle.CheckedChanged += delegate { UpdateModeControls(); };
                Panel.Controls.Add(_modeSingle);
                _modeSlideshow = new RadioButton();
                _modeSlideshow.Text = "Slideshow";
                _modeSlideshow.SetBounds(120, 102, 100, 22);
                _modeSlideshow.CheckedChanged += delegate { UpdateModeControls(); };
                Panel.Controls.Add(_modeSlideshow);

                _list = new ListView();
                _list.SetBounds(12, 130, 572, 108);
                _list.View = View.Details;
                _list.CheckBoxes = true;
                _list.FullRowSelect = true;
                _list.GridLines = true;
                _list.HideSelection = false;
                _list.MultiSelect = false;
                _list.Columns.Add("Image", 548);
                _list.ItemCheck += ItemCheck;
                Panel.Controls.Add(_list);

                _moveUp = MakeButton("Move up", 12, 244, 90, delegate { Move(-1); });
                _moveDown = MakeButton("Move down", 108, 244, 100, delegate { Move(1); });
                _sortAz = MakeButton("Sort A-Z", 214, 244, 90, Sort);
                _refresh = MakeButton("Refresh", 12, 278, 100, delegate { CapturePlaylist(); RefreshImages(); });
                _open = MakeButton("Open folder", 118, 278, 110, OpenFolder);

                MakeLabel(Panel, "Interval (seconds)", 12, 318, 150);
                _interval = new NumericUpDown();
                _interval.SetBounds(164, 314, 76, 24);
                _interval.Minimum = 1;
                _interval.Maximum = 86400;
                Panel.Controls.Add(_interval);

                MakeLabel(Panel, "Crossfade", 260, 318, 90);
                _fadeBar = new TrackBar();
                _fadeBar.SetBounds(342, 306, 154, 42);
                _fadeBar.Minimum = 0;
                _fadeBar.Maximum = 10;
                _fadeBar.TickStyle = TickStyle.BottomRight;
                _fadeBar.ValueChanged += delegate { SnapFade(); UpdateFadeValue(); };
                Panel.Controls.Add(_fadeBar);
                _fadeValue = MakeLabel(Panel, "", 504, 318, 80);

                _fineSteps = new CheckBox();
                _fineSteps.Text = "Half-second fade steps";
                _fineSteps.SetBounds(12, 350, 190, 22);
                _fineSteps.CheckedChanged += delegate { ApplyFadeSteps(); };
                Panel.Controls.Add(_fineSteps);
                _shuffle = new CheckBox();
                _shuffle.Text = "Shuffle slideshow";
                _shuffle.SetBounds(214, 350, 170, 22);
                Panel.Controls.Add(_shuffle);

                _ownControls.Add(_folderBox);
                _ownControls.Add(browse);
                _ownControls.Add(_modeSlideshow);
                _ownControls.Add(_modeSingle);
                _ownControls.Add(_list);
                _ownControls.Add(_refresh);
                _ownControls.Add(_open);
                _ownControls.Add(_interval);
                _ownControls.Add(_fadeBar);
                _ownControls.Add(_fineSteps);
                _ownControls.Add(_shuffle);
                return Panel;
            }

            private Button MakeButton(string text, int x, int y, int width, Action click)
            {
                Button button = new Button();
                button.Text = text;
                button.SetBounds(x, y, width, 28);
                button.Click += delegate { click(); };
                Panel.Controls.Add(button);
                return button;
            }

            public void Bind(string device)
            {
                if (device == null) device = "";
                bool has = device.Length > 0;
                if (string.Equals(device, Device, StringComparison.OrdinalIgnoreCase) && has == HasScreen)
                {
                    UpdateControls();
                    return;
                }
                Device = device;
                HasScreen = has;
                if (HasScreen) LoadFromConfig();
                UpdateControls();
            }

            private void LoadFromConfig()
            {
                if (_folderBox == null) return;
                ScreenSettings settings = _form._config.ScreenFor(Device);
                _loading = true;
                try
                {
                    _same.Checked = settings.ImagesSameAsMain;
                    _folderBox.Text = settings.ImageFolder ?? "";
                    bool single = string.Equals(settings.ImageMode, "single", StringComparison.OrdinalIgnoreCase);
                    _lastSingleMode = single;
                    _singleName = settings.ImageSingle ?? "";
                    _modeSingle.Checked = single;
                    _modeSlideshow.Checked = !single;
                    _interval.Value = Math.Max(1, Math.Min(86400, settings.ImageIntervalSeconds));
                    _fineSteps.Checked = settings.ImageFineCrossfadeSteps;
                    ApplyFadeSteps();
                    int fade = (int)Math.Round(settings.ImageCrossfadeSeconds * 2.0);
                    _fadeBar.Value = Math.Max(0, Math.Min(10, fade));
                    _shuffle.Checked = settings.ImageShuffle;
                }
                finally { _loading = false; }
                _playlist = PlaylistState.CopyEntries(settings.ImagePlaylist);
                RefreshImages();
                UpdateFadeValue();
                UpdateControls();
            }

            public void CaptureInto(ScreenSettings settings)
            {
                if (settings == null) return;
                settings.ImagesSameAsMain = _same.Checked;
                if (_same.Checked) return;
                string folder = _folderBox.Text.Trim();
                if (folder.Length > 0) settings.ImageFolder = folder;
                settings.ImageMode = _modeSingle.Checked ? "single" : "slideshow";
                if (!string.IsNullOrEmpty(_singleName)) settings.ImageSingle = _singleName;
                List<PlaylistEntry> playlist = _modeSingle.Checked ? _playlist : CurrentPlaylist();
                if (playlist.Count == 0 && _playlist.Count > 0) playlist = _playlist;
                settings.ImagePlaylist = playlist;
                settings.ImageIntervalSeconds = (int)_interval.Value;
                settings.ImageCrossfadeSeconds = _fadeBar.Value / 2.0;
                settings.ImageFineCrossfadeSteps = _fineSteps.Checked;
                settings.ImageShuffle = _shuffle.Checked && !_modeSingle.Checked;
            }

            public bool Validate()
            {
                if (!HasScreen || _form._secondDuplicateLayout || _same.Checked) return true;
                string folderText = _folderBox.Text.Trim();
                if (folderText.Length == 0)
                {
                    MessageBox.Show(_form, "Choose an image folder for the " + Ordinal + " screen.",
                        "LiveBackground", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
                string folder = Util.ResolvePath(_form._appRoot, folderText);
                if (MediaPipeline.ScanImages(folder).Count == 0)
                {
                    MessageBox.Show(_form, "Add at least one picture for the " + Ordinal +
                        " screen, then press Refresh.", "LiveBackground",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
                if (_list.CheckedItems.Count == 0)
                {
                    MessageBox.Show(_form, _modeSingle.Checked
                        ? "Tick the image the " + Ordinal + " screen should show."
                        : "Tick at least one image for the " + Ordinal + " screen.",
                        "LiveBackground", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
                return true;
            }

            public void UpdateControls()
            {
                if (_same == null) return;
                bool enabled = HasScreen && !_form._secondDuplicateLayout;
                bool own = enabled && !_same.Checked;
                _same.Enabled = enabled;
                foreach (Control control in _ownControls) control.Enabled = own;
                bool slideshow = _modeSlideshow.Checked;
                _moveUp.Enabled = own && slideshow;
                _moveDown.Enabled = own && slideshow;
                _sortAz.Enabled = own && slideshow;
                _shuffle.Enabled = own && slideshow;
            }

            // "Use the same image/images as the main screen": the noun follows
            // the main Images tab, so it reads right whether the main screen is
            // showing one picture or a slideshow.
            public void UpdateSameLabel()
            {
                if (_same == null) return;
                bool slideshow = _form._imageModeSlideshow != null && _form._imageModeSlideshow.Checked;
                _same.Text = slideshow
                    ? "Use the same images as the main screen"
                    : "Use the same image as the main screen";
            }

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
                        if (_list.Items.Count > 0) _playlist = CurrentPlaylist();
                        ApplySingleTicks();
                    }
                    else
                    {
                        ApplyPlaylistTicks();
                    }
                }
                UpdateControls();
            }

            private void ItemCheck(object sender, ItemCheckEventArgs e)
            {
                if (_loading || !_modeSingle.Checked) return;
                if (e.NewValue != CheckState.Checked)
                {
                    e.NewValue = CheckState.Checked;
                    return;
                }
                _loading = true;
                for (int i = 0; i < _list.Items.Count; i++)
                {
                    if (i != e.Index) _list.Items[i].Checked = false;
                }
                _loading = false;
                if (e.Index >= 0 && e.Index < _list.Items.Count) _singleName = _list.Items[e.Index].Text;
            }

            private void ApplySingleTicks()
            {
                _loading = true;
                int index = PlaylistState.PreferredIndex(ListNames(_list), _singleName);
                SetListChecked(_list, PlaylistState.SingleTicks(_list.Items.Count, index));
                _singleName = index >= 0 ? _list.Items[index].Text : "";
                _loading = false;
            }

            private void ApplyPlaylistTicks()
            {
                _loading = true;
                SetListChecked(_list, PlaylistState.PlaylistTicks(ListNames(_list), _playlist, true));
                _loading = false;
            }

            private void CapturePlaylist()
            {
                if (!_modeSingle.Checked && _list.Items.Count > 0) _playlist = CurrentPlaylist();
            }

            private List<PlaylistEntry> CurrentPlaylist()
            {
                return PlaylistState.Capture(ListNames(_list), ListChecked(_list));
            }

            private void Move(int delta)
            {
                if (_list.SelectedIndices.Count == 0) return;
                int index = _list.SelectedIndices[0];
                int[] order = PlaylistState.SwapOrder(_list.Items.Count, index, delta);
                if (order == null) return;
                ApplyOrder(order, index + delta);
            }

            private void Sort()
            {
                ApplyOrder(PlaylistState.SortedOrder(ListNames(_list)), -1);
            }

            private void ApplyOrder(int[] order, int select)
            {
                _loading = true;
                ListViewItem[] items = new ListViewItem[order.Length];
                for (int i = 0; i < order.Length; i++)
                {
                    ListViewItem source = _list.Items[order[i]];
                    ListViewItem item = new ListViewItem(source.Text);
                    item.Checked = source.Checked;
                    items[i] = item;
                }
                _list.BeginUpdate();
                _list.Items.Clear();
                _list.Items.AddRange(items);
                _list.EndUpdate();
                _loading = false;
                if (select >= 0 && select < _list.Items.Count)
                {
                    _list.Items[select].Selected = true;
                    _list.Items[select].Focused = true;
                    _list.EnsureVisible(select);
                }
            }

            private void RefreshImages()
            {
                if (_list == null) return;
                _loading = true;
                _list.Items.Clear();
                string folder = Util.ResolvePath(_form._appRoot, _folderBox.Text);
                List<string> all = MediaPipeline.ScanImages(folder);
                Dictionary<string, string> files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string file in all) files[Path.GetFileName(file)] = file;
                Dictionary<string, PlaylistEntry> saved = new Dictionary<string, PlaylistEntry>(StringComparer.OrdinalIgnoreCase);
                foreach (PlaylistEntry entry in _playlist)
                {
                    if (entry != null && !saved.ContainsKey(entry.Name)) saved[entry.Name] = entry;
                }

                List<string> ordered = new List<string>();
                HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (PlaylistEntry entry in _playlist)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.Name)) continue;
                    string file;
                    if (seen.Add(entry.Name) && files.TryGetValue(entry.Name, out file)) ordered.Add(file);
                }
                foreach (string file in all)
                {
                    if (seen.Add(Path.GetFileName(file))) ordered.Add(file);
                }
                foreach (string file in ordered)
                {
                    string name = Path.GetFileName(file);
                    PlaylistEntry entry;
                    bool enabled = true;
                    if (saved.TryGetValue(name, out entry)) enabled = entry.Enabled;
                    ListViewItem item = new ListViewItem(name);
                    item.Checked = enabled;
                    _list.Items.Add(item);
                }
                if (_modeSingle.Checked) ApplySingleTicks();
                _loading = false;
            }

            private void BrowseFolder()
            {
                using (FolderBrowserDialog dialog = new FolderBrowserDialog())
                {
                    dialog.Description = "Choose the images for the " + Ordinal + " screen";
                    dialog.ShowNewFolderButton = true;
                    string current = Util.ResolvePath(_form._appRoot, _folderBox.Text);
                    string start = _form.InitialPickerFolder(_folderBox.Text, true);
                    if (Directory.Exists(start)) dialog.SelectedPath = start;
                    if (dialog.ShowDialog(_form) == DialogResult.OK)
                    {
                        _form._lastPickerFolder = dialog.SelectedPath;
                        if (!string.Equals(dialog.SelectedPath, current, StringComparison.OrdinalIgnoreCase))
                        {
                            _playlist = new List<PlaylistEntry>();
                            _singleName = "";
                        }
                        _folderBox.Text = dialog.SelectedPath;
                        RefreshImages();
                    }
                }
            }

            private void OpenFolder()
            {
                try
                {
                    string folder = Util.ResolvePath(_form._appRoot, _folderBox.Text);
                    if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                    System.Diagnostics.Process.Start("explorer.exe", Util.Quote(folder));
                }
                catch (Exception ex)
                {
                    MessageBox.Show(_form, "Could not open the folder: " + ex.Message,
                        "LiveBackground", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }

            private void ApplyFadeSteps()
            {
                if (_fadeBar == null) return;
                int step = CrossfadeState.Step(_fineSteps.Checked);
                _fadeBar.SmallChange = step;
                _fadeBar.LargeChange = step * 2;
                _fadeBar.TickFrequency = step;
                SnapFade();
                UpdateFadeValue();
            }

            private void SnapFade()
            {
                if (_loading || _fadeBar == null) return;
                int snapped = CrossfadeState.Snap(_fadeBar.Value, _fineSteps.Checked);
                if (snapped != _fadeBar.Value) _fadeBar.Value = snapped;
            }

            private void UpdateFadeValue()
            {
                if (_fadeValue == null) return;
                _fadeValue.Text = CrossfadeState.Text(_fadeBar.Value, "Off");
            }
        }
    }
}
