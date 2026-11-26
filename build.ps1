#requires -Version 3.0
<#
    Builds LiveBackground.

    Output (default: %LOCALAPPDATA%\LiveBackgroundBuild):
        LiveBackground.exe        video-wallpaper host (no console window)
        LiveBackgroundCli.exe     command-line helper (install / rebuild / task)
        LiveBackground.cer        public self-signed code-signing certificate
        portable.txt              marks this folder as a portable copy
        install.cmd, uninstall.cmd
        README.txt, THIRD-PARTY.txt
        mpv\                      bundled mpv player
        tools\ffmpeg\             bundled ffmpeg + ffprobe
        videos\                   bundled clips, seeded on first install
        images\wallpapers\        bundled picture, seeded on first install
        assets\licenses\          license texts
        installer\
            LiveBackgroundSetup.exe   official Windows installer (Inno Setup)

    Compiling:
        Uses dotnet build / MSBuild (net48) because the settings UI is WPF and
        requires XAML compilation. The old csc/Roslyn fallback cannot build WPF
        XAML and is no longer supported. Install the .NET SDK (or Visual Studio
        Build Tools with .NET desktop build tools) to build from source.

    Run from a normal PowerShell window; build into the local build folder:
        .\build.ps1 -OutDir "$env:LOCALAPPDATA\LiveBackgroundBuild" `
                    -MpvSource "C:\Path\To\mpv" `
                    -FfmpegSource "C:\Path\To\tools\ffmpeg"

    If the self-signed certificate does not exist yet, create it once with:
        $c = New-SelfSignedCertificate -Type CodeSigningCert `
             -Subject 'CN=LiveBackground Test' `
             -CertStoreLocation Cert:\CurrentUser\My -HashAlgorithm SHA256 -KeyLength 2048

    Export the public certificate and trust it locally to validate test builds:
        Export-Certificate -Cert $c -FilePath "$env:TEMP\LiveBackgroundTest.cer" -Type CERT
        Import-Certificate -FilePath "$env:TEMP\LiveBackgroundTest.cer" `
                           -CertStoreLocation Cert:\CurrentUser\Root
    This is local test trust only; it does not give the signature any public
    reputation. Signatures are not timestamped by default (test certificate).
    Use -TimestampServer <RFC3161 URL> only with a future publicly trusted
    certificate, so its signatures survive certificate expiry.
#>
[CmdletBinding()]
param(
    [string]$OutDir = '',
    [string]$CertThumbprint = '',
    [string]$CertSubject = 'CN=LiveBackground Test',
    [string]$MpvSource = '',
    [string]$FfmpegSource = '',
    [string]$SampleVideo = '',
    [string]$InnoCompiler = '',
    [string]$TimestampServer = '',
    [switch]$SkipSign,
    [switch]$SkipInstaller,
    [switch]$CompileOnly
)

$ErrorActionPreference = 'Stop'

$scriptRoot = $PSScriptRoot
if (-not $scriptRoot) {
    $scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
}
if (-not $OutDir) {
    $localAppData = $env:LOCALAPPDATA
    if (-not $localAppData -and $env:USERPROFILE) {
        $localAppData = Join-Path $env:USERPROFILE 'AppData\Local'
    }
    if (-not $localAppData) { $localAppData = [IO.Path]::GetTempPath() }
    $OutDir = Join-Path $localAppData 'LiveBackgroundBuild'
}

function Write-Step($text) {
    Write-Host ''
    Write-Host "== $text" -ForegroundColor Cyan
}

function Find-Dotnet {
    $candidates = @()
    if ($env:DOTNET_ROOT) { $candidates += (Join-Path $env:DOTNET_ROOT 'dotnet.exe') }
    $candidates += @(
        (Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'),
        (Join-Path $env:ProgramFiles 'dotnet\dotnet.exe')
    )
    foreach ($path in $candidates) {
        if ($path -and (Test-Path $path)) { return $path }
    }
    $command = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    return $null
}

function Find-Inno {
    $candidates = @(
        $InnoCompiler,
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe')
    )
    foreach ($path in $candidates) {
        if ($path -and (Test-Path $path)) { return $path }
    }
    return $null
}

function Copy-Tree($source, $destination, [string[]]$excludeNames) {
    if (-not (Test-Path $source)) { throw "Source folder not found: $source" }
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    $resolvedSource = (Resolve-Path $source).Path.TrimEnd('\')
    $resolvedDestination = (Resolve-Path $destination).Path.TrimEnd('\')
    if ($resolvedSource -ieq $resolvedDestination) {
        Write-Host "  already in place: $destination"
        return
    }

    Get-ChildItem -LiteralPath $source -Force | ForEach-Object {
        if ($excludeNames -and ($excludeNames -contains $_.Name)) { return }
        $target = Join-Path $destination $_.Name
        if ($_.PSIsContainer) {
            Copy-Tree $_.FullName $target $excludeNames
        }
        else {
            Copy-Item -LiteralPath $_.FullName -Destination $target -Force
        }
    }
}

function Find-SigningCert {
    if ($CertThumbprint) {
        return Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
            Where-Object { $_.Thumbprint -eq $CertThumbprint } | Select-Object -First 1
    }
    return Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
        Where-Object { $_.Subject -eq $CertSubject } | Select-Object -First 1
}

function Sign-File($path, $cert) {
    if (-not $cert) { throw "Signing requested but no certificate was supplied for $path." }
    if (-not (Test-Path $path)) { throw "Cannot sign a missing file: $path" }
    try {
        $parameters = @{ FilePath = $path; Certificate = $cert; HashAlgorithm = 'SHA256' }
        if ($TimestampServer) { $parameters.TimestampServer = $TimestampServer }
        $signature = Set-AuthenticodeSignature @parameters
        if ($signature.Status -ne 'Valid') {
            throw "signature status was $($signature.Status): $($signature.StatusMessage)"
        }
        Write-Host ("  signed " + (Split-Path $path -Leaf) + " (" + $signature.Status + ")")
    }
    catch {
        throw "Could not sign $(Split-Path $path -Leaf): $($_.Exception.Message)"
    }
}

$root = $scriptRoot
if (-not (Test-Path (Join-Path $root 'src'))) { throw "src folder not found next to build.ps1" }

$dotnetExe = Find-Dotnet
if (-not $dotnetExe) {
    throw 'The .NET SDK was not found. The WPF settings UI needs MSBuild/XAML compilation; install the SDK or Visual Studio Build Tools with .NET desktop build tools.'
}
$projectPath = Join-Path $root 'src\LiveBackground.csproj'
if (-not (Test-Path $projectPath)) { throw "Project not found: $projectPath" }
$icon = Join-Path $root 'assets\LiveBackground.ico'

# Every output folder is rebuilt from scratch so config.json, caches, logs,
# backups and old installers from a previous local run cannot leak into a
# release package.
if (-not $CompileOnly -and (Test-Path $OutDir)) {
    Write-Step 'Cleaning the build output'
    Remove-Item -LiteralPath $OutDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$appExe = Join-Path $OutDir 'LiveBackground.exe'
$cliExe = Join-Path $OutDir 'LiveBackgroundCli.exe'
$settingsExe = Join-Path $OutDir 'LiveBackgroundSettings.exe'
$trayExe = Join-Path $OutDir 'LiveBackgroundTray.exe'
$bootstrapperExe = Join-Path $OutDir 'LiveBackgroundBootstrap.exe'

Write-Host ("  compiler: dotnet build (MSBuild, net48) via " + $dotnetExe)

function Invoke-DotnetBuildExe([string]$name, [string]$outputType, [string]$assemblyName, [string]$startupObject, [string]$destExe, [string[]]$extraProps) {
    $buildOut = Join-Path $OutDir (".build_" + $name)
    if (Test-Path $buildOut) { Remove-Item $buildOut -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $buildOut | Out-Null
    $args = @(
        'build', $projectPath,
        '-c', 'Release',
        '-nologo',
        '-o', $buildOut,
        "-p:IntermediateOutputPath=$buildOut\obj\",
        "-p:OutputType=$outputType",
        "-p:AssemblyName=$assemblyName",
        "-p:StartupObject=$startupObject"
    )
    if ($extraProps) { foreach ($p in $extraProps) { $args += "-p:$p" } }
    & $dotnetExe @args
    if ($LASTEXITCODE -ne 0) { throw "Compilation failed for $name (exit $LASTEXITCODE)." }
    $built = Join-Path $buildOut ($assemblyName + '.exe')
    if (-not (Test-Path $built)) { throw "Build output not found: $built" }
    Copy-Item -LiteralPath $built -Destination $destExe -Force
    Remove-Item $buildOut -Recurse -Force
}

Write-Step "Compiling LiveBackground.exe"
Invoke-DotnetBuildExe 'gui' 'WinExe' 'LiveBackground' 'LiveBackground.Program' $appExe @('UseWPF=false', 'ExcludeSettings=true')

Write-Step "Compiling LiveBackgroundSettings.exe"
Invoke-DotnetBuildExe 'settings' 'WinExe' 'LiveBackgroundSettings' 'LiveBackground.SettingsProgram' $settingsExe

Write-Step "Compiling LiveBackgroundTray.exe"
Invoke-DotnetBuildExe 'tray' 'WinExe' 'LiveBackgroundTray' 'LiveBackground.TrayProgram' $trayExe @('UseWPF=false', 'ExcludeSettings=true', 'BuildTray=true')

Write-Step "Compiling LiveBackgroundCli.exe"
Invoke-DotnetBuildExe 'cli' 'Exe' 'LiveBackgroundCli' 'LiveBackground.SetupProgram' $cliExe @('UseWPF=false', 'ExcludeSettings=true')

Write-Step "Copying LiveBackgroundBootstrap.exe"
# The scheduled task launches this executable before the managed app, so every
# build uses the same tracked binary instead of recompiling it differently on
# each machine. To change it, rebuild from src\bootstrapper.c and commit the
# result deliberately.
$bootstrapSource = Join-Path $root 'tools\LiveBackgroundBootstrap.exe'
if (-not (Test-Path $bootstrapSource)) {
    throw 'tools\LiveBackgroundBootstrap.exe is missing. The bootstrapper is the scheduled-task target and must be present.'
}
Copy-Item -LiteralPath $bootstrapSource -Destination $bootstrapperExe -Force

if ($CompileOnly) {
    Write-Step 'Done (compile only)'
    Write-Host ("  " + $appExe)
    Write-Host ("  " + $settingsExe)
    Write-Host ("  " + $trayExe)
    Write-Host ("  " + $cliExe)
    Write-Host ("  " + $bootstrapperExe)
    return
}

Write-Step 'Copying mpv'
if (-not $MpvSource) {
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'LiveBackground\mpv'),
        (Join-Path ${env:ProgramFiles} 'LiveBackground\mpv'),
        (Join-Path ${env:ProgramFiles(x86)} 'LiveBackground\mpv'),
        (Join-Path $root 'mpv'),
        (Join-Path $OutDir 'mpv')
    )
    foreach ($candidate in $candidates) {
        if (Test-Path (Join-Path $candidate 'mpv.exe')) { $MpvSource = $candidate; break }
    }
}
if (-not $MpvSource -or -not (Test-Path (Join-Path $MpvSource 'mpv.exe'))) {
    throw 'mpv.exe was not found. Pass -MpvSource <folder containing mpv.exe>.'
}
Copy-Tree $MpvSource (Join-Path $OutDir 'mpv')

Write-Step 'Copying ffmpeg'
if (-not $FfmpegSource) {
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'LiveBackground\tools\ffmpeg'),
        (Join-Path ${env:ProgramFiles} 'LiveBackground\tools\ffmpeg'),
        (Join-Path ${env:ProgramFiles(x86)} 'LiveBackground\tools\ffmpeg'),
        (Join-Path $root 'tools\ffmpeg'),
        (Join-Path $OutDir 'tools\ffmpeg')
    )
    foreach ($candidate in $candidates) {
        if (Test-Path (Join-Path $candidate 'ffmpeg.exe')) { $FfmpegSource = $candidate; break }
    }
}
if (-not $FfmpegSource -or -not (Test-Path (Join-Path $FfmpegSource 'ffmpeg.exe'))) {
    throw 'ffmpeg.exe was not found. Pass -FfmpegSource <folder containing ffmpeg.exe>.'
}
$ffmpegTarget = Join-Path $OutDir 'tools\ffmpeg'
New-Item -ItemType Directory -Force -Path $ffmpegTarget | Out-Null
$ffmpegSourceDir = (Resolve-Path -LiteralPath $FfmpegSource).Path
$ffmpegFile = Join-Path $ffmpegSourceDir 'ffmpeg.exe'
$ffprobeFile = Join-Path $ffmpegSourceDir 'ffprobe.exe'
if (-not (Test-Path $ffprobeFile)) {
    throw 'ffprobe.exe was not found next to ffmpeg.exe.'
}
foreach ($sourceFile in @($ffmpegFile, $ffprobeFile)) {
    $targetFile = Join-Path $ffmpegTarget (Split-Path $sourceFile -Leaf)
    # When OutDir already holds the tools (the source folder is the target
    # folder) the source and target are the same file: nothing to copy.
    if (-not [string]::Equals([IO.Path]::GetFullPath($targetFile), $sourceFile, [StringComparison]::OrdinalIgnoreCase)) {
        Copy-Item -LiteralPath $sourceFile -Destination $targetFile -Force
    }
}

Write-Step 'Copying the bundled videos'
# The installer ships these two clips; a fresh install seeds them into the
# user's video folder and starts on "Galaxy HD Clip.mp4" in Single clip mode.
# Both files are tracked under assets\media; no legacy archive is consulted.
$videoSourceDir = Join-Path $root 'assets\media\videos'
$galaxySource = if ($SampleVideo) { $SampleVideo } else { Join-Path $videoSourceDir 'Galaxy HD Clip.mp4' }
New-Item -ItemType Directory -Force -Path (Join-Path $OutDir 'videos') | Out-Null
$bundledVideos = @(
    @{ Target = 'Galaxy HD Clip.mp4'; Source = $galaxySource },
    @{ Target = 'Fokuz Recordings Clip.mp4'; Source = Join-Path $videoSourceDir 'Fokuz Recordings Clip.mp4' }
)
foreach ($video in $bundledVideos) {
    if (-not (Test-Path $video.Source)) { throw "Bundled video not found: $($video.Source)" }
    $target = Join-Path (Join-Path $OutDir 'videos') $video.Target
    if (-not [string]::Equals([IO.Path]::GetFullPath($target), (Resolve-Path -LiteralPath $video.Source).Path, [StringComparison]::OrdinalIgnoreCase)) {
        Copy-Item -LiteralPath $video.Source -Destination $target -Force
    }
    Write-Host ("  " + $video.Target)
}

Write-Step 'Copying the bundled images'
# A fresh install seeds these pictures into the user's image folder and starts
# on the first one in One-image mode. They are tracked under assets\media.
$imageSourceDir = Join-Path $root 'assets\media\images\wallpapers'
if (-not (Test-Path $imageSourceDir)) { throw "Bundled image folder not found: $imageSourceDir" }
$bundledImages = @(Get-ChildItem -Path $imageSourceDir -File |
    Where-Object { $_.Extension -match '^\.(jpg|jpeg|png|bmp|gif|webp)$' })
if ($bundledImages.Count -eq 0) { throw "No bundled images found in $imageSourceDir" }
$imageTargetDir = Join-Path $OutDir 'images\wallpapers'
New-Item -ItemType Directory -Force -Path $imageTargetDir | Out-Null
foreach ($image in $bundledImages) {
    $target = Join-Path $imageTargetDir $image.Name
    if (-not [string]::Equals($target, $image.FullName, [StringComparison]::OrdinalIgnoreCase)) {
        Copy-Item $image.FullName $target -Force
    }
    Write-Host ("  " + $image.Name)
}

Write-Step 'Copying documentation and scripts'
foreach ($name in @('README.txt', 'THIRD-PARTY.txt', 'install.cmd', 'uninstall.cmd')) {
    $source = Join-Path $root $name
    if (Test-Path $source) {
        Copy-Item $source (Join-Path $OutDir $name) -Force
        Write-Host "  $name"
    } else {
        Write-Warning "  missing: $name"
    }
}

Write-Step 'Copying license files'
$licenseDir = Join-Path $OutDir 'assets\licenses'
New-Item -ItemType Directory -Force -Path $licenseDir | Out-Null
$projectLicenses = Join-Path $root 'assets\licenses'
if (Test-Path $projectLicenses) {
    Get-ChildItem $projectLicenses -File | ForEach-Object {
        Copy-Item $_.FullName (Join-Path $licenseDir $_.Name) -Force
        Write-Host ("  " + $_.Name)
    }
}
# The tracked assets\licenses\ffmpeg-* texts are authoritative; never overwrite
# them from the ffmpeg source folder.

Write-Step 'Marking the portable package'
$portableMarker = Join-Path $OutDir 'portable.txt'
Set-Content -LiteralPath $portableMarker -Value 'This file makes LiveBackground keep its data in this folder.' -Encoding ASCII
Write-Host "  portable.txt"

$cert = $null
if (-not $SkipSign) {
    Write-Step 'Signing the first-party executables'
    $cert = Find-SigningCert
    if (-not $cert) {
        throw "No code-signing certificate '$CertSubject' was found. Use -SkipSign only for local development; a release build needs a certificate."
    }
    Write-Host ("  certificate: " + $cert.Subject + " (" + $cert.Thumbprint + ")")
    if (-not $TimestampServer) {
        Write-Warning 'Signing without an RFC3161 timestamp (test certificate only). With a publicly trusted certificate, pass -TimestampServer so signatures survive expiry.'
    }

    # mpv and ffmpeg stay as published by their projects; only the binaries
    # built here are signed. The bootstrapper matters because the scheduled
    # task launches it before the main executable.
    foreach ($exe in @($appExe, $cliExe, $settingsExe, $trayExe, $bootstrapperExe)) {
        Sign-File $exe $cert
    }

    $cerPath = Join-Path $OutDir 'LiveBackground.cer'
    if (Test-Path $cerPath) { Remove-Item $cerPath -Force }
    Export-Certificate -Cert $cert -FilePath $cerPath -Type CERT | Out-Null
    Write-Host ("  exported " + $cerPath)
}
else {
    Write-Step 'Signing skipped (-SkipSign)'
}

if (-not $SkipInstaller) {
    Write-Step 'Building the Windows installer'
    $iscc = Find-Inno
    if (-not $iscc) {
        throw 'Inno Setup 6 was not found. Install it or pass -InnoCompiler <path to ISCC.exe>.'
    }
    $iss = Join-Path $root 'installer.iss'
    if (-not (Test-Path $iss)) { throw "installer.iss not found at $iss" }
    $installerDir = Join-Path $OutDir 'installer'
    New-Item -ItemType Directory -Force -Path $installerDir | Out-Null
    # Keep the quotes around each /D value as literal characters so a path with
    # spaces is passed to ISCC as one define.
    & $iscc "/D\`"SourceDir=$OutDir\`"" "/D\`"OutputDir=$installerDir\`"" "/D\`"AppIcon=$icon\`"" $iss
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed (exit $LASTEXITCODE)." }
    $installerExe = Join-Path $installerDir 'LiveBackgroundSetup.exe'
    if (-not $SkipSign -and $cert) { Sign-File $installerExe $cert }
    Write-Host ("  " + $installerExe)
}

Write-Step 'Done'
Get-ChildItem $OutDir -Force | Select-Object Mode, Name, Length | Format-Table -AutoSize | Out-String | Write-Host
if ($SkipInstaller) {
    Write-Host 'Installer was skipped (-SkipInstaller).'
} else {
    $installer = Join-Path $OutDir 'installer\LiveBackgroundSetup.exe'
    if (-not (Test-Path $installer)) { throw "Installer not found after its build: $installer" }
    Write-Host ("Share this file: " + $installer)
}
