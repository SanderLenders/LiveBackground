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
using System.Runtime.InteropServices;

namespace LiveBackground
{
    public partial class SettingsWindow : Window
    {
        private readonly string _appRoot;
        private AppConfig _config;
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
        private System.Windows.Controls.RadioButton _screensaverNone;
        private System.Windows.Controls.RadioButton _screensaverVideoBg;
        private System.Windows.Controls.RadioButton _screensaverDimLock;
        private Slider _screensaverHideSlider;
        private TextBlock _screensaverHideValue;
        private StackPanel _screensaverHidePanel;
        private static readonly int[] ScreensaverHidePresets = new[] { 30, 60, 120, 180, 240, 300, 600, 900, 1200, 1500, 1800, 2700, 3600, 5400, 7200 };
        private RadioButton _lockFrame;
        private RadioButton _lockImages;
        private RadioButton _lockCustom;
        private ComboBox _lockImageCombo;
        private TextBox _lockCustomPath;
        private CheckBox _desktopWallpaper;
        private RadioButton _desktopFrame;
        private RadioButton _desktopCustom;
        private TextBox _desktopCustomPath;
        private StackPanel _desktopSection;
        private Button _desktopBrowseBtn;
        private Border _desktopDropZone;

        private TabControl _monitorTabs;
        private MonitorSlotUi _slot2;
        private MonitorSlotUi _slot3;
        private MonitorSlotUi _slot4;

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
            AppConfig source = null;
            try { source = ConfigStore.LoadOrCreate(appRoot); } catch { /* best-effort: fall back to defaults */ }
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

