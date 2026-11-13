#requires -Version 3.0
<#
    Builds LiveBackground.

    Output (default: .\dist):
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

    Run from a normal PowerShell window:
        .\build.ps1

    If the self-signed certificate does not exist yet, create it once with:
        $c = New-SelfSignedCertificate -Type CodeSigningCert `
             -Subject 'CN=LiveBackground Test' `
             -CertStoreLocation Cert:\CurrentUser\My -HashAlgorithm SHA256 -KeyLength 2048
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
    [switch]$SkipSign,
    [switch]$SkipInstaller
)

$ErrorActionPreference = 'Stop'

$scriptRoot = $PSScriptRoot
if (-not $scriptRoot) {
    $scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
}
if (-not $OutDir) { $OutDir = Join-Path $scriptRoot 'dist' }

function Write-Step($text) {
    Write-Host ''
    Write-Host "== $text" -ForegroundColor Cyan
}

function Find-Csc {
    $candidates = @(
        (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
        (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
    )
    foreach ($path in $candidates) {
        if (Test-Path $path) { return $path }
    }
    throw 'csc.exe (.NET Framework 4.x) was not found.'
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
        $cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
            Where-Object { $_.Thumbprint -eq $CertThumbprint } | Select-Object -First 1
        if ($cert) { return $cert }
    }
    return Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
        Where-Object { $_.Subject -eq $CertSubject } | Select-Object -First 1
}

function Sign-File($path, $cert) {
    if (-not $cert) { return }
    try {
        $signature = Set-AuthenticodeSignature -FilePath $path -Certificate $cert -HashAlgorithm SHA256
        Write-Host ("  signed " + (Split-Path $path -Leaf) + " (" + $signature.Status + ")")
    }
    catch {
        Write-Warning ("  could not sign " + (Split-Path $path -Leaf) + ": " + $_.Exception.Message)
    }
}

$root = $scriptRoot
if (-not (Test-Path (Join-Path $root 'src'))) { throw "src folder not found next to build.ps1" }

$csc = Find-Csc
$sourceFiles = Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName }
if ($sourceFiles.Count -eq 0) { throw 'No .cs files found in src.' }

$referenceArgs = @(
    '/r:System.dll',
    '/r:System.Core.dll',
    '/r:System.Runtime.Serialization.dll',
    '/r:System.Xml.dll',
    '/r:System.Drawing.dll',
    '/r:System.Windows.Forms.dll'
)

$icon = Join-Path $root 'assets\LiveBackground.ico'
$iconArgs = @()
if (Test-Path $icon) { $iconArgs = @("/win32icon:$icon") }

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

Write-Step "Compiling LiveBackground.exe"
$appExe = Join-Path $OutDir 'LiveBackground.exe'
& $csc /nologo /platform:x64 /target:winexe /main:LiveBackground.Program @referenceArgs @iconArgs "/out:$appExe" @sourceFiles
if ($LASTEXITCODE -ne 0) { throw "Compiling the app failed (exit $LASTEXITCODE)." }

Write-Step "Compiling LiveBackgroundCli.exe"
$cliExe = Join-Path $OutDir 'LiveBackgroundCli.exe'
& $csc /nologo /platform:x64 /target:exe /main:LiveBackground.SetupProgram @referenceArgs @iconArgs "/out:$cliExe" @sourceFiles
if ($LASTEXITCODE -ne 0) { throw "Compiling the command-line helper failed (exit $LASTEXITCODE)." }

