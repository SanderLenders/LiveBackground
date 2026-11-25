LiveBackground
==============

A small Windows 11 app that plays your own videos as a seamless animated
desktop wallpaper, or rotates your own pictures as a slideshow wallpaper. It
starts automatically at logon, sits below the desktop icons, and cross-fades
between clips so the loop has no visible seam.

The app is installed like a normal Windows program: the program itself lives in
Program Files and your own library (videos, images, cache, logs and backups)
lives in a separate folder in your user profile.


What you need
-------------
- Windows 11, 64-bit
- .NET Framework 4.8 (already part of Windows 11)
- Up to four monitors (the main screen plus up to three more; a fifth or
  later screen mirrors the main one)
- Video files stored on a local drive


Install
-------
1. Double-click LiveBackgroundSetup.exe. If Windows shows "Windows protected
   your PC" (SmartScreen), choose More info -> Run anyway.
2. Choose Install. Windows will ask for permission to install the program in
   Program Files; choose Yes.
3. Leave "Start LiveBackground when I sign in" ticked if you want the video at
   logon.
4. When the installer finishes, LiveBackground starts and builds its first loop
   in the background. It starts on the bundled "Galaxy HD Clip.mp4"; the bundled
   "Galaxy Image.jpg" and "Universe Clouds.jpg" are waiting in your image
   folder.

The installer places the program in:
    C:\Program Files\LiveBackground

It creates your library in:
    %USERPROFILE%\LiveBackground
        Videos\              your clips
        cache\               the rendered seamless loop and its still frame
        logs\                app.log, setup.log, mpv.log, mpv-<screen>.log
        images\wallpapers\   your image wallpapers
        images\              custom lock-screen and desktop wallpaper images
        backup\              the wallpaper and lock screen from before the install
        config.json          your settings

Your data is never written into Program Files, so the app can be updated or
removed without touching your videos.

To uninstall, use Settings -> Apps -> Installed apps -> LiveBackground, or the
"Uninstall LiveBackground" shortcut in the Start Menu. Uninstalling stops the
wallpaper, removes the logon task and restores the previous desktop wallpaper
and lock screen. Your videos and settings in %USERPROFILE%\LiveBackground are
left in place; delete that folder yourself if you want a clean slate.


The tray icon and settings
--------------------------
The wallpaper has no taskbar button. Look for a small LiveBackground icon in the
notification area; Windows 11 may keep it in the hidden-icons overflow
(the ^ arrow). You can drag it onto the taskbar to keep it visible.

Double-click the icon, or open "LiveBackground settings" from the Start Menu,
to change settings. At the top of the window, choose the wallpaper mode:
Video or Images. Click the pill switch itself or either word. Exactly four
main-row tabs are shown. The pill replaces the first two; Multi Monitor and
Other stay in place:

  Video mode:   Videos, Playback, Multi Monitor, Other
  Images mode:  Images, Slideshow, Multi Monitor, Other

  Videos/Images       folder, one item or playlist/slideshow, ticks, drop box,
                      Refresh, Open folder and Rebuild/Next image
  Playback/Slideshow  main/per-screen video crossfades, resolution, frame rate,
                      quality and screensaver; or picture interval, crossfade
                      and Shuffle
  Multi Monitor       extra screens: Extended/Duplicate mode and per-screen
                      video or image settings, with 2nd/3rd/4th Screen sub-tabs
  Other               start at sign-in, lock-screen and desktop wallpaper image

Every editor stays alive while the pill switches, so unsaved edits are kept.
When you open the settings window it starts on the first tab of the active
mode: Videos or Images.

Click "Save and apply". The settings are written, the loop rebuilds in the
background, and the current video keeps playing until the new loop is ready.

While the loop is being prepared, a small dark card fades in at the bottom-right
of the screen. It shows the percentage, what it is doing and an estimate of the
time left, then fades away when the new wallpaper is in place. It never takes
focus, so you can keep typing. The same card appears when you pick "Rebuild
now". Changing a setting while a build is still running cancels the old build
and starts on the new one immediately, so you never wait for a loop that is
already out of date. Encoding uses the "veryfast" preset, which is about twice
as fast as the old setting and produces no larger file.

