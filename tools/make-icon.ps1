# Generates the Vita application icon (multi-size .ico) without external tools.
# Run from anywhere: powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1

Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$assets = Join-Path $root 'src\Vita.App\Assets'
New-Item -ItemType Directory -Force -Path $assets | Out-Null
$outFile = Join-Path $assets 'vita.ico'

$brandTop = [System.Drawing.Color]::FromArgb(255, 33, 112, 92)
$brandBottom = [System.Drawing.Color]::FromArgb(255, 18, 72, 60)
$cream = [System.Drawing.Color]::FromArgb(255, 245, 242, 234)
$sun = [System.Drawing.Color]::FromArgb(255, 232, 178, 58)

function New-IconPng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.Clear([System.Drawing.Color]::Transparent)

    $pad = [Math]::Max(1, [int]($size * 0.05))
    $rect = New-Object System.Drawing.Rectangle($pad, $pad, ($size - 2 * $pad), ($size - 2 * $pad))
    $radius = [Math]::Max(3, [int]($size * 0.23))
    $diameter = $radius * 2

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($rect.X, $rect.Y, $diameter, $diameter, 180, 90)
    $path.AddArc($rect.Right - $diameter, $rect.Y, $diameter, $diameter, 270, 90)
    $path.AddArc($rect.Right - $diameter, $rect.Bottom - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()

    $gradient = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $brandTop, $brandBottom, 55)
    $g.FillPath($gradient, $path)

    if ($size -ge 32) {
        $dot = [int]($size * 0.15)
        $dotX = $rect.Right - $dot - [int]($size * 0.13)
        $dotY = $rect.Y + [int]($size * 0.12)
        $dotBrush = New-Object System.Drawing.SolidBrush($sun)
        $g.FillEllipse($dotBrush, $dotX, $dotY, $dot, $dot)
        $dotBrush.Dispose()
    }

    $fontPixels = [single]($size * 0.60)
    $font = New-Object System.Drawing.Font('Georgia', $fontPixels, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $format = New-Object System.Drawing.StringFormat
    $format.Alignment = [System.Drawing.StringAlignment]::Center
    $format.LineAlignment = [System.Drawing.StringAlignment]::Center
    $textRect = New-Object System.Drawing.RectangleF(0, [single]($size * 0.015), $size, $size)
    $textBrush = New-Object System.Drawing.SolidBrush($cream)
    $g.DrawString('V', $font, $textBrush, $textRect, $format)

    $stream = New-Object System.IO.MemoryStream
    $bmp.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $stream.ToArray()

    $textBrush.Dispose()
    $font.Dispose()
    $format.Dispose()
    $gradient.Dispose()
    $path.Dispose()
    $g.Dispose()
    $bmp.Dispose()
    $stream.Dispose()

    return ,$bytes
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$images = New-Object System.Collections.ArrayList
foreach ($size in $sizes) {
    [void]$images.Add((New-IconPng $size))
}

$ico = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($ico)
$writer.Write([UInt16]0)
$writer.Write([UInt16]1)
$writer.Write([UInt16]$sizes.Count)

$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $size = $sizes[$i]
    $data = [byte[]]$images[$i]
    $writer.Write([byte]$(if ($size -ge 256) { 0 } else { $size }))
    $writer.Write([byte]$(if ($size -ge 256) { 0 } else { $size }))
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]32)
    $writer.Write([UInt32]$data.Length)
    $writer.Write([UInt32]$offset)
    $offset += $data.Length
}

foreach ($image in $images) {
    $writer.Write([byte[]]$image)
}

$writer.Flush()
[System.IO.File]::WriteAllBytes($outFile, $ico.ToArray())
$writer.Dispose()
$ico.Dispose()

# PNG preview for quick visual inspection.
[System.IO.File]::WriteAllBytes((Join-Path $assets 'vita-preview.png'), [byte[]]$images[$sizes.Count - 1])

Write-Host "Icon written to $outFile"
