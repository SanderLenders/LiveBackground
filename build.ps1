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

    Compiling:
        Uses dotnet build / MSBuild (net48) because the settings UI is WPF and
        requires XAML compilation. The old csc/Roslyn fallback cannot build WPF
        XAML and is no longer supported. Install the .NET SDK (or Visual Studio
        Build Tools with .NET desktop build tools) to build from source.

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
    [switch]$SkipInstaller,
    [switch]$CompileOnly,
    [switch]$FrameworkCsc # kept for compatibility; WPF builds require dotnet/MSBuild
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

# The SDK's Roslyn compiler, which understands up to C# 7.3. Returns the path to
# csc.dll, or $null when no SDK is installed.
function Find-RoslynCsc($dotnetPath) {
    $roots = @()
    if ($env:DOTNET_ROOT) { $roots += (Join-Path $env:DOTNET_ROOT 'sdk') }
    if ($dotnetPath) { $roots += (Join-Path (Split-Path -Parent $dotnetPath) 'sdk') }
    $roots += @(
        (Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\sdk'),
        (Join-Path $env:ProgramFiles 'dotnet\sdk')
    )
    foreach ($root in ($roots | Where-Object { $_ } | Select-Object -Unique)) {
        if (-not (Test-Path $root)) { continue }
        $csc = Get-ChildItem -Path $root -Directory -ErrorAction SilentlyContinue |
            Sort-Object { try { [version]$_.Name } catch { [version]'0.0' } } -Descending |
            ForEach-Object { Join-Path $_.FullName 'Roslyn\bincore\csc.dll' } |
            Where-Object { Test-Path $_ } |
            Select-Object -First 1
        if ($csc) { return $csc }
    }
    return $null
}

function Invoke-Compiler([string[]]$command) {
    $exe = $command[0]
    $arguments = @($command | Select-Object -Skip 1)
    & $exe @arguments
    if ($LASTEXITCODE -ne 0) { throw "Compilation failed (exit $LASTEXITCODE)." }
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
$frameworkDir = Split-Path -Parent $csc
$sourceFiles = Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName }
if ($sourceFiles.Count -eq 0) { throw 'No .cs files found in src.' }

# Prefer the SDK's Roslyn compiler, which understands C# 7.3 (the newest the
# .NET Framework supports). Fall back to the framework csc (C# 5) when the SDK
# is not installed, so the build never hard-fails on a bare machine. Passing
# -FrameworkCsc forces the fallback.
$roslynCsc = $null
$dotnet = $null
if (-not $FrameworkCsc) {
    $dotnet = Find-Dotnet
    if ($dotnet) { $roslynCsc = Find-RoslynCsc $dotnet }
}

# The framework csc resolves these by simple name and references mscorlib
# itself; Roslyn needs the full paths plus /nostdlib+ because it does not
# implicitly reference the .NET Framework.
$frameworkReferenceNames = @(
    'mscorlib.dll',
    'System.dll',
    'System.Core.dll',
    'System.Runtime.Serialization.dll',
    'System.Xml.dll',
    'System.Drawing.dll',
    'System.Windows.Forms.dll',
    'System.Xaml.dll',
    'WindowsBase.dll',
    'PresentationCore.dll',
    'PresentationFramework.dll'
)
if ($roslynCsc) {
    $referenceArgs = $frameworkReferenceNames | ForEach-Object { "/r:" + (Join-Path $frameworkDir $_) }
} else {
    $referenceArgs = $frameworkReferenceNames |
        Where-Object { $_ -ne 'mscorlib.dll' } |
        ForEach-Object { "/r:$_" }
}

$icon = Join-Path $root 'assets\LiveBackground.ico'
$iconArgs = @()
if (Test-Path $icon) { $iconArgs = @("/win32icon:$icon") }

function Compile-Exe([string]$target, [string]$entryPoint, [string]$outFile) {
    $common = @('/nologo', '/platform:x64', "/target:$target", "/main:$entryPoint")
    if ($roslynCsc) {
        Invoke-Compiler (@($dotnet, 'exec', $roslynCsc, '/nostdlib+', '/langversion:7.3') +
            $common + $referenceArgs + $iconArgs + @("/out:$outFile") + $sourceFiles)
    } else {
        Invoke-Compiler (@($csc) + $common + $referenceArgs + $iconArgs + @("/out:$outFile") + $sourceFiles)
    }
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$appExe = Join-Path $OutDir 'LiveBackground.exe'
$cliExe = Join-Path $OutDir 'LiveBackgroundCli.exe'
$settingsExe = Join-Path $OutDir 'LiveBackgroundSettings.exe'
$trayExe = Join-Path $OutDir 'LiveBackgroundTray.exe'
$bootstrapperExe = Join-Path $OutDir 'LiveBackgroundBootstrap.exe'
$dotnetExe = $dotnet
if (-not $dotnetExe) { $dotnetExe = Find-Dotnet }
$projectPath = Join-Path $root 'src\LiveBackground.csproj'

if ($FrameworkCsc) {
    throw 'WPF settings UI requires dotnet build/MSBuild (XAML compilation). -FrameworkCsc is not supported.'
}

if ($dotnetExe -and (Test-Path $projectPath)) {
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

    Write-Step "Compiling LiveBackgroundBootstrap.exe"
    $clang = Get-Command clang -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -First 1
    if (-not $clang) {
        $wg = Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Packages'
        if (Test-Path $wg) { $clang = Get-ChildItem $wg -Recurse -Filter 'clang.exe' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName -First 1 }
    }
    $bootstrapSrc = Join-Path $root 'src\bootstrapper.c'
    if ($clang -and (Test-Path $bootstrapSrc)) {
        & $clang -Os -s -mwindows -municode -o $bootstrapperExe $bootstrapSrc -luser32 -lkernel32
        if ($LASTEXITCODE -ne 0) { throw "Bootstrapper compilation failed (exit $LASTEXITCODE)." }
    } else {
        $prebuilt = Join-Path $root 'tools\LiveBackgroundBootstrap.exe'
        if (Test-Path $prebuilt) { Copy-Item -LiteralPath $prebuilt -Destination $bootstrapperExe -Force }
        else { Write-Warning 'clang not found and no prebuilt bootstrapper; skipping LiveBackgroundBootstrap.exe' }
    }
}
else {
    throw 'WPF settings UI requires dotnet build/MSBuild (XAML compilation). Install the .NET SDK or Visual Studio Build Tools with .NET desktop build tools, then rerun build.ps1.'
}

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
        (Join-Path $OutDir 'mpv'),
        (Join-Path $env:LOCALAPPDATA 'LiveBackground\mpv'),
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
        (Join-Path $OutDir 'tools\ffmpeg'),
        (Join-Path $env:LOCALAPPDATA 'LiveBackground\tools\ffmpeg')
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
New-Item -ItemType Directory -Force -Path (Join-Path $OutDir 'videos') | Out-Null
$bundledVideos = @(
    @{ Target = 'Galaxy HD Clip.mp4'; Sources = @(
        $SampleVideo,
        (Join-Path $root 'dist\videos\Galaxy HD Clip.mp4'),
        (Join-Path $OutDir 'videos\Galaxy HD Clip.mp4')
    ) },
    @{ Target = 'Fokuz Recordings Clip.mp4'; Sources = @(
        # The smaller re-encode ships now. The older, longer template is kept
        # only as a fallback for builds made from an older working copy.
        (Join-Path $root 'dist\videos\Fokuz Recordings Clip.mp4'),
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
        $target = Join-Path (Join-Path $OutDir 'videos') $video.Target
        # When OutDir already holds the clip (the source is the target) there
        # is nothing to copy.
        if (-not [string]::Equals([IO.Path]::GetFullPath($target), (Resolve-Path -LiteralPath $source).Path, [StringComparison]::OrdinalIgnoreCase)) {
            Copy-Item -LiteralPath $source -Destination $target -Force
        }
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

Write-Step 'Copying the bundled images'
# A fresh install seeds these pictures into the user's image folder and starts
# on the first one in One-image mode. Every picture in dist\images\wallpapers
# ships; drop files in there to change what a new install starts with.
$imageSourceDir = Join-Path $root 'dist\images\wallpapers'
$imageTargetDir = Join-Path $OutDir 'images\wallpapers'
New-Item -ItemType Directory -Force -Path $imageTargetDir | Out-Null
if (Test-Path $imageSourceDir) {
    $bundledImages = @(Get-ChildItem -Path $imageSourceDir -File |
        Where-Object { $_.Extension -match '^\.(jpg|jpeg|png|bmp|gif|webp)$' })
    if ($bundledImages.Count -gt 0) {
        foreach ($image in $bundledImages) {
            $target = Join-Path $imageTargetDir $image.Name
            # When OutDir is dist itself the source and target are the same
            # file, so there is nothing to copy.
            if (-not [string]::Equals($target, $image.FullName, [StringComparison]::OrdinalIgnoreCase)) {
                Copy-Item $image.FullName $target -Force
            }
            Write-Host ("  " + $image.Name)
        }
    } else {
        Write-Warning ("  no bundled images found in " + $imageSourceDir)
    }
} else {
    Write-Warning ("  missing bundled image folder: " + $imageSourceDir)
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