Switching clips is instant once a loop has been rendered. Each finished loop is
kept in the cache, and in Single mode the app quietly prepares the loops for
your other clips about a minute after sign-in, at low priority, so the first
switch does not have to encode anything. That preparation is skipped while the
laptop is on battery. The cache keeps the current loop plus the six most recent
ones; older entries are removed automatically.

The window can be minimized to the taskbar. "Stop video" in the Videos tab
pauses playback and releases the video file, so you can delete or replace a
clip while the app keeps running. The desktop wallpaper stays visible; press
"Start video" to resume.

Right-click the tray icon for:
  Pause wallpaper / Resume wallpaper   stop or restart the wallpaper
  Rebuild now                          re-render the loop
  Settings...                          open the settings window
  Open video folder                    show your clips in Explorer
  Open image folder                    show your image wallpapers in Explorer
  Open logs folder                     show the log files
  Exit                                 stop the wallpaper completely

The cross-fade slider runs from 0.5 to 10 seconds in 0.5-second steps. Tick
"Allow half-second steps" only changes the step granularity; the range is the
same. The window shows the fade that will actually be applied and the resulting
loop length.

Fades are capped at one third of the shorter neighbouring clip, so a 10 s
setting becomes 3.3 s on a 10 s clip. Fades longer than 5 seconds look more
like a dissolve than a loop, so use them only when you want that effect.


Your videos and the playlist
----------------------------
The video folder is:
    %USERPROFILE%\LiveBackground\Videos

Drop video files into that folder, or drag them onto the dashed drop box in the
Videos tab. Clicking the drop box opens a file picker. Videos are copied into
the folder shown in the "Video folder" box. Existing files are never
overwritten: dropping a second "space.mp4" creates "space (2).mp4".

Supported formats:
    Any video container that the bundled player can decode. The extension does
    not have to be on a list: each file is checked by content, so MP4, MKV, MOV,
    AVI, WebM, WMV, MPEG, TS, FLV, OGV, 3GP, VOB, MXF, DV and unusual containers
    all work, whatever they are called. Files with no video track (music, notes,
    pictures, half-finished downloads) are ignored instead of breaking the loop.

The Videos tab has two playback modes:

  Playlist   Only the clips with a tick are played, in the order shown in the
             list. Use Move up and Move down to set the order. Sort A-Z only
             re-sorts the list; it does not change which clips are ticked.
  Single     Play one chosen clip on repeat. The same list is used: click the
             checkbox of the clip you want. Ticking another clip moves the tick
             to that clip, so only one clip is ever selected, and clicking the
             ticked clip again leaves it selected.

A newly dropped or newly found clip appears in the list unticked, so it stays
out of the show until you tick it. This is deliberate: dropping a file never
changes what is currently on your desktop.

If every clip is unticked, there is nothing to play: the app reports "No clips
are selected in the playlist" and keeps the current wallpaper on screen. Tick
at least one clip to build a new loop. (The same happens if every ticked clip
has since been deleted from the folder.)

The first time the app runs it adds the bundled clips and pictures to the
folders. After that, the folders are yours: add, replace or delete videos.
The bundled pictures are refreshed if an update ships new ones; pictures you
add yourself are never touched.

If Windows says a video is in use, click "Stop video" in the Videos tab first.
That stops the player and releases the file; "Start video" brings the wallpaper
back. Deleting a clip while a rebuild is still running can also fail for a few
seconds, so wait until the progress line in logs\app.log says the rebuild is
done.

When the folder or the playlist changes, the app waits a few seconds and then
rebuilds the loop in the background. The current video keeps playing while the
new one is rendered. Rendering takes roughly 10-60 seconds per minute of video
on a typical PC; a progress line is written to:
    %USERPROFILE%\LiveBackground\logs\app.log

Once a clip has been rendered, switching to it again reuses the cached loop and
takes well under a second. If a video is shorter than 1 second it is skipped. If
a file cannot be read it is skipped too, and the other clips still play; the
build only fails when no usable clip is left.

The cross-fade is 3 seconds by default and can be changed in the settings
window. It is automatically shortened for short clips (at most one third of
either neighbouring clip), so a 5-second clip does not turn into a blur.


