<#
.SYNOPSIS
    Builds OdinsonsLauncher.exe — a single-file, self-contained Windows x64 build
    (the counterpart to build-macos.sh).

.EXAMPLE
    .\build-windows.ps1

.NOTES
    Output: Launcher.Avalonia\dist\OdinsonsLauncher.exe

    Requires: dotnet on PATH.

    x64 only, deliberately — Valheim itself has no 32-bit (x86) or native ARM64 build, so
    x86/arm64 launcher builds never served a real player: anyone who can run Valheim at all
    already has x64, or ARM64 Windows' built-in x64 emulation covers them transparently. One
    file, no "which one do I download" decision for players who don't know their own CPU.

    No .app-style assembly step needed here, unlike build-macos.sh — the icon is
    already embedded via <ApplicationIcon> in the .csproj, and <DebugType>embedded</DebugType>
    means publish produces just the one .exe, no stray .pdb to clean up.

    The .exe is unsigned: on a machine other than the one that built it, the first
    run trips SmartScreen ("Windows protected your PC" -> More info -> Run anyway).
    Only a purchased code-signing certificate fixes that — nothing this script can do.
#>

$ErrorActionPreference = 'Stop'

$ScriptDir = $PSScriptRoot
$ProjectDir = Split-Path $ScriptDir -Parent
$Project = Join-Path $ProjectDir 'Launcher.Avalonia.csproj'
$DistDir = Join-Path $ProjectDir 'dist'
$ExeName = 'OdinsonsLauncher'
$Rid = 'win-x64'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error "dotnet not found on PATH"
}

$VersionLine = Select-String -Path $Project -Pattern '<Version>(.*)</Version>' | Select-Object -First 1
$Version = if ($VersionLine) { $VersionLine.Matches[0].Groups[1].Value } else { 'unknown' }

Remove-Item -Recurse -Force $DistDir -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $DistDir | Out-Null

Write-Host "Version: $Version"
Write-Host ">> publishing $Rid ..."

dotnet publish $Project -c Release -r $Rid --self-contained `
    -p:PublishSingleFile=true --nologo -v minimal

$PublishDir = Join-Path $ProjectDir "bin\Release\net9.0\$Rid\publish"
$PublishedExe = Join-Path $PublishDir "$ExeName.exe"
if (-not (Test-Path $PublishedExe)) {
    Write-Error "expected output not found: $PublishedExe"
}

$Dest = Join-Path $DistDir "$ExeName.exe"
Copy-Item $PublishedExe $Dest -Force

$SizeMB = [math]::Round((Get-Item $Dest).Length / 1MB, 1)
Write-Host ">> done: $Dest  ($SizeMB MB)"
Write-Host ""
Write-Host "Executable in: $DistDir"
