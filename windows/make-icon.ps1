# Turns a square PNG into a multi-size .ico (PNG-compressed entries, supported since Windows Vista).
param([Parameter(Mandatory = $true)][string]$Source, [Parameter(Mandatory = $true)][string]$Target)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$image = [System.Drawing.Image]::FromFile((Resolve-Path $Source))
$sizes = 16, 24, 32, 48, 64, 128, 256
$entries = foreach ($size in $sizes) {
    $bitmap = New-Object System.Drawing.Bitmap $size, $size
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.DrawImage($image, 0, 0, $size, $size)
    $graphics.Dispose()
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    ,@{ Size = $size; Bytes = $stream.ToArray() }
}
$image.Dispose()
$output = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter $output
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$entries.Count)
$offset = 6 + 16 * $entries.Count
foreach ($entry in $entries) {
    $dimension = if ($entry.Size -ge 256) { 0 } else { $entry.Size }
    $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32)
    $writer.Write([uint32]$entry.Bytes.Length); $writer.Write([uint32]$offset)
    $offset += $entry.Bytes.Length
}
foreach ($entry in $entries) { $writer.Write($entry.Bytes) }
$writer.Flush()
[System.IO.File]::WriteAllBytes($Target, $output.ToArray())
