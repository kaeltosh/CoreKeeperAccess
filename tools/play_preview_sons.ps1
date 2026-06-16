# Joue les previews du sonar a la suite, en annoncant chaque nom (voix Windows SAPI,
# juste pour etiqueter). Ordre : les 3 murs (couleur nord/neutre/sud), puis les 3 trous.
Add-Type -AssemblyName System.Speech
$voice = New-Object System.Speech.Synthesis.SpeechSynthesizer
$dir = "C:\Users\flame\Documents\ck2\preview_sons"
$seq = @(
  @('mur nord',   'mur_nord.wav'),
  @('mur neutre', 'mur_neutre.wav'),
  @('mur sud',    'mur_sud.wav'),
  @('trou nord',  'trou_nord.wav'),
  @('trou neutre','trou_neutre.wav'),
  @('trou sud',   'trou_sud.wav')
)
foreach($item in $seq){
  $voice.Speak($item[0])
  $pl = New-Object System.Media.SoundPlayer (Join-Path $dir $item[1])
  $pl.PlaySync()
  Start-Sleep -Milliseconds 300
}
$voice.Speak('fin')
