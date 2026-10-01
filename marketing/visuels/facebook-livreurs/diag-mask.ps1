Add-Type -AssemblyName System.Drawing

foreach ($name in 'moto', 'phone') {
    $src = [System.Drawing.Bitmap]::FromFile("c:\Dev\Wazap\marketing\visuels\facebook-livreurs\assets\$name.png")
    $mask = New-Object System.Drawing.Bitmap($src.Width, $src.Height)
    for ($y = 0; $y -lt $src.Height; $y++) {
        for ($x = 0; $x -lt $src.Width; $x++) {
            $a = $src.GetPixel($x, $y).A
            $v = if ($a -gt 0) { 0 } else { 255 }
            $mask.SetPixel($x, $y, [System.Drawing.Color]::FromArgb($v, $v, $v))
        }
    }
    $out = Join-Path $env:TEMP ("mask_" + $name + ".png")
    $mask.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
    $mask.Dispose()
    $src.Dispose()
    $out
}
