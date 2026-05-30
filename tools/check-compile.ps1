[CmdletBinding()]
param(
    [string]$ModSource = "C:\Users\flame\Documents\core keeper\CoreKeeperModSDK\Assets\CoreKeeperAccess",
    [string]$GamePath  = "C:\Program Files (x86)\Steam\steamapps\common\Core Keeper",
    [string]$UnityRoot = "C:\Program Files\Unity\Hub\Editor\6000.0.59f2",
    [switch]$Quiet,
    [switch]$NoBeep
)

# Valide la compilation des .cs du mod SANS lancer le jeu ni Unity.
# Reproduit l'environnement du ModLoader : meme csc (Roslyn d'Unity) +
# tout le dossier Managed du jeu reference en bloc + Harmony du SDK.
# Compile-only (-target:library vers un temp jete). Beep + exit code pass/fail.
#   exit 0 = OK, 1 = erreurs de compile, 2 = environnement manquant.

$ErrorActionPreference = "Stop"

function Beep-Success { if (-not $NoBeep) { [System.Media.SystemSounds]::Asterisk.Play() } }
function Beep-Fail    { if (-not $NoBeep) { [System.Media.SystemSounds]::Hand.Play() } }

$managed     = Join-Path $GamePath  "CoreKeeper_Data\Managed"
$harmonyDir  = Join-Path $ModSource "..\Plugins\CoreKeeperModSDK\Harmony"
$harmonyDir  = (Resolve-Path -LiteralPath $harmonyDir -ErrorAction SilentlyContinue).Path
$cscDll      = Join-Path $UnityRoot "Editor\Data\DotNetSdkRoslyn\csc.dll"

# --- Verifs d'environnement ---
foreach ($p in @(@($ModSource,"source du mod"), @($managed,"dossier Managed du jeu"), @($cscDll,"csc Roslyn d'Unity"))) {
    if (-not (Test-Path $p[0])) {
        Write-Host "ERREUR: $($p[1]) introuvable: $($p[0])"
        Beep-Fail
        exit 2
    }
}

# --- Rassembler sources et references ---
# Sources: tous les .cs du mod sauf ceux sous Editor/ (cote SDK, pas embarques dans le mod).
$sources = Get-ChildItem -Path $ModSource -Filter "*.cs" -Recurse -File | Where-Object {
    ($_.FullName.Substring($ModSource.Length) -replace '\\','/') -notmatch '(^|/)Editor/'
}
if ($sources.Count -eq 0) {
    Write-Host "ERREUR: aucun .cs trouve dans $ModSource"
    Beep-Fail
    exit 2
}

$refs = @(Get-ChildItem -Path $managed -Filter "*.dll" -File)
if ($harmonyDir) {
    $refs += Get-ChildItem -Path $harmonyDir -Filter "*.dll" -File
}

# --- Construire le response file (evite la limite de longueur de ligne) ---
$tmpDll = Join-Path $env:TEMP "ckaccess_check.dll"
$rsp    = Join-Path $env:TEMP "ckaccess_check.rsp"

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("-nostdlib+")            # mscorlib vient du jeu, pas du Mono de csc
$lines.Add("-target:library")
$lines.Add("-nologo")
$lines.Add("-nowarn:CS1701,CS1702") # bruit de versions d'assembly, sans interet ici
$lines.Add("-unsafe+")
$lines.Add("-langversion:latest")
$lines.Add("-out:`"$tmpDll`"")
foreach ($r in $refs)    { $lines.Add("-reference:`"$($r.FullName)`"") }
foreach ($s in $sources) { $lines.Add("`"$($s.FullName)`"") }
Set-Content -Path $rsp -Value $lines -Encoding UTF8

# --- Compiler ---
if (-not $Quiet) { Write-Host "Verif compile: $($sources.Count) source(s), $($refs.Count) reference(s)..." }
$output = & dotnet $cscDll "@$rsp" 2>&1
$code   = $LASTEXITCODE

Remove-Item $rsp, $tmpDll -ErrorAction SilentlyContinue

# --- Rapport ---
$errors = $output | Where-Object { $_ -match ': error ' }
if ($code -eq 0 -and $errors.Count -eq 0) {
    if (-not $Quiet) { Write-Host "OK : le code compile." }
    Beep-Success
    exit 0
}

Write-Host ""
Write-Host "ECHEC compile :"
foreach ($e in $errors) { Write-Host "  $e" }
if ($errors.Count -eq 0) {
    # erreur sans ligne ': error ' (ex: probleme csc lui-meme) -> tout dumper
    $output | ForEach-Object { Write-Host "  $_" }
}
Beep-Fail
exit 1
