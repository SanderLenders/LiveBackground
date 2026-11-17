# Building LiveBackground

The application users install is built by `build.ps1`, which compiles `src\*.cs`
with the .NET Framework `csc` and then packages the result with Inno Setup. That
path is unchanged and remains the single source of truth for what ships.

Next to it, the repository carries a small modern tooling layer so the same
sources can be built, analysed and unit-tested quickly:

```
LiveBackground.sln
  src\LiveBackground.csproj      SDK-style project, targets net48
  tests\LiveBackground.Tests.csproj   xUnit tests
.editorconfig                    analyser severities
.gitattributes                   line-ending policy (text files are LF)
.github\workflows\build.yml      CI: build + test + a shipping-compile check
```

Nothing here changes the runtime, the installer or the shipped binaries. The
projects target .NET Framework 4.8 on purpose: the app still ships on the .NET
Framework, so the tests exercise the exact code `build.ps1` compiles.

## Prerequisites

- The .NET SDK 10 (or newer). The SDK only *builds* the projects; the app itself
  still runs on .NET Framework 4.8, which is part of Windows.
- The .NET Framework 4.8 reference assemblies are restored automatically from
  NuGet (`Microsoft.NETFramework.ReferenceAssemblies`), so the targeting pack
  does not need to be installed.

## Build and test

```powershell
dotnet restore LiveBackground.sln
dotnet build   LiveBackground.sln -c Release
dotnet test    LiveBackground.sln -c Release
```

`dotnet test` also builds the app, so a bare `dotnet test` is enough.

The tests cover the pure logic only: path and command-line helpers, natural
sorting, configuration normalisation and migration, playlist and crossfade
state, the JSON store's file round-trip, the folder watchers and the cache
signature. They need no display, no ffmpeg and no mpv, so they run anywhere,
including CI.

## The shipping build

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 `
  -OutDir "$env:LOCALAPPDATA\LiveBackgroundBuild" `
  -MpvSource "C:\Program Files\LiveBackground\mpv" `
  -FfmpegSource "C:\Program Files\LiveBackground\tools\ffmpeg" `
  -SkipSign
```

This is the command that produces the installer. It is not run in CI because it
needs mpv and ffmpeg and takes several minutes; CI instead recompiles `src\*.cs`
with the same `csc` reference set to catch a change that would break it.

## Analysers

The SDK build runs the built-in Roslyn analysers at the *Recommended* level, plus
two rules that are not in that set but are worth having here: `CA1508` (dead
conditional code) and `CA2000` (dispose objects before losing scope). The handful
of rules that flag deliberate patterns in this codebase (public `[DataContract]`
fields, the Win32 names in `Native.cs`, the visible P/Invokes) are turned off in
`.editorconfig`, with the reason next to each one. Everything else keeps its
default severity, so a genuinely new issue shows up.

Warnings are errors in this build (`TreatWarningsAsErrors`), so a new issue fails
`dotnet build` and CI instead of being printed and forgotten. The few findings
that are genuinely deliberate carry a `SuppressMessage` attribute with a
justification. Note that the shipping `csc` is the legacy C# 5 compiler, which
does not accept analyser IDs in `#pragma warning`, so suppressions use
`[SuppressMessage]` rather than a pragma.

This affects the developer / CI build only: `build.ps1` compiles the sources with
the framework `csc` and is unaffected.

## Continuous integration

`.github\workflows\build.yml` runs on every push to `main` and every pull
request. It restores, builds (analysers on, warnings as errors) and tests the
solution with the .NET SDK, then recompiles `src\*.cs` with the shipping `csc`
reference set, so a change that would break the real build is caught even though
the full packaging build (which needs mpv and ffmpeg) does not run there.

## Line endings

`.gitattributes` stores every text file with LF, so the sources are byte-identical
on every machine.

## Manual testing

The unit tests cover the pure logic only. The paths that touch Windows (the
window and threading model, multi-monitor, DPI, the mpv and ffmpeg lifecycles)
are checked by hand before a release; `TESTING.md` is the short checklist.
