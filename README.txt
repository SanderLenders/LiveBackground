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
- One or two monitors (the main screen plus one extra)
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
   "Galaxy Image.jpg" is waiting in your image folder.

The installer places the program in:
    C:\Program Files\LiveBackground

It creates your library in:
    %USERPROFILE%\LiveBackground
        Videos\              your clips
        cache\               the rendered seamless loop and its still frame
        logs\                app.log, setup.log, mpv.log, mpv2.log
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
Video or Images. Click the pill switch itself or either word; the pill also
responds to Space or Enter when focused. The tabs follow the mode, in this
fixed order:

  Videos     video folder, playlist with checkboxes, Move up/down, drop box,
             Refresh, Open folder, Rebuild now and Stop video   (Video mode)
  Second     the second screen: Extended/Duplicate mode, mirror the main clip,
  screen     or give it its own folder, playlist and clip   (Video mode, two monitors)
  Playback   cross-fade length, resolution, frame rate, quality and screensaver
             (Video mode)
  Images     image folder, one-image or slideshow mode, ticks, Move up/down,
             drop box, Refresh, Open folder and Next image   (Images mode)
  Slideshow  preset interval slider, optional custom seconds, crossfade and
             Shuffle   (Images mode)
  Windows    start at sign-in, lock-screen image, desktop wallpaper image
             (shared by both modes)

The tabs that belong to the other mode are greyed and cannot be selected. When
you open the settings window it starts on the first tab of the active mode:
Videos or Images.

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

The cross-fade slider runs from 1 to 10 seconds in 1-second steps. Tick
"Allow half-second steps" to use 0.5-second steps. The window shows
the fade that will actually be applied and the resulting loop length.

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

If every clip is unticked, the app falls back to playing every clip in the
folder in filename order, so an empty playlist is never a black desktop.

The first time the app runs it adds the bundled clips and picture to the
folders. After that, the folders are yours: add, replace or delete videos.

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


The second screen
-----------------
If two monitors are connected, LiveBackground covers both with the wallpaper.
The Second screen tab controls what the extra monitor shows; it is greyed out
when only one monitor is attached.

The top of the tab shows the Windows display mode: Extended (two separate
screens) or Duplicate (both screens show the same). The radio buttons show what
Windows is doing right now; click one to change it. The app never changes the
display mode on its own. The "Display settings" button opens the Windows display
settings, where the screens can be arranged. While Windows is duplicating, the
second-screen loop controls are disabled with an explanation, because both
screens show the same picture anyway.

The tab also keeps up with changes: while the settings window is open it checks
the display layout about every 1.5 seconds, so plugging in a screen or changing
the Windows mode enables or greys the tab without pressing Save first.

By default the second screen shows the same clip as the main screen. The same
loop is cropped to fit each monitor, so a wide clip fills a taller or narrower
second panel without stretching, and only one loop is ever rendered.

Turn off "Use the same video as the main screen" to give the second screen its
own video. Its folder, playlist (with the same ticks, Move up/down and Sort A-Z
controls as the Videos tab) and single clip are chosen on the Second screen
tab. Press Rebuild now to render that loop; it is rendered once and cached, so
later restarts reuse it. The two screens then play independently. Rebuild now is
available whenever a second screen is attached, including while the screens are
mirroring.

Adding, removing or moving a monitor while the app runs is picked up within a
couple of seconds; the players move to the right screen and keep playing. A
third monitor is ignored: only the main screen and one extra are used.


Image wallpapers and the slideshow
----------------------------------
Switch the wallpaper mode at the top of the settings window to Images. The
video stops completely: mpv is shut down and no loop is built or prepared, so
the app stays light while pictures are shown. Switching back to Video starts
the cached loop again without rebuilding it.

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

The slideshow interval is chosen on the Slideshow tab with a preset slider
(5, 10, 15, 20, 30 or 60 seconds). Tick "Custom" to enter any number of seconds
from 1 to 86400. The "Crossfade" slider controls picture transitions: 0 = off
(instant), 1 to 5 seconds in 1-second steps. Tick "Allow half-second steps" for
0.5-second steps. The crossfade is drawn below the icons and only runs while a
picture is changing.

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
  about half a second, so there is no visible cut.
- Images mode: the selected picture, or the ticked pictures in slideshow order.
  No video is running.
