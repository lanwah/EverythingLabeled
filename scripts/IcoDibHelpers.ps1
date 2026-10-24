#Requires -Version 5.1
# Shared ICO (DIB frame) helpers for make-explorer-icons / make-*-icon.ps1.

function Draw-Mdl2Glyph {
    param(
        [System.Drawing.Graphics]$Graphics,
        [int]$CodePoint,
        [single]$CenterX,
        [single]$CenterY,
        [single]$FontSizePx,
        [System.Drawing.Color]$Ink
    )

    $font = New-Object System.Drawing.Font('Segoe MDL2 Assets', $FontSizePx, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
    $brush = New-Object System.Drawing.SolidBrush($Ink)
    $format = New-Object System.Drawing.StringFormat
    try {
        $format.Alignment = [System.Drawing.StringAlignment]::Center
        $format.LineAlignment = [System.Drawing.StringAlignment]::Center
        $format.FormatFlags = [System.Drawing.StringFormatFlags]::NoClip
        $half = $FontSizePx * 0.55
        $rect = New-Object System.Drawing.RectangleF(($CenterX - $half), ($CenterY - $half), ($half * 2), ($half * 2))
        $Graphics.DrawString([string][char]$CodePoint, $font, $brush, $rect, $format)
    } finally {
        $format.Dispose(); $brush.Dispose(); $font.Dispose()
    }
}

function New-GlyphBitmap {
    param(
        [int]$CodePoint,
        [int]$Size,
        [System.Drawing.Color]$Ink,
        [single]$GlyphScale = 0.95
    )

    $bitmap = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAlias
        $g.Clear([System.Drawing.Color]::Transparent)
        Draw-Mdl2Glyph -Graphics $g -CodePoint $CodePoint -CenterX ($Size / 2) -CenterY ($Size / 2) -FontSizePx ($Size * $GlyphScale) -Ink $Ink
    } finally {
        $g.Dispose()
    }

    return $bitmap
}

# Service 守护进程：服务器字形 + 右下角标签角标（与 Tray 纯标签 el.ico 区分）
function New-ServiceIconBitmap {
    param(
        [int]$Size,
        [System.Drawing.Color]$Ink
    )

    return New-CompoundBrandIconBitmap -Size $Size -Ink $Ink -PrimaryGlyph 0xE968
}

# ExplorerHost：打开文件夹 + 右下角标签角标（与 Tray 纯标签、Service 服务器区分）
function New-HostIconBitmap {
    param(
        [int]$Size,
        [System.Drawing.Color]$Ink
    )

    # OpenFolder：Explorer 壳层集成的进程外 Host
    return New-CompoundBrandIconBitmap -Size $Size -Ink $Ink -PrimaryGlyph 0xE838
}

# CLI el.exe：命令提示符 + 右下角标签角标（与 Tray / Host / Service 四分开）
function New-CliIconBitmap {
    param(
        [int]$Size,
        [System.Drawing.Color]$Ink
    )

    # CommandPrompt：薄客户端命令行入口
    return New-CompoundBrandIconBitmap -Size $Size -Ink $Ink -PrimaryGlyph 0xE756
}

# 品牌复合图标：主字形偏左上 + Tag(E8EC) 角标；≥24px 带淡青绿圆底板
function New-CompoundBrandIconBitmap {
    param(
        [int]$Size,
        [System.Drawing.Color]$Ink,
        [int]$PrimaryGlyph,
        [int]$BadgeGlyph = 0xE8EC   # Tag（与品牌 el.ico 同源字形）
    )

    $bitmap = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAlias
        $g.Clear([System.Drawing.Color]::Transparent)

        if ($Size -ge 24) {
            $plate = [single]($Size * 0.86)
            $x = ($Size - $plate) / 2
            $brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(40, $Ink.R, $Ink.G, $Ink.B))
            try { $g.FillEllipse($brush, $x, $x, $plate, $plate) } finally { $brush.Dispose() }
        }

        $primarySize = [single]($Size * $(if ($Size -le 20) { 0.78 } else { 0.68 }))
        Draw-Mdl2Glyph -Graphics $g -CodePoint $PrimaryGlyph -CenterX ($Size * 0.46) -CenterY ($Size * 0.44) -FontSizePx $primarySize -Ink $Ink

        $badgeSize = [single]($Size * $(if ($Size -le 20) { 0.36 } else { 0.40 }))
        Draw-Mdl2Glyph -Graphics $g -CodePoint $BadgeGlyph -CenterX ($Size * 0.72) -CenterY ($Size * 0.72) -FontSizePx $badgeSize -Ink $Ink
    } finally {
        $g.Dispose()
    }

    return $bitmap
}