Multiple monitors
-----------------
LiveBackground supports up to four monitors. The Multi Monitor tab has 2nd,
3rd and 4th Screen sub-tabs for connected extra screens. A fifth or later
monitor mirrors the main screen. Settings are remembered by monitor device
name, so unplugging, replugging and reordering screens does not mix them up. A
disconnected screen keeps its saved settings.

The top of the tab shows the Windows display mode: Extended (separate screens)
or Duplicate (every screen shows the same). The radio buttons show what Windows
is doing right now; click one to change it. The app never changes the display
mode on its own and never spoofs or blocks it. "Display settings" opens the
Windows arrangement page, where display placement stays fully controllable.
Controls that are not currently usable are dimmed but remain clickable by
design. Values are saved only for a connected screen that is not mirroring the
main screen; a disconnected slot or a section still set to "Same as main
screen" has nowhere to store those edits and reloads the saved values later.

The tab follows the Video/Images pill, but each screen can override it. Under
global Video, the checkbox reads "Image mode" and flips only that screen to
pictures; under global Images it reads "Video mode" and flips only that screen
to its own video loop. The mirror checkbox appears only in the section that
follows the main pill and is always labelled "Same as main screen". The
opposite-mode section is an explicit per-screen override: on first use it is
seeded from the main settings, then it keeps its own folder and list. Turning
a mirror back on restores the values saved for that section.

In Video mode, each extra screen mirrors the main clip by default. Turn off
"Same as main screen" in the Video section to choose its own folder, playlist
or single clip and pick which clip plays in Single clip mode; its loop is
rendered once and cached. Rebuild now is available whenever that screen is
attached.

In Images mode, each extra screen likewise mirrors the main picture or
slideshow by default. Turn off "Same as main screen" in the Images section to
give it an independent image folder, one image or slideshow, pick which image is
used in One image mode, and set interval (5-300 seconds), picture crossfade and
Shuffle. Each independent slideshow has its own folder watcher and timer.

Every per-screen Playback tab offers "Use main Playback settings", ticked by
default. Untick it to set that monitor's resolution, frame rate and quality;
the monitor's native resolution is offered when Windows reports it. Those
values are baked into the screen's own cached loop, so a change builds a new
signature-keyed loop in the background while the current one keeps playing.
Mirroring screens share the main loop, and the x264 speed preset stays global.

Each per-screen Playback tab has a video crossfade control, but there is no
per-screen desktop-fallback tab in the settings window. An independent screen
can use the first frame of its own loop or a custom fallback image while its
video is paused, stopped or being built; those per-screen choices are
config-only and can be hand-edited as "desktopWallpaperMode" and
"desktopWallpaperImage" on that screen's entry. Mirroring screens use the main
fallback.

Adding, removing or moving a monitor while the app runs is picked up within a
couple of seconds; the players and image layers move to the right screens.


Image wallpapers and the slideshow
----------------------------------
Switch the main wallpaper mode at the top of the settings window to Images.
The main video stops completely: its mpv player is shut down and no main loop
is built or prepared, so the app stays light while pictures are shown. A screen
you explicitly flipped to "Video mode" may still play its already cached loop,
but global Images never starts a new encode. Switching the main pill back to
Video restarts the cached loop without rebuilding it.

The image folder is:
    %USERPROFILE%\LiveBackground\images\wallpapers

Drop pictures into that folder, or drag them onto the dashed drop box on the
Images tab. Clicking the box opens a file picker. Pictures are copied into the
folder shown in the "Image folder" box. JPG, PNG, BMP, GIF and WEBP are
supported. Existing files are never overwritten.

The Images tab has two modes:

  Slideshow  Only the pictures with a tick are shown, in the order of the
             list. Use Move up and Move down to set the order. Sort A-Z only
             re-sorts the list and does not change which pictures are ticked.
             Tick "Shuffle the order" on the Slideshow tab to pick a random
             picture each time.
  One image  One chosen picture stays on the desktop. Click the checkbox of
             the picture you want; ticking another picture switches to it.
             The ticked picture cannot be unticked by clicking it again.

New pictures start ticked, so a picture you add joins the slideshow straight
away. Pictures you untick are remembered and skipped.