- Lock screen: the first frame of the video loop, a custom image you pick, or
  one of your image wallpapers. Windows has no public API for a synced
  lock-screen slideshow, so the lock screen is always one still picture.
- Power: the video does not keep the display awake by default. The screen dims,
  turns off and locks according to your Windows power settings, exactly as it
  did before the app was installed. You can change this with "Screensaver
  active" on the Playback tab; turning it off keeps the screen awake while the
  video plays.

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
    uninstall --remove         same, and delete the app data folder
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
    imageIntervalSeconds  seconds between slideshow pictures (default 5)
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
    screensaverActive  let Windows dim/lock the screen (default true)
    secondScreenSameAsMain true: second screen mirrors the main clip (default true)
    secondScreenFolder folder used when the second screen plays its own video
    secondScreenPlaybackMode "playlist" or "single" for the second screen
    secondScreenSingleClip file name used for the second screen in single mode
    secondScreenPlaylist ordered {name, enabled} entries for the second screen
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
    %USERPROFILE%\LiveBackground\logs\mpv2.log   (the second screen's player)

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
- Use a smaller renderWidth/renderHeight or a faster preset, then rebuild.
- Hardware decoding is enabled by default. Very high-bitrate 4K clips may still
  be heavy on older GPUs.

A rebuild says "ffmpeg failed":
- Check logs\app.log for the last ffmpeg message. A damaged or unusual file is
  the usual cause. Remove it from the folder and the app rebuilds without it.


Smart App Control and code signing
----------------------------------
LiveBackground.exe, LiveBackgroundCli.exe and LiveBackgroundSetup.exe are signed
with a self-signed certificate named "LiveBackground Test". The certificate is
included next to the app as LiveBackground.cer.

This is NOT a publicly trusted signature. It only tells Windows that the files
belong together. It does not make the app trusted by Microsoft.

On a normal PC, SmartScreen may show "Windows protected your PC" the first
time you run the installer:
    More info -> Run anyway
The prompt stops appearing once the file has a clean download history.

Windows 11 Smart App Control (SAC) is stricter, and nothing in this app or
its installer can change how it behaves:

- SAC judges every executable file separately, by exact content. A newly
  built exe gets its own judgment; once a particular file is blocked, running
  it again keeps failing. A rebuild is a new file and gets a new judgment.
- SAC does not honor self-signed certificates. Adding LiveBackground.cer to
  your own trusted stores does not unblock a blocked file (tested).
- SAC shows no "Run anyway" choice; the block is silent. Most PCs have SAC
  off or in evaluation mode, so on other machines the SmartScreen prompt
  above is all you normally see.

If SAC blocks this build, the options are: rebuild the app (new judgment),
sign with a publicly trusted code-signing certificate, publish through the
Microsoft Store, or turn SAC off in Windows Security -> App & browser
control -> Smart App Control. Turning SAC off is permanent until Windows is
reinstalled, so weigh that carefully.

For wide distribution, sign all shipped files (including the bundled,
unsigned mpv.exe and ffmpeg) with a publicly trusted OV code-signing
certificate, or publish the app through the Microsoft Store. That removes
the "unknown publisher" warning and lets SmartScreen reputation build over
time.

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
%USERPROFILE%\LiveBackground.

The portable package still includes install.cmd and uninstall.cmd. install.cmd
copies the folder to %LOCALAPPDATA%\LiveBackground and creates the logon task
without needing Program Files. This is the advanced path; the normal way to
install is LiveBackgroundSetup.exe.


Third-party software
--------------------
This folder bundles mpv and FFmpeg. Both are free software under the GNU GPL.
See THIRD-PARTY.txt and assets\licenses\ for versions, licenses and source
links. The bundled clips and picture are not part of the software.


Known limitations
-----------------
- One or two monitors. A third monitor is ignored. The two-screen feature has
  been tested with real two-monitor hardware; the newer Extended/Duplicate
  switching on the Second screen tab (see "The second screen") is still new.
- Windows 11 may put the tray icon in the hidden-icons overflow. Drag it onto
  the taskbar if you want it always visible.
- The video must be a normal local file; DRM-protected content will not play.
- The lock screen stays a still image, not video and not a synced slideshow.
- Rebuilding uses ffmpeg and can take a while for long or high-resolution clips.
