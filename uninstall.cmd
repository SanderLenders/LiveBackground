@echo off
setlocal EnableExtensions
title LiveBackground - uninstall

set "DEST=%LOCALAPPDATA%\LiveBackground"

echo ==============================================
echo   LiveBackground - uninstall
echo ==============================================
echo.
echo This stops the video wallpaper, removes it from logon,
echo restores your previous desktop wallpaper and lock screen.
echo Your videos, pictures and settings are kept.
echo.

if not exist "%DEST%\config.json" (
    if not exist "%DEST%\LiveBackgroundCli.exe" (
        echo LiveBackground is not installed in:
        echo   %DEST%
        echo.
        pause
        exit /b 1
    )
)

set "CLI=%DEST%\LiveBackgroundCli.exe"
if not exist "%CLI%" set "CLI=%~dp0LiveBackgroundCli.exe"

pushd "%DEST%" 2>nul || pushd "%~dp0"
"%CLI%" uninstall --remove
set "RC=%ERRORLEVEL%"
popd

echo.
if "%RC%"=="0" (
    echo Uninstall finished. The app's cache, logs and backups will be
    echo removed in a moment. Your videos, pictures and settings in
    echo   %DEST%
    echo are kept; delete that folder yourself for a clean slate.
) else (
    echo Uninstall failed with exit code %RC%.
    echo See "%DEST%\logs\setup.log" for details.
)
echo.
pause
exit /b %RC%