            var folderBlock = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            folderBlock.Children.Add(Header("Video folder"));
            var row = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _videoFolderBox = new TextBox { MinWidth = 360, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(_videoFolderBox, 0);
            var browse = new Button { Content = "Browse...", MinWidth = 100, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            browse.Click += delegate { BrowseInto(_videoFolderBox); RefreshClipList(); };
            Grid.SetColumn(browse, 1);
            var clipDrop = CreateDropZone("Drop videos", AddVideos, BrowseAndAddVideos, false);
            clipDrop.Margin = new Thickness(8, 0, 0, 0);
            clipDrop.MinWidth = 100;
            clipDrop.MinHeight = 0;
            clipDrop.Padding = new Thickness(6, 4, 6, 4);
            clipDrop.VerticalAlignment = VerticalAlignment.Stretch;
            if (clipDrop.Child is TextBlock) ((TextBlock)clipDrop.Child).Margin = new Thickness(0);
            Grid.SetColumn(clipDrop, 2);
            row.Children.Add(_videoFolderBox);
            row.Children.Add(browse);
            row.Children.Add(clipDrop);
            folderBlock.Children.Add(row);
            panel.Children.Add(folderBlock);

            var modeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            _videoModeSingle = new RadioButton { Content = "Single clip", GroupName = "vmode", Margin = new Thickness(0, 0, 16, 0) };
            _videoModePlaylist = new RadioButton { Content = "All clips in this order", GroupName = "vmode" };
            _videoModeSingle.Checked += delegate { UpdateClipListMode(); };
            _videoModePlaylist.Checked += delegate { UpdateClipListMode(); };
            modeRow.Children.Add(_videoModeSingle);
            modeRow.Children.Add(_videoModePlaylist);
            panel.Children.Add(modeRow);

            _clipList = new ListView { Height = 300, Margin = new Thickness(0, 0, 0, 8) };
            var gv = new GridView();
            gv.Columns.Add(new GridViewColumn { Header = "", Width = 34, CellTemplate = ClipCheckCellTemplate() });
            gv.Columns.Add(new GridViewColumn { Header = "Clip", Width = 320, DisplayMemberBinding = new System.Windows.Data.Binding("Name") });
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
            _clipHint = Muted("");
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

            var folderBlock = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            folderBlock.Children.Add(Header("Image folder"));
            var row = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _imageFolderBox = new TextBox { MinWidth = 360, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(_imageFolderBox, 0);
            var browse = new Button { Content = "Browse...", MinWidth = 100, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            browse.Click += delegate { BrowseInto(_imageFolderBox); RefreshImageList(); RefreshLockImageChoices(); };
            Grid.SetColumn(browse, 1);
            var imageDrop = CreateDropZone("Drop images", AddImages, BrowseAndAddImages, true);
            imageDrop.Margin = new Thickness(8, 0, 0, 0);
            imageDrop.MinWidth = 100;
            imageDrop.MinHeight = 0;
            imageDrop.Padding = new Thickness(6, 4, 6, 4);
            imageDrop.VerticalAlignment = VerticalAlignment.Stretch;
            if (imageDrop.Child is TextBlock) ((TextBlock)imageDrop.Child).Margin = new Thickness(0);
            Grid.SetColumn(imageDrop, 2);
            row.Children.Add(_imageFolderBox);
            row.Children.Add(browse);
            row.Children.Add(imageDrop);
            folderBlock.Children.Add(row);
            panel.Children.Add(folderBlock);

            var modeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            _imageModeSingle = new RadioButton { Content = "One image", GroupName = "imode", Margin = new Thickness(0, 0, 16, 0) };
            _imageModeSlideshow = new RadioButton { Content = "Slideshow", GroupName = "imode" };
            _imageModeSingle.Checked += delegate { UpdateImageListMode(); };
            _imageModeSlideshow.Checked += delegate { UpdateImageListMode(); };
            modeRow.Children.Add(_imageModeSingle); modeRow.Children.Add(_imageModeSlideshow); panel.Children.Add(modeRow);

            _imageList = new ListView { Height = 300, Margin = new Thickness(0, 0, 0, 8) };
            var gv = new GridView();
            gv.Columns.Add(new GridViewColumn { Header = "", Width = 34, CellTemplate = ImageCheckCellTemplate() });
            gv.Columns.Add(new GridViewColumn { Header = "Image", Width = 420, DisplayMemberBinding = new System.Windows.Data.Binding("Name") });
            _imageList.View = gv;
            panel.Children.Add(_imageList);

            var btns = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            btns.Children.Add(ActionButton("Move up", delegate { MoveSelected(_imageList, -1); }));
            btns.Children.Add(ActionButton("Move down", delegate { MoveSelected(_imageList, 1); }));
            btns.Children.Add(ActionButton("Sort A-Z", delegate { SortImageItems(); }));
            btns.Children.Add(ActionButton("Refresh", delegate { RefreshImageList(); RefreshLockImageChoices(); }));
            btns.Children.Add(ActionButton("Open folder", delegate { OpenFolderSafe(Resolve(_imageFolderBox.Text)); }));
            panel.Children.Add(btns);
            _imageHint = Muted("");
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

            var tabsGrid = new Grid();
            _monitorTabs = new TabControl { MinHeight = 320 };
            tabsGrid.Children.Add(_monitorTabs);

            var openDisplay = ActionButton("Open Windows display settings...", OpenWindowsDisplaySettings);
            openDisplay.Margin = new Thickness(0, 2, 2, 0);
            openDisplay.Padding = new Thickness(10, 2, 10, 2);
            openDisplay.HorizontalAlignment = HorizontalAlignment.Right;
            openDisplay.VerticalAlignment = VerticalAlignment.Top;
            Panel.SetZIndex(openDisplay, 1);
            tabsGrid.Children.Add(openDisplay);

            panel.Children.Add(tabsGrid);
            _slot2 = new MonitorSlotUi(this, 2);
            _slot3 = new MonitorSlotUi(this, 3);
            _slot4 = new MonitorSlotUi(this, 4);
            _monitorTabs.Items.Add(_slot2.BuildTabItem());
            _monitorTabs.Items.Add(_slot3.BuildTabItem());
            _monitorTabs.Items.Add(_slot4.BuildTabItem());
        }

        private void BuildOtherTab()
        {
            var panel = OtherPanel; panel.Children.Clear();
            panel.Children.Add(Header("Startup"));
            _startWithWindows = new CheckBox { Content = "Start LiveBackground when I sign in to Windows", Margin = new Thickness(0, 6, 0, 16) };
            panel.Children.Add(_startWithWindows);

            panel.Children.Add(Header("Screensaver"));
            _screensaverNone = new System.Windows.Controls.RadioButton { Content = "No screensaver", GroupName = "screensaverMode", Margin = new Thickness(0, 6, 0, 4) };
            _screensaverVideoBg = new System.Windows.Controls.RadioButton { Content = "Hide taskbar and desktop icons after idle", GroupName = "screensaverMode", Margin = new Thickness(0, 0, 0, 4) };
            _screensaverDimLock = new System.Windows.Controls.RadioButton { Content = "Screensaver Dim and Lock", GroupName = "screensaverMode", Margin = new Thickness(0, 0, 0, 6) };
            _screensaverNone.Checked += delegate { UpdateScreensaverIdleEnabled(); };
            _screensaverVideoBg.Checked += delegate { UpdateScreensaverIdleEnabled(); };
            _screensaverDimLock.Checked += delegate { UpdateScreensaverIdleEnabled(); };
            panel.Children.Add(_screensaverNone);
            panel.Children.Add(_screensaverVideoBg);
            panel.Children.Add(_screensaverDimLock);

            _screensaverHidePanel = new StackPanel { Margin = new Thickness(20, 0, 0, 16) };
            var hideRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            hideRow.Children.Add(new TextBlock { Text = "After idle:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            _screensaverHideValue = new TextBlock { Text = "5m", VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold, MinWidth = 60 };
            hideRow.Children.Add(_screensaverHideValue);
            _screensaverHidePanel.Children.Add(hideRow);
            _screensaverHideSlider = new Slider
            {
                Minimum = 0,
                Maximum = ScreensaverHidePresets.Length - 1,
                TickFrequency = 1,
                IsSnapToTickEnabled = true,
                TickPlacement = System.Windows.Controls.Primitives.TickPlacement.BottomRight,
                SmallChange = 1,
                LargeChange = 1,
                MinWidth = 320
            };
            _screensaverHideSlider.ValueChanged += delegate { UpdateScreensaverHideLabel(); };
            _screensaverHidePanel.Children.Add(_screensaverHideSlider);
            panel.Children.Add(_screensaverHidePanel);

            panel.Children.Add(Header("Lock screen"));
            _lockFrame = new RadioButton { Content = "Use the first frame of the video loop", GroupName = "lock", Margin = new Thickness(0, 6, 0, 4) };
            _lockImages = new RadioButton { Content = "Use an image from your image folder", GroupName = "lock", Margin = new Thickness(0, 0, 0, 4) };
            _lockImageCombo = new ComboBox { MinWidth = 260, Margin = new Thickness(20, 0, 0, 8) };
            _lockCustom = new RadioButton { Content = "Use a custom image", GroupName = "lock", Margin = new Thickness(0, 0, 0, 4) };
            var lockPathRow = new Grid { Margin = new Thickness(20, 0, 0, 16) };
            lockPathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            lockPathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            lockPathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _lockCustomPath = new TextBox();
            Grid.SetColumn(_lockCustomPath, 0);
            var lockBrowse = new Button { Content = "Browse...", MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
            lockBrowse.Click += delegate { BrowseImageInto(_lockCustomPath); _lockCustom.IsChecked = true; };
            Grid.SetColumn(lockBrowse, 1);
            var lockDrop = CreateSingleFileDropZone("Drop image", delegate(string path)
            {
                _lockCustomPath.Text = path ?? "";
                _lockCustom.IsChecked = true;
            }, delegate
            {
                BrowseImageInto(_lockCustomPath);
                _lockCustom.IsChecked = true;
            }, true);
            lockDrop.MinWidth = 110;
            lockDrop.MinHeight = 0;
            lockDrop.Margin = new Thickness(8, 0, 0, 0);
            lockDrop.VerticalAlignment = VerticalAlignment.Stretch;
            Grid.SetColumn(lockDrop, 2);
            lockPathRow.Children.Add(_lockCustomPath);
            lockPathRow.Children.Add(lockBrowse);
            lockPathRow.Children.Add(lockDrop);
            panel.Children.Add(_lockFrame);
            panel.Children.Add(_lockImages);
            panel.Children.Add(_lockImageCombo);
            panel.Children.Add(_lockCustom);
            panel.Children.Add(lockPathRow);

            _desktopSection = new StackPanel();
            _desktopSection.Children.Add(Header("Desktop wallpaper (video mode)"));
            _desktopWallpaper = new CheckBox { Content = "Set a desktop wallpaper image behind the video", Margin = new Thickness(0, 6, 0, 6) };
            _desktopWallpaper.Checked += delegate { UpdateDesktopEnabled(); };
            _desktopWallpaper.Unchecked += delegate { UpdateDesktopEnabled(); };
            _desktopSection.Children.Add(_desktopWallpaper);
            _desktopFrame = new RadioButton { Content = "Use the first frame of the video loop", GroupName = "desk", Margin = new Thickness(20, 0, 0, 4) };
            _desktopSection.Children.Add(_desktopFrame);
            _desktopCustom = new RadioButton { Content = "Use a custom image", GroupName = "desk", Margin = new Thickness(20, 0, 0, 4) };
            _desktopSection.Children.Add(_desktopCustom);
            var deskPathRow = new Grid { Margin = new Thickness(40, 0, 0, 8) };
            deskPathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            deskPathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            deskPathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _desktopCustomPath = new TextBox();
            Grid.SetColumn(_desktopCustomPath, 0);
            _desktopBrowseBtn = new Button { Content = "Browse...", MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
            _desktopBrowseBtn.Click += delegate { BrowseImageInto(_desktopCustomPath); _desktopCustom.IsChecked = true; };
            Grid.SetColumn(_desktopBrowseBtn, 1);
            _desktopDropZone = CreateSingleFileDropZone("Drop image", delegate(string path)
            {
                _desktopCustomPath.Text = path ?? "";
                _desktopCustom.IsChecked = true;
            }, delegate
            {
                BrowseImageInto(_desktopCustomPath);
                _desktopCustom.IsChecked = true;
            }, true);
            _desktopDropZone.MinWidth = 110;
            _desktopDropZone.MinHeight = 0;
            _desktopDropZone.Margin = new Thickness(8, 0, 0, 0);
            _desktopDropZone.VerticalAlignment = VerticalAlignment.Stretch;
            Grid.SetColumn(_desktopDropZone, 2);
            deskPathRow.Children.Add(_desktopCustomPath);
            deskPathRow.Children.Add(_desktopBrowseBtn);
            deskPathRow.Children.Add(_desktopDropZone);
            _desktopSection.Children.Add(deskPathRow);
            panel.Children.Add(_desktopSection);
        }

        private void UpdateFadeLabel() { if (_videoFadeValue != null && _videoFadeSlider != null) _videoFadeValue.Text = _videoFadeSlider.Value.ToString("0.0", CultureInfo.InvariantCulture) + " s"; }
        private void UpdateIntervalLabel() { if (_intervalValue != null && _intervalSlider != null) _intervalValue.Text = ((int)Math.Round(_intervalSlider.Value)).ToString(CultureInfo.InvariantCulture) + " s"; }
        private void UpdateImageFadeLabel() { if (_imageFadeValue != null && _imageFadeSlider != null) _imageFadeValue.Text = _imageFadeSlider.Value.ToString("0.0", CultureInfo.InvariantCulture) + " s"; }

        private static string FormatHideDelay(int seconds)
        {
            if (seconds < 60) return seconds.ToString(CultureInfo.InvariantCulture) + "s";
            int minutes = seconds / 60;
            return minutes.ToString(CultureInfo.InvariantCulture) + "m";
        }

        private void UpdateScreensaverHideLabel()
        {
            if (_screensaverHideValue == null) return;
            int idx = 0;
            if (_screensaverHideSlider != null) idx = (int)Math.Round(_screensaverHideSlider.Value);
            if (idx < 0) idx = 0;
            if (idx >= ScreensaverHidePresets.Length) idx = ScreensaverHidePresets.Length - 1;
            _screensaverHideValue.Text = FormatHideDelay(ScreensaverHidePresets[idx]);
        }

        private void SetScreensaverHideSeconds(int seconds)
        {
            if (_screensaverHideSlider == null) return;
            int best = 4; // default 5 minutes
            int bestDiff = int.MaxValue;
            for (int i = 0; i < ScreensaverHidePresets.Length; i++)
            {
                int diff = Math.Abs(ScreensaverHidePresets[i] - seconds);
                if (diff < bestDiff) { bestDiff = diff; best = i; }
            }
            _screensaverHideSlider.Value = best;
            UpdateScreensaverHideLabel();
        }

        private int GetScreensaverHideSeconds()
        {
            int idx = 0;
            if (_screensaverHideSlider != null) idx = (int)Math.Round(_screensaverHideSlider.Value);
            if (idx < 0) idx = 0;
            if (idx >= ScreensaverHidePresets.Length) idx = ScreensaverHidePresets.Length - 1;
            return ScreensaverHidePresets[idx];
        }

        private static TextBlock Header(string text) { return new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(0, 0, 0, 2) }; }
        private static TextBlock Muted(string text) { return new TextBlock { Text = text, Foreground = new SolidColorBrush(Color.FromRgb(167, 176, 188)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) }; }

        // Temporary review mode: keep not-functional controls visually greyed but
        // clickable so behavior/layout can be reviewed. Set to false to restore
        // normal disabled behavior.
        private static readonly bool ReviewDisabledControlsClickable = true;
        private static void ApplyReviewEnabled(System.Windows.UIElement element, bool functional)
        {
            if (element == null) return;
            if (ReviewDisabledControlsClickable)
            {
                var control = element as System.Windows.Controls.Control;
                if (control != null) control.IsEnabled = true;
                element.Opacity = functional ? 1.0 : 0.45;
            }
            else
            {
                var control = element as System.Windows.Controls.Control;
                if (control != null) control.IsEnabled = functional;
                element.Opacity = 1.0;
            }
        }
        private static Button ActionButton(string text, Action action) { var b = new Button { Content = text, Margin = new Thickness(0, 0, 8, 8), MinWidth = 90 }; b.Click += delegate { action(); }; return b; }

        private DataTemplate ClipCheckCellTemplate()
        {
            var fef = new FrameworkElementFactory(typeof(CheckBox));
            fef.SetBinding(ToggleButton.IsCheckedProperty, new System.Windows.Data.Binding("Enabled") { Mode = System.Windows.Data.BindingMode.TwoWay, UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged });
            fef.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            fef.AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler(ClipCheckBox_Checked));
            return new DataTemplate { VisualTree = fef };
        }

        private DataTemplate ImageCheckCellTemplate()
        {
            var fef = new FrameworkElementFactory(typeof(CheckBox));
            fef.SetBinding(ToggleButton.IsCheckedProperty, new System.Windows.Data.Binding("Enabled") { Mode = System.Windows.Data.BindingMode.TwoWay, UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged });
            fef.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            fef.AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler(ImageCheckBox_Checked));
            return new DataTemplate { VisualTree = fef };
        }

        private void ClipCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (_videoModeSingle == null || _videoModeSingle.IsChecked != true) return;
            var cb = sender as System.Windows.Controls.CheckBox;
            var current = cb != null ? cb.DataContext as ClipItem : null;
            if (current == null) return;
            var items = _clipList != null ? _clipList.ItemsSource as ObservableCollection<ClipItem> : null;
            if (items == null) return;
            bool changed = false;
            foreach (var item in items)
            {
                bool should = object.ReferenceEquals(item, current);
                if (item.Enabled != should) { item.Enabled = should; changed = true; }
            }
            if (changed && _clipList != null)
            {
                var view = System.Windows.Data.CollectionViewSource.GetDefaultView(_clipList.ItemsSource);
                if (view != null) view.Refresh();
            }
        }

        private void ImageCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (_imageModeSingle == null || _imageModeSingle.IsChecked != true) return;
            var cb = sender as System.Windows.Controls.CheckBox;
            var current = cb != null ? cb.DataContext as ImageItem : null;
            if (current == null) return;
            var items = _imageList != null ? _imageList.ItemsSource as ObservableCollection<ImageItem> : null;
            if (items == null) return;
            bool changed = false;
            foreach (var item in items)
            {
                bool should = object.ReferenceEquals(item, current);
                if (item.Enabled != should) { item.Enabled = should; changed = true; }
            }
            if (changed && _imageList != null)
            {
                var view = System.Windows.Data.CollectionViewSource.GetDefaultView(_imageList.ItemsSource);
                if (view != null) view.Refresh();
            }
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



        private static void OpenWindowsDisplaySettings()
        {
            try { var psi = new ProcessStartInfo("ms-settings:display") { UseShellExecute = true }; Process.Start(psi); }
            catch (Exception ex) { try { Log.Warn("could not open display settings: " + ex.Message); } catch { } }
        }

        private bool IsImagesMode()
        {
            return ModeToggleBtn != null && ModeToggleBtn.IsChecked == true;
        }

        private void ApplyModeUi()
        {
            bool images = IsImagesMode();

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
            if (_slot4 != null) _slot4.SetMode(images);
            UpdateMonitorTabEnabled();
        }

        private void UpdateDesktopEnabled()
        {
            bool images = IsImagesMode();
            bool sectionFunctional = !images;
            if (_desktopSection != null)
            {
                if (ReviewDisabledControlsClickable)
                {
                    _desktopSection.IsEnabled = true;
                    _desktopSection.Opacity = sectionFunctional ? 1.0 : 0.45;
                }
                else
                {
                    _desktopSection.IsEnabled = sectionFunctional;
                    _desktopSection.Opacity = 1.0;
                }
            }
            if (_desktopWallpaper != null)
            {
                bool childrenFunctional = sectionFunctional && _desktopWallpaper.IsChecked == true;
                ApplyReviewChildEnabled(_desktopFrame, sectionFunctional, childrenFunctional);
                ApplyReviewChildEnabled(_desktopCustom, sectionFunctional, childrenFunctional);
                ApplyReviewChildEnabled(_desktopCustomPath, sectionFunctional, childrenFunctional);
                ApplyReviewChildEnabled(_desktopBrowseBtn, sectionFunctional, childrenFunctional);
                ApplyReviewChildEnabled(_desktopDropZone, sectionFunctional, childrenFunctional);
            }
        }

        private static void ApplyReviewChildEnabled(System.Windows.UIElement element, bool sectionFunctional, bool functional)
        {
            if (element == null) return;
            if (ReviewDisabledControlsClickable)
            {
                var control = element as System.Windows.Controls.Control;
                if (control != null) control.IsEnabled = true;
                // Parent section already dims when section is not functional.
                element.Opacity = sectionFunctional ? (functional ? 1.0 : 0.45) : 1.0;
            }
            else
            {
                var control = element as System.Windows.Controls.Control;
                if (control != null) control.IsEnabled = functional;
                element.Opacity = 1.0;
            }
        }

        private void UpdateScreensaverIdleEnabled()
        {
            bool functional = _screensaverNone == null || _screensaverNone.IsChecked != true;
            ApplyReviewEnabled(_screensaverHidePanel, functional);
        }

        private void ModeToggleBtn_Click(object sender, RoutedEventArgs e) { ApplyModeUi(); }

        private void LoadFromConfig()
        {
            bool images = string.Equals(_config.WallpaperMode, "images", StringComparison.OrdinalIgnoreCase);
            ModeToggleBtn.IsChecked = images;

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
            bool ssVideo = _config.ScreensaverVideo;
            bool ssDimLock = _config.ScreensaverActive && !ssVideo;
            bool ssNone = !ssVideo && !_config.ScreensaverActive;
            if (_screensaverNone != null) _screensaverNone.IsChecked = ssNone;
            if (_screensaverVideoBg != null) _screensaverVideoBg.IsChecked = ssVideo;
            if (_screensaverDimLock != null) _screensaverDimLock.IsChecked = ssDimLock;
            SetScreensaverHideSeconds(_config.ScreensaverHideSeconds);
            UpdateScreensaverIdleEnabled();

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
            if (_slot4 != null) _slot4.LoadFromConfig(_config, _screens, _hasSecondScreen, _duplicateLayout);
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
                    if (!enabledMap.TryGetValue(name, out en)) en = false;
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
                foreach (var f in ordered)
                {
                    string name = Path.GetFileName(f);
                    bool en;
                    if (!enabledMap.TryGetValue(name, out en)) en = true;
                    items.Add(new ImageItem { Name = name, Path = f, Enabled = en });
                }
            }
            catch { }
            _imageList.ItemsSource = items;
            UpdateImageListMode();
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

        private void UpdateClipListMode()
        {
            if (_videoModeSingle == null || _videoModeSingle.IsChecked != true) return;
            var items = _clipList != null ? _clipList.ItemsSource as ObservableCollection<ClipItem> : null;
            if (items == null) return;
            var checkedItems = items.Where(i => i.Enabled).ToList();
            if (checkedItems.Count <= 1) return;
            ClipItem keep = null;
            var selected = _clipList.SelectedItem as ClipItem;
            if (selected != null && selected.Enabled) keep = selected;
            if (keep == null) keep = checkedItems[0];
            foreach (var item in items) item.Enabled = object.ReferenceEquals(item, keep);
            var view = System.Windows.Data.CollectionViewSource.GetDefaultView(_clipList.ItemsSource);
            if (view != null) view.Refresh();
        }
        private void UpdateImageListMode()
        {
            if (_imageModeSingle == null || _imageModeSingle.IsChecked != true) return;
            var items = _imageList != null ? _imageList.ItemsSource as ObservableCollection<ImageItem> : null;
            if (items == null) return;
            var checkedItems = items.Where(i => i.Enabled).ToList();
            if (checkedItems.Count <= 1) return;
            ImageItem keep = null;
            var selected = _imageList.SelectedItem as ImageItem;
            if (selected != null && selected.Enabled) keep = selected;
            if (keep == null) keep = checkedItems[0];
            foreach (var item in items) item.Enabled = object.ReferenceEquals(item, keep);
            var view = System.Windows.Data.CollectionViewSource.GetDefaultView(_imageList.ItemsSource);
            if (view != null) view.Refresh();
        }

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
            var names = items.Select(x => x.Name).ToList();
            var order = PlaylistState.SortedOrder(names);
            var sorted = order.Select(i => items[i]).ToList();
            items.Clear();
            foreach (var it in sorted) items.Add(it);
        }
        private void SortImageItems()
        {
            var items = _imageList.ItemsSource as ObservableCollection<ImageItem>;
            if (items == null) return;
            var names = items.Select(x => x.Name).ToList();
            var order = PlaylistState.SortedOrder(names);
            var sorted = order.Select(i => items[i]).ToList();
            items.Clear();
            foreach (var it in sorted) items.Add(it);
        }

        private void UpdateMonitorState(bool reloadValues = true)
        {
            RefreshScreens();
            if (_slot2 != null) _slot2.RefreshBinding(_config, _screens, _hasSecondScreen, _duplicateLayout, reloadValues);
            if (_slot3 != null) _slot3.RefreshBinding(_config, _screens, _hasSecondScreen, _duplicateLayout, reloadValues);
            if (_slot4 != null) _slot4.RefreshBinding(_config, _screens, _hasSecondScreen, _duplicateLayout, reloadValues);
            UpdateMonitorTabEnabled();
        }

        private void UpdateMonitorTabEnabled()
        {
            bool available = _hasSecondScreen || _duplicateLayout;
            ApplyReviewEnabled(TabMultiMonitor, available);
            if (!ReviewDisabledControlsClickable && !available && MainTabs.SelectedItem == TabMultiMonitor) MainTabs.SelectedItem = IsImagesMode() ? TabImages : TabVideos;
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            UpdateMonitorState(false);
        }

        private void ShowNotify(string title, string text)
        {
            try { MessageBox.Show(this, text, title, MessageBoxButton.OK, MessageBoxImage.Information); }
            catch { /* best-effort notification */ }
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

            _config = updated.Clone();
            try { File.WriteAllText(Installer.ApplyConfigFlag(_appRoot), "apply"); }
            catch (Exception ex) { try { Log.Warn("could not signal config apply: " + ex.Message); } catch { } }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e) { Close(); }

        private AppConfig CollectConfig()
        {
            bool imagesMode = IsImagesMode();
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
            updated.Playlist = MergeMissingSavedEntries(clipItems.Select(c => new PlaylistEntry(c.Name, c.Enabled)).ToList(), _config.Playlist);

            updated.ImageMode = _imageModeSingle.IsChecked == true ? "single" : "slideshow";
            var imageItems = GetImageItems();
            var checkedImages = imageItems.Where(i => i.Enabled).ToList();
            if (_imageModeSingle.IsChecked == true && checkedImages.Count > 0) updated.ImageSingle = checkedImages[0].Name;
            else if (checkedImages.Count > 0 && string.IsNullOrEmpty(updated.ImageSingle)) updated.ImageSingle = checkedImages[0].Name;
            updated.ImagePlaylist = MergeMissingSavedEntries(imageItems.Select(i => new PlaylistEntry(i.Name, i.Enabled)).ToList(), _config.ImagePlaylist);

            updated.ImageIntervalSeconds = (int)Math.Round(_intervalSlider.Value);
            updated.ImageCrossfadeSeconds = _imageFadeSlider.Value;
            updated.ImageFineCrossfadeSteps = _imageFineSteps.IsChecked == true;
            updated.ImageShuffle = _imageShuffle.IsChecked == true && _imageModeSlideshow.IsChecked == true;

            updated.LockScreenMode = lockMode;
            updated.LockScreenImage = lockImage;
            updated.ScreensaverActive = _screensaverDimLock.IsChecked == true;
            updated.ScreensaverVideo = _screensaverVideoBg.IsChecked == true;
            updated.ScreensaverHideSeconds = GetScreensaverHideSeconds();

            if (!imagesMode)
            {
                updated.DesktopWallpaper = _desktopWallpaper.IsChecked == true;
                updated.DesktopWallpaperMode = useCustomDesktop ? "custom" : "frame";
                if (useCustomDesktop) updated.DesktopWallpaperImage = desktopImage;
            }

            if (_slot2 != null && !_slot2.CaptureInto(updated)) return null;
            if (_slot3 != null && !_slot3.CaptureInto(updated)) return null;
            if (_slot4 != null && !_slot4.CaptureInto(updated)) return null;

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
            // Copy into app data images area with a stable name so repeated Apply
            // overwrites the same file instead of accumulating timestamped copies.
            try
            {
                string ext = Path.GetExtension(sourceFullPath);
                string destDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "LiveBackground", "images");
                Directory.CreateDirectory(destDir);
                string hash = "copy";
                try
                {
                    using (var sha = System.Security.Cryptography.SHA256.Create())
                    using (var stream = File.OpenRead(sourceFullPath))
                    {
                        byte[] bytes = sha.ComputeHash(stream);
                        hash = BitConverter.ToString(bytes).Replace("-", "").Substring(0, 16).ToLowerInvariant();
                    }
                }
                catch { /* best-effort: fall back to a stable kind-based name */ hash = (kind ?? "image").ToLowerInvariant(); }
                string safe = (kind ?? "image") + "-" + hash + ext;
                string dest = Path.Combine(destDir, safe);
                string srcFull = Path.GetFullPath(sourceFullPath);
                string dstFull = Path.GetFullPath(dest);
                if (!string.Equals(srcFull, dstFull, StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(sourceFullPath, dest, true);
                }
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

        private static List<PlaylistEntry> MergeMissingSavedEntries(List<PlaylistEntry> visible, List<PlaylistEntry> saved)
        {
            var merged = new List<PlaylistEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (visible != null)
            {
                foreach (var e in visible)
                {
                    if (e == null || string.IsNullOrEmpty(e.Name)) continue;
                    string name = e.Name.Trim();
                    if (name.Length == 0 || !seen.Add(name)) continue;
                    merged.Add(new PlaylistEntry(name, e.Enabled));
                }
            }
            if (saved != null)
            {
                foreach (var e in saved)
                {
                    if (e == null || string.IsNullOrEmpty(e.Name)) continue;
                    string name = e.Name.Trim();
                    if (name.Length == 0 || !seen.Add(name)) continue;
                    merged.Add(new PlaylistEntry(name, e.Enabled));
                }
            }
            return merged;
        }


        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            TryApplyDarkTitleBar();
        }

        private void TryApplyDarkTitleBar()
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return;
                int useDark = 1;
                // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE (Win10 19041+/Win11), 19 for older builds
                DwmSetWindowAttribute(hwnd, 20, ref useDark, sizeof(int));
                DwmSetWindowAttribute(hwnd, 19, ref useDark, sizeof(int));
                // 35 = DWMWA_CAPTION_COLOR, 36 = DWMWA_TEXT_COLOR (Win11)
                int caption = ColorToBgr(Color.FromRgb(30, 34, 40)); // #1E2228
                int text = ColorToBgr(Color.FromRgb(232, 237, 242));
                DwmSetWindowAttribute(hwnd, 35, ref caption, sizeof(int));
                DwmSetWindowAttribute(hwnd, 36, ref text, sizeof(int));
            }
            catch { }
        }

        private static int ColorToBgr(Color c) { return c.R | (c.G << 8) | (c.B << 16); }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private static Border CreateDropZone(string text, Action<string[]> onDropFiles, Action onClick, bool forImages)
        {
            var label = new TextBlock
            {
                Text = text,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(170, 180, 191)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(8)
            };
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(47, 54, 64)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(107, 119, 133)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                MinHeight = 56,
                MinWidth = 170,
                Padding = new Thickness(6),
                Child = label,
                AllowDrop = true,
                Cursor = Cursors.Hand,
                SnapsToDevicePixels = true
            };
            border.MouseLeftButtonUp += delegate { try { onClick(); } catch { } };
            border.DragOver += delegate(object sender, DragEventArgs e)
            {
                try
                {
                    bool ok = HasDroppedMedia(e.Data, forImages);
                    e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
                    e.Handled = true;
                    border.Background = new SolidColorBrush(ok ? Color.FromRgb(58, 69, 82) : Color.FromRgb(47, 54, 64));
                }
                catch { }
            };
            border.DragLeave += delegate { border.Background = new SolidColorBrush(Color.FromRgb(47, 54, 64)); };
            border.Drop += delegate(object sender, DragEventArgs e)
            {
                try
                {
                    border.Background = new SolidColorBrush(Color.FromRgb(47, 54, 64));
                    var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                    if (files != null) onDropFiles(files);
                }
                catch { }
            };
            return border;
        }

