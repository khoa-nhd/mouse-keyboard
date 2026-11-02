# Generates the app icon (app.ico, 16-256 px) and the tray icons
# (tray-on.ico / tray-off.ico, 16-48 px) into src/KeyboardMouse/Assets.
# Also writes icon-preview.png next to this script for a quick visual check.
# Requires Windows PowerShell 5.1 with System.Drawing (built in).
#
# Design notes (Microsoft's Windows app icon guidance):
#   * One metaphor: a keyboard key that is also the pointer. No letters.
#   * Plate radius is 21% of the size; the key sits on a 48 px grid.
#   * Below 24 px the key is dropped and only the pointer is drawn, because a
#     white plate plus a tiny arrow turns to mush at 16 px.
#   * Gradients only where they can be seen (48 px and up); nothing below 40.
Add-Type -AssemblyName System.Drawing

$ErrorActionPreference = 'Stop'

$accent   = [System.Drawing.Color]::FromArgb(255, 99, 102, 241)   # indigo 500
$accent2  = [System.Drawing.Color]::FromArgb(255, 139, 92, 246)   # violet 500
$ink      = [System.Drawing.Color]::FromArgb(255, 49, 46, 129)    # indigo 900
$slate1   = [System.Drawing.Color]::FromArgb(255, 148, 163, 184)  # slate 400
$slate2   = [System.Drawing.Color]::FromArgb(255, 100, 116, 139)  # slate 500

