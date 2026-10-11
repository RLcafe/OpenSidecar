# Draws assets\WidgetScreen.ico: a Midnight tile with the default dashboard columns
# (Focus in Lime, then Now Playing over Quick Launch, then the mixer). Run with -STA.
param([string]$PreviewDirectory)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase
$projectRoot = Split-Path -Parent $PSScriptRoot
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256

function Brush([string]$hex) { $b = New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.ColorConverter]::ConvertFromString($hex)); $b.Freeze(); $b }
$surface = Brush '#161A22'; $border = Brush '#252B36'; $lime = Brush '#BAF28B'; $tile = Brush '#465061'

function Draw-Icon([int]$size) {
    # Shapes are laid out on a 32-unit grid and snapped to whole pixels at each size.
    $u = $size / 32.0
    function Px([double]$units) { [Math]::Round($units * $u) }
    function Box([double]$x1, [double]$y1, [double]$x2, [double]$y2) { New-Object System.Windows.Rect ((Px $x1), (Px $y1), ((Px $x2) - (Px $x1)), ((Px $y2) - (Px $y1))) }
    $visual = New-Object System.Windows.Media.DrawingVisual
    $dc = $visual.RenderOpen()
    $outline = if ($size -ge 48) { New-Object System.Windows.Media.Pen ($border, [Math]::Max(1, $u)) } else { $null }
    $inset = if ($outline) { $outline.Thickness / 2 } else { 0 }
    $dc.DrawRoundedRectangle($surface, $outline, (New-Object System.Windows.Rect ($inset, $inset, ($size - 2 * $inset), ($size - 2 * $inset))), (7 * $u), (7 * $u))
    $r = [Math]::Max(1, 1.5 * $u)
    $dc.DrawRoundedRectangle($lime, $null, (Box 5 7 11 25), $r, $r)
    $dc.DrawRoundedRectangle($tile, $null, (Box 13 7 19 15), $r, $r)
    $dc.DrawRoundedRectangle($tile, $null, (Box 13 17 19 25), $r, $r)
    $dc.DrawRoundedRectangle($tile, $null, (Box 21 7 27 25), $r, $r)
    $dc.Close()
    $bitmap = New-Object System.Windows.Media.Imaging.RenderTargetBitmap ($size, $size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = New-Object System.IO.MemoryStream
    $encoder.Save($stream)
    , $stream.ToArray()
}

$images = @($sizes | ForEach-Object { , (Draw-Icon $_) })
$icon = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter $icon
$writer.Write([UInt16]0); $writer.Write([UInt16]1); $writer.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $edge = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $writer.Write([byte]$edge); $writer.Write([byte]$edge); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([UInt16]1); $writer.Write([UInt16]32); $writer.Write([UInt32]$images[$i].Length); $writer.Write([UInt32]$offset)
    $offset += $images[$i].Length
}
foreach ($image in $images) { $writer.Write($image) }
$writer.Flush()
$iconPath = Join-Path $projectRoot 'assets\WidgetScreen.ico'
[System.IO.File]::WriteAllBytes($iconPath, $icon.ToArray())
Write-Output "Icon: $iconPath"
if ($PreviewDirectory) {
    New-Item -ItemType Directory -Path $PreviewDirectory -Force | Out-Null
    for ($i = 0; $i -lt $sizes.Count; $i++) { [System.IO.File]::WriteAllBytes((Join-Path $PreviewDirectory "icon-$($sizes[$i]).png"), $images[$i]) }
}
