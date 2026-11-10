# LiveBackground change list

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
