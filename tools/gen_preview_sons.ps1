# Generateur de previews audio pour le sonar d'interstices (a11y Core Keeper).
# Produit des boucles de 3 s (WAV 16 bits mono, 44,1 kHz) pour ECOUTER les timbres
# avant de les implementer dans le mod. 6 sons : 3 couleurs (nord clair / neutre /
# sud sombre, = filtrage du bruit rose) x 2 textures (mur mat / trou clapotis).
# Reutilisable pour iterer : ajuster les coefficients de filtre / la modulation ici.

$rate = 44100
$dur  = 3.0
$n    = [int]($rate * $dur)
$out  = "C:\Users\flame\Documents\ck2\preview_sons"
New-Item -ItemType Directory -Force $out | Out-Null

$rng = New-Object System.Random 12345

# --- 1) Bruit rose de base (filtre de Paul Kellet, ~-3 dB/octave) ---
$pink = New-Object 'double[]' $n
$b0=0.0;$b1=0.0;$b2=0.0;$b3=0.0;$b4=0.0;$b5=0.0;$b6=0.0
for ($i=0; $i -lt $n; $i++) {
    $w = $rng.NextDouble()*2.0 - 1.0
    $b0 = 0.99886*$b0 + $w*0.0555179
    $b1 = 0.99332*$b1 + $w*0.0750759
    $b2 = 0.96900*$b2 + $w*0.1538520
    $b3 = 0.86650*$b3 + $w*0.3104856
    $b4 = 0.55000*$b4 + $w*0.5329522
    $b5 = -0.7616*$b5 - $w*0.0168980
    $pink[$i] = $b0+$b1+$b2+$b3+$b4+$b5+$b6 + $w*0.5362
    $b6 = $w*0.115926
}

# --- Filtres one-pole ---
function LowPass([double[]]$x,[double]$a){
    $y = New-Object 'double[]' $x.Length; $p=0.0
    for($i=0;$i -lt $x.Length;$i++){ $p = $p + $a*($x[$i]-$p); $y[$i]=$p }
    ,$y
}
function HighPass([double[]]$x,[double]$a){
    $lp = LowPass $x $a
    $y = New-Object 'double[]' $x.Length
    for($i=0;$i -lt $x.Length;$i++){ $y[$i] = $x[$i]-$lp[$i] }
    ,$y
}

# --- Couleurs (axe nord-sud) ---
$neutre = $pink                    # est / ouest : rose median
$sombre = LowPass  $pink 0.10      # sud  : passe-bas appuye -> grave, feutre
$clair  = HighPass $pink 0.45      # nord : enleve le bas -> aigu, clair

# Adoucissement global (passe-bas doux) : rabote les aigus sifflants, timbre plus
# rond / feutre, sans casser le contraste de couleur nord-sud. Coeff plus petit = plus doux.
$soft   = 0.25
$neutre = LowPass $neutre $soft
$sombre = LowPass $sombre $soft
$clair  = LowPass $clair  0.055    # nord : adouci plus fort (trop agressif/sifflant sinon)

# --- Texture "clapotis" (trou / eau) : modulation d'amplitude lente irreguliere ---
function Clapotis([double[]]$x){
    $y = New-Object 'double[]' $x.Length
    for($i=0;$i -lt $x.Length;$i++){
        $t = $i/$script:rate
        $m = 0.5 + 0.5*( 0.6*[math]::Sin(2*[math]::PI*2.3*$t) + 0.4*[math]::Sin(2*[math]::PI*0.7*$t) )
        if($m -lt 0){$m=0}; if($m -gt 1){$m=1}
        $y[$i] = $x[$i]*$m
    }
    ,$y
}

# --- Ecriture WAV 16 bits mono, normalisee, avec fondu 10 ms aux bords ---
function Write-Wav([string]$path,[double[]]$s,[double]$trim=1.0){
    # Normalisation par RMS (energie moyenne = loudness percue) vers une cible commune,
    # pour que tous les bruits sonnent a la MEME force quelle que soit leur couleur.
    # Garde anti-saturation : on plafonne le gain pour que la crete ne depasse pas 0.9.
    $sum=0.0; $max=0.0
    for($i=0;$i -lt $s.Length;$i++){ $v=$s[$i]; $sum+=$v*$v; $a=[math]::Abs($v); if($a -gt $max){$max=$a} }
    $rms=[math]::Sqrt($sum/$s.Length)
    if($rms -lt 1e-9){$rms=1.0}; if($max -lt 1e-9){$max=1.0}
    $targetRms=0.15
    $g = $targetRms/$rms
    $peakCap = 0.9/$max
    if($g -gt $peakCap){ $g = $peakCap }
    # Atterissage perceptif : compense la sensibilite de l'oreille (un bruit clair
    # sonne plus fort qu'un grave a RMS egal). 1.0 = neutre, < 1 = attenue.
    $g = $g * $trim
    $fade=[int]($script:rate*0.01)
    $fs = New-Object System.IO.FileStream($path,[System.IO.FileMode]::Create)
    $bw = New-Object System.IO.BinaryWriter($fs)
    $dataLen = $s.Length*2
    $bw.Write([System.Text.Encoding]::ASCII.GetBytes('RIFF'))
    $bw.Write([int](36+$dataLen))
    $bw.Write([System.Text.Encoding]::ASCII.GetBytes('WAVE'))
    $bw.Write([System.Text.Encoding]::ASCII.GetBytes('fmt '))
    $bw.Write([int]16)
    $bw.Write([int16]1)                          # PCM
    $bw.Write([int16]1)                          # mono
    $bw.Write([int]$script:rate)
    $bw.Write([int]($script:rate*2))             # byte rate
    $bw.Write([int16]2)                          # block align
    $bw.Write([int16]16)                         # bits
    $bw.Write([System.Text.Encoding]::ASCII.GetBytes('data'))
    $bw.Write([int]$dataLen)
    for($i=0;$i -lt $s.Length;$i++){
        $v=$s[$i]*$g
        if($i -lt $fade){ $v *= $i/$fade }
        elseif($i -ge $s.Length-$fade){ $v *= ($s.Length-1-$i)/$fade }
        if($v -gt 1){$v=1}; if($v -lt -1){$v=-1}
        $bw.Write([int16][math]::Round($v*32767))
    }
    $bw.Close(); $fs.Close()
}

# Trim perceptif du nord (clair) : il sonne plus fort a RMS egal -> on l'attenue.
# 0.7 ~ -3 dB. Baisser encore si toujours trop present.
$nordTrim = 0.7

Write-Wav (Join-Path $out 'mur_nord.wav')    $clair             $nordTrim
Write-Wav (Join-Path $out 'mur_neutre.wav')  $neutre
Write-Wav (Join-Path $out 'mur_sud.wav')     $sombre
Write-Wav (Join-Path $out 'trou_nord.wav')   (Clapotis $clair)  $nordTrim
Write-Wav (Join-Path $out 'trou_neutre.wav') (Clapotis $neutre)
Write-Wav (Join-Path $out 'trou_sud.wav')    (Clapotis $sombre)

Write-Output ("OK : 6 fichiers dans " + $out)