        private static bool HasDroppedMedia(IDataObject data, bool forImages)
        {
            if (data == null || !data.GetDataPresent(DataFormats.FileDrop)) return false;
            var files = data.GetData(DataFormats.FileDrop) as string[];
            if (files == null) return false;
            foreach (string file in files)
            {
                try
                {
                    if (File.Exists(file))
                    {
                        if (forImages ? MediaPipeline.IsImage(file) : MediaPipeline.IsVideo(file)) return true;
                    }
                    else if (Directory.Exists(file))
                    {
                        if (forImages ? MediaPipeline.ScanImages(file).Count > 0 : MediaPipeline.ScanVideos(file).Count > 0) return true;
                    }
                }
                catch { }
            }
            return false;
        }

        private static string FirstDroppedImagePath(IDataObject data)
        {
            if (data == null || !data.GetDataPresent(DataFormats.FileDrop)) return null;
            var files = data.GetData(DataFormats.FileDrop) as string[];
            if (files == null) return null;
            foreach (string file in files)
            {
                try
                {
                    if (File.Exists(file) && MediaPipeline.IsImage(file)) return file;
                    if (Directory.Exists(file))
                    {
                        var imgs = MediaPipeline.ScanImages(file);
                        if (imgs != null && imgs.Count > 0) return imgs[0];
                    }
                }
                catch { }
            }
            return null;
        }