function Get-IcoDibFrame {
    param([System.Drawing.Bitmap]$Bitmap)

    $w = $Bitmap.Width
    $h = $Bitmap.Height
    $stream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($stream)
    try {
        $writer.Write([uint32]40)
        $writer.Write([int32]$w)
        $writer.Write([int32]($h * 2))
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]0)
        $writer.Write([uint32]($w * $h * 4))
        $writer.Write([int32]0); $writer.Write([int32]0)
        $writer.Write([uint32]0); $writer.Write([uint32]0)

        for ($y = $h - 1; $y -ge 0; $y--) {
            for ($x = 0; $x -lt $w; $x++) {
                $c = $Bitmap.GetPixel($x, $y)
                $writer.Write([byte]$c.B); $writer.Write([byte]$c.G); $writer.Write([byte]$c.R); $writer.Write([byte]$c.A)
            }
        }

        $maskStride = [int]([Math]::Floor(($w + 31) / 32) * 4)
        $mask = New-Object byte[] ($maskStride * $h)
        $writer.Write($mask)
    } finally {
        $writer.Flush()
    }

    $bytes = $stream.ToArray()
    $writer.Dispose(); $stream.Dispose()
    return , $bytes
}

function Write-IcoFile {
    param([string]$Path, [System.Drawing.Bitmap[]]$Frames)

    $blobs = @()
    foreach ($frame in $Frames) { $blobs += , (Get-IcoDibFrame -Bitmap $frame) }

    $headerSize = 6 + (16 * $Frames.Count)
    $stream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($stream)
    try {
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$Frames.Count)

        $offset = $headerSize
        for ($i = 0; $i -lt $Frames.Count; $i++) {
            $w = $Frames[$i].Width
            $h = $Frames[$i].Height
            $writer.Write([byte]$(if ($w -ge 256) { 0 } else { $w }))
            $writer.Write([byte]$(if ($h -ge 256) { 0 } else { $h }))
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$blobs[$i].Length)
            $writer.Write([uint32]$offset)
            $offset += $blobs[$i].Length
        }

        foreach ($blob in $blobs) { $writer.Write($blob) }
        $writer.Flush()
        [System.IO.File]::WriteAllBytes($Path, $stream.ToArray())
    } finally {
        $writer.Dispose(); $stream.Dispose()
    }
}

function Get-IcoFramePng {
    param([string]$Path, [int]$Size)

    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $count = [BitConverter]::ToUInt16($bytes, 4)
    for ($i = 0; $i -lt $count; $i++) {
        $entry = 6 + ($i * 16)
        $w = $bytes[$entry]; if ($w -eq 0) { $w = 256 }
        if ($w -ne $Size) { continue }

        $length = [BitConverter]::ToUInt32($bytes, $entry + 8)
        $start = [BitConverter]::ToUInt32($bytes, $entry + 12)
        $frame = New-Object byte[] $length
        [Array]::Copy($bytes, $start, $frame, 0, $length)
        $signature = ($frame[0..7] | ForEach-Object { $_.ToString('X2') }) -join ''
        if ($signature -ne '89504E470D0A1A0A') {
            throw "ICO frame ${Size}x${Size} is not PNG compressed: $Path"
        }
        return $frame
    }
    throw "ICO missing ${Size}x${Size} frame: $Path"
}

function Write-ScaledPng {
    param([System.Drawing.Image]$Source, [string]$Path, [int]$Size)

    $bitmap = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $g.Clear([System.Drawing.Color]::Transparent)
        $g.DrawImage($Source, (New-Object System.Drawing.Rectangle(0, 0, $Size, $Size)))
    } finally { $g.Dispose() }
    try { $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png) } finally { $bitmap.Dispose() }
}
