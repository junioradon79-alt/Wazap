#:package QRCoder@1.8.0
// Genere un QR code (PNG + SVG) hors ligne - meme bibliotheque que l'API WAZAP (QRCoder 1.8.0).
// Usage :
//   dotnet run gen-qr.cs
//   dotnet run gen-qr.cs -- "https://wa.me/2250575803801?text=je%20veux%20livrer" qr-livreur.png 1000
//
// Le 3e argument est la TAILLE CIBLE en pixels (le "pixels par module" est calcule).
using QRCoder;

var target = args.Length > 0 ? args[0] : "https://junioradon79gm-001-site1.jtempurl.com/devenir-livreur";
var outPng = args.Length > 1 ? args[1] : "qr-livreur.png";
var targetPx = args.Length > 2 && int.TryParse(args[2], out var s) ? s : 1000;

var dark = args.Length > 3 ? args[3] : "#15221b";

using var generator = new QRCodeGenerator();
using var qrData = generator.CreateQrCode(target, QRCodeGenerator.ECCLevel.Q);

int modules = qrData.ModuleMatrix.Count;              // inclut les zones de silence
int ppm = Math.Max(1, targetPx / modules);

using var png = new PngByteQRCode(qrData);
var bytes = png.GetGraphic(ppm, drawQuietZones: true);
File.WriteAllBytes(outPng, bytes);

var svgPath = Path.ChangeExtension(outPng, ".svg");
using var svg = new SvgQRCode(qrData);
File.WriteAllText(svgPath, svg.GetGraphic(20, dark, "#ffffff", drawQuietZones: true));

Console.WriteLine($"modules={modules} ppm={ppm} -> {outPng} ({bytes.Length} o) + {svgPath} : {target}");

