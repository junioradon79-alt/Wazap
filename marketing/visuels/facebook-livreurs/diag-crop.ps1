Add-Type -AssemblyName System.Drawing
$srcPath = 'c:\Dev\Wazap\marketing\visuels\facebook-livreurs\assets\moto.jpg'
$src = [System.Drawing.Image]::FromFile($srcPath)
"source: $($src.Width)x$($src.Height)"

$regions = @(
    @{ X = 30;  Y = 560; W = 180; H = 150; Tag = 'band' },
    @{ X = 30;  Y = 20;  W = 180; H = 120; Tag = 'top' },
    @{ X = 600; Y = 540; W = 180; H = 150; Tag = 'lowright' }
)

foreach ($r in $regions) {
    $zx = [int]$r.W * 3
    $zy = [int]$r.H * 3
    $srcRect = New-Object System.Drawing.Rectangle([int]$r.X, [int]$r.Y, [int]$r.W, [int]$r.H)
    $dstRect = New-Object System.Drawing.Rectangle(0, 0, $zx, $zy)
    $crop = New-Object System.Drawing.Bitmap($zx, $zy)
    $g = [System.Drawing.Graphics]::FromImage($crop)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
    $g.DrawImage($src, $dstRect, $srcRect, [System.Drawing.GraphicsUnit]::Pixel)
    $g.Dispose()
    $out = Join-Path $env:TEMP ("diag_" + $r.Tag + ".png")
    $crop.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
    $crop.Dispose()
    $out
}
$src.Dispose()
