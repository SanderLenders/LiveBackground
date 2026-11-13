# LiveBackground change list

## 1.6.1

### Second screen

- **Duplicate vs Extended is now visible and switchable from the app.** The
  Second screen tab has a "Windows display mode" row with **Extended** and
  **Duplicate** radio buttons that show what Windows is currently doing. Clicking
  one changes the mode (via `SetDisplayConfig`, with `DisplaySwitch.exe` as a
  fallback). The app **never** changes the mode on its own.
- **"Display settings" button** opens the Windows display settings
  (`ms-settings:display`) for arranging the screens.
- While Windows is **duplicating**, the second-screen loop controls are disabled
  with an explanation, because both screens show the same picture. The tab stays
  open so the mode can be switched back to Extended.
- **The tab now updates live.** While the settings window is open it re-checks
  the display layout about every 1.5 seconds, so plugging a screen or changing
  the Windows display mode enables or greys the tab without pressing Save first.

### Settings window

- **"Rebuild now" on the Second screen tab** is enabled whenever a second screen
  is attached, including while the screens are mirroring (it applies the whole
  form, so it just rebuilds the main loop in that case).
- Every "Rebuild now" press is written to the log, along with whether it was
  applied or cancelled.

## 1.6.0

### Second screen (two monitors)

- LiveBackground now supports **two monitors** (the main screen plus one extra).
  Each screen is cropped independently, so a 16:9 clip fills a differently
  shaped panel without stretching. A third monitor is ignored; the first two
  are used.
- **Hot-plugging works.** Adding, removing, moving or resizing a monitor is
  detected within a couple of seconds; the players are re-placed automatically
  and keep playing.
- New **Second screen** tab in the settings window, between Videos and Playback.
  It is greyed out when only one monitor is connected, with a status line that
  explains why. With a second monitor attached it offers the same controls as
  the Videos tab.
- **"Use the same video as the main screen"** (default on): the second screen
  mirrors the main clip, cropped to fit, and no extra loop is rendered. Turn it
  off to give the second screen its own folder, playlist or single clip; that
  loop is rendered once and cached separately (`cache\second.json`).
- The second player writes to `logs\mpv2.log`, so the two players never share a
  log file.
- The app now opts into per-monitor DPI awareness (PMv2) so placement uses
  physical pixels on mixed-scaling setups.
- **Not yet validated on real hardware.** This build was developed on a
  single-monitor PC, so two-screen placement, hot-plug and the Second screen tab
  have not been tested with an actual second display.

### Settings window polish

- **The settings window is a staging area.** Changing ticks, order or text no
  longer applies anything until you press **Rebuild now** (applies and keeps the
  window open) or **Save and apply** (writes and closes). Cancel closes the
  window and keeps whatever "Rebuild now" already applied.
- **"Save and apply" only rebuilds when the ticks changed.** When nothing that
  affects the loop changed it just saves and closes; a build for the exact same
  configuration that is already running is continued instead of restarted.
- Every tab that can start a build now has its own **Rebuild now** button
  (Videos and Images).
- **Lock-screen choices reworked:** "First frame of the video loop" (default),
  "Use a custom image", or "Use an image from my image wallpapers". The old
  `followimage`/`previous` values migrate to "first frame".
- The wallpaper-mode pill now switches mode on any click on the pill, not only
  on its side labels.
- Removed the long help paragraphs from the Videos, Playback, Slideshow and
  Other tabs; slider tick labels are now centred under their ticks.

### Fresh installs

- New installs start in Video mode with only "Galaxy HD Clip.mp4" playing
  (Playback: crossfade 3 s, half-second steps off, 1920x1080, 30 fps, quality
  Balanced/CRF 18, screensaver active), Images in one-image mode with
  "Galaxy Image.jpg" ticked, and Other with start-with-Windows on, lock screen
  "First frame of the video loop" and the desktop wallpaper image on (also the
  first frame).
- Reinstalling over an existing install is an in-place upgrade: your library,
  settings and wording are preserved. The new defaults only apply to a genuine
  first install.

### Fixes

- `mpv.log` rotates to `mpv.old.log` at 4 MB, so a long-running session cannot
  grow a single log without bound.
- Missing-media messages now distinguish "the selected clip is missing",
  "no clips are ticked" and "no videos in the folder".
- The image crossfade layer is one window per screen, so a slideshow covers
  every monitor.
- The version is 1.6.0.

## 1.5.1

### Fixes from a code review

- **The mode pill itself now switches the mode.** Clicking the pill in
  1.5.0 did nothing when its side was already active; only the side
  labels flipped the mode. The pill (and its keyboard equivalent) now
  toggles between Video and Images like a real switch.
- **`uninstall --remove` no longer deletes your videos and pictures.**
  It removes only the app's cache, logs and backups, and empty library
  folders. The README always promised your library and settings survive
  an uninstall; now the code does too.
- **Uninstalling via Windows Settings now restores your previous
  desktop wallpaper and lock screen**, as the README describes. The
  Inno uninstaller previously only stopped the app and removed the
  logon task.
- **A UTF-8 BOM no longer breaks config.json.** A config saved or
  touched by an editor or script that adds a BOM loaded as defaults
  ("unexpected character ï" in the log); the reader now skips the BOM.
- **Saving config.json is now atomic** (write to a temporary file,
  then swap it in), so a crash or power cut mid-save cannot leave a
  truncated file that would load as defaults.
