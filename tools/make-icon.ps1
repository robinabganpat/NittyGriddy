# Draws the NittyGriddy icon and writes App\icon.ico and icon.png.
#
#   powershell -STA -ExecutionPolicy Bypass -File tools\make-icon.ps1
#
# The icon is the application's own picture of a grid: a dark tile holding four slots, three empty (the blue
# of an empty slot in the monitor preview) and one holding the active table (the green of the grid switch and
# the active-table border). Every size is drawn on its own pixel grid rather than scaled down from one
# drawing, so the 16 px tray icon has whole-pixel slots and gaps.

param(
    [string]$IcoPath = (Join-Path $PSScriptRoot '..\App\icon.ico'),
    [string]$PngPath = (Join-Path $PSScriptRoot '..\icon.png'),
    [string]$PreviewPath = ''
)

Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase

function Color([string]$hex) { [System.Windows.Media.ColorConverter]::ConvertFromString($hex) }

function Vertical([string]$top, [string]$bottom) {
    $brush = New-Object System.Windows.Media.LinearGradientBrush (Color $top), (Color $bottom), 90.0
    $brush.Freeze()
    $brush
}

# Layout per size, in pixels: margin around the tile, padding inside it, gap between slots, slot size,
# corner radius of the tile and of a slot. margin*2 + pad*2 + gap + slot*2 = size.
$layouts = @{
    16  = @{ Margin = 0;  Pad = 2;  Gap = 2;  Slot = 5;   TileRadius = 3;   SlotRadius = 1 }
    20  = @{ Margin = 0;  Pad = 2;  Gap = 2;  Slot = 7;   TileRadius = 4;   SlotRadius = 1 }
    24  = @{ Margin = 0;  Pad = 3;  Gap = 2;  Slot = 8;   TileRadius = 5;   SlotRadius = 1.5 }
    32  = @{ Margin = 0;  Pad = 4;  Gap = 2;  Slot = 11;  TileRadius = 7;   SlotRadius = 2 }
    40  = @{ Margin = 0;  Pad = 5;  Gap = 2;  Slot = 14;  TileRadius = 9;   SlotRadius = 2.5 }
    48  = @{ Margin = 0;  Pad = 6;  Gap = 4;  Slot = 16;  TileRadius = 10;  SlotRadius = 3 }
    64  = @{ Margin = 0;  Pad = 8;  Gap = 4;  Slot = 22;  TileRadius = 14;  SlotRadius = 4 }
    256 = @{ Margin = 8;  Pad = 30; Gap = 16; Slot = 82;  TileRadius = 54;  SlotRadius = 15 }
    1024 = @{ Margin = 32; Pad = 120; Gap = 64; Slot = 328; TileRadius = 216; SlotRadius = 60 }
}

function Render([int]$size) {
    $l = $layouts[$size]
    $visual = New-Object System.Windows.Media.DrawingVisual
    $dc = $visual.RenderOpen()

    # Tile
    $tileSize = $size - 2 * $l.Margin
    $tile = New-Object System.Windows.Rect $l.Margin, $l.Margin, $tileSize, $tileSize
    $dc.DrawRoundedRectangle((Vertical '#FF2A2F38' '#FF14161A'), $null, $tile, $l.TileRadius, $l.TileRadius)

    # A rim keeps the tile's shape on a dark taskbar; too fine to draw below 32 px
    if ($size -ge 32) {
        $w = [Math]::Max(1, [Math]::Round($size / 64))
        $pen = New-Object System.Windows.Media.Pen (Vertical '#FF5A6270' '#FF333841'), $w
        $rim = New-Object System.Windows.Rect ($l.Margin + $w / 2), ($l.Margin + $w / 2), ($tileSize - $w), ($tileSize - $w)
        $dc.DrawRoundedRectangle($null, $pen, $rim, ($l.TileRadius - $w / 2), ($l.TileRadius - $w / 2))
    }

    # Slots: slot 1 holds the active table
    $empty = Vertical '#FF6C9BF2' '#FF4F7FDD'
    $active = Vertical '#FF5BEB9C' '#FF2FCB74'
    $start = $l.Margin + $l.Pad
    $step = $l.Slot + $l.Gap

    foreach ($row in 0, 1) {
        foreach ($column in 0, 1) {
            $rect = New-Object System.Windows.Rect ($start + $column * $step), ($start + $row * $step), $l.Slot, $l.Slot
            $brush = if ($row -eq 0 -and $column -eq 0) { $active } else { $empty }
            $dc.DrawRoundedRectangle($brush, $null, $rect, $l.SlotRadius, $l.SlotRadius)
        }
    }

    $dc.Close()

    $bitmap = New-Object System.Windows.Media.Imaging.RenderTargetBitmap $size, $size, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $bitmap.Freeze()
    $bitmap
}

function PngBytes($bitmap) {
    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = New-Object System.IO.MemoryStream
    $encoder.Save($stream)
    , $stream.ToArray()
}

