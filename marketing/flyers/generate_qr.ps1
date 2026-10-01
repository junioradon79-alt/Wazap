$dllPath = "c:\Dev\Wazap\WazapSln\src\Wazap.API\bin\Debug\net10.0\QRCoder.dll"
Add-Type -Path $dllPath

$url = "https://wa.me/2250575803801?text=je%20veux%20livrer"
$generator = New-Object QRCoder.QRCodeGenerator
$eccLevel = [QRCoder.QRCodeGenerator+ECCLevel]::H
$qrData = $generator.CreateQrCode($url, $eccLevel)

$qr = New-Object QRCoder.PngByteQRCode($qrData)
# Noir sur fond blanc avec marge propre
$bytes = $qr.GetGraphic(20, $true)

$outPath = "c:\Dev\Wazap\marketing\flyers\qr_recrutement_whatsapp.png"
[System.IO.File]::WriteAllBytes($outPath, $bytes)

Write-Output "QR Code généré avec succès dans : $outPath"
