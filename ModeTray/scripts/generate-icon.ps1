$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$output = Join-Path $PSScriptRoot '..\assets\id6-mode.ico'
$size = 256
$bitmap = New-Object Drawing.Bitmap $size, $size
$graphics = [Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([Drawing.Color]::Transparent)

$bounds = New-Object Drawing.RectangleF 20, 20, 216, 216
$gradient = New-Object Drawing.Drawing2D.LinearGradientBrush(
    $bounds,
    [Drawing.Color]::FromArgb(35, 225, 185),
    [Drawing.Color]::FromArgb(36, 120, 255),
    45
)
$hex = New-Object Drawing.Drawing2D.GraphicsPath
$hex.AddPolygon([Drawing.PointF[]]@(
    (New-Object Drawing.PointF 74, 20),
    (New-Object Drawing.PointF 182, 20),
    (New-Object Drawing.PointF 236, 128),
    (New-Object Drawing.PointF 182, 236),
    (New-Object Drawing.PointF 74, 236),
    (New-Object Drawing.PointF 20, 128)
))
$graphics.FillPath($gradient, $hex)

$pen = New-Object Drawing.Pen ([Drawing.Color]::White), 19
$pen.StartCap = [Drawing.Drawing2D.LineCap]::Round
$pen.EndCap = [Drawing.Drawing2D.LineCap]::Round
$pen.LineJoin = [Drawing.Drawing2D.LineJoin]::Round
$graphics.DrawLines($pen, [Drawing.PointF[]]@(
    (New-Object Drawing.PointF 69, 148),
    (New-Object Drawing.PointF 107, 92),
    (New-Object Drawing.PointF 107, 164),
    (New-Object Drawing.PointF 184, 87)
))

$pngStream = New-Object IO.MemoryStream
$bitmap.Save($pngStream, [Drawing.Imaging.ImageFormat]::Png)
$png = $pngStream.ToArray()
$file = [IO.File]::Open($output, [IO.FileMode]::Create)
$writer = New-Object IO.BinaryWriter $file
$writer.Write([uint16]0)
$writer.Write([uint16]1)
$writer.Write([uint16]1)
$writer.Write([byte]0)
$writer.Write([byte]0)
$writer.Write([byte]0)
$writer.Write([byte]0)
$writer.Write([uint16]1)
$writer.Write([uint16]32)
$writer.Write([uint32]$png.Length)
$writer.Write([uint32]22)
$writer.Write($png)
$writer.Dispose()

$pen.Dispose()
$hex.Dispose()
$gradient.Dispose()
$graphics.Dispose()
$bitmap.Dispose()
$pngStream.Dispose()
