param([string]$Path)
Add-Type -AssemblyName System.Drawing
$b = [System.Drawing.Bitmap]::FromFile($Path)
$w = $b.Width; $h = $b.Height
$cols = 56; $rows = 40
$op = 0
for ($y = 0; $y -lt $h; $y++) {
  for ($x = 0; $x -lt $w; $x++) {
    if ($b.GetPixel($x, $y).A -gt 32) { $op++ }
  }
}
"$Path  ${w}x${h}  opaques=$op"
for ($ry = 0; $ry -lt $rows; $ry++) {
  $line = ''
  for ($rx = 0; $rx -lt $cols; $rx++) {
    $x = [Math]::Min($w - 1, [int](($rx + 0.5) * $w / $cols))
    $y = [Math]::Min($h - 1, [int](($ry + 0.5) * $h / $rows))
    $c = $b.GetPixel($x, $y)
    if ($c.A -le 32) { $line += '.' }
    else {
      $l = 0.299 * $c.R + 0.587 * $c.G + 0.114 * $c.B
      if ($l -lt 110) { $line += '#' } else { $line += 'o' }
    }
  }
  $line
}
$b.Dispose()
