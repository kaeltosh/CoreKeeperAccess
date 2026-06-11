# CoreKeeperAccess installer for testers.
# Copies the prebuilt mod from dist/ into the Core Keeper install, and the two
# native TTS DLLs (Tolk) next to CoreKeeper.exe. Safe to re-run after a git pull
# to update the mod. No build tools required.
#
# Usage (from the repository root):
#   powershell -ExecutionPolicy Bypass -File .\install.ps1
#   powershell -ExecutionPolicy Bypass -File .\install.ps1 -GamePath "D:\SteamLibrary\steamapps\common\Core Keeper"

[CmdletBinding()]
param(
    [string]$GamePath = "C:\Program Files (x86)\Steam\steamapps\common\Core Keeper"
)

$ErrorActionPreference = "Stop"

$distMod     = Join-Path $PSScriptRoot "dist\CoreKeeperAccess"
$distNatives = Join-Path $PSScriptRoot "dist\natives"

# --- Sanity checks ---------------------------------------------------------
if (-not (Test-Path (Join-Path $distMod "ModManifest.json"))) {
    Write-Host "ERROR: dist\CoreKeeperAccess not found or incomplete."
    Write-Host "Run this script from the root of the cloned repository."
    exit 1
}
if (-not (Test-Path (Join-Path $GamePath "CoreKeeper.exe"))) {
    Write-Host "ERROR: Core Keeper not found at: $GamePath"
    Write-Host "If the game is installed elsewhere, pass the path explicitly:"
    Write-Host '  powershell -ExecutionPolicy Bypass -File .\install.ps1 -GamePath "<path to Core Keeper>"'
    exit 1
}
if (Get-Process -Name "CoreKeeper" -ErrorAction SilentlyContinue) {
    Write-Host "ERROR: Core Keeper is running. Close the game first, then re-run this script."
    exit 1
}

# --- Install the mod -------------------------------------------------------
$modsDir   = Join-Path $GamePath "CoreKeeper_Data\StreamingAssets\Mods"
$installed = Join-Path $modsDir "CoreKeeperAccess"

New-Item -ItemType Directory -Path $modsDir -Force | Out-Null
if (Test-Path $installed) {
    Remove-Item $installed -Recurse -Force   # clean update: no stale files left behind
}
Copy-Item $distMod -Destination $installed -Recurse
Write-Host "Mod copied to: $installed"

# --- Install the native TTS DLLs -------------------------------------------
foreach ($dll in "Tolk.dll", "nvdaControllerClient64.dll") {
    Copy-Item (Join-Path $distNatives $dll) -Destination $GamePath -Force
    Write-Host "Copied $dll to the game folder."
}

Write-Host ""
Write-Host "Done. Start NVDA, then launch Core Keeper."
Write-Host "At the main menu you should hear: 'Accessibility mod loaded' plus a build number."
