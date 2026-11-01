# Generates the app icon (app.ico, 16-256 px) and the tray icons
# (tray-on.ico / tray-off.ico, 16-48 px) into src/KeyboardMouse/Assets.
# Also writes icon-preview.png next to this script for a quick visual check.
# Requires Windows PowerShell 5.1 with System.Drawing (built in).
Add-Type -AssemblyName System.Drawing

$ErrorActionPreference = 'Stop'

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

function Fill-Tile($g, [int]$size, [System.Drawing.Color]$from, [System.Drawing.Color]$to) {
    $s = [single]$size
    $pad = [single][Math]::Max(0.5, $s * 0.04)
    $tile = New-RoundedRect $pad $pad ($s - 2 * $pad) ($s - 2 * $pad) ([single]($s * 0.22))
    $rect = New-Object System.Drawing.RectangleF(0, 0, $s, $s)
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $from, $to, [single]45)
    $g.FillPath($grad, $tile)
    $grad.Dispose()

    if ($size -ge 48) {
        # Soft top highlight for depth.
        $hiRect = New-Object System.Drawing.RectangleF(0, 0, $s, [single]($s * 0.55))
        $hi = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
            $hiRect,
            [System.Drawing.Color]::FromArgb(60, 255, 255, 255),
            [System.Drawing.Color]::FromArgb(0, 255, 255, 255),
            [single]90)
        $g.SetClip($tile)
        $g.FillRectangle($hi, $hiRect)
        $g.ResetClip()
        $hi.Dispose()
    }

    $tile.Dispose()
}

function Draw-Pointer($g, [single]$x, [single]$y, [single]$h, [int]$size) {
    $ptr = New-Pointer $x $y $h
    if ($size -ge 32) {
        $shadow = $ptr.Clone()
        $m = New-Object System.Drawing.Drawing2D.Matrix
        $m.Translate([single]($size * 0.015), [single]($size * 0.025))
        $shadow.Transform($m)
        $sb = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(70, 15, 23, 42))
        $g.FillPath($sb, $shadow)
        $sb.Dispose()
        $shadow.Dispose()
    }

    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $g.FillPath($white, $ptr)
    $white.Dispose()

    $penWidth = [single][Math]::Max(1.0, $size * 0.022)
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 30, 27, 75), $penWidth)
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $g.DrawPath($pen, $ptr)
    $pen.Dispose()
    $ptr.Dispose()
}

function Draw-Keycap($g, [int]$size) {
    $s = [single]$size
    $x = [single]($s * 0.15); $y = [single]($s * 0.47)
    $w = [single]($s * 0.46); $h = [single]($s * 0.38)
    $r = [single]($s * 0.07)

    # Key body (darker base gives a 3D edge).
    $base = New-RoundedRect $x ([single]($y + $s * 0.035)) $w $h $r
    $baseBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 199, 210, 254))
    $g.FillPath($baseBrush, $base)
    $baseBrush.Dispose(); $base.Dispose()

    $top = New-RoundedRect $x $y $w $h $r
    $topBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $g.FillPath($topBrush, $top)
    $topBrush.Dispose(); $top.Dispose()

    # "K" legend.
    $font = New-Object System.Drawing.Font('Segoe UI', [single]($s * 0.22), [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $fmt = New-Object System.Drawing.StringFormat
    $fmt.Alignment = [System.Drawing.StringAlignment]::Center
    $fmt.LineAlignment = [System.Drawing.StringAlignment]::Center
    $ink = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 67, 56, 202))
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.DrawString('K', $font, $ink, (New-Object System.Drawing.RectangleF($x, $y, $w, $h)), $fmt)
    $ink.Dispose(); $font.Dispose(); $fmt.Dispose()
}

$indigo = [System.Drawing.Color]::FromArgb(255, 99, 102, 241)
$cyan = [System.Drawing.Color]::FromArgb(255, 6, 182, 212)

function New-AppIcon([int]$size) {
    $bmp, $g = New-Canvas $size
    Fill-Tile $g $size $indigo $cyan
    $s = [single]$size
    if ($size -ge 48) {
        Draw-Keycap $g $size
        Draw-Pointer $g ([single]($s * 0.50)) ([single]($s * 0.17)) ([single]($s * 0.56)) $size
    }
    else {
        Draw-Pointer $g ([single]($s * 0.30)) ([single]($s * 0.17)) ([single]($s * 0.68)) $size
    }
    $g.Dispose()
    return $bmp
}

function New-TrayIcon([int]$size, [System.Drawing.Color]$from, [System.Drawing.Color]$to) {
    $bmp, $g = New-Canvas $size
    Fill-Tile $g $size $from $to
    $s = [single]$size
    Draw-Pointer $g ([single]($s * 0.30)) ([single]($s * 0.16)) ([single]($s * 0.70)) $size
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

$green1 = [System.Drawing.Color]::FromArgb(255, 34, 197, 94)
$green2 = [System.Drawing.Color]::FromArgb(255, 20, 184, 166)
$gray1 = [System.Drawing.Color]::FromArgb(255, 148, 163, 184)
$gray2 = [System.Drawing.Color]::FromArgb(255, 100, 116, 139)

Write-Ico (Join-Path $assets 'app.ico') @(16, 20, 24, 32, 40, 48, 64, 128, 256) { param($s) New-AppIcon $s }
Write-Ico (Join-Path $assets 'tray-on.ico') @(16, 20, 24, 32, 40, 48) { param($s) New-TrayIcon $s $green1 $green2 }
Write-Ico (Join-Path $assets 'tray-off.ico') @(16, 20, 24, 32, 40, 48) { param($s) New-TrayIcon $s $gray1 $gray2 }

# The 256 px app icon doubles as the logo shown in the guide window.
$logo = New-AppIcon 256
$logo.Save((Join-Path $assets 'logo.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$logo.Dispose()

# Preview strip: app icon at every size, then tray on/off.
$preview = New-Object System.Drawing.Bitmap(760, 300, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$pg = [System.Drawing.Graphics]::FromImage($preview)
$pg.Clear([System.Drawing.Color]::FromArgb(255, 241, 245, 249))
$pg.FillRectangle((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 30, 41, 59))), 0, 150, 760, 150)
$x = 10
foreach ($size in @(256, 128, 64, 48, 32, 24, 16)) {
    $img = New-AppIcon $size
    $drawSize = [Math]::Min($size, 128)
    $pg.DrawImage($img, $x, 10, $drawSize, $drawSize)
    $img.Dispose()
    $x += $drawSize + 12
}
$x = 10
foreach ($size in @(48, 32, 24, 16)) {
    foreach ($pair in @(@($green1, $green2), @($gray1, $gray2))) {
        $img = New-TrayIcon $size $pair[0] $pair[1]
        $pg.DrawImage($img, $x, 170, $size, $size)
        $img.Dispose()
        $x += $size + 10
    }
}
$pg.Dispose()
$preview.Save((Join-Path $PSScriptRoot 'icon-preview.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$preview.Dispose()
Write-Host 'wrote icon-preview.png'