        private static Border CreateSingleFileDropZone(string text, Action<string> onPicked, Action onClick, bool forImages)
        {
            var label = new TextBlock
            {
                Text = text,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(170, 180, 191)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(6, 2, 6, 2)
            };
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(47, 54, 64)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(107, 119, 133)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                MinHeight = 28,
                MinWidth = 90,
                Padding = new Thickness(4, 2, 4, 2),
                Child = label,
                AllowDrop = true,
                Cursor = Cursors.Hand,
                SnapsToDevicePixels = true
            };
            border.MouseLeftButtonUp += delegate { try { if (onClick != null) onClick(); } catch { } };
            border.DragOver += delegate(object sender, DragEventArgs e)
            {
                try
                {
                    bool ok = forImages ? FirstDroppedImagePath(e.Data) != null : HasDroppedMedia(e.Data, false);
                    e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
                    e.Handled = true;
                    border.Background = new SolidColorBrush(ok ? Color.FromRgb(58, 69, 82) : Color.FromRgb(47, 54, 64));
                }
                catch { }
            };
            border.DragLeave += delegate { border.Background = new SolidColorBrush(Color.FromRgb(47, 54, 64)); };
            border.Drop += delegate(object sender, DragEventArgs e)
            {
                try
                {
                    border.Background = new SolidColorBrush(Color.FromRgb(47, 54, 64));
                    string picked = null;
                    if (forImages) picked = FirstDroppedImagePath(e.Data);
                    else
                    {
                        var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                        if (files != null && files.Length > 0) picked = files[0];
                    }
                    if (!string.IsNullOrEmpty(picked) && onPicked != null) onPicked(picked);
                }
                catch { }
            };
            return border;
        }

