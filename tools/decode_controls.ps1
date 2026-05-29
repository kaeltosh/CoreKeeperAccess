# Décode CoreKeeper_Controls.json (Rewired) -> HTML accessible (tableaux par contexte).
# Manette uniquement (controllerMapType=2). Source de vérité = le fichier de config réel.
$path = 'C:\Users\flame\AppData\LocalLow\Pugstorm\Core Keeper\CoreKeeper_Controls.json'
$out  = 'C:\Users\flame\Documents\core keeper\mapping_manette.html'

$json = Get-Content -Raw -Path $path | ConvertFrom-Json

# actionId -> nom (issu de l'enum PlayerInput.InputType + déductions). "inutile a11y" = candidat au remappage.
$actionNames = @{
  0='Déplacement horizontal'; 1='Déplacement vertical'; 2='Interagir (INTERACT)'; 3='Interagir secondaire'; 4='Nav UI'; 5='Menu / Pause'; 6='Nav UI'; 7='Nav UI';
  15='Annuler (CANCEL)'; 17='Slot suivant'; 47='Slot précédent';
  46='Slot équipé 1'; 19='Slot équipé 2'; 20='Slot équipé 3'; 21='Slot équipé 4'; 48='Slot équipé 5'; 49='Slot équipé 6'; 50='Slot équipé 7'; 51='Slot équipé 8'; 52='Slot équipé 9'; 53='Slot équipé 10';
  54='Inventaire (TOGGLE_INVENTORY)'; 55='Carte (TOGGLE_MAP) — inutile a11y';
  59='Visée horizontale'; 60='Visée verticale';
  68='Interagir avec objet'; 87='Lâcher objet sélectionné'; 90='Déplacer objets (quick move)';
  92='Zoom carte + — inutile a11y'; 93='Zoom carte - — inutile a11y'; 94='Ouvrir le chat';
  98='Ramasser 10'; 99='Ramasser la moitié'; 101='Changer de torche (quick swap)'; 104='Spectateur (joueur observé)';
  105='UI interagir'; 106='UI interagir secondaire'; 107='Ramasser objets'; 108='Tout ramasser';
  109='Trier (SORT)'; 110='Ping carte — inutile a11y'; 111='Empiler vite (quick stack)'; 112='Utiliser main gauche (off-hand)';
  113='Marqueur carte suivant — inutile a11y'; 114='Marqueur carte précédent — inutile a11y';
  115='Accélérer véhicule'; 116='Reculer véhicule'; 117='Klaxon — inutile a11y';
  207='Pivoter (placement)'; 208='Basculer UI'; 209='Jeter objet (poubelle)';
  210='Menu bas'; 211='Menu haut'; 212='Menu gauche'; 213='Menu droite';
  214='Fenêtre raccourcis'; 215='Preset équipement 1'; 216='Preset équipement 2'; 217='Preset équipement 3';
  218='Courir plus vite'; 225='Modificateur swap hotbar'; 226='Verrouillage (toggle)'; 228='Touchpad';
  292='Déplacement horizontal'; 293='Déplacement vertical'; 294='Visée horizontale'; 295='Visée verticale';
  296='Carte horizontale — inutile a11y'; 297='Carte verticale — inutile a11y';
  304='Hotbar suivante'; 305='Hotbar précédente'
}

# elementIdentifierId -> bouton (template Rewired Gamepad). 6-11 + axes = sûrs ; 12-19 = à confirmer en jeu.
$btn = @{
  6='A / Croix'; 7='B / Cercle'; 8='X / Carré'; 9='Y / Triangle'; 10='LB (bumper gauche)'; 11='RB (bumper droit)';
  12='Back / View'; 13='Start / Menu'; 14='L3 (clic stick gauche)'; 15='R3 (clic stick droit)';
  16='D-pad haut'; 17='D-pad droite'; 18='D-pad bas'; 19='D-pad gauche'
}
$ax = @{ 0='Stick gauche (X)'; 1='Stick gauche (Y)'; 2='Stick droit (X)'; 3='Stick droit (Y)'; 4='Gâchette gauche (LT)'; 5='Gâchette droite (RT)' }