function New-RoundedRect([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

# Classic pointer arrow, tip at ($x, $y), $h tall.
function New-Pointer([single]$x, [single]$y, [single]$h) {
    $unit = @(
        @(0.00, 0.00), @(0.00, 0.86), @(0.22, 0.67), @(0.36, 0.98),
        @(0.50, 0.92), @(0.37, 0.62), @(0.64, 0.62)
    )
    $pts = foreach ($p in $unit) {
        New-Object System.Drawing.PointF([single]($x + $p[0] * $h), [single]($y + $p[1] * $h))
    }
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddPolygon([System.Drawing.PointF[]]$pts)
    return $path
}

function New-Canvas([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.Clear([System.Drawing.Color]::Transparent)
    return @($bmp, $g)
}

# Rounded-square plate with a two-step vertical gradient.
function Fill-Tile($g, [int]$size, [System.Drawing.Color]$from, [System.Drawing.Color]$to) {
    $s = [single]$size
    $pad = [single]($s * 0.045)
    $tile = New-RoundedRect $pad $pad ($s - 2 * $pad) ($s - 2 * $pad) ([single]($s * 0.21))
    $rect = New-Object System.Drawing.RectangleF(0, 0, $s, $s)
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $from, $to, [single]90)
    $g.FillPath($grad, $tile)
    $grad.Dispose()
    $tile.Dispose()
}

# White keycap, centred on the plate.
function Draw-Keycap($g, [int]$size) {
    $s = [single]$size
    $cap = New-RoundedRect ([single]($s * 0.13)) ([single]($s * 0.15)) ([single]($s * 0.74)) ([single]($s * 0.70)) ([single]($s * 0.155))
    $brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $g.FillPath($brush, $cap)
    $brush.Dispose(); $cap.Dispose()
}

# The pointer itself: solid ink, so it stays crisp at every size.
function Draw-InkPointer($g, [int]$size) {
    $s = [single]$size
    $ptr = New-Pointer ([single]($s * 0.315)) ([single]($s * 0.235)) ([single]($s * 0.50))
    $brush = New-Object System.Drawing.SolidBrush($ink)
    $g.FillPath($brush, $ptr)
    $brush.Dispose(); $ptr.Dispose()
}

# Small-size pointer: white on the plate, thin ink edge for definition.
function Draw-WhitePointer($g, [int]$size, [System.Drawing.Color]$edge) {
    $s = [single]$size
    $ptr = New-Pointer ([single]($s * 0.23)) ([single]($s * 0.09)) ([single]($s * 0.70))
    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $g.FillPath($white, $ptr)
    $white.Dispose()

    $penWidth = [single][Math]::Max(1.0, $s * 0.02)
    $pen = New-Object System.Drawing.Pen($edge, $penWidth)
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $g.DrawPath($pen, $ptr)
    $pen.Dispose(); $ptr.Dispose()
}

function New-AppIcon([int]$size) {
    $bmp, $g = New-Canvas $size
    Fill-Tile $g $size $accent $accent2

    if ($size -ge 24) {
        Draw-Keycap $g $size
        Draw-InkPointer $g $size
    }
    else {
        Draw-WhitePointer $g $size $ink
    }

    $g.Dispose()
    return $bmp
}

function New-TrayIcon([int]$size, [System.Drawing.Color]$from, [System.Drawing.Color]$to) {
    $bmp, $g = New-Canvas $size
    Fill-Tile $g $size $from $to

    $s = [single]$size
    $ptr = New-Pointer ([single]($s * 0.24)) ([single]($s * 0.12)) ([single]($s * 0.66))
    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $g.FillPath($white, $ptr)
    $white.Dispose(); $ptr.Dispose()

    $g.Dispose()
    return $bmp
}

function Get-PngBytes([System.Drawing.Bitmap]$bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $ms.ToArray()
    $ms.Dispose()
    return , $bytes
}

# ICO with PNG-compressed frames (supported since Windows Vista).
function Write-Ico([string]$outPath, [int[]]$sizes, [scriptblock]$render) {
    $frames = @()
    foreach ($size in $sizes) {
        $bmp = & $render $size
        $frames += , (Get-PngBytes $bmp)
        $bmp.Dispose()
    }

    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    $bw.Write([uint16]0)
    $bw.Write([uint16]1)
    $bw.Write([uint16]$sizes.Count)

    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
        $bw.Write([byte]$dim)
        $bw.Write([byte]$dim)
        $bw.Write([byte]0)
        $bw.Write([byte]0)
        $bw.Write([uint16]1)
        $bw.Write([uint16]32)
        $bw.Write([uint32]$frames[$i].Length)
        $bw.Write([uint32]$offset)
        $offset += $frames[$i].Length
    }

    foreach ($frame in $frames) {
        $bw.Write($frame)
    }

    $bw.Flush()
    [System.IO.File]::WriteAllBytes($outPath, $ms.ToArray())
    $bw.Dispose()
    $ms.Dispose()
    Write-Host "wrote $outPath"
}

$assets = Join-Path $PSScriptRoot '..\src\KeyboardMouse\Assets'
New-Item -ItemType Directory -Force -Path $assets | Out-Null
$assets = (Resolve-Path $assets).Path

Write-Ico (Join-Path $assets 'app.ico') @(16, 20, 24, 32, 40, 48, 64, 128, 256) { param($s) New-AppIcon $s }
Write-Ico (Join-Path $assets 'tray-on.ico') @(16, 20, 24, 32, 40, 48) { param($s) New-TrayIcon $s $accent $accent2 }
Write-Ico (Join-Path $assets 'tray-off.ico') @(16, 20, 24, 32, 40, 48) { param($s) New-TrayIcon $s $slate1 $slate2 }

# The 256 px app icon doubles as the logo shown in the guide window; that copy
# lives with the page it decorates, not with the icon resources.
$logo = New-AppIcon 256
$logo.Save((Join-Path $PSScriptRoot '..\src\KeyboardMouse\Ui\logo.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$logo.Dispose()

# Preview strip: app icon on a light and a dark band, then the tray pair.
$preview = New-Object System.Drawing.Bitmap(900, 420, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$pg = [System.Drawing.Graphics]::FromImage($preview)
$pg.Clear([System.Drawing.Color]::FromArgb(255, 241, 245, 249))
$pg.FillRectangle((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 32, 32, 32))), 0, 300, 900, 120)

$x = 12
foreach ($size in @(256, 128, 64, 48, 40, 32, 24, 20, 16)) {
    $img = New-AppIcon $size
    $pg.DrawImage($img, $x, 12, $size, $size)
    $img.Dispose()
    $x += $size + 12
    if ($x -gt 860) { break }
}

$x = 12
foreach ($size in @(48, 40, 32, 24, 20, 16)) {
    foreach ($pair in @(@($accent, $accent2), @($slate1, $slate2))) {
        $img = New-TrayIcon $size $pair[0] $pair[1]
        $pg.DrawImage($img, $x, 316 + (48 - $size), $size, $size)
        $pg.DrawImage($img, $x, 316 + (48 - $size), $size, $size)
        $img.Dispose()
        $x += $size + 10
    }
}
$pg.Dispose()
$preview.Save((Join-Path $PSScriptRoot 'icon-preview.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$preview.Dispose()
Write-Host 'wrote icon-preview.png'
