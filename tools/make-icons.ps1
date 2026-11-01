# Generates the tray icons (16px + 32px frames) into src/KeyboardMouse/Assets.
# Requires Windows PowerShell 5.1 with System.Drawing (built in).
Add-Type -AssemblyName System.Drawing

function New-Glyph([int]$size, [System.Drawing.Color]$color) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $pad = [Math]::Max(1.0, $size * 0.06)
    $brush = New-Object System.Drawing.SolidBrush($color)
    $g.FillEllipse($brush, [single]$pad, [single]$pad, [single]($size - 2 * $pad), [single]($size - 2 * $pad))
    $brush.Dispose()

    $s = [double]$size
    $pts = @(
        (New-Object System.Drawing.PointF([single]($s * 0.36), [single]($s * 0.24))),
        (New-Object System.Drawing.PointF([single]($s * 0.36), [single]($s * 0.70))),
        (New-Object System.Drawing.PointF([single]($s * 0.47), [single]($s * 0.60))),
        (New-Object System.Drawing.PointF([single]($s * 0.55), [single]($s * 0.79))),
        (New-Object System.Drawing.PointF([single]($s * 0.64), [single]($s * 0.75))),
        (New-Object System.Drawing.PointF([single]($s * 0.56), [single]($s * 0.56))),
        (New-Object System.Drawing.PointF([single]($s * 0.69), [single]($s * 0.56)))
    )
    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $g.FillPolygon($white, $pts)
    $white.Dispose()
    $g.Dispose()
    return $bmp
}

function Save-Png([System.Drawing.Bitmap]$bmp, [string]$path) {
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

function Write-Ico([string]$outPath, [string[]]$pngPaths) {
    $images = @()
    foreach ($p in $pngPaths) {
        $images += , ([System.IO.File]::ReadAllBytes($p))
    }

    $count = $images.Count
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    $bw.Write([uint16]0)
    $bw.Write([uint16]1)
    $bw.Write([uint16]$count)

    $offset = 6 + 16 * $count
    for ($i = 0; $i -lt $count; $i++) {
        $size = if ($i -eq 0) { 16 } else { 32 }
        $bw.Write([byte]$size)
        $bw.Write([byte]$size)
        $bw.Write([byte]0)
        $bw.Write([byte]0)
        $bw.Write([uint16]1)
        $bw.Write([uint16]32)
        $bw.Write([uint32]$images[$i].Length)
        $bw.Write([uint32]$offset)
        $offset += $images[$i].Length
    }

    foreach ($img in $images) {
        $bw.Write($img)
    }

    $bw.Flush()
    [System.IO.File]::WriteAllBytes($outPath, $ms.ToArray())
    $bw.Dispose()
    $ms.Dispose()
}

$tmp = Join-Path $env:TEMP "km-icons"
New-Item -ItemType Directory -Force -Path $tmp | Out-Null

$specs = @(
    @{ Name = 'tray-on';  Color = [System.Drawing.Color]::FromArgb(46, 204, 113) },
    @{ Name = 'tray-off'; Color = [System.Drawing.Color]::FromArgb(149, 165, 166) }
)

foreach ($spec in $specs) {
    $p16 = Join-Path $tmp "$($spec.Name)-16.png"
    $p32 = Join-Path $tmp "$($spec.Name)-32.png"
    Save-Png (New-Glyph 16 $spec.Color) $p16
    Save-Png (New-Glyph 32 $spec.Color) $p32

    $out = Join-Path $PSScriptRoot "..\src\KeyboardMouse\Assets\$($spec.Name).ico"
    New-Item -ItemType Directory -Force -Path (Split-Path $out) | Out-Null
    Write-Ico $out @($p16, $p32)
    Write-Host "wrote $out"
}
