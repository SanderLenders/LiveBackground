# Manual smoke test

The unit tests cover the pure logic, but the parts that touch Windows - the
window and threading model, multi-monitor, DPI, the mpv and ffmpeg lifecycles -
can only be checked on a real desktop. Run this list before publishing a build.
It is deliberately short: the highest-value manual pass, not a full QA suite.

## Before you start

- Build the installer (see `BUILDING.md`).
- Use a real Windows 11 PC, ideally with up to four monitors in Extended mode.
- Keep `%USERPROFILE%\LiveBackground\logs\app.log` open. It should stay free of
  unexpected `ERROR` lines.

## Install, upgrade, uninstall

- [ ] Fresh install: setup runs and Windows shows the UAC prompt after
      "Install" is clicked; "Start LiveBackground when I sign in" is ticked by
      default; the "View the README" checkbox is unticked; the Start Menu
      shortcut is created and the desktop shortcut is not.
- [ ] The file being tested is
      `%LOCALAPPDATA%\LiveBackgroundBuild\installer\LiveBackgroundSetup.exe`,
      built from the current tracked sources, not a copy from an older package.
- [ ] First run seeds "Galaxy HD Clip.mp4" and starts in Single clip mode.
- [ ] Upgrade over an existing install: videos, pictures and settings are kept,
      and the autostart box is still ticked.
- [ ] Uninstall: the wallpaper stops, the logon task is removed, the previous
      wallpaper and lock screen are restored, and `%USERPROFILE%\LiveBackground`
      (videos, images, `config.json`) is left in place.
- [ ] Portable copy (a folder with `portable.txt`): all data stays in the folder.

## Video wallpaper

- [ ] Playlist mode: only ticked clips play, in list order; Move up/down and
      Sort A-Z behave.
- [ ] Single clip mode: the ticked clip repeats; ticking another moves the tick.
- [ ] The loop has no visible seam. Changing the crossfade and saving rebuilds
      in the background while the old loop keeps playing.
- [ ] Drop a new clip on the drop box: it is copied, appears unticked, and does
      not change what is on screen until it is ticked.
- [ ] Delete the playing clip: the app reports it and keeps the current
      wallpaper; tick another clip and rebuild.
- [ ] "Stop video" releases the file and "Start video" takes it back.
- [ ] Pause and Resume from the tray icon.

## Images wallpaper

- [ ] One image: the chosen picture stays on the desktop and the Windows
      wallpaper matches it.
- [ ] Slideshow: interval, picture crossfade (0 = instant) and Shuffle behave.
- [ ] Slider interval (5..300 seconds, 5-second steps) and the half-second crossfade steps.
- [ ] Add or remove a picture in the folder: picked up without a restart.
- [ ] Lock screen mode: frame, custom image and image wallpaper.
- [ ] Desktop wallpaper: frame versus custom image on the main screen; a
      per-screen fallback is config-only, with no tab in the settings window.

## Multi-monitor and DPI

- [ ] Extended: each extra screen mirrors the main clip or picture by default.
- [ ] Global Video + per-screen "Image mode": only that screen switches to
      pictures; the mirror checkbox is hidden in its Images section.
- [ ] Global Images + per-screen "Video mode": only that screen plays a cached
      video loop; no new loop is built while the main pill stays on Images.
- [ ] Independent 2nd screen: its own folder, playlist or single clip; its loop
      builds and is then reused from the cache.
- [ ] Untick "Use main Playback settings" on a second monitor: choose a
      different resolution, frame rate and quality, save, and confirm that only
      that monitor's loop is rebuilt and the main loop is untouched.
- [ ] Turn a mirror back on: the previously saved folder/list and render values
      return when it is turned off again.
- [ ] Duplicate mode: dimmed per-screen controls stay clickable by design.
      Folder and playlist values are not saved for a mirroring or disconnected
      screen; other fade and render values may be saved. Windows display mode
      and placement remain fully controllable via "Open Windows display
      settings...".
- [ ] Unplug and replug a screen: its settings are remembered by device name.
- [ ] Move the settings window between monitors at different DPI (100% / 125% /
      150%): it stays readable and nothing is clipped.
- [ ] Add or remove a monitor while running: the players and image layers move
      within a few seconds.
- [ ] With three and four screens: the 3rd and 4th Screen sub-tabs work end to
      end.

## Robustness

- [ ] Restart Explorer: the wallpaper re-attaches within a few seconds.
- [ ] Kill `LiveBackground.exe`: the watchdog restarts it (when the scheduled
      task is installed).
- [ ] Exit from the tray: the watchdog does NOT restart it.
- [ ] On battery: the background loop preparation is skipped.
- [ ] Sign out and back in: the wallpaper starts and the still frame shows first.
- [ ] Signature check on all first-party EXEs in the build folder: app, CLI,
      settings, tray, bootstrapper and installer. A missing signature fails the
      build.
- [ ] On a clean Windows 11 PC, note whether SmartScreen or Smart App Control
      evaluates the installer and record the result. A self-signed test build is
      expected to be treated as unknown publisher.

## Settings window and command line

- [ ] Exactly four main-row tabs; the Video/Images pill switches the first two
      and keeps unsaved edits.
- [ ] "Apply": settings are written, the loop rebuilds, the progress card
      appears bottom-right and fades away.
- [ ] Validation messages appear (no clips ticked; an unreadable clip).
- [ ] `LiveBackgroundCli.exe status | rebuild | stop | start`
- [ ] `LiveBackground.exe --settings | --pause | --resume | --stop`
