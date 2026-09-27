# Regenerates src\app.ico: a blue rounded square with a white USB glyph,
# at every size Windows asks for (PNG-compressed ICO).
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$ico = Join-Path $PSScriptRoot '..\src\app.ico'

function New-IconPng([int]$size) {
    $bmp = New-Object Drawing.Bitmap $size, $size, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.TextRenderingHint = 'AntiAliasGridFit'
    $g.Clear([Drawing.Color]::Transparent)
    $m = [math]::Max(0.5, $size * 0.03)
    $r = New-Object Drawing.RectangleF $m, $m, ($size - 2 * $m), ($size - 2 * $m)
    $d = $size * 0.46
    $path = New-Object Drawing.Drawing2D.GraphicsPath
    $path.AddArc($r.X, $r.Y, $d, $d, 180, 90); $path.AddArc($r.Right - $d, $r.Y, $d, $d, 270, 90)
    $path.AddArc($r.Right - $d, $r.Bottom - $d, $d, $d, 0, 90); $path.AddArc($r.X, $r.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $brush = New-Object Drawing.Drawing2D.LinearGradientBrush $r, ([Drawing.Color]::FromArgb(37, 99, 235)), ([Drawing.Color]::FromArgb(6, 182, 212)), 45
    $g.FillPath($brush, $path)
    $fontName = 'Segoe MDL2 Assets'
    foreach ($f in (New-Object Drawing.Text.InstalledFontCollection).Families) { if ($f.Name -eq 'Segoe Fluent Icons') { $fontName = $f.Name } }
    $font = New-Object Drawing.Font $fontName, ($size * 0.56), ([Drawing.GraphicsUnit]::Pixel)
    $fmt = New-Object Drawing.StringFormat; $fmt.Alignment = 'Center'; $fmt.LineAlignment = 'Center'
    $g.DrawString([string][char]0xE88E, $font, [Drawing.Brushes]::White, (New-Object Drawing.RectangleF 0, ($size * 0.02), $size, $size), $fmt)
    $g.Dispose()
    $ms = New-Object IO.MemoryStream
    $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ,$ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$images = New-Object 'System.Collections.Generic.List[byte[]]'
foreach ($s in $sizes) { $images.Add((New-IconPng $s)) }
$fs = [IO.File]::Create($ico); $w = New-Object IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $data = $images[$i]
    $b = [byte]($(if ($s -ge 256) { 0 } else { $s }))
    $w.Write($b); $w.Write($b); $w.Write([byte]0); $w.Write([byte]0); $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$data.Length); $w.Write([uint32]$offset); $offset += $data.Length
}
foreach ($data in $images) { $w.Write($data) }
$w.Close()
"wrote $ico"
