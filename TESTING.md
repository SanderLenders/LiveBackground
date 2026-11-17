# Manual smoke test

The unit tests cover the pure logic, but the parts that touch Windows - the
window and threading model, multi-monitor, DPI, the mpv and ffmpeg lifecycles -
can only be checked on a real desktop. Run this list before publishing a build.
It is deliberately short: the highest-value manual pass, not a full QA suite.

## Before you start

- Build the installer (see `BUILDING.md`).
- Use a real Windows 11 PC, with a second monitor if you have one.
- Keep `%USERPROFILE%\LiveBackground\logs\app.log` open. It should stay free of
  unexpected `ERROR` lines.

## Install, upgrade, uninstall

- [ ] Fresh install: setup runs; "Start LiveBackground when I sign in" is ticked
      by default; the Start Menu shortcut is created and the desktop shortcut is
      not.
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
- [ ] Custom interval (1..86400) and the half-second crossfade steps.
- [ ] Add or remove a picture in the folder: picked up without a restart.
- [ ] Lock screen mode: frame, custom image and image wallpaper.
- [ ] Desktop wallpaper: frame versus custom image, on the main and extra
      screens.

## Multi-monitor and DPI

- [ ] Extended: each extra screen mirrors the main clip or picture by default.
- [ ] Independent 2nd screen: its own folder, playlist or single clip; its loop
      builds and is then reused from the cache.
- [ ] Duplicate mode: the per-screen controls are disabled with an explanation.
- [ ] Unplug and replug a screen: its settings are remembered by device name.
- [ ] Move the settings window between monitors at different DPI (100% / 125% /
      150%): it stays readable and nothing is clipped.
- [ ] Add or remove a monitor while running: the players and image layers move
      within a few seconds.
- [ ] With three screens: the 3rd Screen sub-tab works end to end.

## Robustness

- [ ] Restart Explorer: the wallpaper re-attaches within a few seconds.
- [ ] Kill `LiveBackground.exe`: the watchdog restarts it (when the scheduled
      task is installed).
- [ ] Exit from the tray: the watchdog does NOT restart it.
- [ ] On battery: the background loop preparation is skipped.
- [ ] Sign out and back in: the wallpaper starts and the still frame shows first.

## Settings window and command line

- [ ] Exactly four main-row tabs; the Video/Images pill switches the first two
      and keeps unsaved edits.
- [ ] "Save and apply": settings are written, the loop rebuilds, the progress
      card appears bottom-right and fades away.
- [ ] Validation messages appear (no clips ticked; an unreadable clip).
- [ ] `LiveBackgroundCli.exe status | rebuild | stop | start`
- [ ] `LiveBackground.exe --settings | --pause | --resume | --stop`
