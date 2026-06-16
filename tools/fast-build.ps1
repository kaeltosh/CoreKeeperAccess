[CmdletBinding()]
param(
    # Auto-localise : deduit le ModSource depuis l'emplacement du script.
    # Dans chaque worktree, deploie SA propre feature sans -ModSource a la main.
    [string]$ModSource = (Join-Path (Split-Path $PSScriptRoot -Parent) "CoreKeeperModSDK\Assets\CoreKeeperAccess"),
    [string]$GamePath  = "C:\Program Files (x86)\Steam\steamapps\common\Core Keeper",
    [string]$ModName   = "CoreKeeperAccess",
    [switch]$NoLaunch,
    [switch]$NoCheck,
    [switch]$NoDev,
    # Par defaut, l'install est mis en MIROIR des sources de la branche courante
    # (orphelins supprimes + manifest resynchronise). -NoMirror revient au comportement
    # historique (copie seule + avertissement si fichier non declare, sans rien supprimer).
    [switch]$NoMirror
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

$script:copied        = 0
$script:removed       = 0
$script:deployed      = New-Object System.Collections.Generic.List[object]   # {path; guid} reellement deployes
$script:deployedPaths = [System.Collections.Generic.HashSet[string]]::new()

# Guid Unity d'un fichier source (lu dans son .meta voisin). "" si pas de .meta (fichier
# pas encore importe par Unity) - tolere, comme les entrees Generated/Bundles du manifest.
function Get-MetaGuid($srcFile) {
    $meta = "$srcFile.meta"
    if (Test-Path $meta) {
        $raw = Get-Content $meta -Raw
        if ($raw -match 'guid:\s*([0-9a-fA-F]+)') { return $matches[1] }
    }
    return ""
}

function Copy-One($srcFile, $destFile, $relForManifest) {
    $destDir = Split-Path $destFile -Parent
    if (-not (Test-Path $destDir)) {
        New-Item -ItemType Directory -Path $destDir -Force | Out-Null
    }
    Copy-Item -Path $srcFile -Destination $destFile -Force
    $script:copied++
    [void]$script:deployedPaths.Add($relForManifest)
    $script:deployed.Add([pscustomobject]@{ path = $relForManifest; guid = (Get-MetaGuid $srcFile) })
}

# MIROIR (1/2) : supprime de l'install les .cs/.json deployes par un build precedent mais
# absents des sources courantes (residus d'une autre branche). On ne touche PAS a
# Scripts/Generated/ (les .g.cs sont produits par Unity, pas par fast-build).
function Remove-Orphans {
    $scriptsDir = Join-Path $installDir "Scripts"
    if (Test-Path $scriptsDir) {
        Get-ChildItem $scriptsDir -Filter *.cs -Recurse -File | ForEach-Object {
            $rel = "Scripts/" + (($_.FullName.Substring($scriptsDir.Length).TrimStart('\','/')) -replace '\\','/')
            if ($rel -match '(^|/)Generated/') { return }
            if (-not $script:deployedPaths.Contains($rel)) { Remove-Item $_.FullName -Force; $script:removed++ }
        }
    }
    $confDir = Join-Path $installDir "Conf"
    if (Test-Path $confDir) {
        Get-ChildItem $confDir -Filter *.json -Recurse -File | ForEach-Object {
            $rel = "Conf/" + (($_.FullName.Substring($confDir.Length).TrimStart('\','/')) -replace '\\','/')
            if (-not $script:deployedPaths.Contains($rel)) { Remove-Item $_.FullName -Force; $script:removed++ }
        }
    }
}

# MIROIR (2/2) : reecrit le champ "files" du ModManifest.json sur les fichiers REELLEMENT
# presents. On regenere les entrees gerees par fast-build (Scripts/ hors Generated, Conf/)
# depuis les sources, et on PRESERVE telles quelles les entrees qu'il ne gere pas
# (Scripts/Generated/*.g.cs et Bundles/* : produits par Unity). Backup .bak avant ecriture.
function Sync-Manifest {
    $m = Get-Content $manifestPath -Raw | ConvertFrom-Json
    $newFiles = New-Object System.Collections.Generic.List[object]
    foreach ($d in $script:deployed) {
        $newFiles.Add([pscustomobject]@{ path = $d.path; guid = $d.guid })
    }
    foreach ($f in $m.files) {
        $p = $f.path
        $managed = ($p -like 'Conf/*') -or (($p -like 'Scripts/*') -and ($p -notmatch '(^|/)Generated/'))
        if (-not $managed) { $newFiles.Add([pscustomobject]@{ path = $f.path; guid = $f.guid }) }
    }
    $m.files = $newFiles
    Copy-Item $manifestPath "$manifestPath.bak" -Force
    $out = $m | ConvertTo-Json -Depth 8
    [System.IO.File]::WriteAllText($manifestPath, $out, (New-Object System.Text.UTF8Encoding $false))
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

if (-not $NoMirror) {
    # Mode MIROIR (defaut) : l'install reflete EXACTEMENT les sources de la branche
    # courante -> alternance entre branches sure SANS rebuild Unity, tant que les branches
    # ne different pas par un fichier GENERE (nouveau systeme ECS) ou un asset (la, build
    # Unity obligatoire : fast-build ne produit ni les .g.cs ni les Bundles).
    Remove-Orphans
    Sync-Manifest
    Write-Host "Miroir: $($script:removed) orphelin(s) supprime(s), ModManifest resynchronise."
} else {
    # Mode -NoMirror : on ne supprime rien et on ne touche pas au manifest ; on AVERTIT
    # juste si un fichier source n'est pas declare (comportement historique).
    $undeclared = New-Object System.Collections.Generic.List[string]
    foreach ($p in $script:deployedPaths) {
        if (-not $declaredPaths.Contains($p)) { $undeclared.Add($p) }
    }
    if ($undeclared.Count -gt 0) {
        Write-Host ""
        Write-Host "ATTENTION: fichier(s) non declare(s) dans ModManifest.json :"
        foreach ($p in $undeclared) { Write-Host "  - $p" }
        Write-Host "Refaire un build Unity, ou laisser le mode miroir (defaut) gerer."
        Beep-Fail
        exit 2
    }
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