$catNames = @{ '0'='Jeu — général'; '9'='Instrument de musique'; '10'='Inventaire / UI'; '11'='Carte (inutile a11y)'; '12'='Véhicule'; '13'='Jeu — monde (déplacement / visée / actions)'; '14'='(vide)' }

function ActName($id){ $i=[int]$id; if($actionNames.ContainsKey($i)){$actionNames[$i]} elseif($i -ge 178 -and $i -le 206){"Note de musique ($i)"} else {"Action $i (non nommée)"} }
function ElemName($type,$id){ $i=[int]$id; if($type -eq 0){ if($ax.ContainsKey($i)){$ax[$i]}else{"Axe $i"} } else { if($btn.ContainsKey($i)){$btn[$i]}else{"Bouton $i"} } }
function RangeNote($r){ switch([int]$r){ 1{' [demi-axe +]'} 2{' [demi-axe -]'} default{''} } }

$cats = [ordered]@{}
foreach($p in $json.PSObject.Properties){
  $n = $p.Name
  if($n -match 'dataType=ControllerMap\|' -and $n -match 'controllerMapType=2' -and $n -notmatch 'KnownActionIds'){
    $catId = [regex]::Match($n,'categoryId=(\d+)').Groups[1].Value
    $map = $p.Value | ConvertFrom-Json
    if(-not $cats.Contains($catId)){ $cats[$catId] = New-Object System.Collections.ArrayList }
    foreach($b in $map.buttonMaps){ [void]$cats[$catId].Add([pscustomobject]@{ el=(ElemName 1 $b.elementIdentifierId); type='Bouton'; act=(ActName $b.actionId) }) }
    foreach($a in $map.axisMaps){ [void]$cats[$catId].Add([pscustomobject]@{ el=((ElemName 0 $a.elementIdentifierId)+(RangeNote $a.axisRange)); type='Axe'; act=(ActName $a.actionId) }) }
  }
}

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('<!DOCTYPE html><html lang="fr"><head><meta charset="utf-8"><title>Mapping manette - Core Keeper</title>')
[void]$sb.AppendLine('<style>table{border-collapse:collapse;margin-bottom:2em}th,td{border:1px solid #888;padding:4px 8px;text-align:left}caption{font-weight:bold;text-align:left;margin-bottom:.3em}</style></head><body>')
[void]$sb.AppendLine('<h1>Mapping manette par défaut - Core Keeper</h1>')
[void]$sb.AppendLine('<p>Source : CoreKeeper_Controls.json (export Rewired). Boutons 6 à 11, sticks et gâchettes = fiables. Mapping physique confirmé en jeu (diagnostic F9). La mention « inutile a11y » signale une action sans valeur pour un joueur non-voyant, donc candidate au remappage.</p>')
foreach($c in $cats.Keys){
  $title = if($catNames.ContainsKey($c)){$catNames[$c]}else{"Catégorie $c"}
  [void]$sb.AppendLine("<h2>$title</h2>")
  [void]$sb.AppendLine("<table><caption>$title (categoryId $c)</caption><thead><tr><th scope=""col"">Élément physique</th><th scope=""col"">Type</th><th scope=""col"">Action</th></tr></thead><tbody>")
  foreach($r in $cats[$c]){ [void]$sb.AppendLine("<tr><td>$($r.el)</td><td>$($r.type)</td><td>$($r.act)</td></tr>") }
  [void]$sb.AppendLine('</tbody></table>')
}
[void]$sb.AppendLine('</body></html>')
Set-Content -Path $out -Value $sb.ToString() -Encoding UTF8
Write-Output "HTML ecrit : $out"
foreach($c in $cats.Keys){ Write-Output ("cat {0} = {1} bindings" -f $c, $cats[$c].Count) }