The slideshow interval is chosen on the Slideshow tab with a slider from 5 to
300 seconds in 5-second steps; it starts at 10 seconds. A hand-edited
config.json can still set 1 to 86400 seconds. The "Crossfade" slider controls
picture transitions: 0 = off (instant), up to 5 seconds in 1-second steps. Tick
"Allow half-second steps" for 0.5-second steps. The crossfade is drawn below the
icons and only runs while a picture is changing.

The rotation is done by LiveBackground itself, not by the Windows slideshow
feature, so the ticks, the custom order and the short intervals work exactly as
shown. Every new picture is also saved as the normal Windows wallpaper, so the
last picture stays on the desktop when the app exits or if it stops
unexpectedly. The picture follows the Windows wallpaper fit setting (Fill, Fit,
Stretch, Center, Tile or Span) without changing that setting.

A small watchdog task checks every few minutes that the wallpaper is still
running and starts it again after a crash. It does not restart the app when you
exit it from the tray. If you chose the Startup-folder fallback instead of the
scheduled tasks, the watchdog is not installed.

While Images mode is active, the desktop-wallpaper options on the Other tab
are switched off, because the picture rotation owns the desktop. The image
folder is watched, so adding or removing a picture takes effect without a
restart.


What appears where
------------------
- Desktop video: the seamless loop, playing below the desktop icons.
- Desktop wallpaper: frame 0 of the loop, or a custom image you choose. At
  logon Windows shows this still image first, then the video takes over within
  about half a second, so there is no visible cut. An independent extra screen
  can instead use the first frame of its own loop or its own custom image.
- Images mode: the selected picture, or the ticked pictures in slideshow order.
  No video is running.
- Lock screen: the first frame of the video loop, a custom image you pick, or
  one of your image wallpapers. Windows has no public API for a synced
  lock-screen slideshow, so the lock screen is always one still picture.
- Power: the video does not keep the display awake by default. The Playback tab
  has three mutually exclusive screensaver choices: "No screensaver" (the app
  does nothing after idle), "Screensaver Dim and Lock" (minimizes windows and
  locks the workstation after the configured idle delay), and "Hide taskbar and
  desktop icons after idle" (keeps the video visible and hides the shell after
  the configured idle delay). The delay slider is shared by the idle actions.
  Any mouse move or key press restores the desktop. The app does not suppress
  normal Windows screen/power behavior unless you choose an idle action.

To use a custom desktop wallpaper, open the Other tab and choose "Use a
custom image" under "Desktop wallpaper image". To use a custom lock-screen
image, choose "Use a custom image" under "Lock screen". Both images are copied
into %USERPROFILE%\LiveBackground\images, so moving the original file later
does not break the setting.

To use one of your image wallpapers on the lock screen, choose "Use an image
from my image wallpapers" on the Other tab and pick a picture from the list.
That picture is read from the image folder, so it changes if you replace or
rename the file.

If a custom image is deleted from the library, the app falls back to the first
frame of the loop for the desktop, and leaves the lock screen unchanged.


Commands
--------
Run these from the install folder, or use the full path:
    "C:\Program Files\LiveBackground\LiveBackgroundCli.exe"

    install                    prepare the library and start (used by install.cmd)
    uninstall                  stop and remove from logon, restore backups
    uninstall --remove         same, and clear cache, logs and backups;
                               your videos, pictures and settings are kept
    rebuild                    rebuild the loop now
    build-only                 build the loop without restarting playback
    start                      start the wallpaper
    stop                       stop the wallpaper
    status                     show videos, loop, task and backup state
    set-lockscreen <image>     use a specific lock-screen image
    restore-lockscreen         restore the saved lock-screen image
    install-task               create the logon task
    remove-task                remove the logon task

The wallpaper itself also accepts a few flags, mainly for scripts and
shortcuts:
    LiveBackground.exe --settings   open the settings window
    LiveBackground.exe --pause      pause the video (desktop wallpaper shows)
    LiveBackground.exe --resume     resume the video
    LiveBackground.exe --rebuild    rebuild the loop now
    LiveBackground.exe --stop       stop the wallpaper

These write a small flag file that the running app picks up within half a
second. If the app is not running, --settings starts it and opens the window.


Changing the settings by hand
-----------------------------
Use the settings window for normal changes (tray icon -> Settings..., or the
"LiveBackground settings" Start Menu shortcut). The app applies them live and
rebuilds the loop in the background.

