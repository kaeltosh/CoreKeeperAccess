[CmdletBinding()]
param(
    [string]$ModSource = "C:\Users\flame\Documents\core keeper\CoreKeeperModSDK\Assets\CoreKeeperAccess",
    [string]$GamePath  = "C:\Program Files (x86)\Steam\steamapps\common\Core Keeper",
    [string]$ModName   = "CoreKeeperAccess",
    [switch]$NoLaunch,
    [switch]$NoCheck,
    [switch]$NoDev
)

$ErrorActionPreference = "Stop"

function Beep-Success { [System.Media.SystemSounds]::Asterisk.Play() }
function Beep-Fail    { [System.Media.SystemSounds]::Hand.Play() }

$installDir   = Join-Path $GamePath  "CoreKeeper_Data\StreamingAssets\Mods\$ModName"
$manifestPath = Join-Path $installDir "ModManifest.json"

if (-not (Test-Path $ModSource)) {
    Write-Host "ERREUR: source introuvable: $ModSource"
    Beep-Fail
    exit 1
}

# Filet de securite : valider la compile avant de deployer (debrayable via -NoCheck).
if (-not $NoCheck) {
    $checkScript = Join-Path $PSScriptRoot "check-compile.ps1"
    if (Test-Path $checkScript) {
        & $checkScript -ModSource $ModSource -GamePath $GamePath -NoBeep
        if ($LASTEXITCODE -ne 0) {
            Write-Host "Deploiement annule (check-compile a echoue). -NoCheck pour forcer."
            Beep-Fail
            exit 3
        }
    } else {
        Write-Host "Note: check-compile.ps1 introuvable, deploiement sans verif."
    }
}
if (-not (Test-Path $manifestPath)) {
    Write-Host "ERREUR: $manifestPath introuvable. Build Unity initial requis."
    Beep-Fail
    exit 1
}

$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
$declaredPaths = [System.Collections.Generic.HashSet[string]]::new()
foreach ($f in $manifest.files) { [void]$declaredPaths.Add($f.path) }

$script:copied     = 0
$script:undeclared = New-Object System.Collections.Generic.List[string]

function Copy-One($srcFile, $destFile, $relForManifest) {
    $destDir = Split-Path $destFile -Parent
    if (-not (Test-Path $destDir)) {
        New-Item -ItemType Directory -Path $destDir -Force | Out-Null
    }
    Copy-Item -Path $srcFile -Destination $destFile -Force
    $script:copied++
    if (-not $declaredPaths.Contains($relForManifest)) {
        $script:undeclared.Add($relForManifest)
    }
}

# Scripts: tous les .cs sauf ceux sous un dossier Editor/
Get-ChildItem -Path $ModSource -Filter "*.cs" -Recurse -File | ForEach-Object {
    $rel = $_.FullName.Substring($ModSource.Length).TrimStart('\','/')
    $relNorm = ($rel -replace '\\','/')
    if ($relNorm -match '(^|/)Editor/') { return }
    $destFile = Join-Path $installDir (Join-Path "Scripts" $rel)
    Copy-One $_.FullName $destFile "Scripts/$relNorm"
}

# Conf/*.json
$confSrc = Join-Path $ModSource "Conf"
if (Test-Path $confSrc) {
    Get-ChildItem -Path $confSrc -Filter "*.json" -Recurse -File | ForEach-Object {
        $rel = $_.FullName.Substring($confSrc.Length).TrimStart('\','/')
        $relNorm = ($rel -replace '\\','/')
        $destFile = Join-Path (Join-Path $installDir "Conf") $rel
        Copy-One $_.FullName $destFile "Conf/$relNorm"
    }
}

# Localization/*.csv (compat I2 si on en utilise un jour)
$locSrc = Join-Path $ModSource "Localization"
if (Test-Path $locSrc) {
    Get-ChildItem -Path $locSrc -Filter "*.csv" -Recurse -File | ForEach-Object {
        $rel = $_.FullName.Substring($locSrc.Length).TrimStart('\','/')
        $relNorm = ($rel -replace '\\','/')
        $destFile = Join-Path (Join-Path $installDir "Localization") $rel
        Copy-One $_.FullName $destFile "Localization/$relNorm"
    }
}

Write-Host "Copies: $($script:copied) fichier(s)"

if ($script:undeclared.Count -gt 0) {
    Write-Host ""
    Write-Host "ATTENTION: fichier(s) non declare(s) dans ModManifest.json :"
    foreach ($p in $script:undeclared) { Write-Host "  - $p" }
    Write-Host "Refaire un build Unity au moins une fois pour les declarer."
    Beep-Fail
    exit 2
}

# Mode dev du mod : fichier-temoin dev.flag dans le dossier d'install (auto-load
# monde 1/perso 1 + skip logos). Pose par defaut pour le cycle dev local ; -NoDev le
# retire pour tester le comportement release (menu complet, intro). Le fichier n'est
# jamais dans l'artefact distribue : seul ce script le cree, sur cette machine.
$devFlag = Join-Path $installDir "dev.flag"
if ($NoDev) {
    if (Test-Path $devFlag) {
        Remove-Item $devFlag -Force
        Write-Host "Mode dev : dev.flag retire (comportement release)."
    }
} elseif (-not (Test-Path $devFlag)) {
    Set-Content -Path $devFlag -Encoding utf8 -Value "Mode dev CoreKeeperAccess : auto-load monde 1/perso 1 + skip logos studio. Supprimer ce fichier (ou fast-build -NoDev) pour le comportement release."
    Write-Host "Mode dev : dev.flag pose (auto-load actif)."
}

Write-Host "OK : mod a jour."
Beep-Success

# Lancement du jeu par defaut (le ModLoader compile le code au demarrage, donc tout
# build doit relancer le jeu pour prendre effet). -NoLaunch pour deployer sans lancer.
# On ferme proprement l'instance en cours d'abord (sinon ancien code / double instance).
if (-not $NoLaunch) {
    $exe = Join-Path $GamePath "CoreKeeper.exe"
    if (Test-Path $exe) {
        $running = Get-Process -Name "CoreKeeper" -ErrorAction SilentlyContinue
        if ($running) {
            Write-Host "Fermeture de l'instance en cours..."
            foreach ($p in $running) { try { $p.CloseMainWindow() | Out-Null } catch {} }
            Start-Sleep -Milliseconds 1500
            Get-Process -Name "CoreKeeper" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        }
        Write-Host "Lancement: $exe"
        Start-Process -FilePath $exe
    } else {
        Write-Host "Note: $exe introuvable, jeu non lance."
    }
}