- **Multi-process log lines no longer overwrite each other.** Helper
  invocations such as `--settings` and `--stop` wrote from a stale file
  position and could corrupt a longer-lived process's log lines.
- **Crash guard:** resolving a tool path before `Paths.Init` has run
  (the settings window in a test harness) no longer crashes with
  `Path.Combine(null, ...)`; it falls back to the executable's folder.
- The first attach of the image layer no longer logs a misleading
  "Explorer restarted" message.
- The version is 1.5.1.

## 1.5.0

### Slideshow controls

- The Slideshow interval is now a preset slider (5/10/15/20/30/60 seconds)
  with a **Custom** checkbox. Turning Custom on shows the seconds box
  (1-86400) and turns the preset slider off.
- A **Crossfade** slider controls picture transitions: 0 = off (instant),
  0.5 to 5 seconds, 0.5-second steps, default 2.0 seconds.

### App-managed image crossfade

- Images mode uses a small desktop layer below the icons to blend between
  pictures. The layer is only animated during a transition; between pictures
  it is static, so it costs no CPU at idle.
- Startup, "Next image", pause/resume and settings changes show the picture
  immediately (no fade).
- Explorer restarts and display-size changes reattach/re-render the layer.

### Crash fallback and watchdog

- Every new picture is also written as the normal Windows wallpaper. If
  LiveBackground stops unexpectedly, the desktop keeps the last picture.
- A hidden watchdog scheduled task checks every few minutes that the
  wallpaper is still running and starts it again after a crash. A deliberate
  tray Exit writes a quit marker so the watchdog does not restart it.
- `LiveBackground.exe --watchdog` performs the lightweight check (used by the
  task). `--stop` now also sets the quit marker.

### Windows wallpaper fit

- The app no longer changes `WallpaperStyle`/`TileWallpaper`. The crossfade
  layer reads the current Windows wallpaper fit setting (Fill, Fit, Stretch,
  Center, Tile or Span) and draws pictures the same way.

### Other

- The wallpaper mode selector is a compact `Video [switch] Images` pill with
  clickable side labels.
- New config field: `imageCrossfadeSeconds` (default 2.0, 0-5).
- The version is 1.5.0.

## 1.4.0

### Image pickers opened in System32 (bug)

The Browse buttons and "Drop image" boxes on the Windows tab opened in
`C:\Windows\System32`, because the pickers set no initial folder and the
scheduled task starts LiveBackground with System32 as its working directory.

Fixed:
- Start in the folder of the current value when it exists, otherwise the last
  folder used, otherwise Documents, otherwise the user profile.
- Remember the last folder used for the rest of the settings session.
- Applied the same rule to the video file picker, the video folder picker and
  the image folder picker.
- `OpenFileDialog.InitialDirectory` is now always set explicitly.

### Image wallpaper mode (new feature)

A wallpaper mode switch above the tabs: **Video / Images**.

- In Images mode the video stops completely: mpv is stopped, no prefetch and
  no rebuild is scheduled. The startup path skips Progman, mpv and the loop
  entirely, so load time is unaffected by images.
- Switching back to Video restarts the cached loop; nothing is re-rendered.
- Tabs are grouped by mode and fixed in order:
  `Videos | Playback | Images | Slideshow | Windows`
  - Video mode: Videos + Playback active; Images + Slideshow greyed.
  - Images mode: Videos + Playback greyed; Images + Slideshow active.
  - Windows is shared by both modes and always active.
- Greyed tabs are owner-drawn in grey text and cannot be selected.
- The window opens on the active mode's first tab (Videos or Images).
- One image mode: the image list checkboxes act like radio buttons. The ticked
  picture cannot be unticked by clicking it again.
- Slideshow mode: only ticked images are shown, in list order, with optional
  Shuffle. New images start ticked; unticked images stay skipped.
- Move up / Move down set the order. Sort A-Z only re-sorts the list.
- Interval presets: 5, 10, 15, 20, 30 and 60 seconds, plus a custom number of
  seconds (1 to 86400). No 30-minute or 1-hour presets.
- New images are copied into `%USERPROFILE%\LiveBackground\images\wallpapers\`
  (portable: `<ProgramRoot>\images\wallpapers`).
- Rotation is app-managed, using the existing `SystemParametersInfo` desktop
  wallpaper call. This keeps ticks, custom order and short intervals exact and
  adds no extra binary size. Windows' own slideshow is not used.
- The image folder is watched; adding or removing a picture takes effect
  without a rebuild.
- The last picture stays on the desktop when the app exits.
- The tray menu has a new "Open image folder" item.

### Lock screen from the image wallpapers

- The Windows tab now offers three lock-screen choices: the previous desktop
  wallpaper, a custom image, or an image from the image wallpapers folder.
- The lock-screen picture is always static. Windows has no public API for a
  lock-screen slideshow synced with the desktop, and the registry/PowerShell
  workarounds are undocumented and slow, so they are not used.
- When Images mode is active, the desktop-wallpaper options on the Windows tab
  are greyed out because the picture rotation owns the desktop.

### Configuration

New fields: `wallpaperMode`, `imageFolder`, `imageMode`, `imageSingle`,
`imagePlaylist`, `imageIntervalSeconds`, `imageShuffle`.

`lockScreenMode` now accepts `previous`, `custom` and `image`.

### Other

- The installer creates the image folder.
- Applying a changed lock-screen setting no longer requires a restart.
- The version is 1.4.0.

## 1.3.0

- Playlist checkboxes, custom order and one-clip radio behaviour.