For advanced options, edit:
    %USERPROFILE%\LiveBackground\config.json

Useful fields:
    wallpaperMode      "video" or "images"
    videoFolder        folder to scan (default %USERPROFILE%\LiveBackground\Videos)
    playbackMode       "playlist" or "single"
    singleClip         file name used in single mode
    playlist           ordered list of {name, enabled} entries
    imageFolder        image wallpapers (default %USERPROFILE%\LiveBackground\images\wallpapers)
    imageMode          "single" or "slideshow"
    imageSingle        file name used when imageMode is "single"
    imagePlaylist      ordered list of {name, enabled} entries for images
    imageIntervalSeconds  seconds between slideshow pictures (default 10)
    imageCrossfadeSeconds picture crossfade, 0 = off, 0.5 to 5 (default 2)
    imageFineCrossfadeSteps true to allow 0.5-second steps for pictures
    imageShuffle       true to pick a random picture each time
    crossfadeSeconds   cross-fade length, 0.5 to 10 (default 3)
    fineCrossfadeSteps true to allow 0.5-second steps in the settings window
    renderWidth        loop width  (default 1920)
    renderHeight       loop height (default 1080)
    fps                loop frame rate (default 30)
    crf                x264 quality, lower is better/bigger (default 18)
    preset             x264 speed preset (default "veryfast")
    lockScreenMode     "frame", "custom" or "image"
    lockScreenImage    path used when lockScreenMode is "custom" or "image"
    desktopWallpaper   turn the still desktop wallpaper on/off (default true)
    desktopWallpaperMode  "frame" (first loop frame) or "custom"
    desktopWallpaperImage path used when desktopWallpaperMode is "custom"
    screensaverActive  dim-and-lock idle action (default false)
    screensaverVideo   hide taskbar and desktop icons after idle
                       (default true)
    screensaverHideSeconds  idle delay in seconds for the selected
                       screensaver action (default 300)
    extraScreens       per-monitor settings keyed by device name: video and
                       image mirror switches (both default true), folders,
                       playlists, optional video crossfade, slideshow settings,
                       desktop fallback mode/image and optional renderWidth,
                       renderHeight, fps and crf overrides (omitted values
                       inherit the main Playback settings; preset stays global)
    secondScreen*      older flat second-screen fields, migrated into
                       extraScreens automatically
    taskName           scheduled-task name

If you edited config.json by hand, restart the app so it re-reads the file:
    LiveBackgroundCli.exe stop
    LiveBackgroundCli.exe start
Or run:
    LiveBackgroundCli.exe rebuild
    LiveBackgroundCli.exe start


