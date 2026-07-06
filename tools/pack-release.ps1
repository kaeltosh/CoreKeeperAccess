# Builds the tester release zip from an EXPLICIT allow-list.
# Philosophy (mirrors the white-list .gitignore): we only ever package what is
# listed below. Nothing is included by "copy everything except X".
#
# Output: release\CoreKeeperAccess-<release-tag>.zip
#
# Usage (from anywhere):
#   powershell -ExecutionPolicy Bypass -File .\tools\pack-release.ps1
#   powershell -ExecutionPolicy Bypass -File .\tools\pack-release.ps1 -OutDir "D:\out"

[CmdletBinding()]
param(
    [string]$OutDir
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

# --- Refresh the standalone HTML copies of README/GUIDE (both languages) ---
# For players who grab the zip and missed them on GitHub. Not versioned in
# git (regenerated fresh from the current .md every time, see the script).
python (Join-Path $repoRoot "tools\gen_doc_html.py")
if ($LASTEXITCODE -ne 0) { throw "gen_doc_html.py failed (exit $LASTEXITCODE) - is 'markdown' installed for this Python?" }

# --- What goes in the release (allow-list) ---------------------------------
# Files at the zip root, plus whole folders. Anything not listed is excluded.
$includeFiles = @(
    "install.ps1",
    "Installer.cmd",
    "README.md",
    "README.fr.md",
    "README.html",
    "README.fr.html",
    "GUIDE.md",
    "GUIDE.fr.md",
    "GUIDE.html",
    "GUIDE.fr.html",
    "CHANGELOG.md",
    "CHANGELOG.fr.md",
    "KNOWN_ISSUES.md",
    "KNOWN_ISSUES.fr.md",
    "LICENSE"
)
$includeDirs = @(
    "dist"
)

# --- Read the release tag to name the zip ----------------------------------
$modCs = Join-Path $repoRoot "dist\CoreKeeperAccess\Scripts\CoreKeeperAccessMod.cs"
$releaseTag = "release"
if (Test-Path $modCs) {
    $m = [regex]::Match((Get-Content $modCs -Raw), 'ReleaseTag\s*=\s*"([^"]+)"')
    if ($m.Success) { $releaseTag = $m.Groups[1].Value }
}
$slug = ($releaseTag -replace "\s+", "-").ToLower()   # "alpha 2" -> "alpha-2"

# --- Stage the allow-listed content ----------------------------------------
$staging = Join-Path $env:TEMP ("ckaccess-release-" + [guid]::NewGuid().ToString("N"))
$payload = Join-Path $staging "CoreKeeperAccess"
New-Item -ItemType Directory -Path $payload -Force | Out-Null

foreach ($f in $includeFiles) {
    $src = Join-Path $repoRoot $f
    if (Test-Path $src) {
        Copy-Item $src -Destination $payload
    } else {
        Write-Host "WARNING: listed file missing, skipped: $f"
    }
}
foreach ($d in $includeDirs) {
    $src = Join-Path $repoRoot $d
    if (Test-Path $src) {
        Copy-Item $src -Destination $payload -Recurse
    } else {
        Write-Host "WARNING: listed folder missing, skipped: $d"
    }
}

# --- Belt and braces: strip anything that must never ship ------------------
# dev.flag (a fast-build can drop one inside dist), and accessibility preview HTML.
Get-ChildItem -Path $payload -Recurse -Force -Include "dev.flag", "*.preview.html" -ErrorAction SilentlyContinue |
    Remove-Item -Force -ErrorAction SilentlyContinue

# --- Zip --------------------------------------------------------------------
if (-not $OutDir) { $OutDir = Join-Path $repoRoot "release" }
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
$zip = Join-Path $OutDir ("CoreKeeperAccess-" + $slug + ".zip")
if (Test-Path $zip) { Remove-Item $zip -Force }

Compress-Archive -Path (Join-Path $payload "*") -DestinationPath $zip
Remove-Item $staging -Recurse -Force

$size = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host ""
Write-Host "Release packaged: $zip ($size MB)"
Write-Host "Release tag: $releaseTag"
