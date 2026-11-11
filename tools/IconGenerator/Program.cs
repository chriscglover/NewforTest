using SkiaSharp;

// Draws the NewforTest app icon.
// Usage: dotnet run --project tools/IconGenerator -- <out-folder> [preview.png]
// Writes NewforTest.ico (16-256 px) and NewforTest-256.png; optionally a preview of every size, magnified.
string outDir = args[0];
Directory.CreateDirectory(outDir);
int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };

var pngs = sizes.Select(s => (Size: s, Png: Render(s))).ToList();
File.WriteAllBytes(Path.Combine(outDir, "NewforTest-256.png"), pngs[^1].Png);
WriteIco(Path.Combine(outDir, "NewforTest.ico"), pngs);
if (args.Length > 1)
    WritePreview(args[1], pngs);
Console.WriteLine("done");

// Design on a 16-unit grid, so 16/32/48/64/128/256 px land every edge on a whole pixel.
// A TV screen with a teletext colour stripe and a boxed yellow two-line subtitle.
static byte[] Render(int size)
{
    float u = size / 16f;
    bool smooth = size >= 48; // antialiased curves only where there are enough pixels
    // Every edge on a whole pixel (exact for multiples of 16; rounded for e.g. 24 px)
    float S(float v) => MathF.Round(v * u, MidpointRounding.AwayFromZero);
    SKRect R(float l, float t, float r, float b) => new(S(l), S(t), S(r), S(b));

    using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
    var c = surface.Canvas;
    c.Clear(SKColors.Transparent);

    // Screen: light rim so it reads on dark and light backgrounds, dark gradient inside
    var rimColor = new SKColor(0x8A, 0x93, 0xA6);
    using var rim = new SKPaint { IsAntialias = smooth, Color = rimColor };
    using var fill = new SKPaint
    {
        IsAntialias = smooth,
        Shader = SKShader.CreateLinearGradient(new SKPoint(0, S(2)), new SKPoint(0, S(14)),
            new[] { new SKColor(0x3A, 0x41, 0x52), new SKColor(0x16, 0x1A, 0x22) }, SKShaderTileMode.Clamp),
    };

    if (smooth)
    {
        c.DrawRoundRect(R(0, 1, 16, 15), 2 * u, 2 * u, rim);
        c.DrawRoundRect(R(1, 2, 15, 14), u, u, fill);
    }
    else
    {
        // Pixel-art corners: cut the outer corners, round the inner ones with rim pixels
        float px = MathF.Max(1, MathF.Round(u));
        var outer = R(0, 1, 16, 15);
        var inner = R(1, 2, 15, 14);
        c.DrawRect(outer, rim);
        using var clear = new SKPaint { BlendMode = SKBlendMode.Clear };
        foreach (var (x, y) in new[] { (outer.Left, outer.Top), (outer.Right - px, outer.Top), (outer.Left, outer.Bottom - px), (outer.Right - px, outer.Bottom - px) })
            c.DrawRect(SKRect.Create(x, y, px, px), clear);
        c.DrawRect(inner, fill);
        foreach (var (x, y) in new[] { (inner.Left, inner.Top), (inner.Right - px, inner.Top), (inner.Left, inner.Bottom - px), (inner.Right - px, inner.Bottom - px) })
            c.DrawRect(SKRect.Create(x, y, px, px), rim);
    }

    // Teletext colour stripe: red, green, yellow, blue, magenta, cyan, white
    SKColor[] palette =
    {
        new(0xFF, 0x30, 0x30), new(0x30, 0xE0, 0x30), new(0xFF, 0xE0, 0x20), new(0x30, 0x60, 0xFF),
        new(0xFF, 0x30, 0xFF), new(0x20, 0xE0, 0xF0), new(0xF0, 0xF0, 0xF0),
    };
    float stripeLeft = S(2), stripeRight = S(14);
    for (int i = 0; i < palette.Length; i++)
    {
        float l = MathF.Round(stripeLeft + (stripeRight - stripeLeft) * i / palette.Length);
        float r = MathF.Round(stripeLeft + (stripeRight - stripeLeft) * (i + 1) / palette.Length);
        using var p = new SKPaint { Color = palette[i] };
        c.DrawRect(new SKRect(l, S(3), r, S(4)), p);
    }

    // Subtitle box with two centred lines of yellow "text"
    using (var box = new SKPaint { Color = SKColors.Black })
        c.DrawRect(R(2, 7, 14, 13), box);

    using var yellow = new SKPaint { Color = new SKColor(0xFF, 0xE0, 0x20) };
    void TextLine(string pattern, float top)
    {
        // One unit per character, centred on the box; '#' is a letter, ' ' a word gap
        float left = 8 - pattern.Length / 2f;
        if (size < 64)
        {
            c.DrawRect(R(left, top, left + pattern.Length, top + 1), yellow); // too small for letters
            return;
        }

        float letter = MathF.Round(0.75f * u);
        for (int i = 0; i < pattern.Length; i++)
        {
            if (pattern[i] != '#')
                continue;
            float x = MathF.Round((left + i) * u);
            c.DrawRect(new SKRect(x, S(top), x + letter, S(top + 1)), yellow);
        }
    }

    TextLine("#### #####", 8);
    TextLine("### ##", 10);

    using var image = surface.Snapshot();
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    return data.ToArray();
}

// ICO with a PNG image per size (supported by Windows Vista and later)
static void WriteIco(string path, List<(int Size, byte[] Png)> images)
{
    using var w = new BinaryWriter(File.Create(path));
    w.Write((ushort)0);
    w.Write((ushort)1);
    w.Write((ushort)images.Count);
    int offset = 6 + 16 * images.Count;
    foreach (var (size, png) in images)
    {
        w.Write((byte)(size >= 256 ? 0 : size));
        w.Write((byte)(size >= 256 ? 0 : size));
        w.Write((byte)0);
        w.Write((byte)0);
        w.Write((ushort)1);
        w.Write((ushort)32);
        w.Write(png.Length);
        w.Write(offset);
        offset += png.Length;
    }
    foreach (var (_, png) in images)
        w.Write(png);
}

// Every size side by side, small ones magnified with hard pixels, on dark and light backgrounds
static void WritePreview(string path, List<(int Size, byte[] Png)> images)
{
    const int cell = 280;
    using var surface = SKSurface.Create(new SKImageInfo(cell * images.Count, cell * 2));
    var c = surface.Canvas;
    using var dark = new SKPaint { Color = new SKColor(0x20, 0x20, 0x20) };
    using var light = new SKPaint { Color = new SKColor(0xF0, 0xF0, 0xF0) };
    c.DrawRect(0, 0, cell * images.Count, cell, dark);
    c.DrawRect(0, cell, cell * images.Count, cell, light);
    using var paint = new SKPaint { FilterQuality = SKFilterQuality.None };
    for (int i = 0; i < images.Count; i++)
    {
        using var bmp = SKBitmap.Decode(images[i].Png);
        float scale = MathF.Floor(256f / images[i].Size);
        var dest = SKRect.Create(i * cell + (cell - bmp.Width * scale) / 2, (cell - bmp.Height * scale) / 2, bmp.Width * scale, bmp.Height * scale);
        c.DrawBitmap(bmp, dest, paint);
        dest.Offset(0, cell);
        c.DrawBitmap(bmp, dest, paint);
    }
    using var image = surface.Snapshot();
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    File.WriteAllBytes(path, data.ToArray());
}