# A 32-bit icon image in the classic format (DIB plus an empty mask), which every consumer of .ico reads
function DibBytes($bitmap) {
    $size = $bitmap.PixelWidth
    $straight = New-Object System.Windows.Media.Imaging.FormatConvertedBitmap $bitmap, ([System.Windows.Media.PixelFormats]::Bgra32), $null, 0
    $pixels = New-Object byte[] ($size * $size * 4)
    $straight.CopyPixels($pixels, $size * 4, 0)

    $stream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter $stream
    $maskStride = [int]([Math]::Ceiling($size / 32.0) * 4)

    $writer.Write([int]40); $writer.Write([int]$size); $writer.Write([int]($size * 2))
    $writer.Write([int16]1); $writer.Write([int16]32); $writer.Write([int]0)
    $writer.Write([int]($pixels.Length + $maskStride * $size))
    $writer.Write([int]0); $writer.Write([int]0); $writer.Write([int]0); $writer.Write([int]0)

    # Rows are stored bottom-up
    for ($y = $size - 1; $y -ge 0; $y--) { $writer.Write($pixels, $y * $size * 4, $size * 4) }
    $writer.Write((New-Object byte[] ($maskStride * $size)))
    $writer.Flush()
    , $stream.ToArray()
}

$iconSizes = 16, 20, 24, 32, 40, 48, 64, 256
$images = foreach ($size in $iconSizes) {
    $bitmap = Render $size
    # 256 px is stored as PNG, as Windows itself does; the smaller ones as plain bitmaps
    $bytes = if ($size -eq 256) { PngBytes $bitmap } else { DibBytes $bitmap }
    [pscustomobject]@{ Size = $size; Bytes = $bytes }
}

$ico = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter $ico
$writer.Write([int16]0); $writer.Write([int16]1); $writer.Write([int16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($image in $images) {
    $dimension = if ($image.Size -eq 256) { 0 } else { $image.Size }
    $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([int16]1); $writer.Write([int16]32)
    $writer.Write([int]$image.Bytes.Length); $writer.Write([int]$offset)
    $offset += $image.Bytes.Length
}
foreach ($image in $images) { $writer.Write($image.Bytes) }
$writer.Flush()

[System.IO.File]::WriteAllBytes([System.IO.Path]::GetFullPath($IcoPath), $ico.ToArray())
[System.IO.File]::WriteAllBytes([System.IO.Path]::GetFullPath($PngPath), (PngBytes (Render 1024)))
Write-Output "Wrote $([System.IO.Path]::GetFullPath($IcoPath)) ($($iconSizes -join ', ') px) and $([System.IO.Path]::GetFullPath($PngPath)) (1024 px)"

# Optional contact sheet: every size at true scale and enlarged, on a dark and a light taskbar colour
if ($PreviewPath) {
    $visual = New-Object System.Windows.Media.DrawingVisual
    $dc = $visual.RenderOpen()
    $dc.DrawRectangle((New-Object System.Windows.Media.SolidColorBrush (Color '#FF1C1C1C')), $null, (New-Object System.Windows.Rect 0, 0, 900, 330))
    $dc.DrawRectangle((New-Object System.Windows.Media.SolidColorBrush (Color '#FFF3F3F3')), $null, (New-Object System.Windows.Rect 0, 330, 900, 330))
    foreach ($band in 0, 330) {
        $x = 20
        foreach ($size in 16, 20, 24, 32, 48, 64) {
            $bitmap = Render $size
            $dc.DrawImage($bitmap, (New-Object System.Windows.Rect $x, ($band + 20), $size, $size))
            $x += $size + 16
        }
        $dc.DrawImage((Render 256), (New-Object System.Windows.Rect 320, ($band + 20), 256, 256))
        # The tray size enlarged eight times without smoothing, to check the pixel grid
        $group = New-Object System.Windows.Media.DrawingGroup
        [System.Windows.Media.RenderOptions]::SetBitmapScalingMode($group, [System.Windows.Media.BitmapScalingMode]::NearestNeighbor)
        $inner = $group.Open(); $inner.DrawImage((Render 16), (New-Object System.Windows.Rect 0, 0, 128, 128)); $inner.Close()
        $dc.PushTransform((New-Object System.Windows.Media.TranslateTransform 20, ($band + 110)))
        $dc.DrawDrawing($group)
        $dc.Pop()
        $group2 = New-Object System.Windows.Media.DrawingGroup
        [System.Windows.Media.RenderOptions]::SetBitmapScalingMode($group2, [System.Windows.Media.BitmapScalingMode]::NearestNeighbor)
        $inner = $group2.Open(); $inner.DrawImage((Render 32), (New-Object System.Windows.Rect 0, 0, 128, 128)); $inner.Close()
        $dc.PushTransform((New-Object System.Windows.Media.TranslateTransform 170, ($band + 110)))
        $dc.DrawDrawing($group2)
        $dc.Pop()
        $dc.DrawImage((Render 1024), (New-Object System.Windows.Rect 610, ($band + 20), 270, 270))
    }
    $dc.Close()
    $sheet = New-Object System.Windows.Media.Imaging.RenderTargetBitmap 900, 660, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
    $sheet.Render($visual)
    [System.IO.File]::WriteAllBytes([System.IO.Path]::GetFullPath($PreviewPath), (PngBytes $sheet))
    Write-Output "Wrote preview $PreviewPath"
}