Logs and troubleshooting
------------------------
Logs are in:
    %USERPROFILE%\LiveBackground\logs\app.log
    %USERPROFILE%\LiveBackground\logs\setup.log
    %USERPROFILE%\LiveBackground\logs\mpv.log
    %USERPROFILE%\LiveBackground\logs\mpv-____DISPLAY2.log
                         (an extra screen's player; name follows the device)

Nothing appears on the desktop:
1. Run LiveBackgroundCli.exe status and check "app running" and "mpv running".
2. Make sure at least one playable video is ticked in the Videos tab (a clip
   marked "unreadable" is skipped).
3. Run LiveBackgroundCli.exe rebuild.
4. Restart Explorer (Task Manager -> Windows Explorer -> Restart) and wait a
   few seconds; the app re-attaches automatically.

Nothing appears in Images mode:
1. Check that at least one picture is ticked on the Images tab.
2. Check that the image folder contains a supported image; press Refresh.
3. Look at the last lines of logs\app.log for "no images in ..." or
   "desktop image: ...".

The desktop is black or the icons disappeared:
- The app only repositions its own video window. If something else changed the
  desktop layers, restart Explorer; the watchdog will re-attach.

The video stutters:
- Use a smaller resolution or lower frame rate on the main Playback tab, or on
  the affected monitor's Playback tab after unticking "Use main Playback
  settings". The new loop builds in the background.
- Hardware decoding is enabled by default. Very high-bitrate 4K clips may still
  be heavy on older GPUs.

A rebuild says "ffmpeg failed":
- Check logs\app.log for the last ffmpeg message. A damaged or unusual file is
  the usual cause. Remove it from the folder and the app rebuilds without it.


Code signing and Smart App Control
----------------------------------
The current build signs all first-party executables (the app, CLI, settings
window, tray helper, bootstrapper and installer) with a local self-signed test
certificate, SHA-256 and no RFC3161 timestamp. mpv and ffmpeg stay exactly as
published by their projects and are deliberately not re-signed. A self-signed
certificate does not create a trusted publisher or SmartScreen reputation, so
Windows may still show "unknown publisher".

A public release needs a publicly trusted OV/EV code-signing certificate and a
timestamp server. That is the remaining reputation step; it does not change how
the app behaves or how quickly it starts.

On a normal PC, SmartScreen may show "Windows protected your PC" the first
time you run the installer:
    More info -> Run anyway
The prompt stops appearing once the file has a clean download history.

Windows 11 Smart App Control (SAC) is stricter, and nothing in this app or
its installer can change how it behaves:

- SAC judges every executable file separately, by exact content. A newly
  built exe gets its own judgment; once a particular file is blocked, running
  it again keeps failing. A rebuild is a new file and gets a new judgment.
- SAC does not honor self-signed certificates, and an unsigned file carries no
  signature to check at all, so neither one can unblock a file SAC has decided
  to block.
- SAC shows no "Run anyway" choice; the block is silent. Most PCs have SAC
  off or in evaluation mode, so on other machines the SmartScreen prompt
  above is all you normally see.

If SAC blocks this build, the options are: rebuild the app (new judgment),
sign it with a publicly trusted code-signing certificate, publish through the
Microsoft Store, or turn SAC off in Windows Security -> App & browser
control -> Smart App Control. Turning SAC off is permanent until Windows is
reinstalled, so weigh that carefully.

For wide distribution, the first-party executables need a publicly trusted
signature. The bundled third-party mpv.exe and ffmpeg are judged separately;
they must come from a trusted source with a stable hash, or be accepted through
Microsoft Store distribution. Either route removes the "unknown publisher"
warning and lets SmartScreen reputation build over time.

This app is not affiliated with Microsoft.


Windows Defender and startup speed
----------------------------------
Windows Defender scans the bundled mpv.exe the first time it runs after a boot.
That scan is normal and is the main reason the video can take a few seconds to
appear at logon. LiveBackground deliberately does not add Defender exclusions
automatically: exclusions need administrator rights, weaken the PC's security,
and are often blocked by company policy. An app that silently adds them also
looks like malware to many users.

If you trust this app and want the fastest possible logon on your own PC, add a
manual exclusion for the LiveBackground folder in Windows Security -> Virus &
threat protection -> Manage settings -> Exclusions. Do not add exclusions on a
PC you do not control.


Portable mode
-------------
A folder containing portable.txt next to LiveBackground.exe is a portable copy.
In portable mode all data stays inside that folder instead of the user profile.
The installer does not create portable.txt, so an installed copy always uses
%USERPROFILE%\LiveBackground. install.cmd copies a portable folder there;
uninstall.cmd removes that installed copy and falls back to the folder it is
run from when no installed copy is found.

The portable package still includes install.cmd and uninstall.cmd. install.cmd
copies the folder to %LOCALAPPDATA%\LiveBackground and creates the logon task
without needing Program Files. This is the advanced path; the normal way to
install is LiveBackgroundSetup.exe.


Third-party software
--------------------
This folder bundles mpv and FFmpeg. Both are free software under the GNU GPL.
See THIRD-PARTY.txt and assets\licenses\ for versions, licenses and source
links. The bundled clips and pictures are not part of the software.


Known limitations
-----------------
- Up to four monitors are supported; a fifth or later monitor mirrors the main
  screen. The extra-screen path is designed to stay inert with two monitors,
  but independent 3rd and 4th screen settings have not been tested on real
  three- and four-monitor hardware.
- Windows 11 may put the tray icon in the hidden-icons overflow. Drag it onto
  the taskbar if you want it always visible.
- The video must be a normal local file; DRM-protected content will not play.
- The lock screen stays a still image, not video and not a synced slideshow.
- Rebuilding uses ffmpeg and can take a while for long or high-resolution clips.
