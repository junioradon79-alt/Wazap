#:package SkiaSharp@3.116.1
// Detoure un objet centre sur un fond "transparent" aplati en DAMIER (JPG/JPEG).
//
// Methode robuste (mesuree sur les visuels WAZAP) : le fond est NEUTRE (R~G~B) et
// CLAIR, meme dans sa zone d'ombre (>= ~120), alors que les parties sombres de
// l'objet sont bien plus foncees (< ~80) et ses parties colorees saturees. Il y a
// donc un VALLON de luminance net. On classe par neutralite + luminance, puis on
// remplit depuis les BORDS : le fond est connexe et touche le cadre, si bien que
// les fragments residuels et les zones ombrees partent avec lui.
//
// Usage : dotnet run cutout.cs -- phone.jpg phone.png [floorLum] [pad]
using System.Globalization;
using SkiaSharp;

var input = args.Length > 0 ? args[0] : "phone.jpg";
var output = args.Length > 1 ? args[1] : "phone.png";
var floor = args.Length > 2 && double.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var fl) ? fl : 95.0;
var pad = args.Length > 3 && int.TryParse(args[3], out var pd) ? pd : 6;

using var src = SKBitmap.Decode(input);
if (src is null) { Console.Error.WriteLine($"Decodage impossible : {input}"); return 1; }

int w = src.Width, h = src.Height;
var px = src.Pixels;
var n = w * h;

// 1) Fond probable : pixel NEUTRE et ASSEZ CLAIR (le damier, meme ombre, reste plus
//    clair que le sombre de l'objet ; un pixel colore appartient a l'objet).
var bgLike = new bool[n];
for (var i = 0; i < n; i++)
{
    var c = px[i];
    int mx = Math.Max(c.Red, Math.Max(c.Green, c.Blue));
    int mn = Math.Min(c.Red, Math.Min(c.Green, c.Blue));
    if (mx - mn > 24) continue;                         // sature => objet (rouge du scooter, logo…)
    double lum = 0.299 * c.Red + 0.587 * c.Green + 0.114 * c.Blue;
    bgLike[i] = lum >= floor;
}

// 2) Remplissage depuis les bords (4-connexite) : absorbe tout le fond connexe,
//    y compris les zones ombrees et les fragments residuels du damier.
var isBg = new bool[n];
var stack = new Stack<int>();

void Seed(int i)
{
    if (!isBg[i] && bgLike[i]) { isBg[i] = true; stack.Push(i); }
}

for (var x = 0; x < w; x++) { Seed(x); Seed((h - 1) * w + x); }
for (var y = 0; y < h; y++) { Seed(y * w); Seed(y * w + w - 1); }

while (stack.Count > 0)
{
    var i = stack.Pop();
    var x = i % w; var y = i / w;
    if (x > 0) Seed(i - 1);
    if (x < w - 1) Seed(i + 1);
    if (y > 0) Seed(i - w);
    if (y < h - 1) Seed(i + w);
}

// 3) Poches de fond ENFERMEES (entre les rayons de la roue, entre les deux telephones…) :
//    une composante non-fond qui n'est presque QUE du fond est du damier piege -> on l'efface.
{
    var seen = new bool[n];
    var members = new List<int>();
    for (var start = 0; start < n; start++)
    {
        if (isBg[start] || seen[start]) continue;

        members.Clear();
        var st = new Stack<int>();
        seen[start] = true; st.Push(start);
        int bgCount = 0;

        while (st.Count > 0)
        {
            var i = st.Pop(); members.Add(i);
            if (bgLike[i]) bgCount++;
            var x = i % w; var y = i / w;
            if (x > 0 && !seen[i - 1] && !isBg[i - 1]) { seen[i - 1] = true; st.Push(i - 1); }
            if (x < w - 1 && !seen[i + 1] && !isBg[i + 1]) { seen[i + 1] = true; st.Push(i + 1); }
            if (y > 0 && !seen[i - w] && !isBg[i - w]) { seen[i - w] = true; st.Push(i - w); }
            if (y < h - 1 && !seen[i + w] && !isBg[i + w]) { seen[i + w] = true; st.Push(i + w); }
        }

        if (members.Count > 0 && bgCount >= members.Count * 9 / 10)
            foreach (var i in members) isBg[i] = true;
    }
}

// 4) Applique l'alpha (fond transparent) et mesure la boite englobante de l'objet.
int minX = w, minY = h, maxX = -1, maxY = -1;
for (var y = 0; y < h; y++)
    for (var x = 0; x < w; x++)
    {
        var i = y * w + x;
        if (isBg[i]) px[i] = new SKColor(px[i].Red, px[i].Green, px[i].Blue, 0);
        else
        {
            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
        }
    }

if (maxX < 0) { Console.Error.WriteLine("Objet introuvable (image entierement 'fond')."); return 1; }

minX = Math.Max(0, minX - pad); minY = Math.Max(0, minY - pad);
maxX = Math.Min(w - 1, maxX + pad); maxY = Math.Min(h - 1, maxY + pad);
int cw = maxX - minX + 1, ch = maxY - minY + 1;

// 5) Recadrage sur l'objet + ecriture PNG (Rgba8888, non premultiplie : l'alpha garde sa valeur).
using var outBmp = new SKBitmap(new SKImageInfo(cw, ch, SKColorType.Rgba8888, SKAlphaType.Unpremul));
var outPx = new SKColor[cw * ch];
for (var y = 0; y < ch; y++)
    Array.Copy(px, (minY + y) * w + minX, outPx, y * cw, cw);
outBmp.Pixels = outPx;

using (var img = SKImage.FromBitmap(outBmp))
using (var data = img.Encode(SKEncodedImageFormat.Png, 100))
using (var fs = File.Create(output))
    data.SaveTo(fs);

Console.WriteLine($"{input} {w}x{h} -> {output} {cw}x{ch}  (luminance fond >= {floor:F0})");
return 0;
