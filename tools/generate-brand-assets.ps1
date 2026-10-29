# Generates WinRedis icon assets from the vector definition in assets/winredis-logo.svg.
# The SVG is the source of truth; this script rasterizes the same geometry for Windows
# (multi-size .ico for the exe/taskbar, PNG for the WPF title bar and About card).
#
# Usage: powershell -ExecutionPolicy Bypass -File tools\generate-brand-assets.ps1

Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$assetsDir = Join-Path $root 'src\WinRedisClient.App\Assets'
New-Item -ItemType Directory -Force -Path $assetsDir | Out-Null

function New-RoundedRectPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-Pen([string]$htmlColor, [float]$width) {
    $color = [System.Drawing.ColorTranslator]::FromHtml($htmlColor)
    $pen = New-Object System.Drawing.Pen($color, $width)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    return $pen
}

function New-IconBitmap([int]$size) {
    $pad = [Math]::Max(1.0, $size * 0.055)
    $inner = $size - 2 * $pad
    $k = $inner / 152.0

    $bitmap = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.Clear([System.Drawing.Color]::Transparent)

    $g.TranslateTransform($pad, $pad)
    $g.ScaleTransform($k, $k)

    # Rounded-square brand plate (152 x 152, r=34) with the SVG gradient.
    $plate = New-RoundedRectPath 0 0 152 152 34
    $gradient = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF(0, 0)),
        (New-Object System.Drawing.PointF(152, 152)),
        [System.Drawing.ColorTranslator]::FromHtml('#756BFF'),
        [System.Drawing.ColorTranslator]::FromHtml('#5548E8'))
    $g.FillPath($gradient, $plate)

    # Redis/data stack (white, 0.96 opacity, 7px round strokes).
    $stackWidth = [Math]::Max(7.0, 1.15 / $k)
    $stackPen = New-Pen '#FFFFFF' $stackWidth
    $diamond = New-Object System.Drawing.Drawing2D.GraphicsPath
    $diamond.AddPolygon([System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF(35, 48)),
        (New-Object System.Drawing.PointF(58, 36)),
        (New-Object System.Drawing.PointF(81, 48)),
        (New-Object System.Drawing.PointF(58, 60))))
    $diamond.CloseFigure()
    $g.DrawPath($stackPen, $diamond)

    # Below 24 px the four extra chevrons collapse into two to keep the mark legible.
    $chevronYs = if ($size -le 24) { @(64, 96) } else { @(64, 80, 96, 112) }
    foreach ($y in $chevronYs) {
        $mid = $y + 12
        $g.DrawLines($stackPen, [System.Drawing.PointF[]]@(
            (New-Object System.Drawing.PointF(35, $y)),
            (New-Object System.Drawing.PointF(58, $mid)),
            (New-Object System.Drawing.PointF(81, $y))))
    }

    # Lightning bolt.
    $boltPoints = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF(94, 35)),
        (New-Object System.Drawing.PointF(72, 79)),
        (New-Object System.Drawing.PointF(91, 79)),
        (New-Object System.Drawing.PointF(78, 119)),
        (New-Object System.Drawing.PointF(116, 68)),
        (New-Object System.Drawing.PointF(96, 68)),
        (New-Object System.Drawing.PointF(110, 35)))
    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $boltPen = New-Pen '#FFFFFF' ([Math]::Max(2.0, 0.9 / $k))
    $g.FillPolygon($white, $boltPoints)
    $g.DrawPolygon($boltPen, $boltPoints)

    # Connection nodes.
    $nodeLinePen = New-Pen '#C9C5FF' ([Math]::Max(3.0, 0.9 / $k))
    $g.DrawLine($nodeLinePen, 119, 116, 132, 103)
    $nodeBrush = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml('#C9C5FF'))
    $g.FillEllipse($nodeBrush, 114, 111, 10, 10)
    $g.FillEllipse($nodeBrush, 128, 99, 8, 8)

    $g.Dispose()
    $gradient.Dispose()
    $plate.Dispose()
    $diamond.Dispose()
    $stackPen.Dispose()
    $boltPen.Dispose()
    $nodeLinePen.Dispose()
    $white.Dispose()
    $nodeBrush.Dispose()

    return $bitmap
}

function Save-Png([int]$size, [string]$path) {
    $bitmap = New-IconBitmap $size
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
}

# WPF-facing PNGs (title bar / About card / window icon).
Save-Png 256 (Join-Path $assetsDir 'winredis-icon.png')
Save-Png 128 (Join-Path $assetsDir 'winredis-icon-128.png')
Save-Png 512 (Join-Path $root 'assets\winredis-icon-512.png')

# Multi-size .ico for the executable, taskbar and Explorer.
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$frames = @()
foreach ($s in $sizes) {
    $bitmap = New-IconBitmap $s
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $frames += ,$stream.ToArray()
    $stream.Dispose()
    $bitmap.Dispose()
}

$icoPath = Join-Path $assetsDir 'winredis-icon.ico'
$file = [System.IO.File]::Create($icoPath)
$writer = New-Object System.IO.BinaryWriter($file)
$writer.Write([UInt16]0)                 # reserved
$writer.Write([UInt16]1)                 # type: icon
$writer.Write([UInt16]$sizes.Count)      # image count

$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $data = $frames[$i]
    $dimension = if ($s -ge 256) { 0 } else { $s }
    $writer.Write([byte]$dimension)      # width (0 = 256)
    $writer.Write([byte]$dimension)      # height
    $writer.Write([byte]0)               # palette count
    $writer.Write([byte]0)               # reserved
    $writer.Write([UInt16]1)             # color planes
    $writer.Write([UInt16]32)            # bits per pixel
    $writer.Write([UInt32]$data.Length)  # bytes in resource
    $writer.Write([UInt32]$offset)       # image offset
    $offset += $data.Length
}

foreach ($data in $frames) {
    $writer.Write($data)
}

$writer.Flush()
$writer.Dispose()
$file.Dispose()

Write-Host "Generated:"
Write-Host "  $icoPath ($([Math]::Round((Get-Item $icoPath).Length / 1KB, 1)) KB, $($sizes.Count) sizes)"
Write-Host "  $(Join-Path $assetsDir 'winredis-icon.png')"
Write-Host "  $(Join-Path $assetsDir 'winredis-icon-128.png')"
Write-Host "  $(Join-Path $root 'assets\winredis-icon-512.png')"
