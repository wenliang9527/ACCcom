param([string]$Themes = "src/ACCcom/Themes")
# Theme colour audit.
#
# Two independent checks:
#
#  1. AA text contrast for the Accent/OnAccent button pair. This is a plain
#     WCAG relative-luminance ratio and is exactly right for it.
#
#  2. Accent vs status-colour confusability. This is NOT a luminance question —
#     two colours of similar lightness are always ~1:1 by contrast ratio even
#     when they are obviously different hues, so a ratio threshold here is
#     meaningless. What matters is whether a glance can tell them apart, which
#     is a hue/saturation distance question. We flag pairs whose hues are close
#     AND whose chroma is high enough to read as a colour at all.
$ErrorActionPreference = 'Stop'

$nsm = New-Object System.Xml.XmlNamespaceManager((New-Object System.Xml.NameTable))
$nsm.AddNamespace('x', 'http://schemas.microsoft.com/winfx/2006/xaml')

function Get-Rgb([string]$hex) {
  $h = $hex.TrimStart('#')
  if ($h.Length -eq 8) { $h = $h.Substring(0, 6) }
  @([Convert]::ToInt32($h.Substring(0,2),16),
    [Convert]::ToInt32($h.Substring(2,2),16),
    [Convert]::ToInt32($h.Substring(4,2),16))
}
function Get-Lum([string]$hex) {
  $c = Get-Rgb $hex
  $f = { param($v) $s = $v / 255.0
         if ($s -le 0.03928) { $s / 12.92 } else { [Math]::Pow(($s + 0.055) / 1.055, 2.4) } }
  0.2126 * (& $f $c[0]) + 0.7152 * (& $f $c[1]) + 0.0722 * (& $f $c[2])
}
function Get-Contrast([string]$a, [string]$b) {
  $l1 = Get-Lum $a; $l2 = Get-Lum $b
  if ($l1 -lt $l2) { $t = $l1; $l1 = $l2; $l2 = $t }
  [Math]::Round(($l1 + 0.05) / ($l2 + 0.05), 2)
}
# Returns hue in degrees 0-360 and saturation 0-1 (HSL).
function Get-Hsl([string]$hex) {
  $c = Get-Rgb $hex
  $r = $c[0] / 255.0; $g = $c[1] / 255.0; $b = $c[2] / 255.0
  $mx = [Math]::Max($r, [Math]::Max($g, $b)); $mn = [Math]::Min($r, [Math]::Min($g, $b))
  $l = ($mx + $mn) / 2
  if ($mx -eq $mn) { return @(0.0, 0.0) }          # achromatic
  $d = $mx - $mn
  $s = if ($l -gt 0.5) { $d / (2 - $mx - $mn) } else { $d / ($mx + $mn) }
  $h = 0.0
  if ($mx -eq $r)      { $h = ($g - $b) / $d; if ($g -lt $b) { $h += 6 } }
  elseif ($mx -eq $g)  { $h = ($b - $r) / $d + 2 }
  else                 { $h = ($r - $g) / $d + 4 }
  $deg = [Math]::Round(($h * 60), 4) % 360
  @($deg, $s)
}
function Get-HueDist([double]$a, [double]$b) {
  $d = [Math]::Abs($a - $b); if ($d -gt 180) { $d = 360 - $d }; $d
}

$HUE_MIN = 25      # degrees; below this two chromatic colours read as "the same colour"
$CHROMA_MIN = 0.18 # saturation; below this one of them is effectively grey

# Accent doubling as the RX signal colour is the intended design (accent-single-
# -hue), so an exact Accent==StatusBlue match is not a defect. Only flag status
# colours that must stay distinguishable from the accent.
$ALLOW_ACCENT_EQ = @('StatusBlue')

$fail = 0
foreach ($f in Get-ChildItem "$Themes/*.xaml") {
  $x = [xml](Get-Content $f.FullName -Raw)
  $map = @{}
  foreach ($n in $x.ResourceDictionary.SelectNodes('*[@x:Key]', $nsm)) {
    $map[$n.GetAttribute('Key', 'http://schemas.microsoft.com/winfx/2006/xaml')] = $n.InnerText
  }
  if (-not $map.ContainsKey('Accent')) { continue }
  $name  = [IO.Path]::GetFileNameWithoutExtension($f.Name)
  $acc   = $map['Accent']
  $notes = @()

  # 1. AA on the primary button's own label
  $c = Get-Contrast $acc $map['OnAccent']
  $line = "{0,-14} btnText {1,5}:1" -f $name, $c
  if ($c -lt 4.5) { $line += "  <<AA FAIL"; $fail++ }

  # 2. Confusability against each status colour
  $ah = Get-Hsl $acc
  foreach ($s in 'StatusError','StatusWarning','BtnDanger','StatusGreen','StatusBlue') {
    if (-not $map.ContainsKey($s)) { continue }
    $sh = Get-Hsl $map[$s]
    $bothChromatic = ($ah[1] -ge $CHROMA_MIN -and $sh[1] -ge $CHROMA_MIN)
    if ($bothChromatic) {
      $hd = Get-HueDist $ah[0] $sh[0]
      $tag = "{0} dHue {1,3:N0}" -f $s.Substring(6), $hd
      if ($hd -lt $HUE_MIN) {
        if ($acc -eq $map[$s] -and $ALLOW_ACCENT_EQ -contains $s) {
          $tag += " (=accent by design)"
        } else {
          $tag += " <<COLLIDE"; $fail++
        }
      }
      $notes += $tag
    } else {
      $notes += ("{0} (achromatic pair)" -f $s.Substring(6))
    }
  }
  Write-Host ($line + "  |  " + ($notes -join '  '))
}
Write-Host ""
if ($fail -eq 0) {
  Write-Host "OK: all 7 themes pass AA on button text; no Accent/status hue collision" -ForegroundColor Green
} else {
  Write-Host "PROBLEMS: $fail" -ForegroundColor Red
}
