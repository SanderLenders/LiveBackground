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

if /I "%SRC%"=="%DEST%" goto run

rem ---------------------------------------------------------------
rem Copy the portable folder to %LOCALAPPDATA%\LiveBackground.
rem Existing config, cache, logs and backups are preserved.
rem ---------------------------------------------------------------
echo Copying files to "%DEST%" ...
if not exist "%DEST%" mkdir "%DEST%" >nul 2>&1

rem The main copy excludes images because that folder becomes the user's
rem library. Seed it with the bundled pictures first, without replacing
rem anything the user already has there.
if exist "%SRC%\images\wallpapers" (
    if not exist "%DEST%\images\wallpapers" mkdir "%DEST%\images\wallpapers" >nul 2>&1
    for %%F in ("%SRC%\images\wallpapers\*.*") do if not exist "%DEST%\images\wallpapers\%%~nxF" copy /Y "%%~fF" "%DEST%\images\wallpapers\" >nul
)

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
        echo If Windows showed a SmartScreen prompt, choose More info
        echo then Run anyway, and run install.cmd again.
        echo If Smart App Control blocked it, see README.txt.
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
