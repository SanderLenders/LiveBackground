@echo off
setlocal EnableExtensions
title LiveBackground - install

set "SRC=%~dp0"
if "%SRC:~-1%"=="\" set "SRC=%SRC:~0,-1%"
set "DEST=%LOCALAPPDATA%\LiveBackground"

echo ==============================================
echo   LiveBackground - install
echo ==============================================
echo.

rem ---------------------------------------------------------------
rem Smart App Control (Windows 11) blocks new unsigned executables.
rem The setup tool is signed with a self-signed certificate; trusting
rem that certificate for the current user lets it run. This is a
rem per-user change and needs no administrator rights.
rem ---------------------------------------------------------------
if exist "%SRC%LiveBackground.cer" (
    echo Preparing the certificate...
    certutil -user -addstore -f TrustedPublisher "%SRC%LiveBackground.cer" >nul 2>&1
    if errorlevel 1 (
        echo Note: the certificate could not be trusted automatically.
        echo       If Windows blocks the setup tool, see README.txt.
    )
)
echo.

if /I "%SRC%"=="%DEST%" goto run

rem ---------------------------------------------------------------
rem Copy the portable folder to %LOCALAPPDATA%\LiveBackground.
rem Existing config, cache, logs and backups are preserved.
rem ---------------------------------------------------------------
echo Copying files to "%DEST%" ...
if not exist "%DEST%" mkdir "%DEST%" >nul 2>&1
robocopy "%SRC%" "%DEST%" /E /XF config.json install-state.json /XD cache logs backup images installer /NFL /NDL /NJH /NJS /NP >nul
if errorlevel 8 (
    echo.
    echo ERROR: copying the files failed.
    echo Close any program that is using the folder and try again.
    echo.
    pause
    exit /b 1
)

:run
pushd "%DEST%"
echo.
"%DEST%\LiveBackgroundCli.exe" install
set "RC=%ERRORLEVEL%"
popd

echo.
if "%RC%"=="0" (
    echo Installation finished.
    tasklist /FI "IMAGENAME eq LiveBackground.exe" 2>nul | find /I "LiveBackground.exe" >nul
    if errorlevel 1 (
        echo.
        echo NOTE: the wallpaper is not running yet.
        echo If Windows showed a Smart App Control message, choose Allow,
        echo then run install.cmd again.
    ) else (
        echo The wallpaper is running. Right-click its tray icon for settings,
        echo or open "LiveBackground settings" from the Start Menu.
    )
) else (
    echo Installation failed with exit code %RC%.
    echo See "%DEST%\logs\setup.log" for details.
)
echo.
pause
exit /b %RC%