Write-Step 'Copying mpv'
if (-not $MpvSource) {
    $candidates = @(
        (Join-Path $OutDir 'mpv'),
        (Join-Path $env:LOCALAPPDATA 'LiveBackground\mpv'),
        (Join-Path $env:LOCALAPPDATA 'GalaxyWallpaper\mpv'),
        (Join-Path $root 'mpv')
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
        (Join-Path $env:LOCALAPPDATA 'Temp\opencode\ffmpeg\extracted\ffmpeg-9.0.2-essentials_build\bin'),
        (Join-Path $OutDir 'tools\ffmpeg'),
        (Join-Path $env:LOCALAPPDATA 'GalaxyWallpaper\tools\ffmpeg')
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
Copy-Item (Join-Path $FfmpegSource 'ffmpeg.exe') $ffmpegTarget -Force
if (Test-Path (Join-Path $FfmpegSource 'ffprobe.exe')) {
    Copy-Item (Join-Path $FfmpegSource 'ffprobe.exe') $ffmpegTarget -Force
} else {
    throw 'ffprobe.exe was not found next to ffmpeg.exe.'
}

Write-Step 'Copying the bundled videos'
# The installer ships these two clips; a fresh install seeds them into the
# user's video folder and starts on "Galaxy HD Clip.mp4" in One-clip mode.
New-Item -ItemType Directory -Force -Path (Join-Path $OutDir 'videos') | Out-Null
$bundledVideos = @(
    @{ Target = 'Galaxy HD Clip.mp4'; Sources = @(
        $SampleVideo,
        'C:\Users\sande\OneDrive\Documenten\Background\Galaxy-hd_1920_1080_30fps.mp4',
        (Join-Path $root 'dist\videos\Galaxy-hd_1920_1080_30fps.mp4'),
        (Join-Path $root 'videos\Galaxy-hd_1920_1080_30fps.mp4'),
        (Join-Path $OutDir 'videos\Galaxy HD Clip.mp4')
    ) },
    @{ Target = 'Fokuz Recordings Clip.mp4'; Sources = @(
        (Join-Path $root 'dist\videos\Fokuz Recordings Youtube Template 60fps.mp4'),
        (Join-Path $OutDir 'videos\Fokuz Recordings Clip.mp4')
    ) }
)
foreach ($video in $bundledVideos) {
    $source = $null
    foreach ($candidate in $video.Sources) {
        if ($candidate -and (Test-Path $candidate)) { $source = $candidate; break }
    }
    if ($source) {
        Copy-Item $source (Join-Path (Join-Path $OutDir 'videos') $video.Target) -Force
        Write-Host ("  " + $video.Target)
    } else {
        Write-Warning ("  missing bundled video: " + $video.Target)
    }
}
# The Galaxy clip used to ship under its stock name; drop that older copy so
# the same video does not ship and seed twice.
$superseded = Join-Path $OutDir 'videos\Galaxy-hd_1920_1080_30fps.mp4'
if (Test-Path $superseded) {
    Remove-Item $superseded -Force
    Write-Host '  removed superseded Galaxy-hd_1920_1080_30fps.mp4'
}

Write-Step 'Copying the bundled image'
# A fresh install seeds this picture into the user's image folder and starts
# on it in One-image mode.
New-Item -ItemType Directory -Force -Path (Join-Path $OutDir 'images\wallpapers') | Out-Null
$bundledImageSources = @(
    (Join-Path $root 'dist\images\wallpapers\Galaxy Image.jpg'),
    (Join-Path $OutDir 'images\wallpapers\Galaxy Image.jpg')
)
$bundledImageSource = $null
foreach ($candidate in $bundledImageSources) {
    if (Test-Path $candidate) { $bundledImageSource = $candidate; break }
}
if ($bundledImageSource) {
    Copy-Item $bundledImageSource (Join-Path $OutDir 'images\wallpapers') -Force
    Write-Host '  Galaxy Image.jpg'
} else {
    Write-Warning '  missing bundled image: Galaxy Image.jpg'
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
if ($FfmpegSource) {
    $ffmpegRoot = Split-Path $FfmpegSource -Parent
    if (Test-Path (Join-Path $ffmpegRoot 'LICENSE')) {
        Copy-Item (Join-Path $ffmpegRoot 'LICENSE') (Join-Path $licenseDir 'ffmpeg-LICENSE.txt') -Force
    }
    if (Test-Path (Join-Path $ffmpegRoot 'README.txt')) {
        Copy-Item (Join-Path $ffmpegRoot 'README.txt') (Join-Path $licenseDir 'ffmpeg-README.txt') -Force
    }
}

Write-Step 'Marking the portable package'
$portableMarker = Join-Path $OutDir 'portable.txt'
Set-Content -LiteralPath $portableMarker -Value 'This file makes LiveBackground keep its data in this folder.' -Encoding ASCII
Write-Host "  portable.txt"

$cert = $null
if (-not $SkipSign) {
    Write-Step 'Signing the executables'
    $cert = Find-SigningCert
    if (-not $cert) {
        Write-Warning "No code-signing certificate '$CertSubject' found. Skipping signing."
        Write-Warning 'Smart App Control may block LiveBackgroundSetup.exe on other PCs.'
    }
    else {
        Write-Host ("  certificate: " + $cert.Subject + " (" + $cert.Thumbprint + ")")
        foreach ($exe in @($appExe, $cliExe)) { Sign-File $exe $cert }

        $cerPath = Join-Path $OutDir 'LiveBackground.cer'
        try {
            if (Test-Path $cerPath) { Remove-Item $cerPath -Force }
            Export-Certificate -Cert $cert -FilePath $cerPath -Type CERT | Out-Null
            Write-Host ("  exported " + $cerPath)
        }
        catch {
            Write-Warning ("  could not export the certificate: " + $_.Exception.Message)
        }
    }
}
else {
    Write-Step 'Signing skipped (-SkipSign)'
}

if (-not $SkipInstaller) {
    Write-Step 'Building the Windows installer'
    $iscc = Find-Inno
    if (-not $iscc) {
        Write-Warning 'Inno Setup 6 was not found. Install it or pass -InnoCompiler <path to ISCC.exe>.'
        Write-Warning 'The portable package is still complete; only installer\LiveBackgroundSetup.exe is missing.'
    }
    else {
        $iss = Join-Path $root 'installer.iss'
        if (-not (Test-Path $iss)) { throw "installer.iss not found at $iss" }
        $installerDir = Join-Path $OutDir 'installer'
        New-Item -ItemType Directory -Force -Path $installerDir | Out-Null
        & $iscc "/DSourceDir=$OutDir" "/DOutputDir=$installerDir" "/DAppIcon=$icon" $iss
        if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed (exit $LASTEXITCODE)." }
        $installerExe = Join-Path $installerDir 'LiveBackgroundSetup.exe'
        if (-not $SkipSign -and $cert) { Sign-File $installerExe $cert }
        Write-Host ("  " + $installerExe)
    }
}

Write-Step 'Done'
Get-ChildItem $OutDir -Force | Select-Object Mode, Name, Length | Format-Table -AutoSize | Out-String | Write-Host
$installer = Join-Path $OutDir 'installer\LiveBackgroundSetup.exe'
if (Test-Path $installer) {
    Write-Host ("Share this file: " + $installer)
} else {
    Write-Host 'The portable folder is ready to share. Run install.cmd on the target PC.'
}
