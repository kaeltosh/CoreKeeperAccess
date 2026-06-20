# CoreKeeperAccess installer for testers.
# Copies the prebuilt mod from dist/ into the Core Keeper install, and the two
# native TTS DLLs (Tolk) next to CoreKeeper.exe. Safe to re-run after an update.
# No build tools required.
#
# For most testers: just double-click Installer.cmd. No command line needed.
#
# Manual usage (from the repository root):
#   powershell -ExecutionPolicy Bypass -File .\install.ps1
#   powershell -ExecutionPolicy Bypass -File .\install.ps1 -GamePath "D:\SteamLibrary\steamapps\common\Core Keeper"

[CmdletBinding()]
param(
    [string]$GamePath
)

$ErrorActionPreference = "Stop"
$CoreKeeperAppId = "1621690"

# --- Locate the Core Keeper install automatically --------------------------
# Resolution order: -GamePath > Steam registry + all library folders >
# the historical default path as a last resort.
function Resolve-GamePath {
    param([string]$Explicit)

    # [IO.Path]::Combine instead of Join-Path: the latter throws on a missing
    # drive letter (e.g. a stale path in the VDF, or a typo in -GamePath).
    function Test-GameDir([string]$dir) {
        if (-not $dir) { return $false }
        return Test-Path -LiteralPath ([System.IO.Path]::Combine($dir, "CoreKeeper.exe"))
    }

    # 1. Explicit -GamePath always wins.
    if ($Explicit) {
        if (Test-GameDir $Explicit) { return $Explicit }
        Write-Host "WARNING: -GamePath given but CoreKeeper.exe not found there. Trying auto-detection..."
    }

    # 2. Find the Steam root via the registry.
    $steamRoot = $null
    foreach ($key in @(
        "HKCU:\Software\Valve\Steam",
        "HKLM:\SOFTWARE\WOW6432Node\Valve\Steam",
        "HKLM:\SOFTWARE\Valve\Steam"
    )) {
        try {
            $props = Get-ItemProperty -Path $key -ErrorAction Stop
            foreach ($val in @($props.SteamPath, $props.InstallPath)) {
                if ($val) {
                    $norm = $val -replace "/", "\"
                    if (Test-Path $norm) { $steamRoot = $norm; break }
                }
            }
        } catch { }
        if ($steamRoot) { break }
    }

    # 3. Collect every Steam library folder (the root + the ones in the VDF).
    $libraries = New-Object System.Collections.Generic.List[string]
    if ($steamRoot) {
        $libraries.Add($steamRoot)
        $vdf = Join-Path $steamRoot "steamapps\libraryfolders.vdf"
        if (Test-Path $vdf) {
            foreach ($line in Get-Content $vdf) {
                $m = [regex]::Match($line, '"path"\s*"([^"]+)"')
                if ($m.Success) {
                    $p = $m.Groups[1].Value -replace "\\\\", "\"
                    if (Test-Path $p) { $libraries.Add($p) }
                }
            }
        }
    }

    # 4. Look for Core Keeper in every library.
    foreach ($lib in $libraries) {
        $candidate = [System.IO.Path]::Combine($lib, "steamapps", "common", "Core Keeper")
        if (Test-GameDir $candidate) { return $candidate }
    }

    # 5. Last-resort default.
    $fallback = "C:\Program Files (x86)\Steam\steamapps\common\Core Keeper"
    if (Test-GameDir $fallback) { return $fallback }

    return $null
}

$distMod     = Join-Path $PSScriptRoot "dist\CoreKeeperAccess"
$distNatives = Join-Path $PSScriptRoot "dist\natives"

# --- Sanity checks ---------------------------------------------------------
if (-not (Test-Path (Join-Path $distMod "ModManifest.json"))) {
    Write-Host "ERROR: dist\CoreKeeperAccess not found or incomplete."
    Write-Host "Run this installer from the folder where you extracted the release."
    exit 1
}

$GamePath = Resolve-GamePath -Explicit $GamePath
if (-not $GamePath) {
    Write-Host "ERROR: could not find your Core Keeper installation automatically."
    Write-Host "Pass the path explicitly, for example:"
    Write-Host '  powershell -ExecutionPolicy Bypass -File .\install.ps1 -GamePath "<path to Core Keeper>"'
    exit 1
}
Write-Host "Core Keeper found at: $GamePath"

if (Get-Process -Name "CoreKeeper" -ErrorAction SilentlyContinue) {
    Write-Host "ERROR: Core Keeper is running. Close the game first, then run this installer again."
    exit 1
}

# --- Copy helper with a clear admin fallback -------------------------------
# We do NOT auto-elevate (UAC is usually unnecessary: Steam runs as the user).
# If a write is denied, tell the tester exactly what to do.
function Invoke-CopyStep {
    param([scriptblock]$Action)
    try {
        & $Action
    } catch [System.UnauthorizedAccessException] {
        Write-Host ""
        Write-Host "ERROR: Windows denied write access to the game folder."
        Write-Host "Close this window, then RIGHT-CLICK Installer.cmd and choose"
        Write-Host "'Run as administrator', and run it again."
        exit 2
    }
}

# --- Install the mod -------------------------------------------------------
$modsDir   = Join-Path $GamePath "CoreKeeper_Data\StreamingAssets\Mods"
$installed = Join-Path $modsDir "CoreKeeperAccess"

Invoke-CopyStep {
    New-Item -ItemType Directory -Path $modsDir -Force | Out-Null
    if (Test-Path $installed) {
        Remove-Item $installed -Recurse -Force   # clean update: no stale files left behind
    }
    Copy-Item $distMod -Destination $installed -Recurse
}
Write-Host "Mod copied to: $installed"

# --- Install the native TTS DLLs -------------------------------------------
Invoke-CopyStep {
    foreach ($dll in "Tolk.dll", "nvdaControllerClient64.dll") {
        Copy-Item (Join-Path $distNatives $dll) -Destination $GamePath -Force
        Write-Host "Copied $dll to the game folder."
    }
}

Write-Host ""
Write-Host "Done. Start NVDA, then launch Core Keeper."
Write-Host "At the main menu you should hear: 'Accessibility mod loaded' plus a build number."
