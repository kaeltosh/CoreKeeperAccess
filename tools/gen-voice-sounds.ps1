<#
.SYNOPSIS
    Genere les WAV de voix pre-rendue du mod, une langue par sous-dossier.

.DESCRIPTION
    Le mod parle normalement via Tolk/NVDA. Une seule famille d'annonces a son propre
    canal audio (volume reglable, hors file du lecteur d'ecran) : les paliers de vie de
    boss. Ces fichiers sont donc du TTS Windows fige en WAV.

    Ce script les (re)genere depuis les traductions du mod, pour n'importe quelle langue
    disposant d'une voix SAPI installee. Les textes viennent de
    Conf/Localization/<langue>.json, cles "voice.*" :
        "voice.hp.70": "70 pour cent"   ->   Sounds/voice/fr/hp_70.wav

    Une langue sans voix installee est simplement sautee : a l'execution, le mod se
    rabat sur l'anglais, puis sur la parole normale. Rien a coder pour l'ajouter.

.PARAMETER Lang
    Codes de langue a traiter (defaut : tous les JSON de Conf/Localization).

.PARAMETER Rate
    Debit de la synthese, -10 a 10 (defaut 3 : rapide, pour ne pas manger la fenetre
    d'action en plein combat).

.PARAMETER Force
    Regenere aussi les fichiers deja presents (sinon ils sont conserves tels quels -
    les WAV francais ont ete valides a l'oreille en combat reel).

.EXAMPLE
    .\gen-voice-sounds.ps1
    .\gen-voice-sounds.ps1 -Lang en -Force
#>
param(
    [string[]] $Lang,
    [int] $Rate = 3,
    [switch] $Force
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Speech

$repoRoot  = Split-Path -Parent $PSScriptRoot
$modSource = Join-Path $repoRoot "CoreKeeperModSDK\Assets\CoreKeeperAccess"
$locDir    = Join-Path $modSource "Conf\Localization"
$outRoot   = Join-Path $modSource "Sounds\voice"

if (-not (Test-Path $locDir)) { throw "Dossier de traductions introuvable : $locDir" }

if (-not $Lang) {
    $Lang = Get-ChildItem -Path $locDir -Filter *.json -File | ForEach-Object { $_.BaseName }
}

$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
$synth.Rate = $Rate

# "Nvda Sapi" est un pont vers le lecteur d'ecran, pas une vraie voix : il ne sait pas
# ecrire dans un fichier. On l'ecarte d'office.
$installed = $synth.GetInstalledVoices() |
    Where-Object { $_.Enabled -and $_.VoiceInfo.Name -notmatch 'Nvda' }

$fmt = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(
    44100,
    [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen,
    [System.Speech.AudioFormat.AudioChannel]::Mono)

$totalMade = 0
$totalKept = 0

foreach ($code in $Lang) {
    $jsonPath = Join-Path $locDir "$code.json"
    if (-not (Test-Path $jsonPath)) { Write-Warning "$code : pas de $code.json, ignore"; continue }

    $voice = $installed |
        Where-Object { $_.VoiceInfo.Culture.TwoLetterISOLanguageName -eq $code } |
        Select-Object -First 1
    if (-not $voice) {
        Write-Warning "$code : aucune voix SAPI installee -> saute (le mod parlera via le lecteur d'ecran)"
        continue
    }
    $synth.SelectVoice($voice.VoiceInfo.Name)

    $json = Get-Content -Path $jsonPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $entries = $json.PSObject.Properties | Where-Object { $_.Name -like 'voice.*' }
    if (-not $entries) { Write-Warning "$code : aucune cle voice.* dans $code.json"; continue }

    $outDir = Join-Path $outRoot $code
    if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }

    $made = 0
    $kept = 0
    foreach ($e in $entries) {
        $name = ($e.Name.Substring(6) -replace '\.', '_')
        $path = Join-Path $outDir "$name.wav"
        if ((Test-Path $path) -and (-not $Force)) { $kept++; continue }

        $synth.SetOutputToWaveFile($path, $fmt)
        $synth.Speak($e.Value)
        $synth.SetOutputToNull()
        $made++
    }

    Write-Host "$code ($($voice.VoiceInfo.Name)) : $made genere(s), $kept conserve(s) -> $outDir"
    $totalMade += $made
    $totalKept += $kept
}

$synth.Dispose()
Write-Host "Total : $totalMade genere(s), $totalKept conserve(s)."
