#:package Svg.Skia@3.0.3

// Erzeugt die PNG-Icons der App aus den SVG-Quellen in src/CodeSail/wwwroot.
// Aufruf aus dem Repo-Stammverzeichnis: dotnet run tools/render-icons.cs

using SkiaSharp;
using Svg.Skia;

var root = Path.Combine(Directory.GetCurrentDirectory(), "src", "CodeSail", "wwwroot");

(string Output, string Source, int Size)[] icons =
[
    ("favicon.png", "icon.svg", 48),
    ("icon-192.png", "icon.svg", 192),
    ("icon-512.png", "icon.svg", 512),
    // Ohne runde Ecken: Android (maskable) und iOS schneiden selbst zu
    ("icon-maskable-512.png", "icon-maskable.svg", 512),
    ("apple-touch-icon.png", "icon-maskable.svg", 180),
];

foreach (var (output, source, size) in icons)
{
    using var svg = new SKSvg();
    svg.Load(Path.Combine(root, source));
    var bounds = svg.Picture!.CullRect;

    using var bitmap = new SKBitmap(size, size);
    using var canvas = new SKCanvas(bitmap);
    canvas.Clear(SKColors.Transparent);
    canvas.Scale(size / bounds.Width, size / bounds.Height);
    canvas.DrawPicture(svg.Picture);

    using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
    File.WriteAllBytes(Path.Combine(root, output), data.ToArray());
    Console.WriteLine($"{output} ({size}x{size}, {data.Size / 1024} KB)");
}