        private void CopyVideosToFolder(string folderPath, string[] paths)
        {
            if (paths == null || paths.Length == 0) return;
            string folder = Resolve(folderPath ?? "");
            if (string.IsNullOrWhiteSpace(folder))
            {
                MessageBox.Show(this, "Choose a video folder first.", "LiveBackground", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            try { Directory.CreateDirectory(folder); }
            catch (Exception ex) { MessageBox.Show(this, "Could not create the video folder: " + ex.Message, "LiveBackground", MessageBoxButton.OK, MessageBoxImage.Error); return; }

            var sources = new List<string>();
            foreach (string path in paths)
            {
                try
                {
                    if (File.Exists(path) && MediaPipeline.IsVideo(path)) sources.Add(path);
                    else if (Directory.Exists(path)) sources.AddRange(MediaPipeline.ScanVideos(path));
                }
                catch { }
            }
            if (sources.Count == 0) return;
            int copied = 0; int skipped = 0; string firstError = "";
            foreach (string source in sources)
            {
                try
                {
                    string sourceFull = System.IO.Path.GetFullPath(source);
                    if (string.Equals(System.IO.Path.GetDirectoryName(sourceFull), folder, StringComparison.OrdinalIgnoreCase)) { skipped++; continue; }
                    string target = UniquePath(folder, System.IO.Path.GetFileName(sourceFull));
                    File.Copy(sourceFull, target, false);
                    copied++;
                }
                catch (Exception ex) { if (firstError.Length == 0) firstError = ex.Message; }
            }
            if (copied > 0) { ShowNotify("LiveBackground", "Added " + copied + " video(s) to " + folder + "."); }
            else if (copied == 0 && firstError.Length > 0) { MessageBox.Show(this, "Could not add the videos. " + firstError, "LiveBackground", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        private void CopyImagesToFolder(string folderPath, string[] paths)
        {
            if (paths == null || paths.Length == 0) return;
            string folder = Resolve(folderPath ?? "");
            if (string.IsNullOrWhiteSpace(folder))
            {
                MessageBox.Show(this, "Choose an image folder first.", "LiveBackground", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            try { Directory.CreateDirectory(folder); }
            catch (Exception ex) { MessageBox.Show(this, "Could not create the image folder: " + ex.Message, "LiveBackground", MessageBoxButton.OK, MessageBoxImage.Error); return; }

            var sources = new List<string>();
            foreach (string path in paths)
            {
                try
                {
                    if (File.Exists(path) && MediaPipeline.IsImage(path)) sources.Add(path);
                    else if (Directory.Exists(path)) sources.AddRange(MediaPipeline.ScanImages(path));
                }
                catch { }
            }
            if (sources.Count == 0) return;
            int copied = 0; int skipped = 0; string firstError = "";
            foreach (string source in sources)
            {
                try
                {
                    string sourceFull = System.IO.Path.GetFullPath(source);
                    if (string.Equals(System.IO.Path.GetDirectoryName(sourceFull), folder, StringComparison.OrdinalIgnoreCase)) { skipped++; continue; }
                    string target = UniquePath(folder, System.IO.Path.GetFileName(sourceFull));
                    File.Copy(sourceFull, target, false);
                    copied++;
                }
                catch (Exception ex) { if (firstError.Length == 0) firstError = ex.Message; }
            }
            if (copied > 0) { ShowNotify("LiveBackground", "Added " + copied + " image(s) to " + folder + "."); }
            else if (copied == 0 && firstError.Length > 0) { MessageBox.Show(this, "Could not add the images. " + firstError, "LiveBackground", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        private void BrowseAndAddVideos()
        {
            try
            {
                var dlg = new Microsoft.Win32.OpenFileDialog();
                dlg.Title = "Choose wallpaper videos";
                dlg.Filter = "Video files|*.mp4;*.mkv;*.mov;*.avi;*.webm;*.m4v;*.wmv;*.mpg;*.mpeg;*.ts;*.m2ts;*.flv;*.ogv;*.3gp|All files|*.*";
                dlg.Multiselect = true;
                dlg.CheckFileExists = true;
                if (dlg.ShowDialog(this) == true) AddVideos(dlg.FileNames);
            }
            catch { }
        }

        private void BrowseAndAddImages()
        {
            try
            {
                var dlg = new Microsoft.Win32.OpenFileDialog();
                dlg.Title = "Choose images";
                dlg.Filter = "Image files|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp|All files|*.*";
                dlg.Multiselect = true;
                dlg.CheckFileExists = true;
                if (dlg.ShowDialog(this) == true) AddImages(dlg.FileNames);
            }
            catch { }
        }

        private static string UniquePath(string folder, string fileName)
        {
            string target = System.IO.Path.Combine(folder, fileName);
            if (!File.Exists(target)) return target;
            string baseName = System.IO.Path.GetFileNameWithoutExtension(fileName);
            string extension = System.IO.Path.GetExtension(fileName);
            for (int i = 2; i < 10000; i++)
            {
                target = System.IO.Path.Combine(folder, baseName + " (" + i + ")" + extension);
                if (!File.Exists(target)) return target;
            }
            return System.IO.Path.Combine(folder, baseName + " (" + DateTime.Now.Ticks + ")" + extension);
        }

        private void AddVideos(string[] paths)
        {
            if (paths == null || paths.Length == 0) return;
            string folder = Resolve(_videoFolderBox.Text);
            try { Directory.CreateDirectory(folder); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not create the video folder: " + ex.Message, "LiveBackground", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Preserve current UI order/enabled state
            _config.Playlist = GetClipItems().Select(c => new PlaylistEntry(c.Name, c.Enabled)).ToList();

            var sources = new List<string>();
            foreach (string path in paths)
            {
                try
                {
                    if (File.Exists(path) && MediaPipeline.IsVideo(path)) sources.Add(path);
                    else if (Directory.Exists(path)) sources.AddRange(MediaPipeline.ScanVideos(path));
                }
                catch { }
            }
            if (sources.Count == 0)
            {
                if (_clipHint != null) _clipHint.Text = "No supported video files were found in what you dropped.";
                return;
            }

            int copied = 0; int skipped = 0; string firstError = "";
            foreach (string source in sources)
            {
                try
                {
                    string sourceFull = System.IO.Path.GetFullPath(source);
                    if (string.Equals(System.IO.Path.GetDirectoryName(sourceFull), folder, StringComparison.OrdinalIgnoreCase)) { skipped++; continue; }
                    string target = UniquePath(folder, System.IO.Path.GetFileName(sourceFull));
                    File.Copy(sourceFull, target, false);
                    copied++;
                }
                catch (Exception ex) { if (firstError.Length == 0) firstError = ex.Message; }
            }
            RefreshClipList();
            string message;
            if (copied == 0 && skipped > 0) message = "Those videos are already in this folder.";
            else if (copied == 0) message = "Could not add the videos." + (firstError.Length > 0 ? " " + firstError : "");
            else
            {
                message = "Added " + copied + " video(s) to " + folder + ". They are in the list but not ticked yet, so they will not play until you tick them.";
                if (skipped > 0) message += " " + skipped + " already in the folder.";
                if (firstError.Length > 0) message += " One file failed: " + firstError;
                ShowNotify("LiveBackground", "Added " + copied + " video(s). Tick them in the playlist to play them.");
            }
            if (_clipHint != null) _clipHint.Text = message;
        }

        private void AddImages(string[] paths)
        {
            if (paths == null || paths.Length == 0) return;
            string folder = Resolve(_imageFolderBox.Text);
            try { Directory.CreateDirectory(folder); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not create the image folder: " + ex.Message, "LiveBackground", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _config.ImagePlaylist = GetImageItems().Select(i => new PlaylistEntry(i.Name, i.Enabled)).ToList();

            var sources = new List<string>();
            foreach (string path in paths)
            {
                try
                {
                    if (File.Exists(path) && MediaPipeline.IsImage(path)) sources.Add(path);
                    else if (Directory.Exists(path)) sources.AddRange(MediaPipeline.ScanImages(path));
                }
                catch { }
            }
            if (sources.Count == 0)
            {
                if (_imageHint != null) _imageHint.Text = "No supported image files were found in what you dropped.";
                return;
            }

            int copied = 0; int skipped = 0; string firstError = "";
            foreach (string source in sources)
            {
                try
                {
                    string sourceFull = System.IO.Path.GetFullPath(source);
                    if (string.Equals(System.IO.Path.GetDirectoryName(sourceFull), folder, StringComparison.OrdinalIgnoreCase)) { skipped++; continue; }
                    string target = UniquePath(folder, System.IO.Path.GetFileName(sourceFull));
                    File.Copy(sourceFull, target, false);
                    copied++;
                }
                catch (Exception ex) { if (firstError.Length == 0) firstError = ex.Message; }
            }
            RefreshImageList();
            RefreshLockImageChoices();
            string message;
            if (copied == 0 && skipped > 0) message = "Those images are already in this folder.";
            else if (copied == 0) message = "Could not add the images." + (firstError.Length > 0 ? " " + firstError : "");
            else
            {
                message = "Added " + copied + " image(s) to " + folder + ".";
                if (skipped > 0) message += " " + skipped + " already in the folder.";
                if (firstError.Length > 0) message += " One file failed: " + firstError;
                ShowNotify("LiveBackground", "Added " + copied + " image(s).");
            }
            if (_imageHint != null) _imageHint.Text = message;
        }
        private sealed class MonitorSlotUi
        {
            private readonly SettingsWindow _owner;
            private readonly int _number;
            private TabItem _tab;
            private TextBlock _deviceLabel;
            private StackPanel _videoSection;
            private StackPanel _imageSection;
            private CheckBox _sameVideo;
            private TextBox _videoFolder;
            private RadioButton _videoModeSingle;
            private RadioButton _videoModePlaylist;
            private Border _videoDrop;
            private ListView _clipList;
            private TextBlock _clipHint;
            private CheckBox _sameImages;
            private TextBox _imageFolder;
            private RadioButton _imageModeSingle;
            private RadioButton _imageModeSlideshow;
            private Border _imageDrop;
            private ListView _imageList;
            private TextBlock _imageHint;
            private TabControl _innerTabs;
            private TabItem _contentTab;
            private TabItem _playbackTab;
            private Slider _videoFadeSlider;
            private TextBlock _videoFadeValue;
            private CheckBox _videoFineSteps;
            private Slider _intervalSlider;
            private TextBlock _intervalValue;
            private Slider _imageFadeSlider;
            private TextBlock _imageFadeValue;
            private CheckBox _imageFineSteps;
            private CheckBox _imageShuffle;
            private StackPanel _videoPlaybackPanel;
            private StackPanel _imagePlaybackPanel;
            private bool _imagesMode;
            private string _device = "";

            public MonitorSlotUi(SettingsWindow owner, int number) { _owner = owner; _number = number; }

            public TabItem BuildTabItem()
            {
                string header = _number == 2 ? "2nd Screen" : _number == 3 ? "3rd Screen" : _number == 4 ? "4th Screen" : "Screen " + _number;
                _tab = new TabItem { Header = header };
                var sp = new StackPanel { Margin = new Thickness(8) };
                _deviceLabel = Muted("No screen detected for this slot.");
                _deviceLabel.FontSize = 11;
                _deviceLabel.TextWrapping = TextWrapping.NoWrap;
                _deviceLabel.TextTrimming = TextTrimming.CharacterEllipsis;
                sp.Children.Add(_deviceLabel);

                _videoSection = new StackPanel();
                _videoSection.Children.Add(Header("Video"));
                _sameVideo = new CheckBox { Content = "Same as main screen", Margin = new Thickness(0, 6, 0, 6), IsChecked = true };
                _videoSection.Children.Add(_sameVideo);
                var vrow = new Grid { Margin = new Thickness(20, 0, 0, 6) };
                vrow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                vrow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                vrow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                _videoFolder = new TextBox();
                _videoFolder.TextChanged += delegate { RefreshClipList(); };
                Grid.SetColumn(_videoFolder, 0);
                var vb = new Button { Content = "Browse...", MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
                vb.Click += delegate { try { using (var dlg = new System.Windows.Forms.FolderBrowserDialog()) { if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK) _videoFolder.Text = dlg.SelectedPath; } } catch { } };
                Grid.SetColumn(vb, 1);
                _videoDrop = CreateDropZone("Drop videos", delegate(string[] files) { if (_sameVideo == null || _sameVideo.IsChecked == true) return; _owner.CopyVideosToFolder(_videoFolder.Text, files); RefreshClipList(); }, BrowseAndAddVideosToSlot, false);
                _videoDrop.MinWidth = 120; _videoDrop.MinHeight = 0; _videoDrop.Margin = new Thickness(8, 0, 0, 0); _videoDrop.VerticalAlignment = VerticalAlignment.Stretch;
                Grid.SetColumn(_videoDrop, 2);
                vrow.Children.Add(_videoFolder); vrow.Children.Add(vb); vrow.Children.Add(_videoDrop);
                _videoSection.Children.Add(vrow);

                var vmRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 0, 0, 6) };
                string vgroup = "vmode" + _number;
                _videoModeSingle = new RadioButton { Content = "Single clip", GroupName = vgroup, Margin = new Thickness(0, 0, 16, 0) };
                _videoModePlaylist = new RadioButton { Content = "All clips in this order", GroupName = vgroup, IsChecked = true };
                _videoModeSingle.Checked += delegate { UpdateClipListMode(); };
                _videoModePlaylist.Checked += delegate { UpdateClipListMode(); };
                vmRow.Children.Add(_videoModeSingle);
                vmRow.Children.Add(_videoModePlaylist);
                _videoSection.Children.Add(vmRow);

                _clipList = new ListView { Height = 210, Margin = new Thickness(20, 0, 0, 6) };
                var vgv = new GridView();
                vgv.Columns.Add(new GridViewColumn { Header = "", Width = 34, CellTemplate = SlotClipCheckCellTemplate() });
                vgv.Columns.Add(new GridViewColumn { Header = "Clip", Width = 300, DisplayMemberBinding = new System.Windows.Data.Binding("Name") });
                vgv.Columns.Add(new GridViewColumn { Header = "Length", Width = 80, DisplayMemberBinding = new System.Windows.Data.Binding("Length") });
                _clipList.View = vgv;
                _videoSection.Children.Add(_clipList);
                var vbtns = new WrapPanel { Margin = new Thickness(20, 0, 0, 4) };
                vbtns.Children.Add(ActionButton("Move up", delegate { MoveSelected(_clipList, -1); }));
                vbtns.Children.Add(ActionButton("Move down", delegate { MoveSelected(_clipList, 1); }));
                vbtns.Children.Add(ActionButton("Sort A-Z", delegate { SortSlotClipItems(); }));
                vbtns.Children.Add(ActionButton("Refresh", delegate { RefreshClipList(); }));
                vbtns.Children.Add(ActionButton("Open folder", delegate { OpenFolderSafe(ResolveSlotPath(_videoFolder)); }));
                _videoSection.Children.Add(vbtns);
                _clipHint = Muted(""); _clipHint.Margin = new Thickness(20, 0, 0, 4);
                _videoSection.Children.Add(_clipHint);

                _imageSection = new StackPanel();
                _imageSection.Children.Add(Header("Images"));
                _sameImages = new CheckBox { Content = "Same as main screen", Margin = new Thickness(0, 6, 0, 6), IsChecked = true };
                _imageSection.Children.Add(_sameImages);
                var irow = new Grid { Margin = new Thickness(20, 0, 0, 6) };
                irow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                irow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                irow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                _imageFolder = new TextBox();
                _imageFolder.TextChanged += delegate { RefreshImageList(); };
                Grid.SetColumn(_imageFolder, 0);
                var ib = new Button { Content = "Browse...", MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
                ib.Click += delegate { try { using (var dlg = new System.Windows.Forms.FolderBrowserDialog()) { if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK) _imageFolder.Text = dlg.SelectedPath; } } catch { } };
                Grid.SetColumn(ib, 1);
                _imageDrop = CreateDropZone("Drop images", delegate(string[] files) { if (_sameImages == null || _sameImages.IsChecked == true) return; _owner.CopyImagesToFolder(_imageFolder.Text, files); RefreshImageList(); }, BrowseAndAddImagesToSlot, true);
                _imageDrop.MinWidth = 120; _imageDrop.MinHeight = 0; _imageDrop.Margin = new Thickness(8, 0, 0, 0); _imageDrop.VerticalAlignment = VerticalAlignment.Stretch;
                Grid.SetColumn(_imageDrop, 2);
                irow.Children.Add(_imageFolder); irow.Children.Add(ib); irow.Children.Add(_imageDrop);
                _imageSection.Children.Add(irow);

                var imRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 0, 0, 6) };
                string igroup = "imode" + _number;
                _imageModeSingle = new RadioButton { Content = "One image", GroupName = igroup, Margin = new Thickness(0, 0, 16, 0) };
                _imageModeSlideshow = new RadioButton { Content = "Slideshow", GroupName = igroup, IsChecked = true };
                _imageModeSingle.Checked += delegate { UpdateImageListMode(); };
                _imageModeSlideshow.Checked += delegate { UpdateImageListMode(); };
                imRow.Children.Add(_imageModeSingle);
                imRow.Children.Add(_imageModeSlideshow);
                _imageSection.Children.Add(imRow);

                _imageList = new ListView { Height = 210, Margin = new Thickness(20, 0, 0, 6) };
                var igv = new GridView();
                igv.Columns.Add(new GridViewColumn { Header = "", Width = 34, CellTemplate = SlotImageCheckCellTemplate() });
                igv.Columns.Add(new GridViewColumn { Header = "Image", Width = 380, DisplayMemberBinding = new System.Windows.Data.Binding("Name") });
                _imageList.View = igv;
                _imageSection.Children.Add(_imageList);
                var ibtns = new WrapPanel { Margin = new Thickness(20, 0, 0, 4) };
                ibtns.Children.Add(ActionButton("Move up", delegate { MoveSelected(_imageList, -1); }));
                ibtns.Children.Add(ActionButton("Move down", delegate { MoveSelected(_imageList, 1); }));
                ibtns.Children.Add(ActionButton("Sort A-Z", delegate { SortSlotImageItems(); }));
                ibtns.Children.Add(ActionButton("Refresh", delegate { RefreshImageList(); }));
                ibtns.Children.Add(ActionButton("Open folder", delegate { OpenFolderSafe(ResolveSlotPath(_imageFolder)); }));
                _imageSection.Children.Add(ibtns);
                _imageHint = Muted(""); _imageHint.Margin = new Thickness(20, 0, 0, 4);
                _imageSection.Children.Add(_imageHint);

                _sameVideo.Checked += delegate { UpdateEnabled(); };
                _sameVideo.Unchecked += delegate { UpdateEnabled(); };
                _sameImages.Checked += delegate { UpdateEnabled(); };
                _sameImages.Unchecked += delegate { UpdateEnabled(); };

                var contentPanel = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
                contentPanel.Children.Add(_videoSection);
                contentPanel.Children.Add(_imageSection);
                _contentTab = new TabItem { Header = "Video", Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = contentPanel } };

                var playbackPanel = new StackPanel { Margin = new Thickness(8) };
                _videoPlaybackPanel = new StackPanel();
                _videoPlaybackPanel.Children.Add(Header("Crossfade"));
                var vfadeRow = new DockPanel { Margin = new Thickness(0, 6, 0, 4) };
                _videoFadeValue = new TextBlock { MinWidth = 60, TextAlignment = TextAlignment.Right, Foreground = Brushes.White };
                DockPanel.SetDock(_videoFadeValue, Dock.Right);
                _videoFadeSlider = new Slider { Minimum = 0.5, Maximum = 10.0, TickFrequency = 0.5, IsSnapToTickEnabled = true, SmallChange = 0.5, LargeChange = 1.0 };
                _videoFadeSlider.ValueChanged += delegate { UpdateVideoFadeLabel(); };
                vfadeRow.Children.Add(_videoFadeValue); vfadeRow.Children.Add(_videoFadeSlider);
                _videoPlaybackPanel.Children.Add(vfadeRow);
                _videoFineSteps = new CheckBox { Content = "Allow half-second steps (finer control)", Margin = new Thickness(0, 0, 0, 16) };
                _videoPlaybackPanel.Children.Add(_videoFineSteps);
                _videoPlaybackPanel.Children.Add(Muted("Resolution, frame rate and quality use the main Playback tab settings."));
                playbackPanel.Children.Add(_videoPlaybackPanel);

                _imagePlaybackPanel = new StackPanel();
                _imagePlaybackPanel.Children.Add(Header("Slideshow"));
                _imagePlaybackPanel.Children.Add(Muted("Time each picture stays on screen:"));
                var intervalRow = new DockPanel { Margin = new Thickness(0, 6, 0, 12) };
                _intervalValue = new TextBlock { MinWidth = 70, TextAlignment = TextAlignment.Right, Foreground = Brushes.White };
                DockPanel.SetDock(_intervalValue, Dock.Right);
                _intervalSlider = new Slider { Minimum = 5, Maximum = 300, TickFrequency = 5, IsSnapToTickEnabled = true };
                _intervalSlider.ValueChanged += delegate { UpdateIntervalLabel(); };
                intervalRow.Children.Add(_intervalValue); intervalRow.Children.Add(_intervalSlider);
                _imagePlaybackPanel.Children.Add(intervalRow);
                _imagePlaybackPanel.Children.Add(Muted("Crossfade between pictures:"));
                var imageFadeRow = new DockPanel { Margin = new Thickness(0, 6, 0, 4) };
                _imageFadeValue = new TextBlock { MinWidth = 70, TextAlignment = TextAlignment.Right, Foreground = Brushes.White };
                DockPanel.SetDock(_imageFadeValue, Dock.Right);
                _imageFadeSlider = new Slider { Minimum = 0, Maximum = 5, TickFrequency = 0.5, IsSnapToTickEnabled = true };
                _imageFadeSlider.ValueChanged += delegate { UpdateImageFadeLabel(); };
                imageFadeRow.Children.Add(_imageFadeValue); imageFadeRow.Children.Add(_imageFadeSlider);
                _imagePlaybackPanel.Children.Add(imageFadeRow);
                _imageFineSteps = new CheckBox { Content = "Allow half-second steps", Margin = new Thickness(0, 0, 0, 10) };
                _imageShuffle = new CheckBox { Content = "Shuffle pictures", Margin = new Thickness(0, 0, 0, 8) };
                _imagePlaybackPanel.Children.Add(_imageFineSteps);
                _imagePlaybackPanel.Children.Add(_imageShuffle);
                playbackPanel.Children.Add(_imagePlaybackPanel);

                _playbackTab = new TabItem { Header = "Playback", Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = playbackPanel } };
                _innerTabs = new TabControl();
                _innerTabs.Items.Add(_contentTab);
                _innerTabs.Items.Add(_playbackTab);
                sp.Children.Add(_innerTabs);
                _tab.Content = sp;
                UpdateEnabled();
                return _tab;
            }

            private DataTemplate SlotClipCheckCellTemplate()
            {
                var fef = new FrameworkElementFactory(typeof(CheckBox));
                fef.SetBinding(ToggleButton.IsCheckedProperty, new System.Windows.Data.Binding("Enabled") { Mode = System.Windows.Data.BindingMode.TwoWay, UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged });
                fef.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
                fef.AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler(SlotClipCheckBox_Checked));
                return new DataTemplate { VisualTree = fef };
            }

            private DataTemplate SlotImageCheckCellTemplate()
            {
                var fef = new FrameworkElementFactory(typeof(CheckBox));
                fef.SetBinding(ToggleButton.IsCheckedProperty, new System.Windows.Data.Binding("Enabled") { Mode = System.Windows.Data.BindingMode.TwoWay, UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged });
                fef.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
                fef.AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler(SlotImageCheckBox_Checked));
                return new DataTemplate { VisualTree = fef };
            }

            private void SlotClipCheckBox_Checked(object sender, RoutedEventArgs e)
            {
                if (_videoModeSingle == null || _videoModeSingle.IsChecked != true) return;
                var cb = sender as System.Windows.Controls.CheckBox;
                var current = cb != null ? cb.DataContext as ClipItem : null;
                if (current == null) return;
                var items = _clipList != null ? _clipList.ItemsSource as ObservableCollection<ClipItem> : null;
                if (items == null) return;
                bool changed = false;
                foreach (var item in items)
                {
                    bool should = object.ReferenceEquals(item, current);
                    if (item.Enabled != should) { item.Enabled = should; changed = true; }
                }
                if (changed)
                {
                    var view = System.Windows.Data.CollectionViewSource.GetDefaultView(_clipList.ItemsSource);
                    if (view != null) view.Refresh();
                }
            }

            private void SlotImageCheckBox_Checked(object sender, RoutedEventArgs e)
            {
                if (_imageModeSingle == null || _imageModeSingle.IsChecked != true) return;
                var cb = sender as System.Windows.Controls.CheckBox;
                var current = cb != null ? cb.DataContext as ImageItem : null;
                if (current == null) return;
                var items = _imageList != null ? _imageList.ItemsSource as ObservableCollection<ImageItem> : null;
                if (items == null) return;
                bool changed = false;
                foreach (var item in items)
                {
                    bool should = object.ReferenceEquals(item, current);
                    if (item.Enabled != should) { item.Enabled = should; changed = true; }
                }
                if (changed)
                {
                    var view = System.Windows.Data.CollectionViewSource.GetDefaultView(_imageList.ItemsSource);
                    if (view != null) view.Refresh();
                }
            }

            private void BrowseAndAddVideosToSlot()
            {
                try
                {
                    if (_sameVideo != null && _sameVideo.IsChecked == true) return;
                    var dlg = new Microsoft.Win32.OpenFileDialog();
                    dlg.Title = "Choose wallpaper videos";
                    dlg.Filter = "Video files|*.mp4;*.mkv;*.mov;*.avi;*.webm;*.m4v;*.wmv;*.mpg;*.mpeg;*.ts;*.m2ts;*.flv;*.ogv;*.3gp|All files|*.*";
                    dlg.Multiselect = true;
                    dlg.CheckFileExists = true;
                    if (dlg.ShowDialog(_owner) == true) { _owner.CopyVideosToFolder(_videoFolder != null ? _videoFolder.Text : "", dlg.FileNames); RefreshClipList(); }
                }
                catch { }
            }

            private void BrowseAndAddImagesToSlot()
            {
                try
                {
                    if (_sameImages != null && _sameImages.IsChecked == true) return;
                    var dlg = new Microsoft.Win32.OpenFileDialog();
                    dlg.Title = "Choose images";
                    dlg.Filter = "Image files|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp|All files|*.*";
                    dlg.Multiselect = true;
                    dlg.CheckFileExists = true;
                    if (dlg.ShowDialog(_owner) == true) { _owner.CopyImagesToFolder(_imageFolder != null ? _imageFolder.Text : "", dlg.FileNames); RefreshImageList(); }
                }
                catch { }
            }

            private string ResolveSlotPath(TextBox box)
            {
                try { return Util.ResolvePath(_owner._appRoot, (box != null ? box.Text : "") ?? ""); }
                catch { return ""; }
            }

            private void RefreshClipList()
            {
                if (_clipList == null) return;
                var items = new ObservableCollection<ClipItem>();
                try
                {
                    string folder = ResolveSlotPath(_videoFolder);
                    var files = MediaPipeline.ScanVideos(folder);
                    var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var f in files) byName[Path.GetFileName(f)] = f;
                    var ordered = new List<string>();
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var saved = GetSavedScreenSettings();
                    var sourceList = saved != null ? saved.Playlist : null;
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
                    foreach (var f in ordered)
                    {
                        string name = Path.GetFileName(f);
                        bool en;
                        if (!enabledMap.TryGetValue(name, out en)) en = false;
                        items.Add(new ClipItem { Name = name, Path = f, Enabled = en, Length = "", Duration = -1 });
                    }
                }
                catch { }
                _clipList.ItemsSource = items;
                UpdateClipListMode();
            }

            private void RefreshImageList()
            {
                if (_imageList == null) return;
                var items = new ObservableCollection<ImageItem>();
                try
                {
                    string folder = ResolveSlotPath(_imageFolder);
                    var files = MediaPipeline.ScanImages(folder);
                    var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var f in files) byName[Path.GetFileName(f)] = f;
                    var ordered = new List<string>();
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var saved = GetSavedScreenSettings();
                    var sourceList = saved != null ? saved.ImagePlaylist : null;
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
                    foreach (var f in ordered)
                    {
                        string name = Path.GetFileName(f);
                        bool en;
                        if (!enabledMap.TryGetValue(name, out en)) en = true;
                        items.Add(new ImageItem { Name = name, Path = f, Enabled = en });
                    }
                }
                catch { }
                _imageList.ItemsSource = items;
                UpdateImageListMode();
            }

            private ScreenSettings GetSavedScreenSettings()
            {
                try { return _owner._config != null ? _owner._config.ScreenFor(_device) : null; }
                catch { return null; }
            }

            private void UpdateVideoFadeLabel()
            {
                if (_videoFadeValue == null || _videoFadeSlider == null) return;
                _videoFadeValue.Text = _videoFadeSlider.Value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "s";
            }

            private void UpdateIntervalLabel()
            {
                if (_intervalValue == null || _intervalSlider == null) return;
                int sec = (int)Math.Round(_intervalSlider.Value);
                if (sec < 60) _intervalValue.Text = sec + "s";
                else
                {
                    int m = sec / 60;
                    int s = sec % 60;
                    _intervalValue.Text = s == 0 ? m + "m" : m + "m " + s + "s";
                }
            }

            private void UpdateImageFadeLabel()
            {
                if (_imageFadeValue == null || _imageFadeSlider == null) return;
                _imageFadeValue.Text = _imageFadeSlider.Value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "s";
            }

            private void UpdateClipListMode()
            {
                bool single = _videoModeSingle != null && _videoModeSingle.IsChecked == true;
                if (!single || _clipList == null) return;
                var items = _clipList.ItemsSource as ObservableCollection<ClipItem>;
                if (items == null) return;
                var checkedItems = items.Where(i => i.Enabled).ToList();
                if (checkedItems.Count <= 1) return;
                ClipItem keep = _clipList.SelectedItem as ClipItem;
                if (keep == null || !keep.Enabled) keep = checkedItems[0];
                foreach (var item in items) item.Enabled = object.ReferenceEquals(item, keep);
                var view = System.Windows.Data.CollectionViewSource.GetDefaultView(_clipList.ItemsSource);
                if (view != null) view.Refresh();
            }

            private void UpdateImageListMode()
            {
                bool single = _imageModeSingle != null && _imageModeSingle.IsChecked == true;
                if (!single || _imageList == null) return;
                var items = _imageList.ItemsSource as ObservableCollection<ImageItem>;
                if (items == null) return;
                var checkedItems = items.Where(i => i.Enabled).ToList();
                if (checkedItems.Count <= 1) return;
                ImageItem keep = _imageList.SelectedItem as ImageItem;
                if (keep == null || !keep.Enabled) keep = checkedItems[0];
                foreach (var item in items) item.Enabled = object.ReferenceEquals(item, keep);
                var view = System.Windows.Data.CollectionViewSource.GetDefaultView(_imageList.ItemsSource);
                if (view != null) view.Refresh();
            }

            private void SortSlotClipItems()
            {
                var items = _clipList != null ? _clipList.ItemsSource as ObservableCollection<ClipItem> : null;
                if (items == null) return;
                var names = items.Select(x => x.Name).ToList();
                var order = PlaylistState.SortedOrder(names);
                var sorted = order.Select(i => items[i]).ToList();
                items.Clear();
                foreach (var it in sorted) items.Add(it);
            }

            private void SortSlotImageItems()
            {
                var items = _imageList != null ? _imageList.ItemsSource as ObservableCollection<ImageItem> : null;
                if (items == null) return;
                var names = items.Select(x => x.Name).ToList();
                var order = PlaylistState.SortedOrder(names);
                var sorted = order.Select(i => items[i]).ToList();
                items.Clear();
                foreach (var it in sorted) items.Add(it);
            }

            private void UpdateEnabled()
            {
                bool has = !string.IsNullOrEmpty(_device);
                ApplyReviewEnabled(_tab, has);
                bool sv = _sameVideo != null && _sameVideo.IsChecked == true;
                bool si = _sameImages != null && _sameImages.IsChecked == true;

                if (_contentTab != null) _contentTab.Header = _imagesMode ? "Images" : "Video";
                if (_videoSection != null) _videoSection.Visibility = _imagesMode ? Visibility.Collapsed : Visibility.Visible;
                if (_imageSection != null) _imageSection.Visibility = _imagesMode ? Visibility.Visible : Visibility.Collapsed;
                if (_videoPlaybackPanel != null) _videoPlaybackPanel.Visibility = _imagesMode ? Visibility.Collapsed : Visibility.Visible;
                if (_imagePlaybackPanel != null) _imagePlaybackPanel.Visibility = _imagesMode ? Visibility.Visible : Visibility.Collapsed;

                ApplyReviewChildEnabled(_sameVideo, has, has);
                ApplyReviewChildEnabled(_sameImages, has, has);
                ApplyReviewChildEnabled(_videoFolder, has, has && !sv);
                ApplyReviewChildEnabled(_videoModeSingle, has, has && !sv);
                ApplyReviewChildEnabled(_videoModePlaylist, has, has && !sv);
                ApplyReviewChildEnabled(_videoDrop, has, has && !sv);
                ApplyReviewChildEnabled(_clipList, has, has && !sv);
                ApplyReviewChildEnabled(_videoFadeSlider, has, has && !sv);
                ApplyReviewChildEnabled(_videoFineSteps, has, has && !sv);
                ApplyReviewChildEnabled(_imageFolder, has, has && !si);
                ApplyReviewChildEnabled(_imageModeSingle, has, has && !si);
                ApplyReviewChildEnabled(_imageModeSlideshow, has, has && !si);
                ApplyReviewChildEnabled(_imageDrop, has, has && !si);
                ApplyReviewChildEnabled(_imageList, has, has && !si);
                ApplyReviewChildEnabled(_intervalSlider, has, has && !si);
                ApplyReviewChildEnabled(_imageFadeSlider, has, has && !si);
                ApplyReviewChildEnabled(_imageFineSteps, has, has && !si);
                ApplyReviewChildEnabled(_imageShuffle, has, has && !si);
            }

            public void SetMode(bool imagesMode) { _imagesMode = imagesMode; UpdateEnabled(); }

            public void LoadFromConfig(AppConfig config, List<ScreenInfo> screens, bool hasSecond, bool duplicate)
            {
                _ = hasSecond; _ = duplicate;
                RefreshBinding(config, screens, hasSecond, duplicate, true);
            }

            public void RefreshBinding(AppConfig config, List<ScreenInfo> screens, bool hasSecond, bool duplicate, bool reloadValues = true)
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
                bool deviceChanged = !string.Equals(_device, dev, StringComparison.OrdinalIgnoreCase);
                _device = dev;
                bool has = _device.Length > 0;
                if (_deviceLabel != null) _deviceLabel.Text = has ? ("Monitor: " + _device) : "No screen detected for this slot.";
                if (reloadValues || deviceChanged)
                {
                    ScreenSettings s = config.ScreenFor(_device);
                    if (_sameVideo != null) _sameVideo.IsChecked = s.SameAsMain;
                    if (_videoFolder != null) _videoFolder.Text = s.Folder ?? "";
                    bool singleVideo = string.Equals(s.PlaybackMode, "single", StringComparison.OrdinalIgnoreCase);
                    if (_videoModeSingle != null) _videoModeSingle.IsChecked = singleVideo;
                    if (_videoModePlaylist != null) _videoModePlaylist.IsChecked = !singleVideo;
                    if (_sameImages != null) _sameImages.IsChecked = s.ImagesSameAsMain;
                    if (_imageFolder != null) _imageFolder.Text = s.ImageFolder ?? "";
                    bool singleImage = string.Equals(s.ImageMode, "single", StringComparison.OrdinalIgnoreCase);
                    if (_imageModeSingle != null) _imageModeSingle.IsChecked = singleImage;
                    if (_imageModeSlideshow != null) _imageModeSlideshow.IsChecked = !singleImage;
                    double vfade = s.CrossfadeSeconds.HasValue ? s.CrossfadeSeconds.Value : (config != null ? config.CrossfadeSeconds : 3.0);
                    bool vfine = s.FineCrossfadeSteps.HasValue ? s.FineCrossfadeSteps.Value : (config != null && config.FineCrossfadeSteps);
                    if (_videoFadeSlider != null) _videoFadeSlider.Value = Math.Max(_videoFadeSlider.Minimum, Math.Min(_videoFadeSlider.Maximum, vfade));
                    if (_videoFineSteps != null) _videoFineSteps.IsChecked = vfine;
                    if (_intervalSlider != null) _intervalSlider.Value = Math.Max(_intervalSlider.Minimum, Math.Min(_intervalSlider.Maximum, s.ImageIntervalSeconds));
                    if (_imageFadeSlider != null) _imageFadeSlider.Value = Math.Max(_imageFadeSlider.Minimum, Math.Min(_imageFadeSlider.Maximum, s.ImageCrossfadeSeconds));
                    if (_imageFineSteps != null) _imageFineSteps.IsChecked = s.ImageFineCrossfadeSteps;
                    if (_imageShuffle != null) _imageShuffle.IsChecked = s.ImageShuffle;
                    UpdateVideoFadeLabel();
                    UpdateIntervalLabel();
                    UpdateImageFadeLabel();
                    RefreshClipList();
                    ApplySingleSelection(_clipList, s.SingleClip, singleVideo);
                    RefreshImageList();
                    ApplySingleSelection(_imageList, s.ImageSingle, singleImage);
                }
                UpdateEnabled();
            }

            private static void ApplySingleSelection(ListView list, string preferredName, bool singleMode)
            {
                if (!singleMode || list == null) return;
                var items = list.ItemsSource as System.Collections.IEnumerable;
                if (items == null) return;
                ClipItem keepClip = null;
                ImageItem keepImage = null;
                var clipItems = list.ItemsSource as ObservableCollection<ClipItem>;
                if (clipItems != null)
                {
                    foreach (var it in clipItems)
                    {
                        if (!string.IsNullOrEmpty(preferredName) && string.Equals(it.Name, preferredName, StringComparison.OrdinalIgnoreCase)) { keepClip = it; break; }
                    }
                    if (keepClip == null) keepClip = clipItems.FirstOrDefault(i => i.Enabled) ?? clipItems.FirstOrDefault();
                    bool changed = false;
                    foreach (var it in clipItems)
                    {
                        bool should = object.ReferenceEquals(it, keepClip);
                        if (it.Enabled != should) { it.Enabled = should; changed = true; }
                    }
                    if (changed) { var view = System.Windows.Data.CollectionViewSource.GetDefaultView(list.ItemsSource); if (view != null) view.Refresh(); }
                    return;
                }
                var imageItems = list.ItemsSource as ObservableCollection<ImageItem>;
                if (imageItems != null)
                {
                    foreach (var it in imageItems)
                    {
                        if (!string.IsNullOrEmpty(preferredName) && string.Equals(it.Name, preferredName, StringComparison.OrdinalIgnoreCase)) { keepImage = it; break; }
                    }
                    if (keepImage == null) keepImage = imageItems.FirstOrDefault(i => i.Enabled) ?? imageItems.FirstOrDefault();
                    bool changed = false;
                    foreach (var it in imageItems)
                    {
                        bool should = object.ReferenceEquals(it, keepImage);
                        if (it.Enabled != should) { it.Enabled = should; changed = true; }
                    }
                    if (changed) { var view = System.Windows.Data.CollectionViewSource.GetDefaultView(list.ItemsSource); if (view != null) view.Refresh(); }
                }
            }

            public bool CaptureInto(AppConfig updated)
            {
                if (string.IsNullOrEmpty(_device)) return true;
                ScreenSettings s = updated.ScreenByDevice(_device, true);
                if (s == null) return true;
                s.SameAsMain = _sameVideo != null && _sameVideo.IsChecked == true;
                s.ImagesSameAsMain = _sameImages != null && _sameImages.IsChecked == true;
                if (_videoFolder != null && !s.SameAsMain) s.Folder = (_videoFolder.Text ?? "").Trim();
                if (!s.SameAsMain)
                {
                    s.PlaybackMode = _videoModeSingle != null && _videoModeSingle.IsChecked == true ? "single" : "playlist";
                    var clipItems = _clipList != null ? _clipList.ItemsSource as ObservableCollection<ClipItem> : null;
                    if (clipItems != null)
                    {
                        var checkedClips = clipItems.Where(c => c.Enabled).ToList();
                        if (checkedClips.Count > 0) s.SingleClip = checkedClips[0].Name;
                        s.Playlist = MergeMissingSavedEntries(clipItems.Select(c => new PlaylistEntry(c.Name, c.Enabled)).ToList(), s.Playlist);
                    }
                }

                if (_imageFolder != null && !s.ImagesSameAsMain) s.ImageFolder = (_imageFolder.Text ?? "").Trim();
                if (!s.ImagesSameAsMain)
                {
                    s.ImageMode = _imageModeSingle != null && _imageModeSingle.IsChecked == true ? "single" : "slideshow";
                    var imageItems = _imageList != null ? _imageList.ItemsSource as ObservableCollection<ImageItem> : null;
                    if (imageItems != null)
                    {
                        var checkedImages = imageItems.Where(i => i.Enabled).ToList();
                        if (checkedImages.Count > 0) s.ImageSingle = checkedImages[0].Name;
                        s.ImagePlaylist = MergeMissingSavedEntries(imageItems.Select(i => new PlaylistEntry(i.Name, i.Enabled)).ToList(), s.ImagePlaylist);
                    }
                }
                if (_videoFadeSlider != null) s.CrossfadeSeconds = _videoFadeSlider.Value;
                if (_videoFineSteps != null) s.FineCrossfadeSteps = _videoFineSteps.IsChecked == true;
                if (_intervalSlider != null) s.ImageIntervalSeconds = (int)Math.Round(_intervalSlider.Value);
                if (_imageFadeSlider != null) s.ImageCrossfadeSeconds = _imageFadeSlider.Value;
                if (_imageFineSteps != null) s.ImageFineCrossfadeSteps = _imageFineSteps.IsChecked == true;
                if (_imageShuffle != null) s.ImageShuffle = _imageShuffle.IsChecked == true;
                return true;
            }
        }
    }
}











