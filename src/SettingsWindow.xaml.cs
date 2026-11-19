using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LiveBackground
{
    public partial class SettingsWindow : Window
    {
        private readonly string _appRoot;
        private readonly AppConfig _config;
        private readonly List<ScreenInfo> _screens = new List<ScreenInfo>();
        private bool _hasSecondScreen;
        private bool _duplicateLayout;

        private TextBox _videoFolderBox;
        private RadioButton _videoModeSingle;
        private RadioButton _videoModePlaylist;
        private ListView _clipList;
        private TextBlock _clipHint;

        private TextBox _imageFolderBox;
        private RadioButton _imageModeSingle;
        private RadioButton _imageModeSlideshow;
        private ListView _imageList;
        private TextBlock _imageHint;

        private Slider _videoFadeSlider;
        private TextBlock _videoFadeValue;
        private CheckBox _videoFineSteps;
        private ComboBox _resolutionCombo;
        private ComboBox _fpsCombo;
        private ComboBox _qualityCombo;

        private Slider _intervalSlider;
        private TextBlock _intervalValue;
        private Slider _imageFadeSlider;
        private TextBlock _imageFadeValue;
        private CheckBox _imageFineSteps;
        private CheckBox _imageShuffle;

        private CheckBox _startWithWindows;
        private CheckBox _screensaverDimLock;
        private CheckBox _screensaverVideoBg;
        private RadioButton _lockFrame;
        private RadioButton _lockImages;
        private RadioButton _lockCustom;
        private ComboBox _lockImageCombo;
        private TextBox _lockCustomPath;
        private CheckBox _desktopWallpaper;
        private RadioButton _desktopFrame;
        private RadioButton _desktopCustom;
        private TextBox _desktopCustomPath;

        private TabControl _monitorTabs;
        private MonitorSlotUi _slot2;
        private MonitorSlotUi _slot3;
        private TextBlock _monitorStatus;

        private sealed class ClipItem
        {
            public string Name { get; set; }
            public string Length { get; set; }
            public bool Enabled { get; set; }
            public string Path { get; set; }
            public double Duration { get; set; }
        }
        private sealed class ImageItem
        {
            public string Name { get; set; }
            public bool Enabled { get; set; }
            public string Path { get; set; }
        }

        public SettingsWindow(string appRoot)
        {
            _appRoot = appRoot;
            AppConfig source = Program.Config;
            _config = source != null ? source.Clone() : new AppConfig();
            try { _config.BindLegacyScreens(Screens.Enumerate()); } catch { }
            InitializeComponent();
            SetWindowIcon();
            RefreshScreens();
            BuildAllTabs();
            LoadFromConfig();
            ApplyModeUi();
            UpdateMonitorState();
        }

        private void SetWindowIcon()
        {
            try
            {
                var exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
                {
                    using (var icon = System.Drawing.Icon.ExtractAssociatedIcon(exe))
                    {
                        if (icon != null)
                        {
                            Icon = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                            return;
                        }
                    }
                }
            }
            catch { }
        }

        private void RefreshScreens()
        {
            try
            {
                var list = Screens.Enumerate();
                _screens.Clear();
                _screens.AddRange(list);
                _hasSecondScreen = list.Count >= 2;
                _duplicateLayout = Screens.IsDuplicate(list);
            }
            catch
            {
                _screens.Clear();
                _hasSecondScreen = false;
                _duplicateLayout = false;
            }
        }

        private static T Find<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            if (parent == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                var fe = child as T;
                if (fe != null && fe.Name == name) return fe;
                var found = Find<T>(child, name);
                if (found != null) return found;
            }
            return null;
        }

        private void BuildAllTabs()
        {
            BuildVideosTab();
            BuildPlaybackTab();
            BuildImagesTab();
            BuildSlideshowTab();
            BuildMultiMonitorTab();
            BuildOtherTab();
        }

        private void BuildVideosTab()
        {
            var panel = VideosPanel;
            panel.Children.Clear();
            panel.Children.Add(Header("Video folder"));
            var row = new DockPanel { Margin = new Thickness(0, 6, 0, 10) };
            var browse = new Button { Content = "Browse...", MinWidth = 100, Margin = new Thickness(8, 0, 0, 0) };
            DockPanel.SetDock(browse, Dock.Right);
            browse.Click += delegate { BrowseInto(_videoFolderBox); RefreshClipList(); };
            _videoFolderBox = new TextBox { MinWidth = 420 };
            row.Children.Add(browse);
            row.Children.Add(_videoFolderBox);
            panel.Children.Add(row);

            var modeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            _videoModeSingle = new RadioButton { Content = "Single clip", GroupName = "vmode", Margin = new Thickness(0, 0, 16, 0) };
            _videoModePlaylist = new RadioButton { Content = "All clips in this order", GroupName = "vmode" };
            _videoModeSingle.Checked += delegate { UpdateClipListMode(); };
            _videoModePlaylist.Checked += delegate { UpdateClipListMode(); };
            modeRow.Children.Add(_videoModeSingle);
            modeRow.Children.Add(_videoModePlaylist);
            panel.Children.Add(modeRow);

            _clipList = new ListView { Height = 280, Margin = new Thickness(0, 0, 0, 8) };
            var gv = new GridView();
            gv.Columns.Add(new GridViewColumn { Header = "", Width = 34, CellTemplate = CheckCellTemplate("Enabled") });
            gv.Columns.Add(new GridViewColumn { Header = "Clip", Width = 280, DisplayMemberBinding = new System.Windows.Data.Binding("Name") });
            gv.Columns.Add(new GridViewColumn { Header = "Length", Width = 90, DisplayMemberBinding = new System.Windows.Data.Binding("Length") });
            _clipList.View = gv;
            panel.Children.Add(_clipList);

            var btns = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            btns.Children.Add(ActionButton("Move up", delegate { MoveSelected(_clipList, -1); }));
            btns.Children.Add(ActionButton("Move down", delegate { MoveSelected(_clipList, 1); }));
            btns.Children.Add(ActionButton("Sort A-Z", delegate { SortClipItems(); }));
            btns.Children.Add(ActionButton("Refresh", delegate { RefreshClipList(); }));
            btns.Children.Add(ActionButton("Open folder", delegate { OpenFolderSafe(Resolve(_videoFolderBox.Text)); }));
            panel.Children.Add(btns);
            _clipHint = Muted("Add video files to the folder, then refresh. Tick clips to include them in the playlist.");
            panel.Children.Add(_clipHint);
        }

        private void BuildPlaybackTab()
        {
            var panel = PlaybackPanel; panel.Children.Clear();
            panel.Children.Add(Header("Crossfade"));
            var fadeRow = new DockPanel { Margin = new Thickness(0, 6, 0, 4) };
            _videoFadeValue = new TextBlock { MinWidth = 60, TextAlignment = TextAlignment.Right, Foreground = Brushes.White };
            DockPanel.SetDock(_videoFadeValue, Dock.Right);
            _videoFadeSlider = new Slider { Minimum = 0.5, Maximum = 10.0, TickFrequency = 0.5, IsSnapToTickEnabled = true, SmallChange = 0.5, LargeChange = 1.0 };
            _videoFadeSlider.ValueChanged += delegate { UpdateFadeLabel(); };
            fadeRow.Children.Add(_videoFadeValue); fadeRow.Children.Add(_videoFadeSlider);
            panel.Children.Add(fadeRow);
            _videoFineSteps = new CheckBox { Content = "Allow half-second steps (finer control)", Margin = new Thickness(0, 0, 0, 16) };
            panel.Children.Add(_videoFineSteps);

            panel.Children.Add(Header("Quality"));
            var grid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            AddLabeledCombo(grid, 0, "Resolution", out _resolutionCombo);
            AddLabeledCombo(grid, 1, "Frame rate", out _fpsCombo);
            AddLabeledCombo(grid, 2, "Quality", out _qualityCombo);
            panel.Children.Add(grid);

            _resolutionCombo.Items.Add("Match screen (recommended)"); _resolutionCombo.Items.Add("1920 x 1080"); _resolutionCombo.Items.Add("2560 x 1440"); _resolutionCombo.Items.Add("3840 x 2160");
            _fpsCombo.Items.Add("24 fps"); _fpsCombo.Items.Add("30 fps"); _fpsCombo.Items.Add("60 fps");
            _qualityCombo.Items.Add("High quality (slower build)"); _qualityCombo.Items.Add("Balanced"); _qualityCombo.Items.Add("Smaller file (faster build)");
        }

        private void BuildImagesTab()
        {
            var panel = ImagesPanel; panel.Children.Clear();
            panel.Children.Add(Header("Image folder"));
            var row = new DockPanel { Margin = new Thickness(0, 6, 0, 10) };
            var browse = new Button { Content = "Browse...", MinWidth = 100, Margin = new Thickness(8, 0, 0, 0) };
            DockPanel.SetDock(browse, Dock.Right);
            browse.Click += delegate { BrowseInto(_imageFolderBox); RefreshImageList(); RefreshLockImageChoices(); };
            _imageFolderBox = new TextBox { MinWidth = 420 };
            row.Children.Add(browse); row.Children.Add(_imageFolderBox); panel.Children.Add(row);

            var modeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            _imageModeSingle = new RadioButton { Content = "One image", GroupName = "imode", Margin = new Thickness(0, 0, 16, 0) };
            _imageModeSlideshow = new RadioButton { Content = "Slideshow", GroupName = "imode" };
            modeRow.Children.Add(_imageModeSingle); modeRow.Children.Add(_imageModeSlideshow); panel.Children.Add(modeRow);

            _imageList = new ListView { Height = 300, Margin = new Thickness(0, 0, 0, 8) };
            var gv = new GridView();
            gv.Columns.Add(new GridViewColumn { Header = "", Width = 34, CellTemplate = CheckCellTemplate("Enabled") });
            gv.Columns.Add(new GridViewColumn { Header = "Image", Width = 380, DisplayMemberBinding = new System.Windows.Data.Binding("Name") });
            _imageList.View = gv;
            panel.Children.Add(_imageList);

            var btns = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            btns.Children.Add(ActionButton("Move up", delegate { MoveSelected(_imageList, -1); }));
            btns.Children.Add(ActionButton("Move down", delegate { MoveSelected(_imageList, 1); }));
            btns.Children.Add(ActionButton("Sort A-Z", delegate { SortImageItems(); }));
            btns.Children.Add(ActionButton("Refresh", delegate { RefreshImageList(); RefreshLockImageChoices(); }));
            btns.Children.Add(ActionButton("Open folder", delegate { OpenFolderSafe(Resolve(_imageFolderBox.Text)); }));
            panel.Children.Add(btns);
            _imageHint = Muted("Add pictures to the folder, then refresh. Tick images to include them.");
            panel.Children.Add(_imageHint);
        }

        private void BuildSlideshowTab()
        {
            var panel = SlideshowPanel; panel.Children.Clear();
            panel.Children.Add(Header("Slideshow"));
            panel.Children.Add(Muted("Time each picture stays on screen:"));
            var irow = new DockPanel { Margin = new Thickness(0, 6, 0, 12) };
            _intervalValue = new TextBlock { MinWidth = 70, TextAlignment = TextAlignment.Right };
            DockPanel.SetDock(_intervalValue, Dock.Right);
            _intervalSlider = new Slider { Minimum = 5, Maximum = 300, TickFrequency = 5, IsSnapToTickEnabled = true };
            _intervalSlider.ValueChanged += delegate { UpdateIntervalLabel(); };
            irow.Children.Add(_intervalValue); irow.Children.Add(_intervalSlider); panel.Children.Add(irow);

            panel.Children.Add(Muted("Crossfade between pictures:"));
            var frow = new DockPanel { Margin = new Thickness(0, 6, 0, 4) };
            _imageFadeValue = new TextBlock { MinWidth = 70, TextAlignment = TextAlignment.Right };
            DockPanel.SetDock(_imageFadeValue, Dock.Right);
            _imageFadeSlider = new Slider { Minimum = 0, Maximum = 5, TickFrequency = 0.5, IsSnapToTickEnabled = true };
            _imageFadeSlider.ValueChanged += delegate { UpdateImageFadeLabel(); };
            frow.Children.Add(_imageFadeValue); frow.Children.Add(_imageFadeSlider); panel.Children.Add(frow);
            _imageFineSteps = new CheckBox { Content = "Allow half-second steps", Margin = new Thickness(0, 0, 0, 10) };
            _imageShuffle = new CheckBox { Content = "Shuffle pictures", Margin = new Thickness(0, 0, 0, 8) };
            panel.Children.Add(_imageFineSteps);
            panel.Children.Add(_imageShuffle);
        }

        private void BuildMultiMonitorTab()
        {
            var panel = MultiMonitorPanel; panel.Children.Clear();
            panel.Children.Add(Header("Multiple monitors"));
            _monitorStatus = Muted("");
            panel.Children.Add(_monitorStatus);
            var openDisplay = ActionButton("Open Windows display settings...", OpenWindowsDisplaySettings);
            openDisplay.Margin = new Thickness(0, 8, 0, 12);
            panel.Children.Add(openDisplay);

            _monitorTabs = new TabControl { MinHeight = 320 };
            panel.Children.Add(_monitorTabs);
            _slot2 = new MonitorSlotUi(this, 2);
            _slot3 = new MonitorSlotUi(this, 3);
            _monitorTabs.Items.Add(_slot2.BuildTabItem());
            _monitorTabs.Items.Add(_slot3.BuildTabItem());
        }

        private void BuildOtherTab()
        {
            var panel = OtherPanel; panel.Children.Clear();
            panel.Children.Add(Header("Startup"));
            _startWithWindows = new CheckBox { Content = "Start LiveBackground when I sign in to Windows", Margin = new Thickness(0, 6, 0, 16) };
            panel.Children.Add(_startWithWindows);

            panel.Children.Add(Header("Screensaver"));
            _screensaverDimLock = new CheckBox { Content = "Screensaver Dim and Lock", Margin = new Thickness(0, 6, 0, 4) };
            _screensaverVideoBg = new CheckBox { Content = "Screensaver Video Background", Margin = new Thickness(0, 0, 0, 6) };
            _screensaverDimLock.Checked += delegate { if (_screensaverDimLock.IsChecked == true) _screensaverVideoBg.IsChecked = false; };
            _screensaverVideoBg.Checked += delegate { if (_screensaverVideoBg.IsChecked == true) _screensaverDimLock.IsChecked = false; };
            panel.Children.Add(_screensaverDimLock);
            panel.Children.Add(_screensaverVideoBg);
            var ssBtn = ActionButton("Screensaver settings...", OpenScreensaverSettings);
            ssBtn.Margin = new Thickness(0, 0, 0, 16);
            panel.Children.Add(ssBtn);

            panel.Children.Add(Header("Lock screen"));
            _lockFrame = new RadioButton { Content = "Use the first frame of the video loop", GroupName = "lock", Margin = new Thickness(0, 6, 0, 4) };
            _lockImages = new RadioButton { Content = "Use an image from your image folder", GroupName = "lock", Margin = new Thickness(0, 0, 0, 4) };
            _lockImageCombo = new ComboBox { MinWidth = 260, Margin = new Thickness(20, 0, 0, 8) };
            _lockCustom = new RadioButton { Content = "Use a custom image", GroupName = "lock", Margin = new Thickness(0, 0, 0, 4) };
            var lockPathRow = new DockPanel { Margin = new Thickness(20, 0, 0, 16) };
            var lockBrowse = new Button { Content = "Browse...", MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
            DockPanel.SetDock(lockBrowse, Dock.Right);
            lockBrowse.Click += delegate { BrowseImageInto(_lockCustomPath); _lockCustom.IsChecked = true; };
            _lockCustomPath = new TextBox();
            lockPathRow.Children.Add(lockBrowse); lockPathRow.Children.Add(_lockCustomPath);
            panel.Children.Add(_lockFrame);
            panel.Children.Add(_lockImages);
            panel.Children.Add(_lockImageCombo);
            panel.Children.Add(_lockCustom);
            panel.Children.Add(lockPathRow);

            panel.Children.Add(Header("Desktop wallpaper (video mode)"));
            _desktopWallpaper = new CheckBox { Content = "Set a desktop wallpaper image behind the video", Margin = new Thickness(0, 6, 0, 6) };
            _desktopFrame = new RadioButton { Content = "Use the first frame of the video loop", GroupName = "desk", Margin = new Thickness(20, 0, 0, 4) };
            _desktopCustom = new RadioButton { Content = "Use a custom image", GroupName = "desk", Margin = new Thickness(20, 0, 0, 4) };
            var deskPathRow = new DockPanel { Margin = new Thickness(40, 0, 0, 8) };
            var deskBrowse = new Button { Content = "Browse...", MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
            DockPanel.SetDock(deskBrowse, Dock.Right);
            deskBrowse.Click += delegate { BrowseImageInto(_desktopCustomPath); _desktopCustom.IsChecked = true; };
            _desktopCustomPath = new TextBox();
            deskPathRow.Children.Add(deskBrowse); deskPathRow.Children.Add(_desktopCustomPath);
            panel.Children.Add(_desktopWallpaper);
            panel.Children.Add(_desktopFrame);
            panel.Children.Add(_desktopCustom);
            panel.Children.Add(deskPathRow);
        }

        private void UpdateFadeLabel() { if (_videoFadeValue != null && _videoFadeSlider != null) _videoFadeValue.Text = _videoFadeSlider.Value.ToString("0.0", CultureInfo.InvariantCulture) + " s"; }
        private void UpdateIntervalLabel() { if (_intervalValue != null && _intervalSlider != null) _intervalValue.Text = ((int)Math.Round(_intervalSlider.Value)).ToString(CultureInfo.InvariantCulture) + " s"; }
        private void UpdateImageFadeLabel() { if (_imageFadeValue != null && _imageFadeSlider != null) _imageFadeValue.Text = _imageFadeSlider.Value.ToString("0.0", CultureInfo.InvariantCulture) + " s"; }

        private static TextBlock Header(string text) { return new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(0, 0, 0, 2) }; }
        private static TextBlock Muted(string text) { return new TextBlock { Text = text, Foreground = new SolidColorBrush(Color.FromRgb(167, 176, 188)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) }; }
        private static Button ActionButton(string text, Action action) { var b = new Button { Content = text, Margin = new Thickness(0, 0, 8, 8), MinWidth = 90 }; b.Click += delegate { action(); }; return b; }

        private static DataTemplate CheckCellTemplate(string bindingPath)
        {
            var fef = new FrameworkElementFactory(typeof(CheckBox));
            fef.SetBinding(ToggleButton.IsCheckedProperty, new System.Windows.Data.Binding(bindingPath) { Mode = System.Windows.Data.BindingMode.TwoWay, UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged });
            fef.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            return new DataTemplate { VisualTree = fef };
        }

        private static void AddLabeledCombo(Grid grid, int row, string label, out ComboBox combo)
        {
            var l = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 12, 4), Foreground = new SolidColorBrush(Color.FromRgb(167, 176, 188)) };
            Grid.SetRow(l, row); Grid.SetColumn(l, 0);
            combo = new ComboBox { Margin = new Thickness(0, 4, 0, 4), MinWidth = 260, HorizontalAlignment = HorizontalAlignment.Left };
            Grid.SetRow(combo, row); Grid.SetColumn(combo, 1);
            grid.Children.Add(l); grid.Children.Add(combo);
        }

        private string Resolve(string path) { try { return Util.ResolvePath(_appRoot, path ?? ""); } catch { return path ?? ""; } }

        private void BrowseInto(TextBox box)
        {
            try
            {
                using (var dlg = new System.Windows.Forms.FolderBrowserDialog())
                {
                    string cur = Resolve(box.Text);
                    if (Directory.Exists(cur)) dlg.SelectedPath = cur;
                    dlg.Description = "Choose a folder";
                    if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK) box.Text = dlg.SelectedPath;
                }
            }
            catch { }
        }

        private static void BrowseImageInto(TextBox box)
        {
            try
            {
                var dlg = new Microsoft.Win32.OpenFileDialog();
                dlg.Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp|All files|*.*";
                if (dlg.ShowDialog() == true) box.Text = dlg.FileName;
            }
            catch { }
        }

        private static void OpenFolderSafe(string folder)
        {
            try { if (!Directory.Exists(folder)) Directory.CreateDirectory(folder); Process.Start("explorer.exe", Util.Quote(folder)); }
            catch (Exception ex) { try { Log.Warn("could not open " + folder + ": " + ex.Message); } catch { } }
        }

        private static void OpenScreensaverSettings()
        {
            try { Process.Start("control.exe", "desk.cpl,,@screensaver"); }
            catch (Exception ex) { try { Log.Warn("could not open screensaver settings: " + ex.Message); } catch { } }
        }

        private static void OpenWindowsDisplaySettings()
        {
            try { var psi = new ProcessStartInfo("ms-settings:display") { UseShellExecute = true }; Process.Start(psi); }
            catch (Exception ex) { try { Log.Warn("could not open display settings: " + ex.Message); } catch { } }
        }

        private void ApplyModeUi()
        {
            bool images = ModeImagesBtn.IsChecked == true;
            ModeVideoBtn.IsChecked = !images;
            ModeImagesBtn.IsChecked = images;

            TabVideos.Visibility = images ? Visibility.Collapsed : Visibility.Visible;
            TabPlayback.Visibility = images ? Visibility.Collapsed : Visibility.Visible;
            TabImages.Visibility = images ? Visibility.Visible : Visibility.Collapsed;
            TabSlideshow.Visibility = images ? Visibility.Visible : Visibility.Collapsed;

            if (MainTabs.SelectedItem is TabItem ti && ti.Visibility != Visibility.Visible)
            {
                MainTabs.SelectedItem = images ? TabImages : TabVideos;
            }
            UpdateDesktopEnabled();
            if (_slot2 != null) _slot2.SetMode(images);
            if (_slot3 != null) _slot3.SetMode(images);
            UpdateMonitorTabEnabled();
        }

        private void UpdateDesktopEnabled()
        {
            bool images = ModeImagesBtn.IsChecked == true;
            if (_desktopWallpaper != null)
            {
                _desktopWallpaper.IsEnabled = !images;
                _desktopFrame.IsEnabled = !images && _desktopWallpaper.IsChecked == true;
                _desktopCustom.IsEnabled = !images && _desktopWallpaper.IsChecked == true;
                _desktopCustomPath.IsEnabled = !images && _desktopWallpaper.IsChecked == true;
            }
        }

        private void ModeVideoBtn_Click(object sender, RoutedEventArgs e) { ModeImagesBtn.IsChecked = false; ModeVideoBtn.IsChecked = true; ApplyModeUi(); }
        private void ModeImagesBtn_Click(object sender, RoutedEventArgs e) { ModeVideoBtn.IsChecked = false; ModeImagesBtn.IsChecked = true; ApplyModeUi(); }

        private void LoadFromConfig()
        {
            bool images = string.Equals(_config.WallpaperMode, "images", StringComparison.OrdinalIgnoreCase);
            ModeImagesBtn.IsChecked = images;
            ModeVideoBtn.IsChecked = !images;

            _videoFolderBox.Text = _config.VideoFolder ?? "";
            _imageFolderBox.Text = _config.ImageFolder ?? "";

            _videoModeSingle.IsChecked = string.Equals(_config.PlaybackMode, "single", StringComparison.OrdinalIgnoreCase);
            _videoModePlaylist.IsChecked = !_videoModeSingle.IsChecked;

            _imageModeSingle.IsChecked = string.Equals(_config.ImageMode, "single", StringComparison.OrdinalIgnoreCase);
            _imageModeSlideshow.IsChecked = !_imageModeSingle.IsChecked;

            _videoFadeSlider.Value = Clamp(_config.CrossfadeSeconds, _videoFadeSlider.Minimum, _videoFadeSlider.Maximum);
            _videoFineSteps.IsChecked = _config.FineCrossfadeSteps;
            UpdateFadeLabel();

            SetComboByContains(_resolutionCombo, _config.RenderWidth + " x " + _config.RenderHeight, 0);
            SetComboByContains(_fpsCombo, _config.Fps + " fps", 1);
            SetQualityCombo(_config.Crf);

            _intervalSlider.Value = Clamp(_config.ImageIntervalSeconds, _intervalSlider.Minimum, _intervalSlider.Maximum);
            _imageFadeSlider.Value = Clamp(_config.ImageCrossfadeSeconds, _imageFadeSlider.Minimum, _imageFadeSlider.Maximum);
            _imageFineSteps.IsChecked = _config.ImageFineCrossfadeSteps;
            _imageShuffle.IsChecked = _config.ImageShuffle;
            UpdateIntervalLabel(); UpdateImageFadeLabel();

            _startWithWindows.IsChecked = IsStartupEnabled(_config.TaskName);
            _screensaverDimLock.IsChecked = _config.ScreensaverActive;
            _screensaverVideoBg.IsChecked = _config.ScreensaverVideo;
            if (_screensaverDimLock.IsChecked == true && _screensaverVideoBg.IsChecked == true) _screensaverVideoBg.IsChecked = false;

            string lockMode = (_config.LockScreenMode ?? "frame").ToLowerInvariant();
            _lockFrame.IsChecked = lockMode == "frame";
            _lockImages.IsChecked = lockMode == "image";
            _lockCustom.IsChecked = lockMode == "custom";
            _lockCustomPath.Text = lockMode == "custom" ? (_config.LockScreenImage ?? "") : "";

            _desktopWallpaper.IsChecked = _config.DesktopWallpaper;
            bool deskCustom = string.Equals(_config.DesktopWallpaperMode, "custom", StringComparison.OrdinalIgnoreCase);
            _desktopCustom.IsChecked = deskCustom;
            _desktopFrame.IsChecked = !deskCustom;
            _desktopCustomPath.Text = deskCustom ? (_config.DesktopWallpaperImage ?? "") : "";

            RefreshClipList();
            RefreshImageList();
            RefreshLockImageChoices();
            if (lockMode == "image" && !string.IsNullOrEmpty(_config.LockScreenImage))
            {
                string name = Path.GetFileName(_config.LockScreenImage);
                for (int i = 0; i < _lockImageCombo.Items.Count; i++) if (string.Equals(_lockImageCombo.Items[i] as string, name, StringComparison.OrdinalIgnoreCase)) { _lockImageCombo.SelectedIndex = i; break; }
            }

            if (_slot2 != null) _slot2.LoadFromConfig(_config, _screens, _hasSecondScreen, _duplicateLayout);
            if (_slot3 != null) _slot3.LoadFromConfig(_config, _screens, _hasSecondScreen, _duplicateLayout);
        }

        private void SetQualityCombo(int crf)
        {
            if (crf <= 18) _qualityCombo.SelectedIndex = 0;
            else if (crf <= 23) _qualityCombo.SelectedIndex = 1;
            else _qualityCombo.SelectedIndex = 2;
        }

        private static double Clamp(double v, double min, double max) { if (v < min) return min; if (v > max) return max; return v; }
        private static void SetComboByContains(ComboBox combo, string needle, int fallback)
        {
            for (int i = 0; i < combo.Items.Count; i++) { var s = combo.Items[i] as string; if (s != null && s.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0) { combo.SelectedIndex = i; return; } }
            combo.SelectedIndex = fallback;
        }

        private void RefreshClipList()
        {
            var items = new ObservableCollection<ClipItem>();
            try
            {
                string folder = Resolve(_videoFolderBox.Text);
                var files = MediaPipeline.ScanVideos(folder);
                var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var f in files) byName[Path.GetFileName(f)] = f;
                var ordered = new List<string>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (_config.Playlist != null)
                {
                    foreach (var e in _config.Playlist)
                    {
                        if (e == null || string.IsNullOrEmpty(e.Name)) continue;
                        string n = e.Name.Trim();
                        if (!seen.Add(n)) continue;
                        string f; if (byName.TryGetValue(n, out f)) ordered.Add(f);
                    }
                }
                foreach (var f in files) { string n = Path.GetFileName(f); if (seen.Add(n)) ordered.Add(f); }

                var enabledMap = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                if (_config.Playlist != null) foreach (var e in _config.Playlist) if (e != null && !string.IsNullOrEmpty(e.Name)) enabledMap[e.Name.Trim()] = e.Enabled;

                foreach (var f in ordered)
                {
                    string name = Path.GetFileName(f);
                    bool en;
                    if (!enabledMap.TryGetValue(name, out en)) en = true;
                    items.Add(new ClipItem { Name = name, Path = f, Enabled = en, Length = "", Duration = -1 });
                }
            }
            catch { }
            _clipList.ItemsSource = items;
            UpdateClipListMode();
        }

        private void RefreshImageList()
        {
            var items = new ObservableCollection<ImageItem>();
            try
            {
                string folder = Resolve(_imageFolderBox.Text);
                var files = MediaPipeline.ScanImages(folder);
                var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var f in files) byName[Path.GetFileName(f)] = f;
                var ordered = new List<string>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var sourceList = _config.ImagePlaylist;
                if (sourceList != null)
                {
                    foreach (var e in sourceList)
                    {
                        if (e == null || string.IsNullOrEmpty(e.Name)) continue;
                        string n = e.Name.Trim();
                        if (!seen.Add(n)) continue;
                        string f; if (byName.TryGetValue(n, out f)) ordered.Add(f);
                    }
                }
                foreach (var f in files) { string n = Path.GetFileName(f); if (seen.Add(n)) ordered.Add(f); }

                var enabledMap = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                if (sourceList != null) foreach (var e in sourceList) if (e != null && !string.IsNullOrEmpty(e.Name)) enabledMap[e.Name.Trim()] = e.Enabled;
                bool hasPlaylist = sourceList != null && sourceList.Count > 0;

                foreach (var f in ordered)
                {
                    string name = Path.GetFileName(f);
                    bool en;
                    if (!enabledMap.TryGetValue(name, out en)) en = !hasPlaylist;
                    items.Add(new ImageItem { Name = name, Path = f, Enabled = en });
                }
            }
            catch { }
            _imageList.ItemsSource = items;
        }

        private void RefreshLockImageChoices()
        {
            if (_lockImageCombo == null) return;
            string current = _lockImageCombo.SelectedItem as string;
            _lockImageCombo.Items.Clear();
            try
            {
                string folder = Resolve(_imageFolderBox.Text);
                if (Directory.Exists(folder))
                {
                    foreach (var f in MediaPipeline.ScanImages(folder)) _lockImageCombo.Items.Add(Path.GetFileName(f));
                }
            }
            catch { }
            if (!string.IsNullOrEmpty(current))
            {
                for (int i = 0; i < _lockImageCombo.Items.Count; i++) if (string.Equals(_lockImageCombo.Items[i] as string, current, StringComparison.OrdinalIgnoreCase)) { _lockImageCombo.SelectedIndex = i; return; }
            }
            if (_lockImageCombo.Items.Count > 0 && _lockImageCombo.SelectedIndex < 0) _lockImageCombo.SelectedIndex = 0;
        }

        private void UpdateClipListMode() { _ = _clipList; }

        private static void MoveSelected(ListView list, int delta)
        {
            if (list == null) return;
            var oc = list.ItemsSource as System.Collections.IList;
            if (oc == null || list.SelectedIndex < 0) return;
            int i = list.SelectedIndex;
            int j = i + delta;
            if (j < 0 || j >= oc.Count) return;
            var item = oc[i];
            oc.RemoveAt(i);
            oc.Insert(j, item);
            list.SelectedIndex = j;
        }

        private void SortClipItems()
        {
            var items = _clipList.ItemsSource as ObservableCollection<ClipItem>;
            if (items == null) return;
            var sorted = items.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
            items.Clear();
            foreach (var it in sorted) items.Add(it);
        }
        private void SortImageItems()
        {
            var items = _imageList.ItemsSource as ObservableCollection<ImageItem>;
            if (items == null) return;
            var sorted = items.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
            items.Clear();
            foreach (var it in sorted) items.Add(it);
        }

        private void UpdateMonitorState()
        {
            RefreshScreens();
            if (_slot2 != null) _slot2.RefreshBinding(_config, _screens, _hasSecondScreen, _duplicateLayout);
            if (_slot3 != null) _slot3.RefreshBinding(_config, _screens, _hasSecondScreen, _duplicateLayout);
            UpdateMonitorTabEnabled();
            if (_monitorStatus != null)
            {
                if (_screens.Count <= 1 && !_duplicateLayout) _monitorStatus.Text = "Only one screen detected. Connect another screen or enable Duplicate in Windows to configure it here.";
                else if (_duplicateLayout) _monitorStatus.Text = "Windows is duplicating displays. Multi-monitor per-screen options are limited while duplicating.";
                else _monitorStatus.Text = "Configure each extra screen separately. Settings are remembered by monitor name.";
            }
        }

        private void UpdateMonitorTabEnabled()
        {
            bool available = _hasSecondScreen || _duplicateLayout;
            TabMultiMonitor.IsEnabled = available;
            if (!available && MainTabs.SelectedItem == TabMultiMonitor) MainTabs.SelectedItem = ModeImagesBtn.IsChecked == true ? TabImages : TabVideos;
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            UpdateMonitorState();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            AppConfig updated = CollectConfig();
            if (updated == null) return;

            try
            {
                ConfigStore.Save(_appRoot, updated);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not save the settings: " + ex.Message, "LiveBackground", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            bool wantStartup = _startWithWindows.IsChecked == true;
            bool startupCurrentlyEnabled = IsStartupEnabled(updated.TaskName);
            if (wantStartup != startupCurrentlyEnabled)
            {
                try
                {
                    InstallState state = InstallState.Load(_appRoot);
                    Action<string> log = delegate(string message) { try { Log.Info("settings: " + message); } catch { } };
                    if (wantStartup)
                    {
                        Installer.CreateStartup(_appRoot, updated, state, log);
                        state.Save(_appRoot);
                    }
                    else
                    {
                        Installer.RemoveStartup(_appRoot, updated, log);
                    }
                }
                catch (Exception ex)
                {
                    try { Log.Warn("could not update startup setting: " + ex.Message); } catch { }
                }
            }

            Program.ApplyConfig(updated);
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e) { Close(); }

        private AppConfig CollectConfig()
        {
            bool imagesMode = ModeImagesBtn.IsChecked == true;
            string folder = (_videoFolderBox.Text ?? "").Trim();
            string imageFolder = (_imageFolderBox.Text ?? "").Trim();

            if (imagesMode)
            {
                string resolvedImages = Util.ResolvePath(_appRoot, imageFolder);
                if (string.IsNullOrWhiteSpace(imageFolder) || !Directory.Exists(resolvedImages))
                {
                    MessageBox.Show(this, "Choose an existing image folder first.", "LiveBackground", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return null;
                }
                if (MediaPipeline.ScanImages(resolvedImages).Count == 0)
                {
                    MessageBox.Show(this, "Add at least one picture to " + resolvedImages + " and press Refresh.", "LiveBackground", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return null;
                }
            }
            else
            {
                string resolvedVideos = Util.ResolvePath(_appRoot, folder);
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(resolvedVideos))
                {
                    MessageBox.Show(this, "Choose an existing video folder first.", "LiveBackground", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return null;
                }
                var checkedClips = GetClipItems().Where(c => c.Enabled).ToList();
                if (_videoModePlaylist.IsChecked == true && checkedClips.Count == 0)
                {
                    MessageBox.Show(this, "Tick at least one clip in the playlist.", "LiveBackground", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return null;
                }
                if (_videoModeSingle.IsChecked == true && checkedClips.Count == 0)
                {
                    MessageBox.Show(this, "Tick the clip you want to play.", "LiveBackground", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return null;
                }
            }

            // Lock screen validation
            string lockMode = "frame";
            string lockImage = _config.LockScreenImage ?? "";
            if (_lockCustom.IsChecked == true)
            {
                string path = (_lockCustomPath.Text ?? "").Trim();
                string full = Util.ResolvePath(_appRoot, path);
                if (path.Length == 0 || !File.Exists(full))
                {
                    MessageBox.Show(this, "Choose an existing image for the lock screen.", "LiveBackground", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return null;
                }
                try { lockImage = ImportImageRelative(full, "lock-screen"); lockMode = "custom"; }
                catch (Exception ex) { MessageBox.Show(this, "Could not copy the lock-screen image: " + ex.Message, "LiveBackground", MessageBoxButton.OK, MessageBoxImage.Warning); return null; }
            }
            else if (_lockImages.IsChecked == true)
            {
                string selected = _lockImageCombo.SelectedItem as string;
                string imgFolder = string.IsNullOrWhiteSpace((_imageFolderBox.Text ?? "").Trim()) ? _config.ImageFolder : (_imageFolderBox.Text ?? "").Trim();
                string full = Path.Combine(Util.ResolvePath(_appRoot, imgFolder ?? ""), selected ?? "");
                if (string.IsNullOrEmpty(selected) || !File.Exists(full))
                {
                    MessageBox.Show(this, "Pick an existing image from your image folder for the lock screen.", "LiveBackground", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return null;
                }
                lockMode = "image";
                lockImage = RelativeToAppRoot(_appRoot, full);
            }

            // Desktop wallpaper validation
            bool useCustomDesktop = !imagesMode && _desktopWallpaper.IsChecked == true && _desktopCustom.IsChecked == true;
            string desktopImage = _config.DesktopWallpaperImage ?? "";
            if (useCustomDesktop)
            {
                string path = (_desktopCustomPath.Text ?? "").Trim();
                string full = Util.ResolvePath(_appRoot, path);
                if (path.Length == 0 || !File.Exists(full))
                {
                    MessageBox.Show(this, "Choose an existing image for the desktop wallpaper.", "LiveBackground", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return null;
                }
                try { desktopImage = ImportImageRelative(full, "desktop-wallpaper"); }
                catch (Exception ex) { MessageBox.Show(this, "Could not copy the desktop image: " + ex.Message, "LiveBackground", MessageBoxButton.OK, MessageBoxImage.Warning); return null; }
            }

            AppConfig updated = _config.Clone();
            updated.WallpaperMode = imagesMode ? "images" : "video";
            if (!string.IsNullOrWhiteSpace(folder)) updated.VideoFolder = folder;
            if (!string.IsNullOrWhiteSpace(imageFolder)) updated.ImageFolder = imageFolder;

            updated.CrossfadeSeconds = _videoFadeSlider.Value;
            updated.FineCrossfadeSteps = _videoFineSteps.IsChecked == true;

            var resSel = _resolutionCombo.SelectedItem as string;
            if (resSel != null && resSel.IndexOf("x", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var parts = resSel.ToLowerInvariant().Replace(" ", "").Split('x');
                int w, h; if (parts.Length == 2 && int.TryParse(parts[0], out w) && int.TryParse(parts[1], out h)) { updated.RenderWidth = w; updated.RenderHeight = h; }
            }
            else { /* match screen/default */ }
            var fpsSel = _fpsCombo.SelectedItem as string;
            if (fpsSel != null) { int fps; if (int.TryParse(new string(fpsSel.TakeWhile(char.IsDigit).ToArray()), out fps) && fps > 0) updated.Fps = fps; }
            var qIdx = _qualityCombo.SelectedIndex;
            updated.Crf = qIdx == 0 ? 18 : (qIdx == 2 ? 28 : 23);

            updated.PlaybackMode = _videoModeSingle.IsChecked == true ? "single" : "playlist";
            var clipItems = GetClipItems();
            var checkedClipsSave = clipItems.Where(c => c.Enabled).ToList();
            if (_videoModeSingle.IsChecked == true && checkedClipsSave.Count > 0)
                updated.SingleClip = checkedClipsSave[0].Name;
            else if (checkedClipsSave.Count > 0 && string.IsNullOrEmpty(updated.SingleClip))
                updated.SingleClip = checkedClipsSave[0].Name;
            updated.Playlist = clipItems.Select(c => new PlaylistEntry(c.Name, c.Enabled)).ToList();

            updated.ImageMode = _imageModeSingle.IsChecked == true ? "single" : "slideshow";
            var imageItems = GetImageItems();
            var checkedImages = imageItems.Where(i => i.Enabled).ToList();
            if (_imageModeSingle.IsChecked == true && checkedImages.Count > 0) updated.ImageSingle = checkedImages[0].Name;
            else if (checkedImages.Count > 0 && string.IsNullOrEmpty(updated.ImageSingle)) updated.ImageSingle = checkedImages[0].Name;
            updated.ImagePlaylist = imageItems.Select(i => new PlaylistEntry(i.Name, i.Enabled)).ToList();

            updated.ImageIntervalSeconds = (int)Math.Round(_intervalSlider.Value);
            updated.ImageCrossfadeSeconds = _imageFadeSlider.Value;
            updated.ImageFineCrossfadeSteps = _imageFineSteps.IsChecked == true;
            updated.ImageShuffle = _imageShuffle.IsChecked == true && _imageModeSlideshow.IsChecked == true;

            updated.LockScreenMode = lockMode;
            updated.LockScreenImage = lockImage;
            updated.ScreensaverActive = _screensaverDimLock.IsChecked == true;
            updated.ScreensaverVideo = _screensaverVideoBg.IsChecked == true;

            if (!imagesMode)
            {
                updated.DesktopWallpaper = _desktopWallpaper.IsChecked == true;
                updated.DesktopWallpaperMode = useCustomDesktop ? "custom" : "frame";
                if (useCustomDesktop) updated.DesktopWallpaperImage = desktopImage;
            }

            if (_slot2 != null && !_slot2.CaptureInto(updated)) return null;
            if (_slot3 != null && !_slot3.CaptureInto(updated)) return null;

            updated.Normalize();
            return updated;
        }

        private List<ClipItem> GetClipItems()
        {
            var oc = _clipList.ItemsSource as ObservableCollection<ClipItem>;
            return oc != null ? oc.ToList() : new List<ClipItem>();
        }
        private List<ImageItem> GetImageItems()
        {
            var oc = _imageList.ItemsSource as ObservableCollection<ImageItem>;
            return oc != null ? oc.ToList() : new List<ImageItem>();
        }

        private static bool IsStartupEnabled(string taskName)
        {
            try { return StartupTask.Exists(taskName) || File.Exists(Shortcut.StartupShortcutPath(taskName)); }
            catch { return false; }
        }

        private static string ImportImageRelative(string sourceFullPath, string kind)
        {
            // Copy into app data images area and return a path relative to app root if possible.
            try
            {
                string name = Path.GetFileNameWithoutExtension(sourceFullPath);
                string ext = Path.GetExtension(sourceFullPath);
                string destDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "LiveBackground", "images");
                Directory.CreateDirectory(destDir);
                string safe = (kind ?? "image") + "-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + ext;
                string dest = Path.Combine(destDir, safe);
                File.Copy(sourceFullPath, dest, true);
                return dest;
            }
            catch { return sourceFullPath; }
        }

        private static string RelativeToAppRoot(string appRoot, string fullPath)
        {
            try
            {
                string a = Path.GetFullPath(appRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string f = Path.GetFullPath(fullPath);
                if (f.StartsWith(a, StringComparison.OrdinalIgnoreCase)) return f.Substring(a.Length);
            }
            catch { }
            return fullPath;
        }

        private sealed class MonitorSlotUi
        {
            private readonly SettingsWindow _owner;
            private readonly int _number;
            private TabItem _tab;
            private TextBlock _deviceLabel;
            private CheckBox _sameVideo;
            private TextBox _videoFolder;
            private ComboBox _videoMode;
            private CheckBox _sameImages;
            private TextBox _imageFolder;
            private ComboBox _imageMode;
            private string _device = "";

            public MonitorSlotUi(SettingsWindow owner, int number) { _owner = owner; _number = number; }

            public TabItem BuildTabItem()
            {
                _tab = new TabItem { Header = _number == 2 ? "2nd Screen" : "3rd Screen" };
                var sp = new StackPanel { Margin = new Thickness(8) };
                _deviceLabel = Muted("No screen detected for this slot.");
                sp.Children.Add(_deviceLabel);

                sp.Children.Add(Header("Video"));
                _sameVideo = new CheckBox { Content = "Same as main screen", Margin = new Thickness(0, 6, 0, 6), IsChecked = true };
                sp.Children.Add(_sameVideo);
                var vrow = new DockPanel { Margin = new Thickness(20, 0, 0, 6) };
                var vb = new Button { Content = "Browse...", MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
                DockPanel.SetDock(vb, Dock.Right);
                vb.Click += delegate { try { using (var dlg = new System.Windows.Forms.FolderBrowserDialog()) { if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK) _videoFolder.Text = dlg.SelectedPath; } } catch { } };
                _videoFolder = new TextBox();
                vrow.Children.Add(vb); vrow.Children.Add(_videoFolder); sp.Children.Add(vrow);
                var vmRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 0, 0, 12) };
                vmRow.Children.Add(new TextBlock { Text = "Mode:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
                _videoMode = new ComboBox { Width = 180 }; _videoMode.Items.Add("Playlist"); _videoMode.Items.Add("Single clip");
                vmRow.Children.Add(_videoMode); sp.Children.Add(vmRow);

                sp.Children.Add(Header("Images"));
                _sameImages = new CheckBox { Content = "Same as main screen", Margin = new Thickness(0, 6, 0, 6), IsChecked = true };
                sp.Children.Add(_sameImages);
                var irow = new DockPanel { Margin = new Thickness(20, 0, 0, 6) };
                var ib = new Button { Content = "Browse...", MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
                DockPanel.SetDock(ib, Dock.Right);
                ib.Click += delegate { try { using (var dlg = new System.Windows.Forms.FolderBrowserDialog()) { if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK) _imageFolder.Text = dlg.SelectedPath; } } catch { } };
                _imageFolder = new TextBox();
                irow.Children.Add(ib); irow.Children.Add(_imageFolder); sp.Children.Add(irow);
                var imRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 0, 0, 8) };
                imRow.Children.Add(new TextBlock { Text = "Mode:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
                _imageMode = new ComboBox { Width = 180 }; _imageMode.Items.Add("Slideshow"); _imageMode.Items.Add("One image");
                imRow.Children.Add(_imageMode); sp.Children.Add(imRow);

                _sameVideo.Checked += delegate { UpdateEnabled(); };
                _sameVideo.Unchecked += delegate { UpdateEnabled(); };
                _sameImages.Checked += delegate { UpdateEnabled(); };
                _sameImages.Unchecked += delegate { UpdateEnabled(); };

                _tab.Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = sp };
                UpdateEnabled();
                return _tab;
            }

            private void UpdateEnabled()
            {
                bool has = !string.IsNullOrEmpty(_device);
                if (_tab != null) _tab.IsEnabled = has;
                bool sv = _sameVideo != null && _sameVideo.IsChecked == true;
                bool si = _sameImages != null && _sameImages.IsChecked == true;
                if (_videoFolder != null) _videoFolder.IsEnabled = has && !sv;
                if (_videoMode != null) _videoMode.IsEnabled = has && !sv;
                if (_imageFolder != null) _imageFolder.IsEnabled = has && !si;
                if (_imageMode != null) _imageMode.IsEnabled = has && !si;
            }

            public void SetMode(bool imagesMode) { _ = imagesMode; UpdateEnabled(); }

            public void LoadFromConfig(AppConfig config, List<ScreenInfo> screens, bool hasSecond, bool duplicate)
            {
                _ = hasSecond; _ = duplicate;
                RefreshBinding(config, screens, hasSecond, duplicate);
            }

            public void RefreshBinding(AppConfig config, List<ScreenInfo> screens, bool hasSecond, bool duplicate)
            {
                _ = hasSecond; _ = duplicate;
                string dev = "";
                try
                {
                    if (screens != null)
                    {
                        var extras = new System.Collections.Generic.List<ScreenInfo>();
                        foreach (var screen in screens) { if (!screen.Primary) extras.Add(screen); }
                        int idx = _number - 2;
                        if (idx >= 0 && idx < extras.Count) dev = extras[idx].Device ?? "";
                    }
                }
                catch { dev = ""; }
                _device = dev;
                bool has = _device.Length > 0;
                if (_deviceLabel != null) _deviceLabel.Text = has ? ("Monitor: " + _device) : "No screen detected for this slot.";
                ScreenSettings s = config.ScreenFor(_device);
                if (_sameVideo != null) _sameVideo.IsChecked = s.SameAsMain;
                if (_videoFolder != null) _videoFolder.Text = s.Folder ?? "";
                if (_videoMode != null) _videoMode.SelectedIndex = string.Equals(s.PlaybackMode, "single", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                if (_sameImages != null) _sameImages.IsChecked = s.ImagesSameAsMain;
                if (_imageFolder != null) _imageFolder.Text = s.ImageFolder ?? "";
                if (_imageMode != null) _imageMode.SelectedIndex = string.Equals(s.ImageMode, "single", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                UpdateEnabled();
            }

            public bool CaptureInto(AppConfig updated)
            {
                if (string.IsNullOrEmpty(_device)) return true;
                ScreenSettings s = updated.ScreenByDevice(_device, true);
                if (s == null) return true;
                s.SameAsMain = _sameVideo != null && _sameVideo.IsChecked == true;
                s.ImagesSameAsMain = _sameImages != null && _sameImages.IsChecked == true;
                if (_videoFolder != null && !s.SameAsMain) s.Folder = (_videoFolder.Text ?? "").Trim();
                if (_videoMode != null && !s.SameAsMain) s.PlaybackMode = _videoMode.SelectedIndex == 1 ? "single" : "playlist";
                if (_imageFolder != null && !s.ImagesSameAsMain) s.ImageFolder = (_imageFolder.Text ?? "").Trim();
                if (_imageMode != null && !s.ImagesSameAsMain) s.ImageMode = _imageMode.SelectedIndex == 1 ? "single" : "slideshow";
                return true;
            }
        }
    }
}






