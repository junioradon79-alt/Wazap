param([string]$Path)
Add-Type -AssemblyName System.Drawing
$src = [System.Drawing.Bitmap]::FromFile($Path)
$dst = New-Object System.Drawing.Bitmap($src.Width, $src.Height)
$g = [System.Drawing.Graphics]::FromImage($dst)
$g.Clear([System.Drawing.Color]::FromArgb(255, 255, 0, 255))
$g.DrawImage($src, 0, 0, $src.Width, $src.Height)
$g.Dispose()
$out = [IO.Path]::ChangeExtension($Path, $null) + '_check.png'
$dst.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$dst.Dispose(); $src.Dispose()
"$out"
